using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Cronplus.Api.Services.TaskSupervision;
using Cronplus.Api.Domain.Entities;
using System.Collections.Concurrent;

namespace Cronplus.Api.Hubs;

/// <summary>
/// SignalR hub for real-time task execution updates
/// </summary>
[Authorize]
public class TaskExecutionHub : Hub
{
    private readonly ITaskSupervisorManager _taskSupervisorManager;
    private readonly ILogger<TaskExecutionHub> _logger;
    private static readonly ConcurrentDictionary<string, HashSet<string>> _userConnections = new();

    public TaskExecutionHub(
        ITaskSupervisorManager taskSupervisorManager,
        ILogger<TaskExecutionHub> logger)
    {
        _taskSupervisorManager = taskSupervisorManager;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier ?? "anonymous";
        
        _userConnections.AddOrUpdate(userId,
            new HashSet<string> { Context.ConnectionId },
            (key, set) =>
            {
                set.Add(Context.ConnectionId);
                return set;
            });

        _logger.LogInformation("User {UserId} connected with connection {ConnectionId}", 
            userId, Context.ConnectionId);

        // Send current task states to the newly connected client
        var taskStates = await GetAllTaskStatesAsync();
        await Clients.Caller.SendAsync("InitialTaskStates", taskStates);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier ?? "anonymous";
        
        if (_userConnections.TryGetValue(userId, out var connections))
        {
            connections.Remove(Context.ConnectionId);
            if (connections.Count == 0)
            {
                _userConnections.TryRemove(userId, out _);
            }
        }

        _logger.LogInformation("User {UserId} disconnected connection {ConnectionId}", 
            userId, Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to specific task updates
    /// </summary>
    public async Task SubscribeToTask(string taskId)
    {
        var groupName = GetTaskGroup(taskId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogDebug("Connection {ConnectionId} subscribed to task {TaskId}", 
            Context.ConnectionId, taskId);

        // Send current state of the subscribed task
        var taskState = await GetTaskStateAsync(taskId);
        if (taskState != null)
        {
            await Clients.Caller.SendAsync("TaskStateUpdate", taskState);
        }
    }

    /// <summary>
    /// Unsubscribe from task updates
    /// </summary>
    public async Task UnsubscribeFromTask(string taskId)
    {
        var groupName = GetTaskGroup(taskId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogDebug("Connection {ConnectionId} unsubscribed from task {TaskId}", 
            Context.ConnectionId, taskId);
    }

    /// <summary>
    /// Subscribe to all task updates
    /// </summary>
    public async Task SubscribeToAllTasks()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "AllTasks");
        _logger.LogDebug("Connection {ConnectionId} subscribed to all tasks", Context.ConnectionId);
    }

    /// <summary>
    /// Unsubscribe from all task updates
    /// </summary>
    public async Task UnsubscribeFromAllTasks()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "AllTasks");
        _logger.LogDebug("Connection {ConnectionId} unsubscribed from all tasks", Context.ConnectionId);
    }

    /// <summary>
    /// Request current state of a specific task
    /// </summary>
    public async Task<TaskStateDto?> GetTaskState(string taskId)
    {
        return await GetTaskStateAsync(taskId);
    }

    /// <summary>
    /// Request current states of all tasks
    /// </summary>
    public async Task<IEnumerable<TaskStateDto>> GetAllTaskStates()
    {
        return await GetAllTaskStatesAsync();
    }

    /// <summary>
    /// Start a task execution (requires permission)
    /// </summary>
    [Authorize(Policy = "RequireTaskWritePermission")]
    public async Task<bool> StartTask(string taskId)
    {
        try
        {
            await _taskSupervisorManager.StartTaskAsync(taskId);
            _logger.LogInformation("Task {TaskId} started by user {UserId}", 
                taskId, Context.UserIdentifier);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start task {TaskId}", taskId);
            await Clients.Caller.SendAsync("Error", new { 
                Message = $"Failed to start task: {ex.Message}" 
            });
            return false;
        }
    }

    /// <summary>
    /// Stop a task execution (requires permission)
    /// </summary>
    [Authorize(Policy = "RequireTaskWritePermission")]
    public async Task<bool> StopTask(string taskId)
    {
        try
        {
            await _taskSupervisorManager.StopTaskAsync(taskId);
            _logger.LogInformation("Task {TaskId} stopped by user {UserId}", 
                taskId, Context.UserIdentifier);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop task {TaskId}", taskId);
            await Clients.Caller.SendAsync("Error", new { 
                Message = $"Failed to stop task: {ex.Message}" 
            });
            return false;
        }
    }

    // Helper methods
    private static string GetTaskGroup(string taskId) => $"Task_{taskId}";

    private async Task<TaskStateDto?> GetTaskStateAsync(string taskId)
    {
        var info = _taskSupervisorManager.GetSupervisorInfo(taskId);
        if (info == null) return null;

        return new TaskStateDto
        {
            TaskId = taskId,
            IsRunning = info.State == TaskState.Processing || info.State == TaskState.Idle,
            LastExecutionTime = info.LastFileProcessed,
            ExecutionCount = info.ProcessedFiles,
            Status = info.State.ToString(),
            CurrentFile = null, // Not available in TaskSupervisorInfo
            ErrorCount = info.FailedFiles,
            LastError = info.LastError
        };
    }

