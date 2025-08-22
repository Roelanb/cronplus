using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Models;
using Cronplus.Api.Services.FileWatching;

namespace Cronplus.Api.Services.TaskSupervision;

/// <summary>
/// Supervisor for file watching tasks
/// </summary>
public class FileWatchTaskSupervisor : TaskSupervisor
{
    private readonly IFileWatcherService _fileWatcherService;
    private readonly IFileEventProcessor _fileEventProcessor;
    private readonly IServiceProvider _serviceProvider;
    private WatchConfiguration? _watchConfiguration;

    public FileWatchTaskSupervisor(
        string taskId,
        TaskEntity taskConfig,
        IFileWatcherService fileWatcherService,
        IFileEventProcessor fileEventProcessor,
        IServiceProvider serviceProvider,
        ILogger<FileWatchTaskSupervisor> logger,
        int maxConcurrentExecutions = 5)
        : base(taskId, taskConfig, logger, maxConcurrentExecutions)
    {
        _fileWatcherService = fileWatcherService;
        _fileEventProcessor = fileEventProcessor;
        _serviceProvider = serviceProvider;
    }

    protected override async Task OnInitializeAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Initializing file watch supervisor for task {TaskId}", TaskId);
        
        // Create watch configuration
        _watchConfiguration = new WatchConfiguration(
            TaskConfig.WatchDirectory,
            TaskConfig.GlobPattern,
            includeSubdirectories: false, // Could be made configurable
            TaskConfig.DebounceMs,
            TaskConfig.StabilizationMs);
        
        // Subscribe to file watcher events
        _fileWatcherService.FileChangeDetected += OnFileChangeDetected;
        _fileWatcherService.WatcherError += OnWatcherError;
        
        // Start watching
        await _fileWatcherService.StartWatchingAsync(TaskId, _watchConfiguration, cancellationToken);
        
        Logger.LogInformation("File watch supervisor initialized for task {TaskId}: {Directory}/{Pattern}", 
            TaskId, TaskConfig.WatchDirectory, TaskConfig.GlobPattern);
    }

    protected override async Task OnPauseAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Pausing file watch supervisor for task {TaskId}", TaskId);
        
        // Stop watching but keep subscriptions
        await _fileWatcherService.StopWatchingAsync(TaskId, cancellationToken);
    }

    protected override async Task OnResumeAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Resuming file watch supervisor for task {TaskId}", TaskId);
        
        // Restart watching
        if (_watchConfiguration != null)
        {
            await _fileWatcherService.StartWatchingAsync(TaskId, _watchConfiguration, cancellationToken);
        }
    }

    protected override async Task OnStopAsync()
    {
        Logger.LogInformation("Stopping file watch supervisor for task {TaskId}", TaskId);
        
        // Unsubscribe from events
        _fileWatcherService.FileChangeDetected -= OnFileChangeDetected;
        _fileWatcherService.WatcherError -= OnWatcherError;
        
        // Stop watching
        await _fileWatcherService.StopWatchingAsync(TaskId);
    }

    protected override void OnHealthCheckPerformed(TaskHealthStatus health)
    {
        // Add file watcher specific metrics
        health.Metrics["IsWatching"] = _fileWatcherService.IsWatching(TaskId);
        
        // Check if watcher is still active
        if (!_fileWatcherService.IsWatching(TaskId) && TaskConfig.Enabled && CurrentState == TaskState.Idle)
        {
            health.Issues.Add("File watcher is not active");
            health.IsHealthy = false;
            
            // Try to restart watcher
            Task.Run(async () =>
            {
                try
                {
                    Logger.LogWarning("Attempting to restart watcher for task {TaskId}", TaskId);
                    if (_watchConfiguration != null)
                    {
                        await _fileWatcherService.StartWatchingAsync(TaskId, _watchConfiguration);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to restart watcher for task {TaskId}", TaskId);
                }
            });
        }
        
        Logger.LogDebug("Health check for task {TaskId}: Healthy={IsHealthy}, Issues={Issues}", 
            TaskId, health.IsHealthy, string.Join(", ", health.Issues));
    }

    private async void OnFileChangeDetected(object? sender, FileChangeEventArgs e)
    {
        if (e.TaskId != TaskId)
            return;

        var executionId = Guid.NewGuid().ToString();
        
        try
        {
            await ExecuteWithSupervisionAsync(executionId, async (ct) =>
            {
                Logger.LogInformation("Processing file change for task {TaskId}: {FilePath}", TaskId, e.FilePath);
                
                // Process the file change
                await _fileEventProcessor.ProcessFileChangeAsync(e, ct);
                
                return true;
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing file change for task {TaskId}: {FilePath}", 
                TaskId, e.FilePath);
        }
    }

    private void OnWatcherError(object? sender, WatcherErrorEventArgs e)
    {
        if (e.TaskId != TaskId)
            return;

        Logger.LogError(e.Exception, "Watcher error for task {TaskId}. Watcher stopped: {WatcherStopped}", 
            TaskId, e.WatcherStopped);

        if (e.WatcherStopped)
        {
            // Change state to degraded if watcher stopped unexpectedly
            TryChangeState(TaskState.Degraded, "File watcher stopped unexpectedly");
        }
    }

    protected override Dictionary<string, object> GetMetrics()
    {
        var metrics = base.GetMetrics();
        
        // Add file watcher specific metrics
        if (_fileEventProcessor != null)
        {
            var stats = _fileEventProcessor.GetStatistics();
            metrics["EventsQueued"] = stats.EventsQueued;
            metrics["EventsProcessing"] = stats.EventsProcessing;
            metrics["EventsProcessed"] = stats.EventsProcessed;
            metrics["EventsFailed"] = stats.EventsFailed;
            metrics["SuccessRate"] = stats.SuccessRate;
            metrics["AverageProcessingTime"] = stats.AverageProcessingTime;
        }
        
        return metrics;
    }
}