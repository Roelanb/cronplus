using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Services.TaskSupervision;
using TaskSupervisorState = Cronplus.Api.Services.TaskSupervision.TaskState;

namespace Cronplus.Api.Features.Tasks;

public class GetTasksRequest
{
    public bool? EnabledOnly { get; set; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
    public string? SortBy { get; set; } = "CreatedAt";
    public bool? SortDescending { get; set; } = true;
}

public class GetTasksResponse
{
    public List<TaskDto> Tasks { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}

public class TaskDto
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
    public int PipelineStepCount { get; set; }
    public int VariableCount { get; set; }
    public string Status { get; set; } = "Unknown";
}

public class GetTasksValidator : Validator<GetTasksRequest>
{
    public GetTasksValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1)
            .When(x => x.PageNumber.HasValue);
            
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .When(x => x.PageSize.HasValue);
            
        RuleFor(x => x.SortBy)
            .Must(x => new[] { "Id", "CreatedAt", "UpdatedAt", "WatchDirectory" }.Contains(x, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrEmpty(x.SortBy))
            .WithMessage("SortBy must be one of: Id, CreatedAt, UpdatedAt, WatchDirectory");
    }
}

public class GetTasksEndpoint : Endpoint<GetTasksRequest, GetTasksResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskSupervisorManager _supervisorManager;
    private readonly ILogger<GetTasksEndpoint> _logger;

    public GetTasksEndpoint(
        IUnitOfWork unitOfWork, 
        ITaskSupervisorManager supervisorManager,
        ILogger<GetTasksEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _supervisorManager = supervisorManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Get("/api/tasks");
        AllowAnonymous();
        Description(b => b
            .Produces<GetTasksResponse>(200)
            .ProducesProblemFE(400)
            .WithTags("Tasks"));
        Summary(s =>
        {
            s.Summary = "Get all tasks with optional filtering";
            s.Description = "Retrieves a paginated list of tasks with optional filtering by enabled status";
        });
    }

    public override async Task<GetTasksResponse> ExecuteAsync(GetTasksRequest req, CancellationToken ct)
    {
        _logger.LogDebug("Fetching tasks with filters: EnabledOnly={EnabledOnly}, Page={Page}, Size={Size}", 
            req.EnabledOnly, req.PageNumber, req.PageSize);

        // Get tasks based on filter
        var tasks = req.EnabledOnly == true 
            ? await _unitOfWork.Tasks.GetEnabledTasksAsync()
            : await _unitOfWork.Tasks.GetAllAsync();

        // Apply sorting
        tasks = req.SortBy?.ToLower() switch
        {
            "id" => req.SortDescending == true 
                ? tasks.OrderByDescending(t => t.Id) 
                : tasks.OrderBy(t => t.Id),
            "updatedat" => req.SortDescending == true 
                ? tasks.OrderByDescending(t => t.UpdatedAt) 
                : tasks.OrderBy(t => t.UpdatedAt),
            "watchdirectory" => req.SortDescending == true 
                ? tasks.OrderByDescending(t => t.WatchDirectory) 
                : tasks.OrderBy(t => t.WatchDirectory),
            _ => req.SortDescending == true 
                ? tasks.OrderByDescending(t => t.CreatedAt) 
                : tasks.OrderBy(t => t.CreatedAt)
        };

        var taskList = tasks.ToList();
        var totalCount = taskList.Count;

        // Apply pagination
        var pageNumber = req.PageNumber ?? 1;
        var pageSize = req.PageSize ?? 20;
        var pagedTasks = taskList
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // Map to DTOs
        var taskDtos = new List<TaskDto>();
        foreach (var task in pagedTasks)
        {
            // Get additional details
            var taskWithDetails = await _unitOfWork.Tasks.GetByIdWithDetailsAsync(task.Id);
            var status = _supervisorManager.GetTaskStatus(task.Id.ToString());
            
            taskDtos.Add(new TaskDto
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
                PipelineStepCount = taskWithDetails?.PipelineSteps?.Count ?? 0,
                VariableCount = taskWithDetails?.Variables?.Count ?? 0,
                Status = status?.ToString() ?? "Unknown"
            });
        }

        return new GetTasksResponse
        {
            Tasks = taskDtos,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }
}