using FluentValidation;
using FluentValidation.Results;

namespace Cronplus.Api.Domain.Models.PipelineSteps;

/// <summary>
/// Pipeline step for printing/logging information
/// </summary>
public class PrintStep : PipelineStepBase
{
    public override string StepType => "print";
    
    public string Message { get; set; } = string.Empty;
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
    public bool IncludeFileInfo { get; set; } = true;
    public bool IncludeVariables { get; set; } = false;
    public bool IncludeTimestamp { get; set; } = true;
    public string? OutputVariable { get; set; } // Store the message in a variable
    
    public override Task<StepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // Resolve variables in the message
            var resolvedMessage = ResolveVariables(Message, context);
            
            // Build the full message
            var fullMessage = BuildFullMessage(resolvedMessage, context);
            
            // Log the message at the appropriate level
            LogMessage(context.Logger, fullMessage);
            
            // Store in output variable if specified
            var outputs = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(OutputVariable))
            {
                context.SetVariable(OutputVariable, fullMessage);
                outputs[OutputVariable] = fullMessage;
            }
            
            stopwatch.Stop();
            
            var result = StepResult.SuccessResult($"Message printed: {resolvedMessage}", outputs);
            result.ExecutionTime = stopwatch.Elapsed;
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            context.Logger?.LogError(ex, "Failed to execute print step: {Name}", Name);
            var result = StepResult.FailureResult($"Print failed: {ex.Message}", ex);
            result.ExecutionTime = stopwatch.Elapsed;
            return Task.FromResult(result);
        }
    }
    
    public override ValidationResult Validate()
    {
        var validator = new PrintStepValidator();
        return validator.Validate(this);
    }
    
    private string BuildFullMessage(string message, ExecutionContext context)
    {
        var parts = new List<string>();
        
        if (IncludeTimestamp)
        {
            parts.Add($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}]");
        }
        
        parts.Add($"[{Name}]");
        parts.Add(message);
        
        if (IncludeFileInfo)
        {
            parts.Add($"| File: {context.FilePath}");
            
            if (File.Exists(context.FilePath))
            {
                var fileInfo = new FileInfo(context.FilePath);
                parts.Add($"| Size: {FormatFileSize(fileInfo.Length)}");
                parts.Add($"| Modified: {fileInfo.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}");
            }
        }
        
        if (IncludeVariables && context.Variables.Any())
        {
            var varList = string.Join(", ", context.Variables.Select(kv => $"{kv.Key}={kv.Value}"));
            parts.Add($"| Variables: {varList}");
        }
        
        return string.Join(" ", parts);
    }
    
    private void LogMessage(ILogger? logger, string message)
    {
        if (logger == null)
        {
            Console.WriteLine(message);
            return;
        }
        
        switch (LogLevel)
        {
            case LogLevel.Trace:
                logger.LogTrace(message);
                break;
            case LogLevel.Debug:
                logger.LogDebug(message);
                break;
            case LogLevel.Information:
                logger.LogInformation(message);
                break;
            case LogLevel.Warning:
                logger.LogWarning(message);
                break;
            case LogLevel.Error:
                logger.LogError(message);
                break;
            case LogLevel.Critical:
                logger.LogCritical(message);
                break;
            default:
                logger.LogInformation(message);
                break;
        }
    }
    
    private string ResolveVariables(string text, ExecutionContext context)
    {
        var result = text;
        
        // Replace built-in variables
        result = result.Replace("{fileName}", context.FileName);
        result = result.Replace("{fileNameWithoutExt}", context.FileNameWithoutExtension);
        result = result.Replace("{fileExt}", context.FileExtension);
        result = result.Replace("{fileDir}", context.FileDirectory);
        result = result.Replace("{filePath}", context.FilePath);
        result = result.Replace("{taskId}", context.TaskId);
        result = result.Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"));
        result = result.Replace("{time}", DateTime.Now.ToString("HH:mm:ss"));
        result = result.Replace("{datetime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        result = result.Replace("{utcNow}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        
        // Replace custom variables
        foreach (var variable in context.Variables)
        {
            result = result.Replace($"{{{variable.Key}}}", variable.Value?.ToString() ?? string.Empty);
        }
        
        // Replace execution statistics
        var executionDuration = DateTime.UtcNow - context.StartedAt;
        result = result.Replace("{executionDuration}", executionDuration.ToString(@"hh\:mm\:ss\.fff"));
        result = result.Replace("{executionSteps}", context.ExecutionLog.Count.ToString());
        result = result.Replace("{successfulSteps}", context.ExecutionLog.Count(l => l.Success).ToString());
        result = result.Replace("{failedSteps}", context.ExecutionLog.Count(l => !l.Success).ToString());
        
        return result;
    }
    
    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Validator for PrintStep
/// </summary>
public class PrintStepValidator : AbstractValidator<PrintStep>
{
    public PrintStepValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Step name is required");
        
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message is required")
            .MaximumLength(5000).WithMessage("Message must not exceed 5000 characters");
        
        RuleFor(x => x.LogLevel)
            .IsInEnum().WithMessage("Invalid log level");
        
        RuleFor(x => x.OutputVariable)
            .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]*$")
            .When(x => !string.IsNullOrEmpty(x.OutputVariable))
            .WithMessage("Output variable name must be a valid identifier");
    }
}