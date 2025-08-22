using System.Text.RegularExpressions;
using System.Linq.Expressions;
using FluentValidation;
using FluentValidation.Results;

namespace Cronplus.Api.Domain.Models.PipelineSteps;

/// <summary>
/// Pipeline step for conditional branching and decision making
/// </summary>
public class DecisionStep : PipelineStepBase
{
    public override string StepType => "decision";
    
    public List<DecisionRule> Rules { get; set; } = new();
    public DecisionAction DefaultAction { get; set; } = DecisionAction.Continue;
    public string? JumpToStepName { get; set; } // For Jump action
    public string? SetVariable { get; set; } // Variable to set based on decision
    public object? SetVariableValue { get; set; } // Value to set
    
    public override Task<StepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            context.Logger?.LogInformation("Executing DecisionStep: {Name}", Name);
            
            DecisionAction actionToTake = DefaultAction;
            DecisionRule? matchedRule = null;
            
            // Evaluate rules in order
            foreach (var rule in Rules.Where(r => r.Enabled))
            {
                if (EvaluateRule(rule, context))
                {
                    matchedRule = rule;
                    actionToTake = rule.Action;
                    
                    context.Logger?.LogDebug("Rule matched: {RuleName} - Action: {Action}", 
                        rule.Name ?? "Unnamed", actionToTake);
                    
                    // Set variable if specified in the rule
                    if (!string.IsNullOrEmpty(rule.SetVariable))
                    {
                        context.SetVariable(rule.SetVariable, rule.SetVariableValue ?? true);
                    }
                    
                    break; // First matching rule wins
                }
            }
            
            // Set default variable if no rule matched and default variable is specified
            if (matchedRule == null && !string.IsNullOrEmpty(SetVariable))
            {
                context.SetVariable(SetVariable, SetVariableValue ?? false);
            }
            
            stopwatch.Stop();
            
            // Build output
            var outputs = new Dictionary<string, object>
            {
                ["DecisionAction"] = actionToTake.ToString(),
                ["MatchedRule"] = matchedRule?.Name ?? "None",
                ["RulesEvaluated"] = Rules.Count
            };
            
            // Handle the action
            string message;
            switch (actionToTake)
            {
                case DecisionAction.Continue:
                    message = "Decision: Continue to next step";
                    break;
                    
                case DecisionAction.Skip:
                    message = "Decision: Skip next step";
                    outputs["SkipNextStep"] = true;
                    break;
                    
                case DecisionAction.Stop:
                    message = "Decision: Stop pipeline execution";
                    outputs["StopPipeline"] = true;
                    break;
                    
                case DecisionAction.Fail:
                    var failResult = StepResult.FailureResult("Decision: Pipeline failed by decision rule");
                    failResult.ExecutionTime = stopwatch.Elapsed;
                    failResult.OutputVariables = outputs;
                    return Task.FromResult(failResult);
                    
                case DecisionAction.Jump:
                    message = $"Decision: Jump to step '{JumpToStepName ?? matchedRule?.JumpToStepName}'";
                    outputs["JumpToStep"] = JumpToStepName ?? matchedRule?.JumpToStepName ?? "";
                    break;
                    
                default:
                    message = "Decision: Unknown action";
                    break;
            }
            
            context.Logger?.LogInformation(message);
            
