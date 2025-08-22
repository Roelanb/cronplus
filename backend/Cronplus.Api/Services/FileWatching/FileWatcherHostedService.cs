using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Infrastructure.Database;

namespace Cronplus.Api.Services.FileWatching;

/// <summary>
/// Hosted service that manages file watchers for all enabled tasks
/// </summary>
public class FileWatcherHostedService : IHostedService, IDisposable
{
    private readonly ILogger<FileWatcherHostedService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IFileWatcherService _fileWatcherService;
    private readonly IFileEventProcessor _fileEventProcessor;
    private Timer? _refreshTimer;
    private bool _disposed;

    public FileWatcherHostedService(
        ILogger<FileWatcherHostedService> logger,
        IServiceProvider serviceProvider,
        IFileWatcherService fileWatcherService,
        IFileEventProcessor fileEventProcessor)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _fileWatcherService = fileWatcherService;
        _fileEventProcessor = fileEventProcessor;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting file watcher hosted service");
        
        // Subscribe to file change events
        _fileWatcherService.FileChangeDetected += OnFileChangeDetected;
        _fileWatcherService.WatcherError += OnWatcherError;
        
        // Load and start watchers for all enabled tasks
        await LoadWatchersAsync(cancellationToken);
        
        // Set up periodic refresh to check for new/updated tasks
        _refreshTimer = new Timer(
            async _ => await RefreshWatchersAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5));
        
        _logger.LogInformation("File watcher hosted service started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping file watcher hosted service");
        
        _refreshTimer?.Change(Timeout.Infinite, 0);
        
        // Unsubscribe from events
        _fileWatcherService.FileChangeDetected -= OnFileChangeDetected;
        _fileWatcherService.WatcherError -= OnWatcherError;
        
        // Stop all watchers
        await _fileWatcherService.StopAllAsync(cancellationToken);
        
        _logger.LogInformation("File watcher hosted service stopped");
    }

    private async Task LoadWatchersAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            
            // Get all enabled tasks
            var tasks = await unitOfWork.Tasks.GetAllAsync();
            var enabledTasks = tasks.Where(t => t.Enabled).ToList();
            
            _logger.LogInformation("Loading watchers for {Count} enabled tasks", enabledTasks.Count);
            
            foreach (var task in enabledTasks)
            {
                try
                {
                    var watchConfig = new Domain.Models.WatchConfiguration(
                        task.WatchDirectory,
                        task.GlobPattern,
                        includeSubdirectories: false, // Could be made configurable
                        task.DebounceMs,
                        task.StabilizationMs);
                    
                    await _fileWatcherService.StartWatchingAsync(task.Id, watchConfig, cancellationToken);
                    
                    _logger.LogInformation("Started watcher for task {TaskId}: {Directory}/{Pattern}", 
                        task.Id, task.WatchDirectory, task.GlobPattern);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start watcher for task {TaskId}", task.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading watchers");
        }
    }

    private async Task RefreshWatchersAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            
            // Get all tasks
            var tasks = await unitOfWork.Tasks.GetAllAsync();
            var activeWatchers = _fileWatcherService.GetActiveWatchers().ToHashSet();
            
            // Start watchers for newly enabled tasks
            foreach (var task in tasks.Where(t => t.Enabled))
            {
                if (!activeWatchers.Contains(task.Id))
                {
                    try
                    {
                        var watchConfig = new Domain.Models.WatchConfiguration(
                            task.WatchDirectory,
                            task.GlobPattern,
                            includeSubdirectories: false,
                            task.DebounceMs,
                            task.StabilizationMs);
                        
                        await _fileWatcherService.StartWatchingAsync(task.Id, watchConfig);
                        
                        _logger.LogInformation("Started watcher for newly enabled task {TaskId}", task.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to start watcher for task {TaskId}", task.Id);
                    }
                }
            }
            
            // Stop watchers for disabled tasks
            var currentTaskIds = tasks.Select(t => t.Id).ToHashSet();
            foreach (var watcherId in activeWatchers)
            {
                var task = tasks.FirstOrDefault(t => t.Id == watcherId);
                
                if (task == null || !task.Enabled)
                {
                    await _fileWatcherService.StopWatchingAsync(watcherId);
                    _logger.LogInformation("Stopped watcher for task {TaskId} (task {Status})", 
                        watcherId, task == null ? "deleted" : "disabled");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing watchers");
        }
    }

    private async void OnFileChangeDetected(object? sender, FileChangeEventArgs e)
    {
        try
        {
            await _fileEventProcessor.ProcessFileChangeAsync(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file change event for {FilePath} in task {TaskId}", 
                e.FilePath, e.TaskId);
        }
    }

    private void OnWatcherError(object? sender, WatcherErrorEventArgs e)
    {
        _logger.LogError(e.Exception, "Watcher error for task {TaskId}. Watcher stopped: {WatcherStopped}", 
            e.TaskId, e.WatcherStopped);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        
        _refreshTimer?.Dispose();
        
        if (_fileWatcherService is IDisposable disposableWatcher)
        {
            disposableWatcher.Dispose();
        }
        
        if (_fileEventProcessor is IDisposable disposableProcessor)
        {
            disposableProcessor.Dispose();
        }
    }
}