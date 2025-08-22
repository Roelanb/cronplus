namespace Cronplus.Api.Services.TaskSupervision;

/// <summary>
/// Represents the current state of a supervised task
/// </summary>
public enum TaskState
{
    /// <summary>
    /// Task is created but not yet started
    /// </summary>
    Created,
    
    /// <summary>
    /// Task is initializing (loading configuration, setting up watchers)
    /// </summary>
    Initializing,
    
    /// <summary>
    /// Task is ready and waiting for events
    /// </summary>
    Idle,
    
    /// <summary>
    /// Task is actively processing one or more files
    /// </summary>
    Processing,
    
    /// <summary>
    /// Task is paused temporarily (manual or due to errors)
    /// </summary>
    Paused,
    
    /// <summary>
    /// Task is in the process of stopping
    /// </summary>
    Stopping,
    
    /// <summary>
    /// Task has stopped normally
    /// </summary>
    Stopped,
    
    /// <summary>
    /// Task has failed and cannot continue
    /// </summary>
    Failed,
    
    /// <summary>
    /// Task is in an unhealthy state but still running
    /// </summary>
    Degraded
}

/// <summary>
/// Valid state transitions
/// </summary>
public static class TaskStateTransitions
{
    private static readonly Dictionary<TaskState, HashSet<TaskState>> ValidTransitions = new()
    {
        [TaskState.Created] = new() { TaskState.Initializing, TaskState.Stopped },
        [TaskState.Initializing] = new() { TaskState.Idle, TaskState.Failed, TaskState.Stopped },
        [TaskState.Idle] = new() { TaskState.Processing, TaskState.Paused, TaskState.Stopping, TaskState.Degraded },
        [TaskState.Processing] = new() { TaskState.Idle, TaskState.Paused, TaskState.Stopping, TaskState.Failed, TaskState.Degraded },
        [TaskState.Paused] = new() { TaskState.Idle, TaskState.Stopping },
        [TaskState.Stopping] = new() { TaskState.Stopped, TaskState.Failed },
        [TaskState.Stopped] = new() { TaskState.Initializing },
        [TaskState.Failed] = new() { TaskState.Initializing },
        [TaskState.Degraded] = new() { TaskState.Idle, TaskState.Failed, TaskState.Stopping }
    };
    
    /// <summary>
    /// Check if a state transition is valid
    /// </summary>
    public static bool IsValidTransition(TaskState from, TaskState to)
    {
        return ValidTransitions.TryGetValue(from, out var validStates) && validStates.Contains(to);
    }
    
    /// <summary>
    /// Get valid next states from current state
    /// </summary>
    public static IEnumerable<TaskState> GetValidNextStates(TaskState currentState)
    {
        return ValidTransitions.TryGetValue(currentState, out var validStates) 
            ? validStates 
            : Enumerable.Empty<TaskState>();
    }
}

/// <summary>
/// Task state change event
/// </summary>
public class TaskStateChangedEventArgs : EventArgs
{
    public string TaskId { get; set; } = string.Empty;
    public TaskState PreviousState { get; set; }
    public TaskState NewState { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Task health status
/// </summary>
public class TaskHealthStatus
{
    public string TaskId { get; set; } = string.Empty;
    public TaskState State { get; set; }
    public bool IsHealthy { get; set; }
    public DateTime LastHealthCheck { get; set; } = DateTime.UtcNow;
    public DateTime? LastActivity { get; set; }
    public long ProcessedCount { get; set; }
    public long ErrorCount { get; set; }
    public long ConsecutiveErrors { get; set; }
    public TimeSpan Uptime { get; set; }
    public double ProcessingRate { get; set; } // Files per minute
    public Dictionary<string, object> Metrics { get; set; } = new();
    public List<string> Issues { get; set; } = new();
}