            var successResult = StepResult.SuccessResult(message, outputs);
            successResult.ExecutionTime = stopwatch.Elapsed;
            return Task.FromResult(successResult);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            context.Logger?.LogError(ex, "Failed to execute decision step: {Name}", Name);
            var failResult = StepResult.FailureResult($"Decision failed: {ex.Message}", ex);
            failResult.ExecutionTime = stopwatch.Elapsed;
            return Task.FromResult(failResult);
        }
    }
    
    public override ValidationResult Validate()
    {
        var validator = new DecisionStepValidator();
        return validator.Validate(this);
    }
    
    private bool EvaluateRule(DecisionRule rule, ExecutionContext context)
    {
        try
        {
            var results = rule.Conditions.Select(c => EvaluateCondition(c, context)).ToList();
            
            bool finalResult = rule.Logic switch
            {
                ConditionLogic.And => results.All(r => r),
                ConditionLogic.Or => results.Any(r => r),
                ConditionLogic.Xor => results.Count(r => r) == 1,
                _ => results.All(r => r) // Default to AND
            };
            
            if (finalResult && !string.IsNullOrEmpty(rule.LogMessage))
            {
                var message = ResolveVariables(rule.LogMessage, context);
                context.Logger?.LogInformation("Decision rule matched: {Message}", message);
            }
            
            return finalResult;
        }
        catch (Exception ex)
        {
            context.Logger?.LogWarning(ex, "Failed to evaluate rule: {RuleName}", rule.Name);
            return false;
        }
    }
    
    private bool EvaluateCondition(DecisionCondition condition, ExecutionContext context)
    {
        // Get the value to compare
        object? value = GetConditionValue(condition.Field, context);
        
        // Resolve the comparison value (could be another field or a literal)
        object? comparisonValue = condition.Value;
        
        // Check if the comparison value is a field reference (starts with $)
        if (comparisonValue is string strValue)
        {
            if (strValue.StartsWith("$"))
            {
                var fieldName = strValue.Substring(1);
                comparisonValue = GetConditionValue(fieldName, context);
            }
            else
            {
                // Resolve variables in the comparison value
                comparisonValue = ResolveVariables(strValue, context);
            }
        }
        
        return condition.Operator switch
        {
            ComparisonOperator.Equals => CompareValues(value, comparisonValue, (a, b) => a.Equals(b)),
            ComparisonOperator.NotEquals => CompareValues(value, comparisonValue, (a, b) => !a.Equals(b)),
            ComparisonOperator.GreaterThan => CompareNumeric(value, comparisonValue, (a, b) => a > b),
            ComparisonOperator.GreaterThanOrEqual => CompareNumeric(value, comparisonValue, (a, b) => a >= b),
            ComparisonOperator.LessThan => CompareNumeric(value, comparisonValue, (a, b) => a < b),
            ComparisonOperator.LessThanOrEqual => CompareNumeric(value, comparisonValue, (a, b) => a <= b),
            ComparisonOperator.Contains => value?.ToString()?.Contains(comparisonValue?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
            ComparisonOperator.StartsWith => value?.ToString()?.StartsWith(comparisonValue?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
            ComparisonOperator.EndsWith => value?.ToString()?.EndsWith(comparisonValue?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
            ComparisonOperator.Matches => Regex.IsMatch(value?.ToString() ?? "", comparisonValue?.ToString() ?? ""),
            ComparisonOperator.Exists => value != null && !string.IsNullOrEmpty(value.ToString()),
            ComparisonOperator.NotExists => value == null || string.IsNullOrEmpty(value.ToString()),
            ComparisonOperator.In => EvaluateInOperator(value, comparisonValue),
            ComparisonOperator.NotIn => !EvaluateInOperator(value, comparisonValue),
            ComparisonOperator.Between => EvaluateBetweenOperator(value, comparisonValue),
            ComparisonOperator.IsTrue => IsTruthy(value),
            ComparisonOperator.IsFalse => !IsTruthy(value),
            _ => false
        };
    }
    
    private bool EvaluateInOperator(object? value, object? comparisonValue)
    {
        if (value == null || comparisonValue == null)
            return false;
        
        // Parse comparison value as a list (comma-separated)
        var valueStr = value.ToString() ?? "";
        var listStr = comparisonValue.ToString() ?? "";
        var items = listStr.Split(',').Select(s => s.Trim());
        
        return items.Any(item => item.Equals(valueStr, StringComparison.OrdinalIgnoreCase));
    }
    
    private bool EvaluateBetweenOperator(object? value, object? comparisonValue)
    {
        if (value == null || comparisonValue == null)
            return false;
        
        // Parse comparison value as a range (e.g., "10-20", "2024-01-01..2024-12-31")
        var rangeStr = comparisonValue.ToString() ?? "";
        var separator = rangeStr.Contains("..") ? ".." : "-";
        var parts = rangeStr.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length != 2)
            return false;
        
        // Try numeric comparison first
        if (double.TryParse(value.ToString(), out var numValue) &&
            double.TryParse(parts[0].Trim(), out var minNum) &&
            double.TryParse(parts[1].Trim(), out var maxNum))
        {
            return numValue >= minNum && numValue <= maxNum;
        }
        
        // Try date comparison
        if (DateTime.TryParse(value.ToString(), out var dateValue) &&
            DateTime.TryParse(parts[0].Trim(), out var minDate) &&
            DateTime.TryParse(parts[1].Trim(), out var maxDate))
        {
            return dateValue >= minDate && dateValue <= maxDate;
        }
        
        // Fall back to string comparison
        var strValue = value.ToString() ?? "";
        var minStr = parts[0].Trim();
        var maxStr = parts[1].Trim();
        
        return string.Compare(strValue, minStr, StringComparison.OrdinalIgnoreCase) >= 0 &&
               string.Compare(strValue, maxStr, StringComparison.OrdinalIgnoreCase) <= 0;
    }
    
    private bool IsTruthy(object? value)
    {
        if (value == null)
            return false;
        
        if (value is bool boolValue)
            return boolValue;
        
        if (value is string strValue)
        {
            if (string.IsNullOrWhiteSpace(strValue))
                return false;
            
            // Check for common truthy strings
            var lower = strValue.ToLower();
            return lower == "true" || lower == "yes" || lower == "1" || lower == "on" || lower == "enabled";
        }
        
        if (value is int intValue)
            return intValue != 0;
        
        if (value is double doubleValue)
            return Math.Abs(doubleValue) > 0.0001;
        
        return true; // Non-null objects are truthy
    }
    
    private object? GetConditionValue(string field, ExecutionContext context)
    {
        // Handle nested property access (e.g., "file.size", "variable.property")
        if (field.Contains('.'))
        {
            var parts = field.Split('.', 2);
            var baseField = parts[0].ToLower();
            var property = parts[1].ToLower();
            
            if (baseField == "file" && File.Exists(context.FilePath))
            {
                var fileInfo = new FileInfo(context.FilePath);
                return property switch
                {
                    "size" => fileInfo.Length,
                    "sizekb" => fileInfo.Length / 1024.0,
                    "sizemb" => fileInfo.Length / (1024.0 * 1024.0),
                    "sizegb" => fileInfo.Length / (1024.0 * 1024.0 * 1024.0),
                    "created" => fileInfo.CreationTimeUtc,
                    "modified" => fileInfo.LastWriteTimeUtc,
                    "accessed" => fileInfo.LastAccessTimeUtc,
                    "age" => (DateTime.UtcNow - fileInfo.LastWriteTimeUtc).TotalMinutes,
                    "agehours" => (DateTime.UtcNow - fileInfo.LastWriteTimeUtc).TotalHours,
                    "agedays" => (DateTime.UtcNow - fileInfo.LastWriteTimeUtc).TotalDays,
                    "readonly" => fileInfo.IsReadOnly,
                    "hidden" => (fileInfo.Attributes & FileAttributes.Hidden) != 0,
                    "system" => (fileInfo.Attributes & FileAttributes.System) != 0,
                    "archive" => (fileInfo.Attributes & FileAttributes.Archive) != 0,
                    "encrypted" => (fileInfo.Attributes & FileAttributes.Encrypted) != 0,
                    _ => null
                };
            }
            else if (context.Variables.ContainsKey(baseField))
            {
                // Try to access property of a variable object
                var obj = context.Variables[baseField];
                if (obj != null)
                {
                    var propInfo = obj.GetType().GetProperty(property, 
                        System.Reflection.BindingFlags.Public | 
                        System.Reflection.BindingFlags.Instance | 
                        System.Reflection.BindingFlags.IgnoreCase);
                    
                    return propInfo?.GetValue(obj);
                }
            }
        }
        
        // Check if it's a built-in field
        return field.ToLower() switch
        {
            "filename" => context.FileName,
            "filepath" => context.FilePath,
            "fileext" or "fileextension" => context.FileExtension,
            "filedir" or "filedirectory" => context.FileDirectory,
            "filenamewithoutextension" => context.FileNameWithoutExtension,
            "filesize" when File.Exists(context.FilePath) => new FileInfo(context.FilePath).Length,
            "fileexists" => File.Exists(context.FilePath),
            "fileage" when File.Exists(context.FilePath) => 
                (DateTime.UtcNow - new FileInfo(context.FilePath).LastWriteTimeUtc).TotalMinutes,
            "taskid" => context.TaskId,
            "date" => DateTime.Now.ToString("yyyy-MM-dd"),
            "time" => DateTime.Now.ToString("HH:mm:ss"),
            "datetime" => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "timestamp" => DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            "random" => new Random().Next(0, 100), // Random number 0-99
            "guid" => Guid.NewGuid().ToString(),
            "machinename" => Environment.MachineName,
            "username" => Environment.UserName,
            "temppath" => Path.GetTempPath(),
            "workingdirectory" => Environment.CurrentDirectory,
            _ => context.Variables.ContainsKey(field) ? context.Variables[field] : null
        };
    }
    
    private bool CompareValues(object? value1, object? value2, Func<object, object, bool> comparison)
    {
        if (value1 == null && value2 == null) return comparison(true, true);
        if (value1 == null || value2 == null) return comparison(false, true);
        
        // Try to convert to same type for comparison
        if (value1.GetType() == value2.GetType())
        {
            return comparison(value1, value2);
        }
        
        // Try string comparison
        return comparison(value1.ToString() ?? "", value2.ToString() ?? "");
    }
    
    private bool CompareNumeric(object? value1, object? value2, Func<double, double, bool> comparison)
    {
        if (double.TryParse(value1?.ToString(), out var num1) && 
            double.TryParse(value2?.ToString(), out var num2))
        {
            return comparison(num1, num2);
        }
        
        return false;
    }
    
    private string ResolveVariables(string text, ExecutionContext context)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        var result = text;
        
        // Replace custom variables
        foreach (var variable in context.Variables)
        {
            result = result.Replace($"{{{variable.Key}}}", variable.Value?.ToString() ?? string.Empty);
        }
        
        return result;
    }
}

