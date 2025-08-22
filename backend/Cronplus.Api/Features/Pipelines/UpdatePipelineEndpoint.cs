using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Services.TaskSupervision;
using System.Text.Json;

namespace Cronplus.Api.Features.Pipelines;

public class UpdatePipelineRequest
{
    public string TaskId { get; set; } = string.Empty;
    public List<UpdatePipelineStepDto> Steps { get; set; } = new();
}

public class UpdatePipelineStepDto
{
    public int? Id { get; set; } // null for new steps
    public int StepOrder { get; set; }
    public string Type { get; set; } = string.Empty;
    public JsonDocument? Configuration { get; set; }
    public int? RetryMax { get; set; }
    public int? RetryBackoffMs { get; set; }
}

public class UpdatePipelineResponse
{
    public string TaskId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int StepsAdded { get; set; }
    public int StepsUpdated { get; set; }
    public int StepsDeleted { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
}

public class UpdatePipelineValidator : Validator<UpdatePipelineRequest>
{
    public UpdatePipelineValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Steps)
            .NotNull()
            .WithMessage("Steps list is required");

        RuleForEach(x => x.Steps)
            .ChildRules(step =>
            {
                step.RuleFor(s => s.Type)
                    .NotEmpty()
                    .Must(type => new[] { "copy", "delete", "archive", "print", "rest-api", "decision", "email", "email-attachment" }
                        .Contains(type, StringComparer.OrdinalIgnoreCase))
                    .WithMessage("Invalid step type");

                step.RuleFor(s => s.StepOrder)
                    .GreaterThan(0)
                    .LessThanOrEqualTo(100)
                    .WithMessage("Step order must be between 1 and 100");

                step.RuleFor(s => s.RetryMax)
                    .InclusiveBetween(0, 10)
                    .When(s => s.RetryMax.HasValue);

                step.RuleFor(s => s.RetryBackoffMs)
                    .InclusiveBetween(100, 60000)
                    .When(s => s.RetryBackoffMs.HasValue);
            });

        // Ensure step orders are unique
        RuleFor(x => x.Steps)
            .Must(steps => steps.Select(s => s.StepOrder).Distinct().Count() == steps.Count)
            .WithMessage("Step orders must be unique");
    }
}