    private async Task<IEnumerable<TaskStateDto>> GetAllTaskStatesAsync()
    {
        var tasks = _taskSupervisorManager.GetAllTaskStatuses();
        return tasks.Select(t => new TaskStateDto
        {
            TaskId = t.TaskId,
            IsRunning = t.State == TaskState.Processing || t.State == TaskState.Idle,
            LastExecutionTime = t.LastActivity,
            ExecutionCount = (int)t.ProcessedCount,
            Status = t.State.ToString(),
            CurrentFile = null, // Not available in TaskHealthStatus
            ErrorCount = (int)t.ErrorCount,
            LastError = null // Not available in TaskHealthStatus
        });
    }
}

/// <summary>
/// DTO for task state updates
/// </summary>
public class TaskStateDto
{
    public string TaskId { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public DateTime? LastExecutionTime { get; set; }
    public long ExecutionCount { get; set; }
    public string Status { get; set; } = "Idle";
    public string? CurrentFile { get; set; }
    public int ErrorCount { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Service to broadcast task updates to SignalR clients
/// </summary>
public interface ITaskExecutionNotifier
{
    Task NotifyTaskStarted(string taskId);
    Task NotifyTaskStopped(string taskId);
    Task NotifyTaskStateChanged(string taskId, TaskStateDto state);
    Task NotifyTaskProgress(string taskId, int current, int total, string message);
    Task NotifyTaskError(string taskId, string error);
    Task NotifyFileProcessed(string taskId, string filePath, bool success);
}

public class TaskExecutionNotifier : ITaskExecutionNotifier
{
    private readonly IHubContext<TaskExecutionHub> _hubContext;
    private readonly ILogger<TaskExecutionNotifier> _logger;

    public TaskExecutionNotifier(
        IHubContext<TaskExecutionHub> hubContext,
        ILogger<TaskExecutionNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyTaskStarted(string taskId)
    {
        _logger.LogDebug("Notifying task {TaskId} started", taskId);
        
        await _hubContext.Clients
            .Group(GetTaskGroup(taskId))
            .SendAsync("TaskStarted", new { TaskId = taskId, Timestamp = DateTime.UtcNow });
        
        await _hubContext.Clients
            .Group("AllTasks")
            .SendAsync("TaskStarted", new { TaskId = taskId, Timestamp = DateTime.UtcNow });
    }

    public async Task NotifyTaskStopped(string taskId)
    {
        _logger.LogDebug("Notifying task {TaskId} stopped", taskId);
        
        await _hubContext.Clients
            .Group(GetTaskGroup(taskId))
            .SendAsync("TaskStopped", new { TaskId = taskId, Timestamp = DateTime.UtcNow });
        
        await _hubContext.Clients
            .Group("AllTasks")
            .SendAsync("TaskStopped", new { TaskId = taskId, Timestamp = DateTime.UtcNow });
    }

    public async Task NotifyTaskStateChanged(string taskId, TaskStateDto state)
    {
        _logger.LogDebug("Notifying task {TaskId} state changed", taskId);
        
        await _hubContext.Clients
            .Group(GetTaskGroup(taskId))
            .SendAsync("TaskStateUpdate", state);
        
        await _hubContext.Clients
            .Group("AllTasks")
            .SendAsync("TaskStateUpdate", state);
    }

    public async Task NotifyTaskProgress(string taskId, int current, int total, string message)
    {
        _logger.LogDebug("Notifying task {TaskId} progress: {Current}/{Total}", 
            taskId, current, total);
        
        var progress = new
        {
            TaskId = taskId,
            Current = current,
            Total = total,
            Percentage = total > 0 ? (current * 100.0 / total) : 0,
            Message = message,
            Timestamp = DateTime.UtcNow
        };

        await _hubContext.Clients
            .Group(GetTaskGroup(taskId))
            .SendAsync("TaskProgress", progress);
    }

    public async Task NotifyTaskError(string taskId, string error)
    {
        _logger.LogWarning("Notifying task {TaskId} error: {Error}", taskId, error);
        
        var errorInfo = new
        {
            TaskId = taskId,
            Error = error,
            Timestamp = DateTime.UtcNow
        };

        await _hubContext.Clients
            .Group(GetTaskGroup(taskId))
            .SendAsync("TaskError", errorInfo);
        
        await _hubContext.Clients
            .Group("AllTasks")
            .SendAsync("TaskError", errorInfo);
    }

    public async Task NotifyFileProcessed(string taskId, string filePath, bool success)
    {
        _logger.LogDebug("Notifying file processed for task {TaskId}: {FilePath} (Success: {Success})", 
            taskId, filePath, success);
        
        var fileInfo = new
        {
            TaskId = taskId,
            FilePath = filePath,
            Success = success,
            Timestamp = DateTime.UtcNow
        };

        await _hubContext.Clients
            .Group(GetTaskGroup(taskId))
            .SendAsync("FileProcessed", fileInfo);
    }

    private static string GetTaskGroup(string taskId) => $"Task_{taskId}";
}