/// <summary>
/// A decision rule containing conditions and an action
/// </summary>
public class DecisionRule
{
    public string? Name { get; set; }
    public bool Enabled { get; set; } = true;
    public List<DecisionCondition> Conditions { get; set; } = new();
    public ConditionLogic Logic { get; set; } = ConditionLogic.And; // How to combine conditions
    public DecisionAction Action { get; set; } = DecisionAction.Continue;
    public string? JumpToStepName { get; set; } // For Jump action
    public string? SetVariable { get; set; } // Variable to set when rule matches
    public object? SetVariableValue { get; set; } // Value to set
    public string? LogMessage { get; set; } // Optional message to log when rule matches
}

/// <summary>
/// Logic for combining multiple conditions
/// </summary>
public enum ConditionLogic
{
    And, // All conditions must be true
    Or,  // At least one condition must be true
    Xor  // Exactly one condition must be true
}

/// <summary>
/// A single condition within a decision rule
/// </summary>
public class DecisionCondition
{
    public string Field { get; set; } = string.Empty;
    public ComparisonOperator Operator { get; set; }
    public object? Value { get; set; }
}

/// <summary>
/// Comparison operators for conditions
/// </summary>
public enum ComparisonOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains,
    StartsWith,
    EndsWith,
    Matches, // Regex match
    Exists,
    NotExists,
    In, // Value is in a list
    NotIn, // Value is not in a list
    Between, // Value is between two values
    IsTrue, // Value is truthy
    IsFalse // Value is falsy
}

