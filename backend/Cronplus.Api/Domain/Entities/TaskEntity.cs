using System.Text.Json;

namespace Cronplus.Api.Domain.Entities;

public class TaskEntity
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string WatchDirectory { get; set; } = string.Empty;
    public string GlobPattern { get; set; } = "*";
    public int DebounceMs { get; set; } = 500;
    public int StabilizationMs { get; set; } = 1000;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? Description { get; set; }
    
    // Navigation properties
    public virtual ICollection<PipelineStep> PipelineSteps { get; set; } = new List<PipelineStep>();
    public virtual ICollection<TaskVariable> Variables { get; set; } = new List<TaskVariable>();
    public virtual ICollection<ExecutionLog> ExecutionLogs { get; set; } = new List<ExecutionLog>();
}

public class PipelineStep
{
    public int Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public int StepOrder { get; set; }
    public string Type { get; set; } = string.Empty; // copy, delete, archive, print, rest, decision
    public JsonDocument? Configuration { get; set; } // JSON configuration for the step
    public int? RetryMax { get; set; }
    public int? RetryBackoffMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public virtual TaskEntity Task { get; set; } = null!;
    public virtual StepCondition? Condition { get; set; }
}

public class StepCondition
{
    public int Id { get; set; }
    public int StepId { get; set; }
    public string Expression { get; set; } = string.Empty;
    public string TrueAction { get; set; } = "continue"; // continue, skip, stop
    public string FalseAction { get; set; } = "continue";
    
    // Navigation property
    public virtual PipelineStep Step { get; set; } = null!;
}

public class TaskVariable
{
    public int Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string"; // string, int, bool, date, datetime
    public string Value { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public virtual TaskEntity Task { get; set; } = null!;
}

public class ExecutionLog
{
    public long Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // started, completed, failed, skipped
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public JsonDocument? ExecutionDetails { get; set; } // JSON details of the execution
    
    // Navigation property
    public virtual TaskEntity Task { get; set; } = null!;
}