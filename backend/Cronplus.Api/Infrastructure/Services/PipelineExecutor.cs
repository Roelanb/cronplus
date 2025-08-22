using System.Diagnostics;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Domain.Models;
using Cronplus.Api.Domain.Models.PipelineSteps;
using Cronplus.Api.Infrastructure.Database.Repositories;
using Polly;
using ExecutionContext = Cronplus.Api.Domain.Models.PipelineSteps.ExecutionContext;

namespace Cronplus.Api.Infrastructure.Services;

/// <summary>
/// Service responsible for executing task pipelines with proper error handling, retry logic, and logging
/// </summary>
public interface IPipelineExecutor
{
    Task<PipelineExecutionResult> ExecuteAsync(string taskId, string filePath, CancellationToken cancellationToken = default);
    Task<PipelineExecutionResult> ExecuteStepsAsync(IEnumerable<PipelineStepBase> steps, ExecutionContext context, CancellationToken cancellationToken = default);
}

public class PipelineExecutor : IPipelineExecutor
{
    private readonly ILogger<PipelineExecutor> _logger;
    private readonly IPipelineStepRepository _stepRepository;
    private readonly IVariableRepository _variableRepository;
    private readonly IVariableInterpolator _variableInterpolator;
    private readonly IDeadLetterQueue _deadLetterQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IActionFactory _actionFactory;
    private readonly IActionValidator _actionValidator;

    public PipelineExecutor(
        ILogger<PipelineExecutor> logger,
        IPipelineStepRepository stepRepository,
        IVariableRepository variableRepository,
        IVariableInterpolator variableInterpolator,
        IDeadLetterQueue deadLetterQueue,
        IServiceProvider serviceProvider,
        IActionFactory? actionFactory = null,
        IActionValidator? actionValidator = null)
    {
        _logger = logger;
        _stepRepository = stepRepository;
        _variableRepository = variableRepository;
        _variableInterpolator = variableInterpolator;
        _deadLetterQueue = deadLetterQueue;
        _serviceProvider = serviceProvider;
        _actionFactory = actionFactory ?? new ActionFactory(_serviceProvider.GetRequiredService<ILogger<ActionFactory>>());
        _actionValidator = actionValidator ?? new ActionValidator(_serviceProvider.GetRequiredService<ILogger<ActionValidator>>(), _actionFactory);
    }

    public async Task<PipelineExecutionResult> ExecuteAsync(string taskId, string filePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting pipeline execution for task {TaskId} with file {FilePath}", taskId, filePath);
        
        var stopwatch = Stopwatch.StartNew();
        var result = new PipelineExecutionResult
        {
            TaskId = taskId,
            FilePath = filePath,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            // Load pipeline steps
            var entitySteps = await _stepRepository.GetByTaskIdAsync(taskId);
            if (!entitySteps.Any())
            {
                _logger.LogWarning("No pipeline steps found for task {TaskId}", taskId);
                result.Success = true;
                result.Message = "No pipeline steps to execute";
                return result;
            }

            // Convert entity steps to domain models using the factory
            var steps = ConvertStepsWithFactory(entitySteps).ToList();
            
            // Validate the pipeline before execution
            var validationResult = _actionValidator.ValidatePipeline(steps);
            if (!validationResult.IsValid)
            {
                _logger.LogError("Pipeline validation failed: {Errors}", 
                    string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage)));
                
                var failureResult = new PipelineExecutionResult
                {
                    TaskId = taskId,
                    FilePath = filePath,
                    Success = false,
                    Message = $"Pipeline validation failed: {string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage))}",
                    StartedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow
                };
                
                // Log validation failure
                _logger.LogError("Pipeline validation failed for task {TaskId}: {Message}", taskId, failureResult.Message);
                
                return failureResult;
            }
            
            if (!steps.Any())
            {
                _logger.LogWarning("No valid pipeline steps after conversion for task {TaskId}", taskId);
                result.Success = true;
                result.Message = "No valid pipeline steps to execute";
                return result;
            }

            // Load task variables
            var variables = await _variableRepository.GetByTaskIdAsync(taskId, cancellationToken);
            
            // Create execution context
            var context = CreateExecutionContext(taskId, filePath, variables);
            
            // Execute the pipeline
            var executionResult = await ExecuteStepsAsync(steps, context, cancellationToken);
            
