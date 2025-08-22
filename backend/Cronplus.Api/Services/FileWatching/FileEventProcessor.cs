using System.Threading.Channels;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Domain.Models.PipelineSteps;
using Cronplus.Api.Infrastructure.Database;
using Microsoft.Extensions.Options;

namespace Cronplus.Api.Services.FileWatching;

/// <summary>
/// Service for processing file events through pipelines
/// </summary>
public interface IFileEventProcessor
{
    /// <summary>
    /// Process a file change event
    /// </summary>
    Task ProcessFileChangeAsync(FileChangeEventArgs args, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get processing statistics
    /// </summary>
    ProcessingStatistics GetStatistics();
}

/// <summary>
/// File event processor implementation
/// </summary>
public class FileEventProcessor : IFileEventProcessor, IHostedService, IDisposable
{
    private readonly ILogger<FileEventProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Channel<FileProcessingJob> _processingChannel;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly ProcessingStatistics _statistics;
    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;

    public FileEventProcessor(
        ILogger<FileEventProcessor> logger,
        IServiceProvider serviceProvider,
        IOptions<FileProcessorOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _statistics = new ProcessingStatistics();
        
        var channelOptions = new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        
        _processingChannel = Channel.CreateBounded<FileProcessingJob>(channelOptions);
        _concurrencySemaphore = new SemaphoreSlim(options.Value.MaxConcurrentProcessing);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessQueueAsync(_cancellationTokenSource.Token);
        
        _logger.LogInformation("File event processor started");
        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping file event processor");
        
        _processingChannel.Writer.TryComplete();
        _cancellationTokenSource?.Cancel();
        
        if (_processingTask != null)
        {
            await _processingTask;
        }
        
        _logger.LogInformation("File event processor stopped");
    }

    public async Task ProcessFileChangeAsync(FileChangeEventArgs args, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileEventProcessor));

        var job = new FileProcessingJob
        {
            Id = Guid.NewGuid().ToString(),
            TaskId = args.TaskId,
            FilePath = args.FilePath,
            ChangeType = args.ChangeType,
            FileSize = args.FileSize,
            OldPath = args.OldPath,
            EnqueuedAt = DateTime.UtcNow
        };

        try
        {
            await _processingChannel.Writer.WriteAsync(job, cancellationToken);
            _statistics.EventsQueued++;
            
            _logger.LogDebug("File processing job {JobId} queued for task {TaskId}", job.Id, job.TaskId);
        }
        catch (ChannelClosedException)
        {
            _logger.LogWarning("Cannot queue job - processing channel is closed");
            _statistics.EventsDropped++;
            throw;
        }
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        
        await foreach (var job in _processingChannel.Reader.ReadAllAsync(cancellationToken))
        {
            // Wait for available concurrency slot
            await _concurrencySemaphore.WaitAsync(cancellationToken);
            
            // Start processing task
            var task = ProcessJobAsync(job, cancellationToken)
                .ContinueWith(t =>
                {
                    _concurrencySemaphore.Release();
                    
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Error processing job {JobId}", job.Id);
                    }
                }, cancellationToken);
            
            tasks.Add(task);
            
