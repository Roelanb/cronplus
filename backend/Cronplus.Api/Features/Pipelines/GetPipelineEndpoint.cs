using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Interfaces;
using System.Text.Json;

namespace Cronplus.Api.Features.Pipelines;

public class GetPipelineRequest
{
    public string TaskId { get; set; } = string.Empty;
}

public class GetPipelineResponse
{
    public string TaskId { get; set; } = string.Empty;
    public List<PipelineStepDto> Steps { get; set; } = new();
    public Dictionary<string, string> AvailableVariables { get; set; } = new();
}

public class PipelineStepDto
{
    public int Id { get; set; }
    public int StepOrder { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public JsonDocument? Configuration { get; set; }
    public int? RetryMax { get; set; }
    public int? RetryBackoffMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public StepValidationInfo? ValidationInfo { get; set; }
}

public class StepValidationInfo
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class GetPipelineValidator : Validator<GetPipelineRequest>
{
    public GetPipelineValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEmpty()
            .MaximumLength(100);
    }
}

public class GetPipelineEndpoint : Endpoint<GetPipelineRequest, GetPipelineResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActionFactory _actionFactory;
    private readonly IActionValidator _actionValidator;
    private readonly ILogger<GetPipelineEndpoint> _logger;

    public GetPipelineEndpoint(
        IUnitOfWork unitOfWork,
        IActionFactory actionFactory,
        IActionValidator actionValidator,
        ILogger<GetPipelineEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _actionFactory = actionFactory;
        _actionValidator = actionValidator;
        _logger = logger;
    }

    public override void Configure()
    {
        Get("/api/tasks/{TaskId}/pipeline");
        AllowAnonymous();
        Description(b => b
            .Produces<GetPipelineResponse>(200)
            .ProducesProblemFE(404)
            .ProducesProblemFE(400)
            .WithTags("Pipelines"));
        Summary(s =>
        {
            s.Summary = "Get pipeline configuration for a task";
            s.Description = "Retrieves the pipeline steps and available variables for a specific task";
        });
    }

    public override async Task<GetPipelineResponse> ExecuteAsync(GetPipelineRequest req, CancellationToken ct)
    {
        _logger.LogDebug("Fetching pipeline for task {TaskId}", req.TaskId);

        // Check if task exists
        var task = await _unitOfWork.Tasks.GetByIdAsync(req.TaskId);
        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", req.TaskId);
            ThrowError("Resource not found", 404);
            return new GetPipelineResponse();
        }

        // Get pipeline steps
        var steps = await _unitOfWork.PipelineSteps.GetByTaskIdAsync(req.TaskId);
        
        // Get variables
        var variables = await _unitOfWork.TaskVariables.GetByTaskIdAsync(req.TaskId);

        // Map to DTOs with validation info
        var stepDtos = new List<PipelineStepDto>();
        foreach (var step in steps.OrderBy(s => s.StepOrder))
        {
            var stepDto = new PipelineStepDto
            {
                Id = step.Id,
                StepOrder = step.StepOrder,
                Type = step.Type,
                Configuration = step.Configuration,
                RetryMax = step.RetryMax,
                RetryBackoffMs = step.RetryBackoffMs,
                CreatedAt = step.CreatedAt
            };

            // Try to create the step instance and validate it
            try
            {
                var stepInstance = _actionFactory.CreateStep(step.Type);
                if (stepInstance != null)
                {
                    stepDto.Name = stepInstance.Name ?? $"Step {step.StepOrder}";
                    
                    // Validate the step
                    var validationResult = _actionValidator.ValidateStep(stepInstance);
                    stepDto.ValidationInfo = new StepValidationInfo
                    {
                        IsValid = validationResult.IsValid,
                        Errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList()
                    };

                    // Check for potential issues
                    if (step.Type == "decision" && step.Configuration != null)
                    {
                        // Check if jump targets exist
                        var config = step.Configuration.RootElement;
                        if (config.TryGetProperty("JumpToStepOnTrue", out var jumpTrue))
                        {
                            var targetStep = jumpTrue.GetString();
                            if (!string.IsNullOrEmpty(targetStep) && 
                                !steps.Any(s => s.Id.ToString() == targetStep || 
                                               (s.Configuration?.RootElement.TryGetProperty("Name", out var name) == true && 
                                                name.GetString() == targetStep)))
                            {
                                stepDto.ValidationInfo.Warnings.Add($"Jump target '{targetStep}' not found");
                            }
                        }
                    }
                }
                else
                {
                    stepDto.Name = $"Unknown Step Type: {step.Type}";
                    stepDto.ValidationInfo = new StepValidationInfo
                    {
                        IsValid = false,
                        Errors = new List<string> { $"Unknown step type: {step.Type}" }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to validate step {StepId} of type {StepType}", step.Id, step.Type);
                stepDto.ValidationInfo = new StepValidationInfo
                {
                    IsValid = false,
                    Errors = new List<string> { $"Validation error: {ex.Message}" }
                };
            }

            stepDtos.Add(stepDto);
        }

        // Validate entire pipeline
        try
        {
            var pipelineSteps = steps
                .OrderBy(s => s.StepOrder)
                .Select(s => _actionFactory.CreateStep(s.Type))
                .Where(s => s != null)
                .ToList();

            if (pipelineSteps.Any())
            {
                var pipelineValidation = _actionValidator.ValidatePipeline(pipelineSteps!);
                if (!pipelineValidation.IsValid)
                {
                    // Add pipeline-level errors to response somehow
                    _logger.LogWarning("Pipeline validation failed for task {TaskId}: {Errors}", 
                        req.TaskId, string.Join(", ", pipelineValidation.Errors.Select(e => e.ErrorMessage)));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate pipeline for task {TaskId}", req.TaskId);
        }

        return new GetPipelineResponse
        {
            TaskId = req.TaskId,
            Steps = stepDtos,
            AvailableVariables = variables.ToDictionary(v => v.Name, v => v.Value)
        };
    }
}