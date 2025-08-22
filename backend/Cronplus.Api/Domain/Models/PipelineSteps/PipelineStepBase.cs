using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.Results;

namespace Cronplus.Api.Domain.Models.PipelineSteps;

/// <summary>
/// Base class for all pipeline steps
/// </summary>
public abstract class PipelineStepBase
{
    public int Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public int StepOrder { get; set; }
    
    [JsonIgnore]
    public abstract string StepType { get; }
    
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    
    // Retry configuration
    public RetryPolicy? RetryPolicy { get; set; }
    
    // Conditional execution
    public StepCondition? Condition { get; set; }
    
    // Timeout configuration
    public virtual int? TimeoutSeconds { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Execute the step with the given context
    /// </summary>
    public abstract Task<StepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validate the step configuration
    /// </summary>
    public abstract FluentValidation.Results.ValidationResult Validate();
    
    /// <summary>
    /// Convert to JSON for storage
    /// </summary>
    public virtual JsonDocument ToJsonDocument()
    {
        var json = JsonSerializer.Serialize(this, GetType());
        return JsonDocument.Parse(json);
    }
}

/// <summary>
/// Retry policy configuration
/// </summary>
public class RetryPolicy
{
    public int MaxAttempts { get; set; } = 3;
    public int BackoffMilliseconds { get; set; } = 1000;
    public RetryBackoffType BackoffType { get; set; } = RetryBackoffType.Linear;
    public double BackoffMultiplier { get; set; } = 2.0;
    public int? MaxBackoffMilliseconds { get; set; } = 30000;
}

/// <summary>
/// Retry backoff types
/// </summary>
public enum RetryBackoffType
{
    Linear,
    Exponential,
    Constant
}

/// <summary>
/// Step execution condition
/// </summary>
public class StepCondition
{
    public string Expression { get; set; } = string.Empty;
    public ConditionAction OnTrue { get; set; } = ConditionAction.Continue;
    public ConditionAction OnFalse { get; set; } = ConditionAction.Skip;
    
    /// <summary>
    /// Evaluate the condition against the context
    /// </summary>
    public bool Evaluate(ExecutionContext context)
    {
        // TODO: Implement expression evaluation
        // For now, return true
        return true;
    }
}

/// <summary>
/// Actions to take based on condition result
/// </summary>
public enum ConditionAction
{
    Continue,  // Continue to next step
    Skip,      // Skip this step
    Stop,      // Stop pipeline execution
    Fail       // Fail the pipeline
}

/// <summary>
/// Result of a step execution
/// </summary>
public class StepResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, object>? OutputVariables { get; set; }
    public Exception? Exception { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    
    public static StepResult SuccessResult(string? message = null, Dictionary<string, object>? outputs = null)
    {
        return new StepResult
        {
            Success = true,
            Message = message,
            OutputVariables = outputs
        };
    }
    
    public static StepResult FailureResult(string message, Exception? exception = null)
    {
        return new StepResult
        {
            Success = false,
            Message = message,
            Exception = exception
        };
    }
}

/// <summary>
/// Execution context passed through the pipeline
/// </summary>
public class ExecutionContext
{
    public string TaskId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string FileDirectory => Path.GetDirectoryName(FilePath) ?? string.Empty;
    public string FileExtension => Path.GetExtension(FilePath);
    public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FilePath);
    
    public Dictionary<string, object> Variables { get; set; } = new();
    public List<StepExecutionLog> ExecutionLog { get; set; } = new();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    
    public ILogger? Logger { get; set; }
    
    /// <summary>
    /// Get a variable value with type conversion
    /// </summary>
    public T? GetVariable<T>(string name, T? defaultValue = default)
    {
        if (Variables.TryGetValue(name, out var value))
        {
            try
            {
                if (value is T typedValue)
                    return typedValue;
                
                if (value is JsonElement jsonElement)
                {
                    var json = jsonElement.GetRawText();
                    return JsonSerializer.Deserialize<T>(json);
                }
                
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        
        return defaultValue;
    }
    
    /// <summary>
    /// Set a variable value
    /// </summary>
    public void SetVariable(string name, object value)
    {
        Variables[name] = value;
    }
    
    /// <summary>
    /// Log step execution
    /// </summary>
    public void LogStepExecution(string stepName, StepResult result)
    {
        ExecutionLog.Add(new StepExecutionLog
        {
            StepName = stepName,
            Timestamp = DateTime.UtcNow,
            Success = result.Success,
            Message = result.Message,
            ExecutionTime = result.ExecutionTime
        });
    }
}

/// <summary>
/// Step execution log entry
/// </summary>
public class StepExecutionLog
{
    public string StepName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public TimeSpan ExecutionTime { get; set; }
}