            // Clean up completed tasks
            tasks.RemoveAll(t => t.IsCompleted);
        }
        
        // Wait for all remaining tasks
        await Task.WhenAll(tasks);
    }

    private async Task ProcessJobAsync(FileProcessingJob job, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        job.StartedAt = startTime;
        
        _logger.LogInformation("Processing file event: {ChangeType} for {FilePath} in task {TaskId}",
            job.ChangeType, job.FilePath, job.TaskId);
        
        _statistics.EventsProcessing++;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            
            // Get task configuration
            var task = await unitOfWork.Tasks.GetByIdAsync(job.TaskId);
            
            if (task == null)
            {
                _logger.LogWarning("Task {TaskId} not found", job.TaskId);
                _statistics.EventsFailed++;
                return;
            }
            
            if (!task.Enabled)
            {
                _logger.LogDebug("Task {TaskId} is disabled, skipping processing", job.TaskId);
                _statistics.EventsSkipped++;
                return;
            }
            
            // Get pipeline steps
            var steps = await unitOfWork.PipelineSteps.GetByTaskIdAsync(job.TaskId);
            
            if (!steps.Any())
            {
                _logger.LogWarning("No pipeline steps configured for task {TaskId}", job.TaskId);
                _statistics.EventsSkipped++;
                return;
            }
            
            // Create execution context
            var context = new Cronplus.Api.Domain.Models.PipelineSteps.ExecutionContext
            {
                TaskId = job.TaskId,
                FilePath = job.FilePath,
                Logger = _logger,
                Variables = new Dictionary<string, object>
                {
                    ["ChangeType"] = job.ChangeType.ToString(),
                    ["FileSize"] = job.FileSize ?? 0,
                    ["ProcessingJobId"] = job.Id
                }
            };
            
            // Add old path if it's a rename
            if (!string.IsNullOrEmpty(job.OldPath))
            {
                context.Variables["OldPath"] = job.OldPath;
            }
            
            // Load task variables
            var variables = await unitOfWork.TaskVariables.GetByTaskIdAsync(job.TaskId);
            foreach (var variable in variables)
            {
                context.Variables[variable.Name] = variable.Value;
            }
            
            // Execute pipeline
            var pipelineSuccess = await ExecutePipelineAsync(context, steps, cancellationToken);
            
            // Log execution
            var executionLog = new Domain.Entities.ExecutionLog
            {
                TaskId = job.TaskId,
                FilePath = job.FilePath,
                Status = pipelineSuccess ? "completed" : "failed",
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                ExecutionDetails = System.Text.Json.JsonDocument.Parse(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        JobId = job.Id,
                        ChangeType = job.ChangeType.ToString(),
                        FileSize = job.FileSize,
                        StepsExecuted = context.ExecutionLog.Count,
                        Variables = context.Variables
                    }))
            };
            
            await unitOfWork.ExecutionLogs.AddAsync(executionLog);
            await unitOfWork.CommitAsync();
            
            job.CompletedAt = DateTime.UtcNow;
            
            if (pipelineSuccess)
            {
                _statistics.EventsProcessed++;
                _logger.LogInformation("Successfully processed file event for {FilePath} in task {TaskId}", 
                    job.FilePath, job.TaskId);
            }
            else
            {
                _statistics.EventsFailed++;
                _logger.LogWarning("Pipeline execution failed for {FilePath} in task {TaskId}", 
                    job.FilePath, job.TaskId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file event for {FilePath} in task {TaskId}", 
                job.FilePath, job.TaskId);
            
            _statistics.EventsFailed++;
            job.Error = ex.Message;
            job.CompletedAt = DateTime.UtcNow;
        }
        finally
        {
            _statistics.EventsProcessing--;
            
            var duration = (job.CompletedAt ?? DateTime.UtcNow) - startTime;
            _statistics.TotalProcessingTime += duration;
            
            if (duration > _statistics.MaxProcessingTime)
                _statistics.MaxProcessingTime = duration;
        }
    }

    private async Task<bool> ExecutePipelineAsync(
        Cronplus.Api.Domain.Models.PipelineSteps.ExecutionContext context, 
        IEnumerable<Domain.Entities.PipelineStep> steps,
        CancellationToken cancellationToken)
    {
        var orderedSteps = steps.OrderBy(s => s.StepOrder).ToList();
        
        foreach (var stepEntity in orderedSteps)
        {
            try
            {
                // TODO: Deserialize step configuration and create appropriate step instance
                // This would require a factory pattern to create the correct step type
                
                _logger.LogDebug("Executing step {StepOrder}: {StepType} for task {TaskId}", 
                    stepEntity.StepOrder, stepEntity.Type, context.TaskId);
                
                // For now, we'll just log that we would execute the step
                // In a complete implementation, we would:
                // 1. Deserialize the configuration JSON
                // 2. Create the appropriate step instance (CopyStep, DeleteStep, etc.)
                // 3. Execute the step
                // 4. Handle the result
                
                await Task.Delay(100, cancellationToken); // Simulate step execution
                
                context.LogStepExecution($"Step_{stepEntity.StepOrder}", StepResult.SuccessResult("Simulated execution"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing step {StepOrder} for task {TaskId}", 
                    stepEntity.StepOrder, context.TaskId);
                
                context.LogStepExecution($"Step_{stepEntity.StepOrder}", 
                    StepResult.FailureResult($"Step failed: {ex.Message}", ex));
                
                return false; // Pipeline failed
            }
        }
        
        return true; // All steps completed successfully
    }

    public ProcessingStatistics GetStatistics()
    {
        return new ProcessingStatistics
        {
            EventsQueued = _statistics.EventsQueued,
            EventsProcessing = _statistics.EventsProcessing,
            EventsProcessed = _statistics.EventsProcessed,
            EventsFailed = _statistics.EventsFailed,
            EventsSkipped = _statistics.EventsSkipped,
            EventsDropped = _statistics.EventsDropped,
            TotalProcessingTime = _statistics.TotalProcessingTime,
            MaxProcessingTime = _statistics.MaxProcessingTime,
            QueueLength = _processingChannel.Reader.Count
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        
        _processingChannel.Writer.TryComplete();
        _cancellationTokenSource?.Cancel();
        _processingTask?.Wait(TimeSpan.FromSeconds(10));
        
        _cancellationTokenSource?.Dispose();
        _concurrencySemaphore.Dispose();
    }

    /// <summary>
    /// File processing job
    /// </summary>
    private class FileProcessingJob
    {
        public string Id { get; set; } = string.Empty;
        public string TaskId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string? OldPath { get; set; }
        public FileChangeType ChangeType { get; set; }
        public long? FileSize { get; set; }
        public DateTime EnqueuedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Error { get; set; }
    }
}

/// <summary>
/// Processing statistics
/// </summary>
public class ProcessingStatistics
{
    public long EventsQueued { get; set; }
    public long EventsProcessing { get; set; }
    public long EventsProcessed { get; set; }
    public long EventsFailed { get; set; }
    public long EventsSkipped { get; set; }
    public long EventsDropped { get; set; }
    public TimeSpan TotalProcessingTime { get; set; }
    public TimeSpan MaxProcessingTime { get; set; }
    public int QueueLength { get; set; }
    
    public double AverageProcessingTime => 
        EventsProcessed > 0 ? TotalProcessingTime.TotalMilliseconds / EventsProcessed : 0;
    
    public double SuccessRate => 
        (EventsProcessed + EventsFailed) > 0 
            ? (double)EventsProcessed / (EventsProcessed + EventsFailed) * 100 
            : 0;
}

/// <summary>
/// File processor options
/// </summary>
public class FileProcessorOptions
{
    public int QueueCapacity { get; set; } = 1000;
    public int MaxConcurrentProcessing { get; set; } = 10;
}