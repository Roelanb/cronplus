using System.Collections.Concurrent;
using System.Threading.Channels;
using Cronplus.Api.Domain.Models;
using Microsoft.Extensions.Options;

namespace Cronplus.Api.Services.FileWatching;

/// <summary>
/// File system watcher service implementation
/// </summary>
public class FileWatcherService : IFileWatcherService, IDisposable
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly FileWatcherOptions _options;
    private readonly ConcurrentDictionary<string, WatcherContext> _watchers = new();
    private readonly SemaphoreSlim _semaphore;
    private bool _disposed;

    public event EventHandler<FileChangeEventArgs>? FileChangeDetected;
    public event EventHandler<WatcherErrorEventArgs>? WatcherError;

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IServiceProvider serviceProvider,
        IOptions<FileWatcherOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _semaphore = new SemaphoreSlim(_options.MaxConcurrentWatchers);
    }

    public async Task<string> StartWatchingAsync(string taskId, WatchConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileWatcherService));

        if (_watchers.ContainsKey(taskId))
        {
            _logger.LogWarning("Task {TaskId} is already being watched", taskId);
            return taskId;
        }

        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            var context = new WatcherContext(taskId, configuration, _logger);
            
            if (!_watchers.TryAdd(taskId, context))
            {
                _semaphore.Release();
                throw new InvalidOperationException($"Failed to add watcher for task {taskId}");
            }

            // Start the watcher
            await StartWatcherAsync(context, cancellationToken);
            
            _logger.LogInformation("Started watching task {TaskId} on directory {Directory}", 
                taskId, configuration.Directory);
            
            return taskId;
        }
        catch (Exception ex)
        {
            _semaphore.Release();
            _logger.LogError(ex, "Failed to start watcher for task {TaskId}", taskId);
            
            // Clean up if failed
            if (_watchers.TryRemove(taskId, out var context))
            {
                context.Dispose();
            }
            
            throw;
        }
    }

    private async Task StartWatcherAsync(WatcherContext context, CancellationToken cancellationToken)
    {
        // Create FileSystemWatcher
        var watcher = new FileSystemWatcher(context.Configuration.Directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = context.Configuration.IncludeSubdirectories,
            EnableRaisingEvents = false
        };

        // Set filter if not watching all files
        if (context.Configuration.GlobPattern != "*" && !context.Configuration.GlobPattern.Contains("*"))
        {
            watcher.Filter = context.Configuration.GlobPattern;
        }

        context.Watcher = watcher;

        // Wire up event handlers
        watcher.Created += (sender, e) => HandleFileEvent(context, e.FullPath, FileChangeType.Created);
        watcher.Changed += (sender, e) => HandleFileEvent(context, e.FullPath, FileChangeType.Modified);
        watcher.Deleted += (sender, e) => HandleFileEvent(context, e.FullPath, FileChangeType.Deleted);
        watcher.Renamed += (sender, e) => HandleRenamedEvent(context, e);
        watcher.Error += (sender, e) => HandleError(context, e.GetException());

        // Start processing queue
        context.ProcessingTask = ProcessFileEventsAsync(context);

        // Enable the watcher
        watcher.EnableRaisingEvents = true;
        
        await Task.CompletedTask;
    }

    private void HandleFileEvent(WatcherContext context, string filePath, FileChangeType changeType)
    {
        try
        {
            // Check if file matches configuration
            if (!context.Configuration.IsFileMatch(filePath))
            {
                _logger.LogDebug("File {FilePath} doesn't match watch configuration for task {TaskId}", 
                    filePath, context.TaskId);
                return;
            }

            // Check change type filter
            var fileChangeTypes = ConvertToFileChangeTypes(changeType);
            if (!context.Configuration.ShouldWatchChangeType(fileChangeTypes))
            {
                _logger.LogDebug("Change type {ChangeType} is not watched for task {TaskId}", 
                    changeType, context.TaskId);
                return;
            }

            var fileEvent = new FileEvent
            {
                FilePath = filePath,
                ChangeType = changeType,
                DetectedAt = DateTime.UtcNow,
                TaskId = context.TaskId
            };

            // Add to debounce dictionary
            context.DebouncedEvents.AddOrUpdate(filePath, fileEvent, (key, existing) =>
            {
                existing.ChangeType = changeType;
                existing.DetectedAt = DateTime.UtcNow;
                return existing;
            });

            _logger.LogDebug("File event detected: {ChangeType} for {FilePath} in task {TaskId}", 
                changeType, filePath, context.TaskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file event for task {TaskId}", context.TaskId);
        }
    }

    private void HandleRenamedEvent(WatcherContext context, RenamedEventArgs e)
    {
        try
        {
            if (!context.Configuration.IsFileMatch(e.FullPath))
                return;

            var fileEvent = new FileEvent
            {
                FilePath = e.FullPath,
                OldPath = e.OldFullPath,
                ChangeType = FileChangeType.Renamed,
                DetectedAt = DateTime.UtcNow,
                TaskId = context.TaskId
            };

            context.DebouncedEvents.AddOrUpdate(e.FullPath, fileEvent, (key, existing) =>
            {
                existing.ChangeType = FileChangeType.Renamed;
                existing.OldPath = e.OldFullPath;
                existing.DetectedAt = DateTime.UtcNow;
                return existing;
            });

            _logger.LogDebug("File renamed from {OldPath} to {NewPath} in task {TaskId}", 
                e.OldFullPath, e.FullPath, context.TaskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling rename event for task {TaskId}", context.TaskId);
        }
    }

    private void HandleError(WatcherContext context, Exception exception)
    {
        _logger.LogError(exception, "Watcher error for task {TaskId}", context.TaskId);

        var errorArgs = new WatcherErrorEventArgs
        {
            TaskId = context.TaskId,
            Exception = exception,
            WatcherStopped = !context.Watcher?.EnableRaisingEvents ?? true
        };

        WatcherError?.Invoke(this, errorArgs);

        // Try to restart the watcher if it stopped
        if (errorArgs.WatcherStopped && !context.CancellationTokenSource.Token.IsCancellationRequested)
        {
            Task.Run(async () =>
            {
                await Task.Delay(5000); // Wait before restart
                try
                {
                    if (_watchers.ContainsKey(context.TaskId))
                    {
                        _logger.LogInformation("Attempting to restart watcher for task {TaskId}", context.TaskId);
                        await RestartWatcherAsync(context);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restart watcher for task {TaskId}", context.TaskId);
                }
            });
        }
    }

    private async Task RestartWatcherAsync(WatcherContext context)
    {
        // Dispose old watcher
        context.Watcher?.Dispose();
        
        // Create new watcher
        await StartWatcherAsync(context, context.CancellationTokenSource.Token);
    }

    private async Task ProcessFileEventsAsync(WatcherContext context)
    {
        var cancellationToken = context.CancellationTokenSource.Token;
        var debounceTimer = new System.Timers.Timer(context.Configuration.DebounceMilliseconds);
        var stabilizationChecks = new ConcurrentDictionary<string, StabilizationInfo>();

        debounceTimer.Elapsed += async (sender, e) =>
        {
            try
            {
                await ProcessDebouncedEventsAsync(context, stabilizationChecks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing debounced events for task {TaskId}", context.TaskId);
            }
        };

        debounceTimer.Start();

        try
        {
            // Keep the task running
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        finally
        {
            debounceTimer.Stop();
            debounceTimer.Dispose();
        }
    }

    private async Task ProcessDebouncedEventsAsync(
        WatcherContext context, 
        ConcurrentDictionary<string, StabilizationInfo> stabilizationChecks)
    {
        var now = DateTime.UtcNow;
        var eventsToProcess = new List<FileEvent>();

        // Check debounced events
        foreach (var kvp in context.DebouncedEvents.ToArray())
        {
            var fileEvent = kvp.Value;
            var timeSinceDetection = now - fileEvent.DetectedAt;

            if (timeSinceDetection.TotalMilliseconds >= context.Configuration.DebounceMilliseconds)
            {
                // Remove from debounce dictionary
                context.DebouncedEvents.TryRemove(kvp.Key, out _);

                // Add to stabilization check if file exists
                if (File.Exists(fileEvent.FilePath))
                {
                    var fileInfo = new FileInfo(fileEvent.FilePath);
                    
                    var stabInfo = stabilizationChecks.AddOrUpdate(fileEvent.FilePath,
                        new StabilizationInfo 
                        { 
                            LastSize = fileInfo.Length,
                            LastModified = fileInfo.LastWriteTimeUtc,
                            FirstChecked = now,
                            Event = fileEvent
                        },
                        (key, existing) =>
                        {
                            existing.LastSize = fileInfo.Length;
                            existing.LastModified = fileInfo.LastWriteTimeUtc;
                            return existing;
                        });
                }
                else if (fileEvent.ChangeType == FileChangeType.Deleted)
                {
                    // Deleted files don't need stabilization
                    eventsToProcess.Add(fileEvent);
                }
            }
        }

        // Check stabilization
        foreach (var kvp in stabilizationChecks.ToArray())
        {
            var stabInfo = kvp.Value;
            var timeSinceFirstCheck = now - stabInfo.FirstChecked;

            if (timeSinceFirstCheck.TotalMilliseconds >= context.Configuration.StabilizationMilliseconds)
            {
                try
                {
                    if (File.Exists(kvp.Key))
                    {
                        var fileInfo = new FileInfo(kvp.Key);
                        
                        // Check if file is stable
                        if (fileInfo.Length == stabInfo.LastSize &&
                            fileInfo.LastWriteTimeUtc == stabInfo.LastModified)
                        {
                            // File is stable
                            stabInfo.Event.FileSize = fileInfo.Length;
                            eventsToProcess.Add(stabInfo.Event);
                            stabilizationChecks.TryRemove(kvp.Key, out _);
                        }
                        else
                        {
                            // File still changing, update and continue monitoring
                            stabInfo.LastSize = fileInfo.Length;
                            stabInfo.LastModified = fileInfo.LastWriteTimeUtc;
                            stabInfo.FirstChecked = now; // Reset timer
                        }
                    }
                    else
                    {
                        // File no longer exists
                        stabilizationChecks.TryRemove(kvp.Key, out _);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking file stabilization for {FilePath}", kvp.Key);
                    stabilizationChecks.TryRemove(kvp.Key, out _);
                }
            }
        }

        // Process events
        foreach (var fileEvent in eventsToProcess)
        {
            await ProcessFileEventAsync(context, fileEvent);
        }
    }

    private async Task ProcessFileEventAsync(WatcherContext context, FileEvent fileEvent)
    {
        try
        {
            // Check for file locks before processing
            if (fileEvent.ChangeType != FileChangeType.Deleted && File.Exists(fileEvent.FilePath))
            {
                if (!await WaitForFileAccessAsync(fileEvent.FilePath, context.CancellationTokenSource.Token))
                {
                    _logger.LogWarning("Could not acquire file access for {FilePath}", fileEvent.FilePath);
                    return;
                }
            }

            var args = new FileChangeEventArgs
            {
                TaskId = context.TaskId,
                FilePath = fileEvent.FilePath,
                ChangeType = fileEvent.ChangeType,
                DetectedAt = fileEvent.DetectedAt,
                FileSize = fileEvent.FileSize,
                OldPath = fileEvent.OldPath
            };

            _logger.LogInformation("File change ready for processing: {ChangeType} for {FilePath} in task {TaskId}", 
                fileEvent.ChangeType, fileEvent.FilePath, context.TaskId);

            // Raise event
            FileChangeDetected?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file event for {FilePath} in task {TaskId}", 
                fileEvent.FilePath, context.TaskId);
        }
    }

    private async Task<bool> WaitForFileAccessAsync(string filePath, CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        const int delayMs = 500;

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                if (i < maxAttempts - 1)
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
        }

        return false;
    }

    public async Task StopWatchingAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (_watchers.TryRemove(taskId, out var context))
        {
            _logger.LogInformation("Stopping watcher for task {TaskId}", taskId);
            
            context.CancellationTokenSource.Cancel();
            context.Dispose();
            
            _semaphore.Release();
            
            await Task.CompletedTask;
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping all watchers");
        
        var tasks = _watchers.Keys.Select(taskId => StopWatchingAsync(taskId, cancellationToken));
        await Task.WhenAll(tasks);
    }

    public bool IsWatching(string taskId)
    {
        return _watchers.ContainsKey(taskId);
    }

    public IEnumerable<string> GetActiveWatchers()
    {
        return _watchers.Keys.ToList();
    }

    private FileChangeTypes ConvertToFileChangeTypes(FileChangeType changeType)
    {
        return changeType switch
        {
            FileChangeType.Created => FileChangeTypes.Created,
            FileChangeType.Modified => FileChangeTypes.Changed,
            FileChangeType.Deleted => FileChangeTypes.Deleted,
            FileChangeType.Renamed => FileChangeTypes.Renamed,
            _ => FileChangeTypes.None
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        StopAllAsync().GetAwaiter().GetResult();
        
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }
        
        _watchers.Clear();
        _semaphore.Dispose();
    }

    /// <summary>
    /// Internal watcher context
    /// </summary>
    private class WatcherContext : IDisposable
    {
        public string TaskId { get; }
        public WatchConfiguration Configuration { get; }
        public FileSystemWatcher? Watcher { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; }
        public Task? ProcessingTask { get; set; }
        public ConcurrentDictionary<string, FileEvent> DebouncedEvents { get; }
        private readonly ILogger _logger;

        public WatcherContext(string taskId, WatchConfiguration configuration, ILogger logger)
        {
            TaskId = taskId;
            Configuration = configuration;
            _logger = logger;
            CancellationTokenSource = new CancellationTokenSource();
            DebouncedEvents = new ConcurrentDictionary<string, FileEvent>();
        }

        public void Dispose()
        {
            try
            {
                CancellationTokenSource.Cancel();
                Watcher?.Dispose();
                ProcessingTask?.Wait(TimeSpan.FromSeconds(5));
                CancellationTokenSource.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing watcher context for task {TaskId}", TaskId);
            }
        }
    }

    /// <summary>
    /// Internal file event
    /// </summary>
    private class FileEvent
    {
        public string FilePath { get; set; } = string.Empty;
        public string? OldPath { get; set; }
        public FileChangeType ChangeType { get; set; }
        public DateTime DetectedAt { get; set; }
        public long? FileSize { get; set; }
        public string TaskId { get; set; } = string.Empty;
    }

    /// <summary>
    /// File stabilization tracking
    /// </summary>
    private class StabilizationInfo
    {
        public long LastSize { get; set; }
        public DateTime LastModified { get; set; }
        public DateTime FirstChecked { get; set; }
        public FileEvent Event { get; set; } = null!;
    }
}

/// <summary>
/// File watcher service options
/// </summary>
public class FileWatcherOptions
{
    public int MaxConcurrentWatchers { get; set; } = 100;
    public int DefaultDebounceMs { get; set; } = 500;
    public int DefaultStabilizationMs { get; set; } = 1000;
    public int MaxQueueSize { get; set; } = 10000;
}