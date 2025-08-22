using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Services.TaskSupervision;
using System.Text.Json;
using TaskSupervisorState = Cronplus.Api.Services.TaskSupervision.TaskState;

namespace Cronplus.Api.Features.Tasks;

public class GetTaskByIdRequest
{
    public string Id { get; set; } = string.Empty;
    public bool IncludeDetails { get; set; } = true;
}

public class GetTaskByIdResponse
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string WatchDirectory { get; set; } = string.Empty;
    public string GlobPattern { get; set; } = string.Empty;
    public int DebounceMs { get; set; }
    public int StabilizationMs { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<PipelineStepDto>? PipelineSteps { get; set; }
    public List<TaskVariableDto>? Variables { get; set; }
    public string Status { get; set; } = "Unknown";
    public Dictionary<string, object>? Metadata { get; set; }
}

public class PipelineStepDto
{
    public int Id { get; set; }
    public int StepOrder { get; set; }
    public string Type { get; set; } = string.Empty;
    public JsonDocument? Configuration { get; set; }
    public int? RetryMax { get; set; }
    public int? RetryBackoffMs { get; set; }
}

public class TaskVariableDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class GetTaskByIdValidator : Validator<GetTaskByIdRequest>
{
    public GetTaskByIdValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .MaximumLength(100);
    }
}

public class GetTaskByIdEndpoint : Endpoint<GetTaskByIdRequest, GetTaskByIdResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskSupervisorManager _supervisorManager;
    private readonly ILogger<GetTaskByIdEndpoint> _logger;

    public GetTaskByIdEndpoint(
        IUnitOfWork unitOfWork,
        ITaskSupervisorManager supervisorManager,
        ILogger<GetTaskByIdEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _supervisorManager = supervisorManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Get("/api/tasks/{id}");
        AllowAnonymous();
        Description(b => b
            .Produces<GetTaskByIdResponse>(200)
            .ProducesProblemFE(404)
            .ProducesProblemFE(400)
            .WithTags("Tasks"));
        Summary(s =>
        {
            s.Summary = "Get a specific task by ID";
            s.Description = "Retrieves detailed information about a specific task including pipeline steps and variables";
        });
    }

    public override async Task<GetTaskByIdResponse> ExecuteAsync(GetTaskByIdRequest req, CancellationToken ct)
    {
        _logger.LogDebug("Fetching task {TaskId} with details: {IncludeDetails}", req.Id, req.IncludeDetails);

        var task = req.IncludeDetails
            ? await _unitOfWork.Tasks.GetByIdWithDetailsAsync(req.Id)
            : await _unitOfWork.Tasks.GetByIdAsync(req.Id);

        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", req.Id);
            ThrowError("Resource not found", 404);
            return new GetTaskByIdResponse(); // Won't be reached
        }

        var status = _supervisorManager.GetTaskStatus(req.Id.ToString());
        var supervisorInfo = _supervisorManager.GetSupervisorInfo(req.Id.ToString());

        var response = new GetTaskByIdResponse
        {
            Id = task.Id,
            Enabled = task.Enabled,
            WatchDirectory = task.WatchDirectory,
            GlobPattern = task.GlobPattern,
            DebounceMs = task.DebounceMs,
            StabilizationMs = task.StabilizationMs,
            Description = task.Description,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            Status = status?.ToString() ?? "Unknown"
        };

        if (req.IncludeDetails && task.PipelineSteps != null)
        {
            response.PipelineSteps = task.PipelineSteps
                .OrderBy(s => s.StepOrder)
                .Select(s => new PipelineStepDto
                {
                    Id = s.Id,
                    StepOrder = s.StepOrder,
                    Type = s.Type,
                    Configuration = s.Configuration,
                    RetryMax = s.RetryMax,
                    RetryBackoffMs = s.RetryBackoffMs
                })
                .ToList();
        }

        if (req.IncludeDetails && task.Variables != null)
        {
            response.Variables = task.Variables
                .Select(v => new TaskVariableDto
                {
                    Id = v.Id,
                    Name = v.Name,
                    Type = v.Type,
                    Value = v.Value
                })
                .ToList();
        }

        if (supervisorInfo != null)
        {
            response.Metadata = new Dictionary<string, object>
            {
                ["SupervisorState"] = supervisorInfo.State.ToString(),
                ["LastStateChange"] = supervisorInfo.LastStateChange,
                ["ProcessedFiles"] = supervisorInfo.ProcessedFiles,
                ["FailedFiles"] = supervisorInfo.FailedFiles
            };
        }

        return response;
    }
}