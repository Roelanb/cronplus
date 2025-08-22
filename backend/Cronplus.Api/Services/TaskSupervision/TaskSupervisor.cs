using System.Collections.Concurrent;
using System.Diagnostics;
using Cronplus.Api.Domain.Entities;

namespace Cronplus.Api.Services.TaskSupervision;

/// <summary>
/// Base class for task supervision
/// </summary>
public abstract class TaskSupervisor : IDisposable
{
    protected readonly ILogger Logger;
    protected readonly string TaskId;
    protected readonly TaskEntity TaskConfig;
    private readonly SemaphoreSlim _executionSemaphore;
    private readonly ConcurrentDictionary<string, ExecutionContext> _activeExecutions;
    private readonly Timer _healthCheckTimer;
    private readonly object _stateLock = new();
    
    private TaskState _currentState = TaskState.Created;
    private DateTime _startedAt;
    private DateTime _lastActivityAt;
    private long _processedCount;
    private long _errorCount;
    private long _consecutiveErrors;
    private bool _disposed;
    private CancellationTokenSource? _cancellationTokenSource;
    
    // Public properties for supervisor info
    public TaskState State => CurrentState;
    public DateTime LastStateChange { get; private set; } = DateTime.UtcNow;
    public string? LastError { get; private set; }
    public int ProcessedFiles => (int)_processedCount;
    public int FailedFiles => (int)_errorCount;
    public DateTime? LastFileProcessed { get; private set; }

    public TaskState CurrentState 
    { 
        get { lock (_stateLock) return _currentState; }
    }
    
    public TaskHealthStatus HealthStatus => GetHealthStatus();
    
    public event EventHandler<TaskStateChangedEventArgs>? StateChanged;
    public event EventHandler<TaskExecutionEventArgs>? ExecutionStarted;
    public event EventHandler<TaskExecutionEventArgs>? ExecutionCompleted;
    public event EventHandler<TaskErrorEventArgs>? ErrorOccurred;

    protected TaskSupervisor(
        string taskId,
        TaskEntity taskConfig,
        ILogger logger,
        int maxConcurrentExecutions = 5)
    {
        TaskId = taskId;
        TaskConfig = taskConfig;
        Logger = logger;
        _executionSemaphore = new SemaphoreSlim(maxConcurrentExecutions, maxConcurrentExecutions);
        _activeExecutions = new ConcurrentDictionary<string, ExecutionContext>();
        _healthCheckTimer = new Timer(PerformHealthCheck, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Initialize the task supervisor
    /// </summary>
    public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!TryChangeState(TaskState.Initializing, "Starting initialization"))
        {
            throw new InvalidOperationException($"Cannot initialize task {TaskId} from state {CurrentState}");
        }

        try
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _startedAt = DateTime.UtcNow;
            _lastActivityAt = DateTime.UtcNow;
            
            await OnInitializeAsync(_cancellationTokenSource.Token);
            
            TryChangeState(TaskState.Idle, "Initialization completed");
            Logger.LogInformation("Task supervisor {TaskId} initialized successfully", TaskId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize task supervisor {TaskId}", TaskId);
            TryChangeState(TaskState.Failed, $"Initialization failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Start processing for this task
    /// </summary>
    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentState == TaskState.Processing || CurrentState == TaskState.Idle)
        {
            Logger.LogDebug("Task {TaskId} is already running", TaskId);
            return;
        }

        if (CurrentState == TaskState.Paused)
        {
            await ResumeAsync(cancellationToken);
            return;
        }

        if (CurrentState != TaskState.Created && CurrentState != TaskState.Stopped && CurrentState != TaskState.Failed)
        {
            throw new InvalidOperationException($"Cannot start task {TaskId} from state {CurrentState}");
        }

        await InitializeAsync(cancellationToken);
    }

    /// <summary>
    /// Pause the task
    /// </summary>
    public virtual async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (!TryChangeState(TaskState.Paused, "Pause requested"))
        {
            throw new InvalidOperationException($"Cannot pause task {TaskId} from state {CurrentState}");
        }

        await OnPauseAsync(cancellationToken);
        Logger.LogInformation("Task supervisor {TaskId} paused", TaskId);
    }

