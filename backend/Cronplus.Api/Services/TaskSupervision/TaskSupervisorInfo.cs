namespace Cronplus.Api.Services.TaskSupervision;

using TaskSupervisorState = TaskState;

/// <summary>
/// Detailed information about a task supervisor
/// </summary>
public class TaskSupervisorInfo
{
    public string TaskId { get; set; } = string.Empty;
    public TaskSupervisorState State { get; set; }
    public DateTime LastStateChange { get; set; }
    public string? LastError { get; set; }
    public int ProcessedFiles { get; set; }
    public int FailedFiles { get; set; }
    public DateTime? LastFileProcessed { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}