            result.Success = executionResult.Success;
            result.Message = executionResult.Message;
            result.ExecutionLog = context.ExecutionLog;
            result.OutputVariables = context.Variables;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Pipeline execution cancelled for task {TaskId}", taskId);
            result.Success = false;
            result.Message = "Pipeline execution was cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline execution failed for task {TaskId}", taskId);
            result.Success = false;
            result.Message = $"Pipeline execution failed: {ex.Message}";
            result.Exception = ex;
            
            // Send to dead letter queue
            await _deadLetterQueue.EnqueueAsync(new DeadLetterItem
            {
                TaskId = taskId,
                FilePath = filePath,
                Error = ex.ToString(),
                Timestamp = DateTime.UtcNow,
                ExecutionResult = result
            }, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            result.ExecutionTime = stopwatch.Elapsed;
            result.CompletedAt = DateTime.UtcNow;
            
            _logger.LogInformation("Pipeline execution completed for task {TaskId} in {ElapsedMilliseconds}ms. Success: {Success}", 
                taskId, stopwatch.ElapsedMilliseconds, result.Success);
        }

        return result;
    }

    public async Task<PipelineExecutionResult> ExecuteStepsAsync(
        IEnumerable<PipelineStepBase> steps, 
        ExecutionContext context, 
        CancellationToken cancellationToken = default)
    {
        var result = new PipelineExecutionResult
        {
            TaskId = context.TaskId,
            FilePath = context.FilePath,
            StartedAt = DateTime.UtcNow
        };

        var orderedSteps = steps.OrderBy(s => s.StepOrder).ToList();
        
        foreach (var step in orderedSteps)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Pipeline execution cancelled at step {StepName}", step.Name);
                result.Success = false;
                result.Message = $"Execution cancelled at step {step.Name}";
                break;
            }

            if (!step.Enabled)
            {
                _logger.LogDebug("Skipping disabled step {StepName}", step.Name);
                continue;
            }

            // Check condition
            if (step.Condition != null)
            {
                var conditionMet = await EvaluateConditionAsync(step.Condition, context);
                var action = conditionMet ? step.Condition.OnTrue : step.Condition.OnFalse;
                
                switch (action)
                {
                    case ConditionAction.Skip:
                        _logger.LogDebug("Skipping step {StepName} due to condition", step.Name);
                        continue;
                    case ConditionAction.Stop:
                        _logger.LogInformation("Stopping pipeline at step {StepName} due to condition", step.Name);
                        result.Success = true;
                        result.Message = $"Pipeline stopped at step {step.Name} due to condition";
                        return result;
                    case ConditionAction.Fail:
                        _logger.LogError("Failing pipeline at step {StepName} due to condition", step.Name);
                        result.Success = false;
                        result.Message = $"Pipeline failed at step {step.Name} due to condition";
                        return result;
                    case ConditionAction.Continue:
                    default:
                        break;
                }
            }

            // Execute step with retry policy
            var stepResult = await ExecuteStepWithRetryAsync(step, context, cancellationToken);
            
            // Log step execution
            context.LogStepExecution(step.Name, stepResult);
            
            if (!stepResult.Success)
            {
                _logger.LogError("Step {StepName} failed: {Message}", step.Name, stepResult.Message);
                result.Success = false;
                result.Message = $"Pipeline failed at step {step.Name}: {stepResult.Message}";
                result.Exception = stepResult.Exception;
                
                // Send to dead letter queue if step failed after retries
                await _deadLetterQueue.EnqueueAsync(new DeadLetterItem
                {
                    TaskId = context.TaskId,
                    FilePath = context.FilePath,
                    StepName = step.Name,
                    Error = stepResult.Exception?.ToString() ?? stepResult.Message ?? "Unknown error",
                    Timestamp = DateTime.UtcNow,
                    ExecutionResult = result
                }, cancellationToken);
                
                break;
            }

            // Merge output variables
            if (stepResult.OutputVariables != null)
            {
                foreach (var (key, value) in stepResult.OutputVariables)
                {
                    context.SetVariable(key, value);
                }
            }
            
            _logger.LogDebug("Step {StepName} completed successfully", step.Name);
        }

        if (result.Success != false)
        {
            result.Success = true;
            result.Message = "Pipeline executed successfully";
        }

        result.ExecutionLog = context.ExecutionLog;
        result.OutputVariables = context.Variables;
        result.CompletedAt = DateTime.UtcNow;
        result.ExecutionTime = DateTime.UtcNow - result.StartedAt;

        return result;
    }

    private async Task<StepResult> ExecuteStepWithRetryAsync(
        PipelineStepBase step, 
        ExecutionContext context, 
        CancellationToken cancellationToken)
    {
        // Apply variable interpolation to step
        await _variableInterpolator.InterpolateStepAsync(step, context);
        
        if (step.RetryPolicy == null || step.RetryPolicy.MaxAttempts <= 1)
        {
            // No retry, execute once
            return await ExecuteStepOnceAsync(step, context, cancellationToken);
        }

        // Build retry policy
        var retryPolicy = BuildRetryPolicy(step.RetryPolicy, step.Name);
        
        // Execute with retry
        var result = await retryPolicy.ExecuteAsync(async (ct) =>
        {
            return await ExecuteStepOnceAsync(step, context, ct);
        }, cancellationToken);

        return result;
    }

    private async Task<StepResult> ExecuteStepOnceAsync(
        PipelineStepBase step,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Apply timeout if configured
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (step.TimeoutSeconds.HasValue)
            {
                cts.CancelAfter(TimeSpan.FromSeconds(step.TimeoutSeconds.Value));
            }

            _logger.LogDebug("Executing step {StepName} of type {StepType}", step.Name, step.StepType);
            
            var result = await step.ExecuteAsync(context, cts.Token);
            
            stopwatch.Stop();
            result.ExecutionTime = stopwatch.Elapsed;
            
            return result;
        }
        catch (OperationCanceledException) when (step.TimeoutSeconds.HasValue)
        {
            _logger.LogError("Step {StepName} timed out after {TimeoutSeconds} seconds", 
                step.Name, step.TimeoutSeconds.Value);
            
            return StepResult.FailureResult(
                $"Step timed out after {step.TimeoutSeconds} seconds",
                new TimeoutException($"Step {step.Name} execution timed out"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step {StepName} threw an exception", step.Name);
            return StepResult.FailureResult($"Step failed with exception: {ex.Message}", ex);
        }
        finally
        {
            if (stopwatch.IsRunning)
                stopwatch.Stop();
        }
    }

    private Polly.Retry.AsyncRetryPolicy<StepResult> BuildRetryPolicy(Domain.Models.PipelineSteps.RetryPolicy retryPolicy, string stepName)
    {
        var sleepDurations = CalculateRetryDelays(retryPolicy);
        
        return Policy<StepResult>
            .HandleResult(r => !r.Success)
            .WaitAndRetryAsync(
                sleepDurations,
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var result = outcome.Result;
                    _logger.LogWarning(
                        "Retrying step {StepName} (attempt {RetryCount}/{MaxAttempts}) after {DelayMs}ms. Previous error: {Error}",
                        stepName, retryCount, retryPolicy.MaxAttempts, timespan.TotalMilliseconds, result?.Message);
                });
    }

    private IEnumerable<TimeSpan> CalculateRetryDelays(Domain.Models.PipelineSteps.RetryPolicy policy)
    {
        var delays = new List<TimeSpan>();
        
        for (int i = 0; i < policy.MaxAttempts - 1; i++)
        {
            double delayMs = policy.BackoffType switch
            {
                RetryBackoffType.Constant => policy.BackoffMilliseconds,
                RetryBackoffType.Linear => policy.BackoffMilliseconds * (i + 1),
                RetryBackoffType.Exponential => policy.BackoffMilliseconds * Math.Pow(policy.BackoffMultiplier, i),
                _ => policy.BackoffMilliseconds
            };

            if (policy.MaxBackoffMilliseconds.HasValue)
            {
                delayMs = Math.Min(delayMs, policy.MaxBackoffMilliseconds.Value);
            }

            delays.Add(TimeSpan.FromMilliseconds(delayMs));
        }

        return delays;
    }

    private async Task<bool> EvaluateConditionAsync(StepCondition condition, ExecutionContext context)
    {
        try
        {
            // Apply variable interpolation to the expression
            var interpolatedExpression = await _variableInterpolator.InterpolateAsync(condition.Expression, context);
            
            // For now, use the simple evaluation from the condition
            // TODO: Implement proper expression evaluation engine
            return condition.Evaluate(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate condition: {Expression}", condition.Expression);
            return false;
        }
    }

    private ExecutionContext CreateExecutionContext(string taskId, string filePath, IEnumerable<VariableModel> variables)
    {
        var context = new ExecutionContext
        {
            TaskId = taskId,
            FilePath = filePath,
            Logger = _serviceProvider.GetService<ILogger<ExecutionContext>>()
        };

        // Add system variables
        context.SetVariable("FILE_PATH", filePath);
        context.SetVariable("FILE_NAME", context.FileName);
        context.SetVariable("FILE_DIR", context.FileDirectory);
        context.SetVariable("FILE_EXT", context.FileExtension);
        context.SetVariable("FILE_NAME_NO_EXT", context.FileNameWithoutExtension);
        context.SetVariable("TASK_ID", taskId);
        context.SetVariable("TIMESTAMP", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        context.SetVariable("DATE", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        context.SetVariable("TIME", DateTime.UtcNow.ToString("HH:mm:ss"));

        // Add task variables
        foreach (var variable in variables)
        {
            context.SetVariable(variable.Name, variable.Value);
        }

        return context;
    }
    
    private IEnumerable<PipelineStepBase> ConvertStepsWithFactory(IEnumerable<Domain.Entities.PipelineStep> steps)
    {
        var domainSteps = new List<PipelineStepBase>();
        
        foreach (var step in steps)
        {
            // Try to create step using factory first (for plugin support)
            var domainStep = _actionFactory.CreateStep(step.Type);
            
            if (domainStep == null)
            {
                // Fallback to adapter for unknown types
                domainStep = PipelineStepAdapter.ConvertToDomainModel(step);
            }
            else
            {
                // Configure the step with entity data
                ConfigureStepFromEntity(domainStep, step);
            }
            
            if (domainStep != null)
            {
                domainSteps.Add(domainStep);
            }
            else
            {
                _logger.LogWarning("Failed to create step of type: {StepType}", step.Type);
            }
        }
        
        return domainSteps;
    }
    
    private void ConfigureStepFromEntity(PipelineStepBase domainStep, Domain.Entities.PipelineStep entity)
    {
        // Set basic properties
        domainStep.Id = entity.Id;
        domainStep.TaskId = entity.TaskId;
        domainStep.StepOrder = entity.StepOrder;
        
        // Parse configuration from entity
        if (entity.Configuration != null)
        {
            var config = entity.Configuration.RootElement;
            
            if (config.TryGetProperty("name", out var name))
                domainStep.Name = name.GetString() ?? entity.Type;
            else
                domainStep.Name = entity.Type;
            
            if (config.TryGetProperty("description", out var desc))
                domainStep.Description = desc.GetString();
            
            if (config.TryGetProperty("enabled", out var enabled))
                domainStep.Enabled = enabled.GetBoolean();
            
            if (config.TryGetProperty("timeoutSeconds", out var timeout))
                domainStep.TimeoutSeconds = timeout.GetInt32();
            
            // Apply type-specific configuration
            ApplyStepConfiguration(domainStep, config);
        }
        
        // Set retry policy
        if (entity.RetryMax.HasValue && entity.RetryMax.Value > 0)
        {
            domainStep.RetryPolicy = new Domain.Models.PipelineSteps.RetryPolicy
            {
                MaxAttempts = entity.RetryMax.Value,
                BackoffMilliseconds = entity.RetryBackoffMs ?? 1000,
                BackoffType = RetryBackoffType.Exponential
            };
        }
    }
    
    private void ApplyStepConfiguration(PipelineStepBase step, System.Text.Json.JsonElement config)
    {
        // Use reflection to set properties from configuration
        var stepType = step.GetType();
        var properties = stepType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        
        foreach (var property in properties)
        {
            // Convert property name to camelCase for JSON
            var jsonName = char.ToLower(property.Name[0]) + property.Name.Substring(1);
            
            if (config.TryGetProperty(jsonName, out var value))
            {
                try
                {
                    object? convertedValue = null;
                    
                    if (property.PropertyType == typeof(string))
                    {
                        convertedValue = value.GetString();
                    }
                    else if (property.PropertyType == typeof(bool))
                    {
                        convertedValue = value.GetBoolean();
                    }
                    else if (property.PropertyType == typeof(int))
                    {
                        convertedValue = value.GetInt32();
                    }
                    else if (property.PropertyType == typeof(long))
                    {
                        convertedValue = value.GetInt64();
                    }
                    else if (property.PropertyType == typeof(double))
                    {
                        convertedValue = value.GetDouble();
                    }
                    else if (property.PropertyType == typeof(Dictionary<string, string>))
                    {
                        var dict = new Dictionary<string, string>();
                        foreach (var item in value.EnumerateObject())
                        {
                            dict[item.Name] = item.Value.GetString() ?? string.Empty;
                        }
                        convertedValue = dict;
                    }
                    
                    if (convertedValue != null)
                    {
                        property.SetValue(step, convertedValue);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set property {Property} on step {StepType}", 
                        property.Name, step.StepType);
                }
            }
        }
    }
}

/// <summary>
/// Result of a pipeline execution
/// </summary>
public class PipelineExecutionResult
{
    public string TaskId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Exception? Exception { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public List<StepExecutionLog> ExecutionLog { get; set; } = new();
    public Dictionary<string, object> OutputVariables { get; set; } = new();
}