/// <summary>
/// Actions that can be taken based on decision
/// </summary>
public enum DecisionAction
{
    Continue,  // Continue to next step
    Skip,      // Skip the next step
    Stop,      // Stop pipeline execution gracefully
    Fail,      // Fail the pipeline
    Jump       // Jump to a specific step
}

/// <summary>
/// Validator for DecisionStep
/// </summary>
public class DecisionStepValidator : AbstractValidator<DecisionStep>
{
    public DecisionStepValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Step name is required");
        
        RuleFor(x => x.Rules)
            .NotEmpty().WithMessage("At least one rule is required")
            .Must(rules => rules.All(r => r.Conditions.Any()))
            .WithMessage("Each rule must have at least one condition");
        
        RuleFor(x => x.JumpToStepName)
            .NotEmpty()
            .When(x => x.DefaultAction == DecisionAction.Jump)
            .WithMessage("Jump target step name is required when default action is Jump");
        
        RuleForEach(x => x.Rules)
            .ChildRules(rule =>
            {
                rule.RuleFor(r => r.JumpToStepName)
                    .NotEmpty()
                    .When(r => r.Action == DecisionAction.Jump)
                    .WithMessage("Jump target step name is required when action is Jump");
                
                rule.RuleForEach(r => r.Conditions)
                    .ChildRules(condition =>
                    {
                        condition.RuleFor(c => c.Field)
                            .NotEmpty().WithMessage("Condition field is required");
                        
                        condition.RuleFor(c => c.Operator)
                            .IsInEnum().WithMessage("Invalid comparison operator");
                    });
            });
        
        RuleFor(x => x.SetVariable)
            .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]*$")
            .When(x => !string.IsNullOrEmpty(x.SetVariable))
            .WithMessage("Variable name must be a valid identifier");
    }
}