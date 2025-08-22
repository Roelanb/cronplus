using System.Text.Json;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Domain.Models.PipelineSteps;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Cronplus.Api.Infrastructure.Services;

/// <summary>
/// Service for validating pipeline actions and their configurations
/// </summary>
public interface IActionValidator
{
    /// <summary>
    /// Validate a pipeline step
    /// </summary>
    ValidationResult ValidateStep(PipelineStepBase step);
    
    /// <summary>
    /// Validate a step configuration before creating the step
    /// </summary>
    ValidationResult ValidateConfiguration(string stepType, Dictionary<string, object> configuration);
    
    /// <summary>
    /// Register a custom validator for a specific step type
    /// </summary>
    void RegisterValidator(string stepType, IValidator validator);
    
    /// <summary>
    /// Validate an entire pipeline
    /// </summary>
    ValidationResult ValidatePipeline(IEnumerable<PipelineStepBase> steps);
}

/// <summary>
/// Implementation of the action validator
/// </summary>
public class ActionValidator : IActionValidator
{
    private readonly ILogger<ActionValidator> _logger;
    private readonly IActionFactory _actionFactory;
    private readonly Dictionary<string, IValidator> _customValidators;
    
    public ActionValidator(ILogger<ActionValidator> logger, IActionFactory actionFactory)
    {
        _logger = logger;
        _actionFactory = actionFactory;
        _customValidators = new Dictionary<string, IValidator>();
    }
    