    /// <summary>
    /// Resume the task from paused state
    /// </summary>
    public virtual async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentState != TaskState.Paused)
        {
            throw new InvalidOperationException($"Cannot resume task {TaskId} from state {CurrentState}");
        }

        if (!TryChangeState(TaskState.Idle, "Resume requested"))
        {
            throw new InvalidOperationException($"Failed to resume task {TaskId}");
        }

        await OnResumeAsync(cancellationToken);
        Logger.LogInformation("Task supervisor {TaskId} resumed", TaskId);
    }

    /// <summary>
    /// Stop the task gracefully
    /// </summary>
    public virtual async Task StopAsync(TimeSpan timeout = default)
    {
        if (CurrentState == TaskState.Stopped || CurrentState == TaskState.Stopping)
        {
            return;
        }

        if (!TryChangeState(TaskState.Stopping, "Stop requested"))
        {
            Logger.LogWarning("Cannot stop task {TaskId} from state {State}", TaskId, CurrentState);
            return;
        }

        try
        {
            Logger.LogInformation("Stopping task supervisor {TaskId}", TaskId);
            
            // Cancel ongoing operations
            _cancellationTokenSource?.Cancel();
            
            // Wait for active executions to complete
            if (timeout == default)
                timeout = TimeSpan.FromSeconds(30);

            var stopwatch = Stopwatch.StartNew();
            while (_activeExecutions.Count > 0 && stopwatch.Elapsed < timeout)
            {
                await Task.Delay(100);
            }

            if (_activeExecutions.Count > 0)
            {
                Logger.LogWarning("Task {TaskId} stopped with {Count} active executions", 
                    TaskId, _activeExecutions.Count);
            }

            await OnStopAsync();
            
            TryChangeState(TaskState.Stopped, "Stop completed");
            Logger.LogInformation("Task supervisor {TaskId} stopped successfully", TaskId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error stopping task supervisor {TaskId}", TaskId);
            TryChangeState(TaskState.Failed, $"Stop failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Execute a job with concurrency control
    /// </summary>
    protected async Task<TResult> ExecuteWithSupervisionAsync<TResult>(
        string executionId,
        Func<CancellationToken, Task<TResult>> execution,
        CancellationToken cancellationToken = default)
    {
        if (CurrentState != TaskState.Idle && CurrentState != TaskState.Processing)
        {
            throw new InvalidOperationException($"Cannot execute in state {CurrentState}");
        }

        var context = new ExecutionContext
        {
            Id = executionId,
            StartedAt = DateTime.UtcNow,
            TaskId = TaskId
        };

        await _executionSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            _activeExecutions.TryAdd(executionId, context);
            
            if (CurrentState == TaskState.Idle)
            {
                TryChangeState(TaskState.Processing, "Execution started");
            }

            _lastActivityAt = DateTime.UtcNow;
            
            ExecutionStarted?.Invoke(this, new TaskExecutionEventArgs 
            { 
                TaskId = TaskId, 
                ExecutionId = executionId 
            });

            var result = await execution(cancellationToken);
            
            _processedCount++;
            _consecutiveErrors = 0;
            
            context.CompletedAt = DateTime.UtcNow;
            context.Success = true;
            
            ExecutionCompleted?.Invoke(this, new TaskExecutionEventArgs 
            { 
                TaskId = TaskId, 
                ExecutionId = executionId,
                Success = true,
                Duration = context.CompletedAt.Value - context.StartedAt
            });

            return result;
        }
        catch (Exception ex)
        {
            _errorCount++;
            _consecutiveErrors++;
            
            context.CompletedAt = DateTime.UtcNow;
            context.Success = false;
            context.Error = ex.Message;
            
            ErrorOccurred?.Invoke(this, new TaskErrorEventArgs 
            { 
                TaskId = TaskId, 
                ExecutionId = executionId,
                Exception = ex 
            });

            // Check if we should degrade or fail
            if (_consecutiveErrors >= 10)
            {
                TryChangeState(TaskState.Failed, $"Too many consecutive errors: {_consecutiveErrors}");
            }
            else if (_consecutiveErrors >= 5)
            {
                TryChangeState(TaskState.Degraded, $"Multiple consecutive errors: {_consecutiveErrors}");
            }

            throw;
        }
        finally
        {
            _activeExecutions.TryRemove(executionId, out _);
            _executionSemaphore.Release();
            
            // Change back to idle if no more executions
            if (_activeExecutions.IsEmpty && CurrentState == TaskState.Processing)
            {
                TryChangeState(TaskState.Idle, "All executions completed");
            }
        }
    }

    /// <summary>
    /// Try to change the task state
    /// </summary>
    protected bool TryChangeState(TaskState newState, string? reason = null)
    {
        lock (_stateLock)
        {
            if (!TaskStateTransitions.IsValidTransition(_currentState, newState))
            {
                Logger.LogWarning("Invalid state transition for task {TaskId}: {From} -> {To}", 
                    TaskId, _currentState, newState);
                return false;
            }

            var previousState = _currentState;
            _currentState = newState;
            
            Logger.LogInformation("Task {TaskId} state changed: {From} -> {To} ({Reason})", 
                TaskId, previousState, newState, reason ?? "No reason");

            StateChanged?.Invoke(this, new TaskStateChangedEventArgs
            {
                TaskId = TaskId,
                PreviousState = previousState,
                NewState = newState,
                Reason = reason
            });

            return true;
        }
    }

    /// <summary>
    /// Perform health check
    /// </summary>
    private void PerformHealthCheck(object? state)
    {
        try
        {
            var health = GetHealthStatus();
            
            if (!health.IsHealthy && CurrentState == TaskState.Idle)
            {
                TryChangeState(TaskState.Degraded, string.Join(", ", health.Issues));
            }
            else if (health.IsHealthy && CurrentState == TaskState.Degraded)
            {
                TryChangeState(TaskState.Idle, "Health restored");
            }
            
            OnHealthCheckPerformed(health);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error performing health check for task {TaskId}", TaskId);
        }
    }

    /// <summary>
    /// Get current health status
    /// </summary>
    private TaskHealthStatus GetHealthStatus()
    {
        var now = DateTime.UtcNow;
        var uptime = now - _startedAt;
        var processingRate = _processedCount > 0 && uptime.TotalMinutes > 0 
            ? _processedCount / uptime.TotalMinutes 
            : 0;

        var health = new TaskHealthStatus
        {
            TaskId = TaskId,
            State = CurrentState,
            LastActivity = _lastActivityAt,
            ProcessedCount = _processedCount,
            ErrorCount = _errorCount,
            ConsecutiveErrors = _consecutiveErrors,
            Uptime = uptime,
            ProcessingRate = processingRate,
            Metrics = GetMetrics()
        };

        // Determine health
        health.IsHealthy = DetermineHealth(health);
        
        return health;
    }

    /// <summary>
    /// Determine if the task is healthy
    /// </summary>
    private bool DetermineHealth(TaskHealthStatus health)
    {
        health.Issues.Clear();

        if (CurrentState == TaskState.Failed)
        {
            health.Issues.Add("Task is in failed state");
            return false;
        }

        if (_consecutiveErrors >= 5)
        {
            health.Issues.Add($"High consecutive error count: {_consecutiveErrors}");
        }

        if (_errorCount > 0 && _processedCount > 0)
        {
            var errorRate = (double)_errorCount / (_errorCount + _processedCount);
            if (errorRate > 0.1) // More than 10% error rate
            {
                health.Issues.Add($"High error rate: {errorRate:P}");
            }
        }

        var timeSinceLastActivity = DateTime.UtcNow - _lastActivityAt;
        if (timeSinceLastActivity > TimeSpan.FromHours(1) && TaskConfig.Enabled)
        {
            health.Issues.Add($"No activity for {timeSinceLastActivity.TotalMinutes:F0} minutes");
        }

        return health.Issues.Count == 0;
    }

    /// <summary>
    /// Get task metrics
    /// </summary>
    protected virtual Dictionary<string, object> GetMetrics()
    {
        return new Dictionary<string, object>
        {
            ["ActiveExecutions"] = _activeExecutions.Count,
            ["AvailableSlots"] = _executionSemaphore.CurrentCount,
            ["TotalProcessed"] = _processedCount,
            ["TotalErrors"] = _errorCount,
            ["ConsecutiveErrors"] = _consecutiveErrors
        };
    }

    // Abstract methods for derived classes
    protected abstract Task OnInitializeAsync(CancellationToken cancellationToken);
    protected abstract Task OnPauseAsync(CancellationToken cancellationToken);
    protected abstract Task OnResumeAsync(CancellationToken cancellationToken);
    protected abstract Task OnStopAsync();
    protected abstract void OnHealthCheckPerformed(TaskHealthStatus health);

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        
        _healthCheckTimer?.Dispose();
        _executionSemaphore?.Dispose();
        _cancellationTokenSource?.Dispose();
    }

    /// <summary>
    /// Execution context for tracking
    /// </summary>
    private class ExecutionContext
    {
        public string Id { get; set; } = string.Empty;
        public string TaskId { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}

/// <summary>
/// Task execution event arguments
/// </summary>
public class TaskExecutionEventArgs : EventArgs
{
    public string TaskId { get; set; } = string.Empty;
    public string ExecutionId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public TimeSpan? Duration { get; set; }
}

/// <summary>
/// Task error event arguments
/// </summary>
public class TaskErrorEventArgs : EventArgs
{
    public string TaskId { get; set; } = string.Empty;
    public string ExecutionId { get; set; } = string.Empty;
    public Exception Exception { get; set; } = null!;
}