using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Services.TaskSupervision;
using System.Text.Json;

namespace Cronplus.Api.Features.Tasks;

public class CreateTaskRequest
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;
    public string WatchDirectory { get; set; } = string.Empty;
    public string GlobPattern { get; set; } = "*";
    public int DebounceMs { get; set; } = 500;
    public int StabilizationMs { get; set; } = 1000;
    public string? Description { get; set; }
    public List<CreatePipelineStepDto>? PipelineSteps { get; set; }
    public Dictionary<string, string>? Variables { get; set; }
}

public class CreatePipelineStepDto
{
    public int StepOrder { get; set; }
    public string Type { get; set; } = string.Empty;
    public JsonDocument? Configuration { get; set; }
    public int? RetryMax { get; set; }
    public int? RetryBackoffMs { get; set; }
}

public class CreateTaskResponse
{
    public string Id { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CreateTaskValidator : Validator<CreateTaskRequest>
{
    public CreateTaskValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .MaximumLength(100)
            .Matches(@"^[a-zA-Z0-9\-_]+$")
            .WithMessage("Task ID must contain only letters, numbers, hyphens, and underscores");

        RuleFor(x => x.WatchDirectory)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.GlobPattern)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.DebounceMs)
            .InclusiveBetween(0, 60000)
            .WithMessage("Debounce must be between 0 and 60000 milliseconds");

        RuleFor(x => x.StabilizationMs)
            .InclusiveBetween(0, 300000)
            .WithMessage("Stabilization must be between 0 and 300000 milliseconds");

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description != null);

        RuleForEach(x => x.PipelineSteps)
            .ChildRules(step =>
            {
                step.RuleFor(s => s.Type)
                    .NotEmpty()
                    .Must(type => new[] { "copy", "delete", "archive", "print", "rest-api", "decision", "email", "email-attachment" }
                        .Contains(type, StringComparer.OrdinalIgnoreCase))
                    .WithMessage("Invalid step type");

                step.RuleFor(s => s.StepOrder)
                    .GreaterThan(0);

                step.RuleFor(s => s.RetryMax)
                    .InclusiveBetween(0, 10)
                    .When(s => s.RetryMax.HasValue);

                step.RuleFor(s => s.RetryBackoffMs)
                    .InclusiveBetween(100, 60000)
                    .When(s => s.RetryBackoffMs.HasValue);
            })
            .When(x => x.PipelineSteps != null);
    }
}

public class CreateTaskEndpoint : Endpoint<CreateTaskRequest, CreateTaskResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskSupervisorManager _supervisorManager;
    private readonly IActionValidator _actionValidator;
    private readonly IActionFactory _actionFactory;
    private readonly ILogger<CreateTaskEndpoint> _logger;

    public CreateTaskEndpoint(
        IUnitOfWork unitOfWork,
        ITaskSupervisorManager supervisorManager,
        IActionValidator actionValidator,
        IActionFactory actionFactory,
        ILogger<CreateTaskEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _supervisorManager = supervisorManager;
        _actionValidator = actionValidator;
        _actionFactory = actionFactory;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/api/tasks");
        AllowAnonymous();
        Description(b => b
            .Produces<CreateTaskResponse>(201)
            .ProducesProblemFE(500)
            .ProducesProblemFE(400)
            .ProducesProblemFE(409)
            .WithTags("Tasks"));
        Summary(s =>
        {
            s.Summary = "Create a new task";
            s.Description = "Creates a new task with optional pipeline steps and variables";
        });
    }

    public override async Task<CreateTaskResponse> ExecuteAsync(CreateTaskRequest req, CancellationToken ct)
    {
        _logger.LogInformation("Creating new task {TaskId}", req.Id);

        // Check if task already exists
        var existingTask = await _unitOfWork.Tasks.GetByIdAsync(req.Id);
        if (existingTask != null)
        {
            _logger.LogWarning("Task {TaskId} already exists", req.Id);
            ThrowError($"Task with ID '{req.Id}' already exists", 409);
            return new CreateTaskResponse { Id = req.Id, Success = false, Message = "Task already exists" };
        }

        // Validate pipeline steps if provided
        if (req.PipelineSteps != null && req.PipelineSteps.Any())
        {
            var pipelineSteps = req.PipelineSteps
                .OrderBy(s => s.StepOrder)
                .Select(s => new PipelineStep
                {
                    Type = s.Type,
                    Configuration = s.Configuration,
                    StepOrder = s.StepOrder
                })
                .ToList();

            var validationResult = _actionValidator.ValidatePipeline(pipelineSteps.Select(s => 
                _actionFactory.CreateStep(s.Type) ?? 
                throw new InvalidOperationException($"Unknown step type: {s.Type}")).Where(s => s != null)!);

            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Pipeline validation failed for task {TaskId}: {Errors}", 
                    req.Id, string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage)));
                
                ThrowError($"Pipeline validation failed: {string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage))}", 400);
                return new CreateTaskResponse { Id = req.Id, Success = false, Message = "Pipeline validation failed" };
            }
        }

        // Begin transaction
        _unitOfWork.BeginTransaction();

        try
        {
            // Create task entity
            var task = new TaskEntity
            {
                Id = req.Id,
                Enabled = req.Enabled,
                WatchDirectory = req.WatchDirectory,
                GlobPattern = req.GlobPattern,
                DebounceMs = req.DebounceMs,
                StabilizationMs = req.StabilizationMs,
                Description = req.Description
            };

            await _unitOfWork.Tasks.AddAsync(task);

            // Add pipeline steps if provided
            if (req.PipelineSteps != null && req.PipelineSteps.Any())
            {
                foreach (var stepDto in req.PipelineSteps.OrderBy(s => s.StepOrder))
                {
                    var step = new PipelineStep
                    {
                        TaskId = req.Id,
                        StepOrder = stepDto.StepOrder,
                        Type = stepDto.Type,
                        Configuration = stepDto.Configuration,
                        RetryMax = stepDto.RetryMax,
                        RetryBackoffMs = stepDto.RetryBackoffMs
                    };
                    await _unitOfWork.PipelineSteps.AddAsync(step);
                }
            }

            // Add variables if provided
            if (req.Variables != null && req.Variables.Any())
            {
                foreach (var (name, value) in req.Variables)
                {
                    var variable = new TaskVariable
                    {
                        TaskId = req.Id,
                        Name = name,
                        Type = "string", // Default to string for now
                        Value = value
                    };
                    await _unitOfWork.TaskVariables.AddAsync(variable);
                }
            }

            _unitOfWork.Commit();

            // If task is enabled, start the supervisor
            if (req.Enabled)
            {
                await _supervisorManager.ReloadTaskAsync(req.Id.ToString(), ct);
            }

            _logger.LogInformation("Successfully created task {TaskId}", req.Id);

            return new CreateTaskResponse
            {
                Id = req.Id,
                Success = true,
                Message = "Task created successfully"
            };

            return new CreateTaskResponse { Id = req.Id, Success = true, Message = "Task created successfully" };
        }
        catch (Exception ex)
        {
            _unitOfWork.Rollback();
            _logger.LogError(ex, "Failed to create task {TaskId}", req.Id);
            
            ThrowError($"Failed to create task: {ex.Message}", 500);
            
            return new CreateTaskResponse { Id = req.Id, Success = false, Message = "Internal server error" };
        }
    }
}