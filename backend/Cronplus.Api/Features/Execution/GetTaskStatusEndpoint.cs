using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Services.TaskSupervision;
using TaskSupervisorState = Cronplus.Api.Services.TaskSupervision.TaskState;

namespace Cronplus.Api.Features.Execution;

public class GetTaskStatusRequest
{
    public string TaskId { get; set; } = string.Empty;
}

public class GetTaskStatusResponse
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public bool IsRunning { get; set; }
    public bool IsEnabled { get; set; }
    public SupervisorInfo? SupervisorInfo { get; set; }
    public RecentActivity? RecentActivity { get; set; }
}

public class SupervisorInfo
{
    public string State { get; set; } = string.Empty;
    public DateTime LastStateChange { get; set; }
    public string? LastError { get; set; }
    public int ProcessedFiles { get; set; }
    public int FailedFiles { get; set; }
    public DateTime? LastFileProcessed { get; set; }
}

public class RecentActivity
{
    public List<RecentFile> ProcessedFiles { get; set; } = new();
    public List<RecentError> Errors { get; set; } = new();
}

public class RecentFile
{
    public string FileName { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
}

public class RecentError
{
    public DateTime OccurredAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? StepName { get; set; }
}

public class GetTaskStatusValidator : Validator<GetTaskStatusRequest>
{
    public GetTaskStatusValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEmpty()
            .MaximumLength(100);
    }
}

public class GetTaskStatusEndpoint : Endpoint<GetTaskStatusRequest, GetTaskStatusResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskSupervisorManager _supervisorManager;
    private readonly ILogger<GetTaskStatusEndpoint> _logger;

    public GetTaskStatusEndpoint(
        IUnitOfWork unitOfWork,
        ITaskSupervisorManager supervisorManager,
        ILogger<GetTaskStatusEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _supervisorManager = supervisorManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Get("/api/tasks/{TaskId}/status");
        AllowAnonymous();
        Description(b => b
            .Produces<GetTaskStatusResponse>(200)
            .ProducesProblemFE(404)
            .ProducesProblemFE(400)
            .WithTags("Execution"));
        Summary(s =>
        {
            s.Summary = "Get task execution status";
            s.Description = "Retrieves detailed status information about a task's execution state";
        });
    }

    public override async Task<GetTaskStatusResponse> ExecuteAsync(GetTaskStatusRequest req, CancellationToken ct)
    {
        _logger.LogDebug("Getting status for task {TaskId}", req.TaskId);

        // Check if task exists
        var task = await _unitOfWork.Tasks.GetByIdAsync(req.TaskId);
        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", req.TaskId);
            ThrowError("Resource not found", 404);
            return new GetTaskStatusResponse();
        }

        // Get supervisor status
        var status = _supervisorManager.GetTaskStatus(req.TaskId.ToString());
        var supervisorInfo = _supervisorManager.GetSupervisorInfo(req.TaskId.ToString());

        var response = new GetTaskStatusResponse
        {
            TaskId = req.TaskId,
            Status = status?.ToString() ?? "Unknown",
            IsRunning = status == TaskSupervisorState.Processing || status == TaskSupervisorState.Idle,
            IsEnabled = task.Enabled
        };

        if (supervisorInfo != null)
        {
            response.SupervisorInfo = new SupervisorInfo
            {
                State = supervisorInfo.State.ToString(),
                LastStateChange = supervisorInfo.LastStateChange,
                LastError = supervisorInfo.LastError,
                ProcessedFiles = supervisorInfo.ProcessedFiles,
                FailedFiles = supervisorInfo.FailedFiles,
                LastFileProcessed = supervisorInfo.LastFileProcessed
            };
        }

        // Get recent activity from execution logs
        try
        {
            var recentLogs = await _unitOfWork.ExecutionLogs.FindAsync(
                "task_id = $taskId AND started_at > $since",
                new { taskId = req.TaskId, since = DateTime.UtcNow.AddHours(-1) });

            var logsList = recentLogs.OrderByDescending(l => l.StartedAt).Take(10).ToList();

            response.RecentActivity = new RecentActivity
            {
                ProcessedFiles = logsList
                    .Where(l => l.Status == "Success")
                    .Select(l => new RecentFile
                    {
                        FileName = Path.GetFileName(l.FilePath),
                        ProcessedAt = l.StartedAt,
                        Status = l.Status,
                        Duration = l.CompletedAt.HasValue 
                            ? l.CompletedAt.Value - l.StartedAt 
                            : TimeSpan.Zero
                    })
                    .ToList(),
                    
                Errors = logsList
                    .Where(l => l.Status == "Failed" && !string.IsNullOrEmpty(l.ErrorMessage))
                    .Select(l => new RecentError
                    {
                        OccurredAt = l.StartedAt,
                        Message = l.ErrorMessage ?? "Unknown error",
                        FileName = Path.GetFileName(l.FilePath)
                    })
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get recent activity for task {TaskId}", req.TaskId);
        }

        return response;
    }
}