using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Services.TaskSupervision;
using TaskSupervisorState = Cronplus.Api.Services.TaskSupervision.TaskState;

namespace Cronplus.Api.Features.Execution;

public class StartTaskRequest
{
    public string TaskId { get; set; } = string.Empty;
    public bool Force { get; set; } = false; // Force start even if already running
}

public class StartTaskResponse
{
    public string TaskId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class StartTaskValidator : Validator<StartTaskRequest>
{
    public StartTaskValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEmpty()
            .MaximumLength(100);
    }
}

public class StartTaskEndpoint : Endpoint<StartTaskRequest, StartTaskResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskSupervisorManager _supervisorManager;
    private readonly ILogger<StartTaskEndpoint> _logger;

    public StartTaskEndpoint(
        IUnitOfWork unitOfWork,
        ITaskSupervisorManager supervisorManager,
        ILogger<StartTaskEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _supervisorManager = supervisorManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/api/tasks/{TaskId}/start");
        AllowAnonymous();
        Description(b => b
            .Produces<StartTaskResponse>(200)
            .ProducesProblemFE(404)
            .ProducesProblemFE(400)
            .ProducesProblemFE(409)
            .WithTags("Execution"));
        Summary(s =>
        {
            s.Summary = "Start a task";
            s.Description = "Starts or restarts a task supervisor. The task must be enabled to start.";
        });
    }

    public override async Task<StartTaskResponse> ExecuteAsync(StartTaskRequest req, CancellationToken ct)
    {
        _logger.LogInformation("Starting task {TaskId} (Force: {Force})", req.TaskId, req.Force);

        // Check if task exists
        var task = await _unitOfWork.Tasks.GetByIdAsync(req.TaskId);
        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", req.TaskId);
            ThrowError("Resource not found", 404);
            return null!; // Won't be reached due to SendNotFoundAsync
        }

        // Check if task is enabled
        if (!task.Enabled)
        {
            _logger.LogWarning("Cannot start disabled task {TaskId}", req.TaskId);
            return new StartTaskResponse
            {
                TaskId = req.TaskId,
                Success = false,
                Message = "Task is disabled. Enable the task first before starting.",
                Status = "Disabled"
            };
        }

        // Get current status
        var currentStatus = _supervisorManager.GetTaskStatus(req.TaskId.ToString());
        
        // Check if already running
        if ((currentStatus == TaskSupervisorState.Processing || currentStatus == TaskSupervisorState.Idle) && !req.Force)
        {
            _logger.LogInformation("Task {TaskId} is already running", req.TaskId);
            return new StartTaskResponse
            {
                TaskId = req.TaskId,
                Success = true,
                Message = "Task is already running",
                Status = currentStatus.ToString()
            };
        }

        try
        {
            // Start or reload the task
            await _supervisorManager.ReloadTaskAsync(req.TaskId.ToString(), ct);
            
            // Wait a moment for the supervisor to initialize
            await Task.Delay(500, ct);
            
            // Get new status
            var newStatus = _supervisorManager.GetTaskStatus(req.TaskId.ToString());
            
            _logger.LogInformation("Successfully started task {TaskId}. Status: {Status}", req.TaskId, newStatus);

            return new StartTaskResponse
            {
                TaskId = req.TaskId,
                Success = true,
                Message = req.Force ? "Task restarted successfully" : "Task started successfully",
                Status = newStatus?.ToString() ?? "Unknown"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start task {TaskId}", req.TaskId);
            
            return new StartTaskResponse
            {
                TaskId = req.TaskId,
                Success = false,
                Message = $"Failed to start task: {ex.Message}",
                Status = "Failed"
            };
        }
    }
}