public class UpdatePipelineEndpoint : Endpoint<UpdatePipelineRequest, UpdatePipelineResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IActionFactory _actionFactory;
    private readonly IActionValidator _actionValidator;
    private readonly ITaskSupervisorManager _supervisorManager;
    private readonly ILogger<UpdatePipelineEndpoint> _logger;

    public UpdatePipelineEndpoint(
        IUnitOfWork unitOfWork,
        IActionFactory actionFactory,
        IActionValidator actionValidator,
        ITaskSupervisorManager supervisorManager,
        ILogger<UpdatePipelineEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _actionFactory = actionFactory;
        _actionValidator = actionValidator;
        _supervisorManager = supervisorManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Put("/api/tasks/{TaskId}/pipeline");
        AllowAnonymous();
        Description(b => b
            .Produces<UpdatePipelineResponse>(200)
            .ProducesProblemFE(404)
            .ProducesProblemFE(400)
            .WithTags("Pipelines"));
        Summary(s =>
        {
            s.Summary = "Update pipeline configuration for a task";
            s.Description = "Replaces the entire pipeline configuration for a task. Existing steps not in the request will be deleted.";
        });
    }

    public override async Task<UpdatePipelineResponse> ExecuteAsync(UpdatePipelineRequest req, CancellationToken ct)
    {
        _logger.LogInformation("Updating pipeline for task {TaskId}", req.TaskId);

        // Check if task exists
        var task = await _unitOfWork.Tasks.GetByIdAsync(req.TaskId);
        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", req.TaskId);
            ThrowError("Resource not found", 404);
            return new UpdatePipelineResponse();
        }

        // Validate the new pipeline
        var validationErrors = new List<string>();
        try
        {
            var pipelineSteps = req.Steps
                .OrderBy(s => s.StepOrder)
                .Select(s =>
                {
                    var step = _actionFactory.CreateStep(s.Type);
                    if (step == null)
                    {
                        validationErrors.Add($"Unknown step type: {s.Type}");
                    }
                    return step;
                })
                .Where(s => s != null)
                .ToList();

            if (pipelineSteps.Any() && !validationErrors.Any())
            {
                var pipelineValidation = _actionValidator.ValidatePipeline(pipelineSteps!);
                if (!pipelineValidation.IsValid)
                {
                    validationErrors.AddRange(pipelineValidation.Errors.Select(e => e.ErrorMessage));
                }
            }
        }
        catch (Exception ex)
        {
            validationErrors.Add($"Pipeline validation error: {ex.Message}");
        }

        if (validationErrors.Any())
        {
            _logger.LogWarning("Pipeline validation failed for task {TaskId}: {Errors}", 
                req.TaskId, string.Join(", ", validationErrors));
            
            return new UpdatePipelineResponse
            {
                TaskId = req.TaskId,
                Success = false,
                Message = "Pipeline validation failed",
                ValidationErrors = validationErrors
            };
        }

        // Begin transaction
        _unitOfWork.BeginTransaction();

        try
        {
            // Get existing steps
            var existingSteps = await _unitOfWork.PipelineSteps.GetByTaskIdAsync(req.TaskId);
            var existingStepIds = existingSteps.Select(s => s.Id).ToHashSet();
            var requestedStepIds = req.Steps.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToHashSet();

            // Determine steps to delete (existing steps not in the request)
            var stepsToDelete = existingSteps.Where(s => !requestedStepIds.Contains(s.Id)).ToList();
            
            // Delete removed steps
            foreach (var step in stepsToDelete)
            {
                await _unitOfWork.PipelineSteps.DeleteAsync(step.Id);
            }

            int stepsAdded = 0;
            int stepsUpdated = 0;

            // Process each step in the request
            foreach (var stepDto in req.Steps.OrderBy(s => s.StepOrder))
            {
                if (stepDto.Id.HasValue && existingStepIds.Contains(stepDto.Id.Value))
                {
                    // Update existing step
                    var existingStep = existingSteps.First(s => s.Id == stepDto.Id.Value);
                    existingStep.StepOrder = stepDto.StepOrder;
                    existingStep.Type = stepDto.Type;
                    existingStep.Configuration = stepDto.Configuration;
                    existingStep.RetryMax = stepDto.RetryMax;
                    existingStep.RetryBackoffMs = stepDto.RetryBackoffMs;
                    
                    await _unitOfWork.PipelineSteps.UpdateAsync(existingStep);
                    stepsUpdated++;
                }
                else
                {
                    // Add new step
                    var newStep = new PipelineStep
                    {
                        TaskId = req.TaskId,
                        StepOrder = stepDto.StepOrder,
                        Type = stepDto.Type,
                        Configuration = stepDto.Configuration,
                        RetryMax = stepDto.RetryMax,
                        RetryBackoffMs = stepDto.RetryBackoffMs
                    };
                    
                    await _unitOfWork.PipelineSteps.AddAsync(newStep);
                    stepsAdded++;
                }
            }

            _unitOfWork.Commit();

            // Reload supervisor if task is enabled
            if (task.Enabled)
            {
                await _supervisorManager.ReloadTaskAsync(req.TaskId, ct);
                _logger.LogInformation("Reloaded supervisor for task {TaskId} after pipeline update", req.TaskId);
            }

            _logger.LogInformation("Successfully updated pipeline for task {TaskId}: {Added} added, {Updated} updated, {Deleted} deleted", 
                req.TaskId, stepsAdded, stepsUpdated, stepsToDelete.Count);

            return new UpdatePipelineResponse
            {
                TaskId = req.TaskId,
                Success = true,
                Message = "Pipeline updated successfully",
                StepsAdded = stepsAdded,
                StepsUpdated = stepsUpdated,
                StepsDeleted = stepsToDelete.Count
            };
        }
        catch (Exception ex)
        {
            _unitOfWork.Rollback();
            _logger.LogError(ex, "Failed to update pipeline for task {TaskId}", req.TaskId);
            
            return new UpdatePipelineResponse
            {
                TaskId = req.TaskId,
                Success = false,
                Message = $"Failed to update pipeline: {ex.Message}"
            };
        }
    }
}