using System.Collections.Concurrent;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Infrastructure.Database;
using Cronplus.Api.Services.FileWatching;

namespace Cronplus.Api.Services.TaskSupervision;

// Type alias for clarity in endpoints
using TaskSupervisorState = Cronplus.Api.Services.TaskSupervision.TaskState;

/// <summary>
/// Manages all task supervisors
/// </summary>
public interface ITaskSupervisorManager
{
    /// <summary>
    /// Start a task supervisor
    /// </summary>
    Task<bool> StartTaskAsync(string taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop a task supervisor
    /// </summary>
    Task<bool> StopTaskAsync(string taskId, TimeSpan timeout = default);
    
    /// <summary>
    /// Stop a task supervisor (with cancellation token)
    /// </summary>
    Task StopTaskAsync(string taskId, CancellationToken cancellationToken);
    
    /// <summary>
    /// Pause a task supervisor
    /// </summary>
    Task<bool> PauseTaskAsync(string taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resume a task supervisor
    /// </summary>
    Task<bool> ResumeTaskAsync(string taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get task status
    /// </summary>
    TaskSupervisorState? GetTaskStatus(string taskId);
    
    /// <summary>
    /// Get all task statuses
    /// </summary>
    IEnumerable<TaskHealthStatus> GetAllTaskStatuses();
    
    /// <summary>
    /// Check if a task is supervised
    /// </summary>
    bool IsSupervised(string taskId);
    
    /// <summary>
    /// Reload a task (stop and start with latest configuration)
    /// </summary>
    Task ReloadTaskAsync(string taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get detailed supervisor information
    /// </summary>
    TaskSupervisorInfo? GetSupervisorInfo(string taskId);
    
    /// <summary>
    /// Event raised when a task state changes
    /// </summary>
    event EventHandler<TaskStateChangedEventArgs>? TaskStateChanged;
}

/// <summary>
/// Implementation of task supervisor manager
/// </summary>
public class TaskSupervisorManager : ITaskSupervisorManager, IHostedService, IDisposable
{
    private readonly ILogger<TaskSupervisorManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, TaskSupervisor> _supervisors;
    private readonly Timer _refreshTimer;
    private readonly SemaphoreSlim _refreshSemaphore;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;

    public event EventHandler<TaskStateChangedEventArgs>? TaskStateChanged;

    public TaskSupervisorManager(
        ILogger<TaskSupervisorManager> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _supervisors = new ConcurrentDictionary<string, TaskSupervisor>();
        _refreshSemaphore = new SemaphoreSlim(1, 1);
        
        // Set up periodic refresh to check for new/updated tasks
        _refreshTimer = new Timer(
            async _ => await RefreshSupervisorsAsync(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting task supervisor manager");
        
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        // Load and start supervisors for all enabled tasks
        await LoadSupervisorsAsync(cancellationToken);
        
        // Start refresh timer
        _refreshTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        
        _logger.LogInformation("Task supervisor manager started with {Count} supervisors", _supervisors.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping task supervisor manager");
        
        // Stop refresh timer
        _refreshTimer.Change(Timeout.Infinite, 0);
        
        // Stop all supervisors
        var stopTasks = _supervisors.Values.Select(s => s.StopAsync(TimeSpan.FromSeconds(30)));
        await Task.WhenAll(stopTasks);
        
        _cancellationTokenSource?.Cancel();
        
        _logger.LogInformation("Task supervisor manager stopped");
    }

    public async Task<bool> StartTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_supervisors.TryGetValue(taskId, out var supervisor))
            {
                await supervisor.StartAsync(cancellationToken);
                return true;
            }
            
            // Try to create supervisor if task exists
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var task = await unitOfWork.Tasks.GetByIdAsync(taskId);
            
            if (task == null)
            {
                _logger.LogWarning("Task {TaskId} not found", taskId);
                return false;
            }
            
            supervisor = CreateSupervisor(task);
            if (_supervisors.TryAdd(taskId, supervisor))
            {
                SubscribeToSupervisorEvents(supervisor);
                await supervisor.StartAsync(cancellationToken);
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting task {TaskId}", taskId);
            return false;
        }
    }

    public async Task<bool> StopTaskAsync(string taskId, TimeSpan timeout = default)
    {
        try
        {
            if (_supervisors.TryRemove(taskId, out var supervisor))
            {
                await supervisor.StopAsync(timeout);
                supervisor.Dispose();
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping task {TaskId}", taskId);
            return false;
        }
    }

    public async Task<bool> PauseTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_supervisors.TryGetValue(taskId, out var supervisor))
            {
                await supervisor.PauseAsync(cancellationToken);
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing task {TaskId}", taskId);
            return false;
        }
    }

    public async Task<bool> ResumeTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_supervisors.TryGetValue(taskId, out var supervisor))
            {
                await supervisor.ResumeAsync(cancellationToken);
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming task {TaskId}", taskId);
            return false;
        }
    }

    public TaskSupervisorState? GetTaskStatus(string taskId)
    {
        return _supervisors.TryGetValue(taskId, out var supervisor) 
            ? supervisor.State 
            : null;
    }
    
    public TaskSupervisorInfo? GetSupervisorInfo(string taskId)
    {
        if (!_supervisors.TryGetValue(taskId, out var supervisor))
            return null;
            
        return new TaskSupervisorInfo
        {
            TaskId = taskId,
            State = supervisor.State,
            LastStateChange = supervisor.LastStateChange,
            LastError = supervisor.LastError,
            ProcessedFiles = supervisor.ProcessedFiles,
            FailedFiles = supervisor.FailedFiles,
            LastFileProcessed = supervisor.LastFileProcessed
        };
    }
    
    public async Task ReloadTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reloading task {TaskId}", taskId);
        
        // Stop the existing supervisor if running
        if (_supervisors.TryRemove(taskId, out var existingSupervisor))
        {
            try
            {
                await existingSupervisor.StopAsync(TimeSpan.FromSeconds(30));
                existingSupervisor.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping existing supervisor for task {TaskId}", taskId);
            }
        }
        
        // Load the latest task configuration
        using var scope = _serviceProvider.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var task = await unitOfWork.Tasks.GetByIdAsync(taskId);
        
        if (task != null && task.Enabled)
        {
            try
            {
                var supervisor = CreateSupervisor(task);
                
                if (_supervisors.TryAdd(task.Id, supervisor))
                {
                    SubscribeToSupervisorEvents(supervisor);
                    await supervisor.InitializeAsync(cancellationToken);
                    await supervisor.StartAsync(cancellationToken);
                    
                    _logger.LogInformation("Reloaded supervisor for task {TaskId}", task.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload supervisor for task {TaskId}", task.Id);
                throw;
            }
        }
    }
    
    public async Task StopTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping task {TaskId}", taskId);
        
        if (_supervisors.TryRemove(taskId, out var supervisor))
        {
            try
            {
                await supervisor.StopAsync(TimeSpan.FromSeconds(30));
                supervisor.Dispose();
                _logger.LogInformation("Stopped supervisor for task {TaskId}", taskId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping supervisor for task {TaskId}", taskId);
                throw;
            }
        }
    }

    public IEnumerable<TaskHealthStatus> GetAllTaskStatuses()
    {
        return _supervisors.Values.Select(s => s.HealthStatus);
    }

    public bool IsSupervised(string taskId)
    {
        return _supervisors.ContainsKey(taskId);
    }

    private async Task LoadSupervisorsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            
            // Get all enabled tasks
            var tasks = await unitOfWork.Tasks.GetAllAsync();
            var enabledTasks = tasks.Where(t => t.Enabled).ToList();
            
            _logger.LogInformation("Loading supervisors for {Count} enabled tasks", enabledTasks.Count);
            
            foreach (var task in enabledTasks)
            {
                try
                {
                    var supervisor = CreateSupervisor(task);
                    
                    if (_supervisors.TryAdd(task.Id, supervisor))
                    {
                        SubscribeToSupervisorEvents(supervisor);
                        await supervisor.InitializeAsync(cancellationToken);
                        
                        _logger.LogInformation("Started supervisor for task {TaskId}", task.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start supervisor for task {TaskId}", task.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading supervisors");
        }
    }

    private async Task RefreshSupervisorsAsync()
    {
        if (!await _refreshSemaphore.WaitAsync(0))
            return; // Skip if already refreshing

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            
            // Get all tasks
            var tasks = await unitOfWork.Tasks.GetAllAsync();
            var activeSupervisors = _supervisors.Keys.ToHashSet();
            
            // Start supervisors for newly enabled tasks
            foreach (var task in tasks.Where(t => t.Enabled))
            {
                if (!activeSupervisors.Contains(task.Id))
                {
                    await StartTaskAsync(task.Id, _cancellationTokenSource?.Token ?? default);
                }
            }
            
            // Stop supervisors for disabled or deleted tasks
            var currentTaskIds = tasks.Select(t => t.Id).ToHashSet();
            foreach (var supervisorId in activeSupervisors)
            {
                var task = tasks.FirstOrDefault(t => t.Id == supervisorId);
                
                if (task == null || !task.Enabled)
                {
                    await StopTaskAsync(supervisorId);
                    _logger.LogInformation("Stopped supervisor for task {TaskId} (task {Status})", 
                        supervisorId, task == null ? "deleted" : "disabled");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing supervisors");
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    private TaskSupervisor CreateSupervisor(Domain.Entities.TaskEntity task)
    {
        using var scope = _serviceProvider.CreateScope();
        
        var fileWatcherService = scope.ServiceProvider.GetRequiredService<IFileWatcherService>();
        var fileEventProcessor = scope.ServiceProvider.GetRequiredService<IFileEventProcessor>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<FileWatchTaskSupervisor>>();
        
        // For now, we only support file watch tasks
        // In the future, could have different supervisor types
        return new FileWatchTaskSupervisor(
            task.Id,
            task,
            fileWatcherService,
            fileEventProcessor,
            scope.ServiceProvider,
            logger,
            maxConcurrentExecutions: 5); // Could be made configurable
    }

    private void SubscribeToSupervisorEvents(TaskSupervisor supervisor)
    {
        supervisor.StateChanged += OnSupervisorStateChanged;
        supervisor.ExecutionStarted += OnExecutionStarted;
        supervisor.ExecutionCompleted += OnExecutionCompleted;
        supervisor.ErrorOccurred += OnErrorOccurred;
    }

    private void OnSupervisorStateChanged(object? sender, TaskStateChangedEventArgs e)
    {
        _logger.LogInformation("Task {TaskId} state changed: {From} -> {To}", 
            e.TaskId, e.PreviousState, e.NewState);
        
        TaskStateChanged?.Invoke(this, e);
    }

    private void OnExecutionStarted(object? sender, TaskExecutionEventArgs e)
    {
        _logger.LogDebug("Execution {ExecutionId} started for task {TaskId}", 
            e.ExecutionId, e.TaskId);
    }

    private void OnExecutionCompleted(object? sender, TaskExecutionEventArgs e)
    {
        _logger.LogDebug("Execution {ExecutionId} completed for task {TaskId}: Success={Success}, Duration={Duration}ms", 
            e.ExecutionId, e.TaskId, e.Success, e.Duration?.TotalMilliseconds);
    }

    private void OnErrorOccurred(object? sender, TaskErrorEventArgs e)
    {
        _logger.LogError(e.Exception, "Error in execution {ExecutionId} for task {TaskId}", 
            e.ExecutionId, e.TaskId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        _refreshTimer?.Dispose();
        
        // Stop and dispose all supervisors
        var disposeTasks = _supervisors.Values.Select(async s =>
        {
            try
            {
                await s.StopAsync(TimeSpan.FromSeconds(10));
                s.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing supervisor");
            }
        });
        
        Task.WhenAll(disposeTasks).GetAwaiter().GetResult();
        
        _supervisors.Clear();
        _refreshSemaphore?.Dispose();
        _cancellationTokenSource?.Dispose();
    }
}