using Cronplus.Api.Domain.Models;

namespace Cronplus.Api.Services.FileWatching;

/// <summary>
/// Interface for file system watching service
/// </summary>
public interface IFileWatcherService
{
    /// <summary>
    /// Start watching a directory with the specified configuration
    /// </summary>
    Task<string> StartWatchingAsync(string taskId, WatchConfiguration configuration, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop watching a specific task
    /// </summary>
    Task StopWatchingAsync(string taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop all watchers
    /// </summary>
    Task StopAllAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if a task is currently being watched
    /// </summary>
    bool IsWatching(string taskId);
    
    /// <summary>
    /// Get all active watcher task IDs
    /// </summary>
    IEnumerable<string> GetActiveWatchers();
    
    /// <summary>
    /// Event raised when a file change is detected and ready for processing
    /// </summary>
    event EventHandler<FileChangeEventArgs>? FileChangeDetected;
    
    /// <summary>
    /// Event raised when a watcher encounters an error
    /// </summary>
    event EventHandler<WatcherErrorEventArgs>? WatcherError;
}

/// <summary>
/// File change event arguments
/// </summary>
public class FileChangeEventArgs : EventArgs
{
    public string TaskId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public FileChangeType ChangeType { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public long? FileSize { get; set; }
    public string? OldPath { get; set; } // For rename events
}

/// <summary>
/// File change types
/// </summary>
public enum FileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed
}

/// <summary>
/// Watcher error event arguments
/// </summary>
public class WatcherErrorEventArgs : EventArgs
{
    public string TaskId { get; set; } = string.Empty;
    public Exception Exception { get; set; } = null!;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public bool WatcherStopped { get; set; }
}