    public ValidationResult ValidateStep(PipelineStepBase step)
    {
        if (step == null)
        {
            return new ValidationResult(new[]
            {
                new ValidationFailure("Step", "Step cannot be null")
            });
        }
        
        _logger.LogDebug("Validating step: {StepName} of type {StepType}", step.Name, step.StepType);
        
        // First, use the step's built-in validation
        var result = step.Validate();
        
        // If there's a custom validator registered, use it as well
        if (_customValidators.TryGetValue(step.StepType.ToLowerInvariant(), out var customValidator))
        {
            var customResult = customValidator.Validate(new ValidationContext<object>(step));
            
            // Merge results
            if (!customResult.IsValid)
            {
                var errors = result.Errors.Concat(customResult.Errors).ToList();
                result = new ValidationResult(errors);
            }
        }
        
        // Additional common validations
        var commonErrors = ValidateCommonProperties(step);
        if (commonErrors.Any())
        {
            var errors = result.Errors.Concat(commonErrors).ToList();
            result = new ValidationResult(errors);
        }
        
        if (!result.IsValid)
        {
            _logger.LogWarning("Step validation failed: {Errors}", 
                string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
        }
        
        return result;
    }
    
    public ValidationResult ValidateConfiguration(string stepType, Dictionary<string, object> configuration)
    {
        _logger.LogDebug("Validating configuration for step type: {StepType}", stepType);
        
        var errors = new List<ValidationFailure>();
        
        // Get metadata for the action type
        var metadata = _actionFactory.GetActionMetadata(stepType);
        if (metadata == null)
        {
            errors.Add(new ValidationFailure("StepType", $"Unknown step type: {stepType}"));
            return new ValidationResult(errors);
        }
        
        // Validate required properties
        foreach (var property in metadata.Properties.Values.Where(p => p.Required))
        {
            if (!configuration.ContainsKey(property.Name))
            {
                errors.Add(new ValidationFailure(property.Name, 
                    $"Required property '{property.DisplayName}' is missing"));
            }
        }
        
        // Validate property types and constraints
        foreach (var kvp in configuration)
        {
            if (!metadata.Properties.TryGetValue(kvp.Key, out var propertyMeta))
            {
                // Unknown property - log warning but don't fail
                _logger.LogWarning("Unknown property '{Property}' for step type '{StepType}'", 
                    kvp.Key, stepType);
                continue;
            }
            
            // Validate type
            var validationError = ValidatePropertyType(kvp.Key, kvp.Value, propertyMeta);
            if (validationError != null)
            {
                errors.Add(validationError);
            }
            
            // Validate allowed values
            if (propertyMeta.AllowedValues != null && propertyMeta.AllowedValues.Any())
            {
                var valueStr = kvp.Value?.ToString();
                if (!propertyMeta.AllowedValues.Any(v => v?.ToString() == valueStr))
                {
                    errors.Add(new ValidationFailure(kvp.Key,
                        $"Value '{valueStr}' is not allowed. Allowed values: {string.Join(", ", propertyMeta.AllowedValues)}"));
                }
            }
            
            // Validate pattern
            if (!string.IsNullOrEmpty(propertyMeta.ValidationPattern))
            {
                var valueStr = kvp.Value?.ToString();
                if (!string.IsNullOrEmpty(valueStr))
                {
                    var regex = new System.Text.RegularExpressions.Regex(propertyMeta.ValidationPattern);
                    if (!regex.IsMatch(valueStr))
                    {
                        errors.Add(new ValidationFailure(kvp.Key,
                            $"Value does not match required pattern: {propertyMeta.ValidationPattern}"));
                    }
                }
            }
        }
        
        return new ValidationResult(errors);
    }
    
    public void RegisterValidator(string stepType, IValidator validator)
    {
        if (string.IsNullOrWhiteSpace(stepType))
            throw new ArgumentException("Step type cannot be empty", nameof(stepType));
        
        if (validator == null)
            throw new ArgumentNullException(nameof(validator));
        
        var normalizedType = stepType.ToLowerInvariant();
        _customValidators[normalizedType] = validator;
        
        _logger.LogInformation("Registered custom validator for step type: {StepType}", normalizedType);
    }
    
    public ValidationResult ValidatePipeline(IEnumerable<PipelineStepBase> steps)
    {
        var errors = new List<ValidationFailure>();
        var stepList = steps.ToList();
        
        if (!stepList.Any())
        {
            errors.Add(new ValidationFailure("Pipeline", "Pipeline must contain at least one step"));
            return new ValidationResult(errors);
        }
        
        // Validate each step
        var stepNames = new HashSet<string>();
        var stepIndex = 0;
        
        foreach (var step in stepList)
        {
            var stepResult = ValidateStep(step);
            if (!stepResult.IsValid)
            {
                foreach (var error in stepResult.Errors)
                {
                    errors.Add(new ValidationFailure(
                        $"Step[{stepIndex}].{error.PropertyName}",
                        error.ErrorMessage));
                }
            }
            
            // Check for duplicate step names
            if (!string.IsNullOrEmpty(step.Name))
            {
                if (!stepNames.Add(step.Name))
                {
                    errors.Add(new ValidationFailure($"Step[{stepIndex}].Name",
                        $"Duplicate step name: {step.Name}"));
                }
            }
            
            // Validate Decision step jump targets
            if (step is DecisionStep decisionStep)
            {
                ValidateDecisionStepTargets(decisionStep, stepList, errors, stepIndex);
            }
            
            stepIndex++;
        }
        
        // Validate pipeline flow
        ValidatePipelineFlow(stepList, errors);
        
        return new ValidationResult(errors);
    }
    
    private void ValidateDecisionStepTargets(DecisionStep decisionStep, List<PipelineStepBase> allSteps, 
        List<ValidationFailure> errors, int stepIndex)
    {
        var stepNames = allSteps.Where(s => !string.IsNullOrEmpty(s.Name))
            .Select(s => s.Name).ToHashSet();
        
        // Check default jump target
        if (decisionStep.DefaultAction == DecisionAction.Jump && 
            !string.IsNullOrEmpty(decisionStep.JumpToStepName))
        {
            if (!stepNames.Contains(decisionStep.JumpToStepName))
            {
                errors.Add(new ValidationFailure($"Step[{stepIndex}].JumpToStepName",
                    $"Jump target '{decisionStep.JumpToStepName}' does not exist"));
            }
        }
        
        // Check rule jump targets
        foreach (var rule in decisionStep.Rules)
        {
            if (rule.Action == DecisionAction.Jump && !string.IsNullOrEmpty(rule.JumpToStepName))
            {
                if (!stepNames.Contains(rule.JumpToStepName))
                {
                    errors.Add(new ValidationFailure($"Step[{stepIndex}].Rule.JumpToStepName",
                        $"Jump target '{rule.JumpToStepName}' in rule '{rule.Name}' does not exist"));
                }
            }
        }
    }
    
    private void ValidatePipelineFlow(List<PipelineStepBase> steps, List<ValidationFailure> errors)
    {
        // Check for potential infinite loops in decision steps
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        
        foreach (var step in steps.Where(s => s is DecisionStep && !string.IsNullOrEmpty(s.Name)))
        {
            if (HasCycle(step.Name!, steps, visited, recursionStack))
            {
                errors.Add(new ValidationFailure("Pipeline",
                    $"Potential infinite loop detected starting from step '{step.Name}'"));
            }
        }
    }
    
    private bool HasCycle(string stepName, List<PipelineStepBase> steps, 
        HashSet<string> visited, HashSet<string> recursionStack)
    {
        visited.Add(stepName);
        recursionStack.Add(stepName);
        
        var step = steps.FirstOrDefault(s => s.Name == stepName);
        if (step is DecisionStep decisionStep)
        {
            var targets = new List<string>();
            
            if (decisionStep.DefaultAction == DecisionAction.Jump && 
                !string.IsNullOrEmpty(decisionStep.JumpToStepName))
            {
                targets.Add(decisionStep.JumpToStepName);
            }
            
            foreach (var rule in decisionStep.Rules)
            {
                if (rule.Action == DecisionAction.Jump && !string.IsNullOrEmpty(rule.JumpToStepName))
                {
                    targets.Add(rule.JumpToStepName);
                }
            }
            
            foreach (var target in targets)
            {
                if (!visited.Contains(target))
                {
                    if (HasCycle(target, steps, visited, recursionStack))
                        return true;
                }
                else if (recursionStack.Contains(target))
                {
                    return true;
                }
            }
        }
        
        recursionStack.Remove(stepName);
        return false;
    }
    
    private List<ValidationFailure> ValidateCommonProperties(PipelineStepBase step)
    {
        var errors = new List<ValidationFailure>();
        
        // Validate timeout
        if (step.TimeoutSeconds.HasValue)
        {
            if (step.TimeoutSeconds.Value <= 0)
            {
                errors.Add(new ValidationFailure("TimeoutSeconds", 
                    "Timeout must be greater than 0"));
            }
            else if (step.TimeoutSeconds.Value > 3600)
            {
                errors.Add(new ValidationFailure("TimeoutSeconds", 
                    "Timeout cannot exceed 3600 seconds (1 hour)"));
            }
        }
        
        // Validate retry policy
        if (step.RetryPolicy != null)
        {
            if (step.RetryPolicy.MaxAttempts < 0)
            {
                errors.Add(new ValidationFailure("RetryPolicy.MaxAttempts", 
                    "Max attempts cannot be negative"));
            }
            else if (step.RetryPolicy.MaxAttempts > 10)
            {
                errors.Add(new ValidationFailure("RetryPolicy.MaxAttempts", 
                    "Max attempts cannot exceed 10"));
            }
            
            if (step.RetryPolicy.BackoffMilliseconds < 0)
            {
                errors.Add(new ValidationFailure("RetryPolicy.BackoffMilliseconds", 
                    "Backoff milliseconds cannot be negative"));
            }
        }
        
        return errors;
    }
    
    private ValidationFailure? ValidatePropertyType(string propertyName, object? value, PropertyMetadata metadata)
    {
        if (value == null)
        {
            if (metadata.Required)
            {
                return new ValidationFailure(propertyName, $"Required property cannot be null");
            }
            return null;
        }
        
        switch (metadata.Type.ToLower())
        {
            case "string":
                if (!(value is string || value is JsonElement { ValueKind: JsonValueKind.String }))
                {
                    return new ValidationFailure(propertyName, $"Expected string but got {value.GetType().Name}");
                }
                break;
                
            case "number":
                if (!IsNumericType(value))
                {
                    return new ValidationFailure(propertyName, $"Expected number but got {value.GetType().Name}");
                }
                break;
                
            case "boolean":
                if (!(value is bool || value is JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False }))
                {
                    return new ValidationFailure(propertyName, $"Expected boolean but got {value.GetType().Name}");
                }
                break;
                
            case "array":
                if (!(value is Array || value is System.Collections.IEnumerable || 
                      value is JsonElement { ValueKind: JsonValueKind.Array }))
                {
                    return new ValidationFailure(propertyName, $"Expected array but got {value.GetType().Name}");
                }
                break;
                
            case "object":
                if (value is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
                {
                    return new ValidationFailure(propertyName, $"Expected object but got null");
                }
                break;
        }
        
        return null;
    }
    
    private bool IsNumericType(object value)
    {
        return value is int || value is long || value is float || value is double || value is decimal ||
               value is byte || value is sbyte || value is short || value is ushort || 
               value is uint || value is ulong ||
               value is JsonElement { ValueKind: JsonValueKind.Number };
    }
}