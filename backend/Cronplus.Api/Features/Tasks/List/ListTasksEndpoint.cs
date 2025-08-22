using Cronplus.Api.Domain.Interfaces;
using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Infrastructure.Database;
using Cronplus.Api.Common.Endpoints;
using Cronplus.Api.Common.Models;
using Cronplus.Api.Common.Processors;

namespace Cronplus.Api.Features.Tasks.List;

public class ListTasksRequest : PaginatedRequest
{
    public bool? EnabledOnly { get; set; }
}

public class ListTasksResponse : PaginatedResponse<TaskDto>
{
}

public class TaskDto
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string WatchDirectory { get; set; } = string.Empty;
    public string GlobPattern { get; set; } = string.Empty;
    public int PipelineStepsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastExecutedAt { get; set; }
    public string Status { get; set; } = "Idle";
}

public class ListTasksValidator : PaginatedRequestValidator<ListTasksRequest>
{
    public ListTasksValidator()
    {
        // Additional validation rules specific to ListTasks
        // Base validation is already handled by PaginatedRequestValidator
    }
}

[Cacheable(durationInSeconds: 30, varyByQueryKeys: new[] { "page", "pageSize", "searchTerm", "enabledOnly" })]
public class ListTasksEndpoint : BaseEndpoint<ListTasksRequest, ListTasksResponse>
{
    private readonly IUnitOfWork _unitOfWork;

    public ListTasksEndpoint(ILogger<ListTasksEndpoint> logger, IUnitOfWork unitOfWork)
    {
        Logger = logger;
        _unitOfWork = unitOfWork;
    }

    protected override string EndpointName => "ListTasks";
    protected override string EndpointSummary => "Get a paginated list of all configured tasks";

    protected override void ConfigureEndpoint()
    {
        Get("/tasks");
        Group<TasksGroup>();
        AllowAnonymous(); // Will be changed when auth is implemented
        Description(b => b
            .WithDescription("Returns a paginated list of tasks with their current status and configuration")
            .WithTags("Tasks"));
    }

    public override async Task HandleAsync(ListTasksRequest req, CancellationToken ct)
    {
        Logger.LogInformation("Listing tasks with page: {Page}, pageSize: {PageSize}", req.Page, req.PageSize);

        // Get tasks from database
        var allTasks = await _unitOfWork.Tasks.GetAllAsync();
        
        // Convert to DTOs and get additional info
        var taskDtos = new List<TaskDto>();
        foreach (var task in allTasks)
        {
            // Get pipeline steps count
            var steps = await _unitOfWork.PipelineSteps.GetByTaskIdAsync(task.Id);
            
            // Get last execution log
            var lastLog = await _unitOfWork.ExecutionLogs.GetLatestByTaskIdAsync(task.Id);
            
            taskDtos.Add(new TaskDto
            {
                Id = task.Id,
                Enabled = task.Enabled,
                WatchDirectory = task.WatchDirectory,
                GlobPattern = task.GlobPattern,
                PipelineStepsCount = steps.Count(),
                CreatedAt = task.CreatedAt,
                LastExecutedAt = lastLog?.CompletedAt,
                Status = task.Enabled ? (lastLog?.Status ?? "Idle") : "Disabled"
            });
        }

        // Apply filters
        var filteredTasks = taskDtos.AsEnumerable();

        if (req.EnabledOnly.HasValue && req.EnabledOnly.Value)
        {
            filteredTasks = filteredTasks.Where(t => t.Enabled);
        }

        if (!string.IsNullOrWhiteSpace(req.SearchTerm))
        {
            filteredTasks = filteredTasks.Where(t =>
                t.Id.Contains(req.SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                t.WatchDirectory.Contains(req.SearchTerm, StringComparison.OrdinalIgnoreCase));
        }

        // Apply sorting
        if (!string.IsNullOrWhiteSpace(req.SortBy))
        {
            filteredTasks = req.SortBy.ToLower() switch
            {
                "id" => req.SortDescending ? filteredTasks.OrderByDescending(t => t.Id) : filteredTasks.OrderBy(t => t.Id),
                "enabled" => req.SortDescending ? filteredTasks.OrderByDescending(t => t.Enabled) : filteredTasks.OrderBy(t => t.Enabled),
                "createdat" => req.SortDescending ? filteredTasks.OrderByDescending(t => t.CreatedAt) : filteredTasks.OrderBy(t => t.CreatedAt),
                _ => filteredTasks.OrderByDescending(t => t.CreatedAt)
            };
        }
        else
        {
            filteredTasks = filteredTasks.OrderByDescending(t => t.CreatedAt);
        }

        var totalCount = filteredTasks.Count();
        var pagedTasks = filteredTasks
            .Skip((req.Page - 1) * req.PageSize)
            .Take(req.PageSize)
            .ToList();

        Response = new ListTasksResponse
        {
            Items = pagedTasks,
            TotalCount = totalCount,
            Page = req.Page,
            PageSize = req.PageSize
        };
    }
}