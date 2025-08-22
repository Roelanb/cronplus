using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Interfaces;
using System.Text.Json;

namespace Cronplus.Api.Features.Monitoring;

public class GetLogsRequest
{
    public string? TaskId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Status { get; set; } // Success, Failed, Running
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public bool IncludeDetails { get; set; } = false;
}

public class GetLogsResponse
{
    public List<ExecutionLogDto> Logs { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public LogsSummary Summary { get; set; } = new();
}

public class ExecutionLogDto
{
    public long Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public JsonDocument? ExecutionDetails { get; set; }
}

public class LogsSummary
{
    public int TotalExecutions { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int RunningCount { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageDuration { get; set; }
}

public class GetLogsValidator : Validator<GetLogsRequest>
{
    public GetLogsValidator()
    {
        RuleFor(x => x.TaskId)
            .MaximumLength(100)
            .When(x => !string.IsNullOrEmpty(x.TaskId));

        RuleFor(x => x.Status)
            .Must(s => new[] { "Success", "Failed", "Running" }.Contains(s, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrEmpty(x.Status))
            .WithMessage("Status must be one of: Success, Failed, Running");

        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 200);

        RuleFor(x => x.StartDate)
            .LessThanOrEqualTo(x => x.EndDate ?? DateTime.UtcNow)
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue)
            .WithMessage("Start date must be before end date");
    }
}

public class GetLogsEndpoint : Endpoint<GetLogsRequest, GetLogsResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GetLogsEndpoint> _logger;

    public GetLogsEndpoint(
        IUnitOfWork unitOfWork,
        ILogger<GetLogsEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public override void Configure()
    {
        Get("/api/logs");
        AllowAnonymous();
        Description(b => b
            .Produces<GetLogsResponse>(200)
            .ProducesProblemFE(400)
            .WithTags("Monitoring"));
        Summary(s =>
        {
            s.Summary = "Get execution logs";
            s.Description = "Retrieves paginated execution logs with optional filtering";
        });
    }

    public override async Task<GetLogsResponse> ExecuteAsync(GetLogsRequest req, CancellationToken ct)
    {
        _logger.LogDebug("Fetching logs with filters: TaskId={TaskId}, Status={Status}, StartDate={StartDate}, EndDate={EndDate}", 
            req.TaskId, req.Status, req.StartDate, req.EndDate);

        // Build WHERE clause
        var whereConditions = new List<string>();
        var parameters = new Dictionary<string, object?>();

        if (!string.IsNullOrEmpty(req.TaskId))
        {
            whereConditions.Add("task_id = $taskId");
            parameters["taskId"] = req.TaskId;
        }

        if (!string.IsNullOrEmpty(req.Status))
        {
            whereConditions.Add("status = $status");
            parameters["status"] = req.Status;
        }

        if (req.StartDate.HasValue)
        {
            whereConditions.Add("started_at >= $startDate");
            parameters["startDate"] = req.StartDate.Value;
        }

        if (req.EndDate.HasValue)
        {
            whereConditions.Add("started_at <= $endDate");
            parameters["endDate"] = req.EndDate.Value;
        }

        // Get logs
        IEnumerable<Domain.Entities.ExecutionLog> logs;
        if (whereConditions.Any())
        {
            var whereClause = string.Join(" AND ", whereConditions);
            logs = await _unitOfWork.ExecutionLogs.FindAsync(whereClause, parameters);
        }
        else
        {
            logs = await _unitOfWork.ExecutionLogs.GetAllAsync();
        }

        var logsList = logs.OrderByDescending(l => l.StartedAt).ToList();
        var totalCount = logsList.Count;

        // Calculate summary
        var summary = new LogsSummary
        {
            TotalExecutions = totalCount,
            SuccessCount = logsList.Count(l => l.Status == "Success"),
            FailedCount = logsList.Count(l => l.Status == "Failed"),
            RunningCount = logsList.Count(l => l.Status == "Running")
        };
        
        if (summary.TotalExecutions > 0)
        {
            summary.SuccessRate = (double)summary.SuccessCount / summary.TotalExecutions * 100;
            
            var completedLogs = logsList.Where(l => l.CompletedAt.HasValue).ToList();
            if (completedLogs.Any())
            {
                var totalTicks = completedLogs.Sum(l => (l.CompletedAt!.Value - l.StartedAt).Ticks);
                summary.AverageDuration = new TimeSpan(totalTicks / completedLogs.Count);
            }
        }

        // Apply pagination
        var pagedLogs = logsList
            .Skip((req.PageNumber - 1) * req.PageSize)
            .Take(req.PageSize)
            .ToList();

        // Get task names for better display
        var taskIds = pagedLogs.Select(l => l.TaskId).Distinct().ToList();
        var tasks = new Dictionary<string, string>();
        foreach (var taskId in taskIds)
        {
            var task = await _unitOfWork.Tasks.GetByIdAsync(taskId);
            if (task != null)
            {
                tasks[taskId] = task.Description ?? taskId;
            }
        }

        // Map to DTOs
        var logDtos = pagedLogs.Select(log => new ExecutionLogDto
        {
            Id = log.Id,
            TaskId = log.TaskId,
            TaskName = tasks.TryGetValue(log.TaskId, out var name) ? name : log.TaskId,
            FilePath = log.FilePath,
            FileName = Path.GetFileName(log.FilePath),
            Status = log.Status,
            StartedAt = log.StartedAt,
            CompletedAt = log.CompletedAt,
            Duration = log.CompletedAt.HasValue ? log.CompletedAt.Value - log.StartedAt : null,
            ErrorMessage = log.ErrorMessage,
            ExecutionDetails = req.IncludeDetails ? log.ExecutionDetails : null
        }).ToList();

        return new GetLogsResponse
        {
            Logs = logDtos,
            TotalCount = totalCount,
            PageNumber = req.PageNumber,
            PageSize = req.PageSize,
            Summary = summary
        };
    }
}