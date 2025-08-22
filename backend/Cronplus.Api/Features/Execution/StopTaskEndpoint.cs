using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Services.TaskSupervision;
using TaskSupervisorState = Cronplus.Api.Services.TaskSupervision.TaskState;

namespace Cronplus.Api.Features.Execution;

public class StopTaskRequest
{
    public string TaskId { get; set; } = string.Empty;
    public bool Graceful { get; set; } = true; // Allow current operations to complete
}

public class StopTaskResponse
{
    public string TaskId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class StopTaskValidator : Validator<StopTaskRequest>
{
    public StopTaskValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEmpty()
            .MaximumLength(100);
    }
}

public class StopTaskEndpoint : Endpoint<StopTaskRequest, StopTaskResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskSupervisorManager _supervisorManager;
    private readonly ILogger<StopTaskEndpoint> _logger;

    public StopTaskEndpoint(
        IUnitOfWork unitOfWork,
        ITaskSupervisorManager supervisorManager,
        ILogger<StopTaskEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _supervisorManager = supervisorManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/api/tasks/{TaskId}/stop");
        AllowAnonymous();
        Description(b => b
            .Produces<StopTaskResponse>(200)
            .ProducesProblemFE(404)
            .ProducesProblemFE(400)
            .WithTags("Execution"));
        Summary(s =>
        {
            s.Summary = "Stop a running task";
            s.Description = "Stops a task supervisor. The task remains enabled but the supervisor is stopped.";
        });
    }

    public override async Task<StopTaskResponse> ExecuteAsync(StopTaskRequest req, CancellationToken ct)
    {
        _logger.LogInformation("Stopping task {TaskId} (Graceful: {Graceful})", req.TaskId, req.Graceful);

        // Check if task exists
        var task = await _unitOfWork.Tasks.GetByIdAsync(req.TaskId);
        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", req.TaskId);
            ThrowError("Resource not found", 404);
            return new StopTaskResponse();
        }

        // Get current status
        var currentStatus = _supervisorManager.GetTaskStatus(req.TaskId.ToString());
        
        // Check if already stopped
        if (currentStatus == TaskSupervisorState.Stopped || 
            currentStatus == TaskSupervisorState.Created ||
            currentStatus == null)
        {
            _logger.LogInformation("Task {TaskId} is not running", req.TaskId);
            return new StopTaskResponse
            {
                TaskId = req.TaskId,
                Success = true,
                Message = "Task is not running",
                Status = currentStatus?.ToString() ?? "Stopped"
            };
        }

        try
        {
            // Stop the task
            await _supervisorManager.StopTaskAsync(req.TaskId.ToString(), ct);
            
            // Wait a moment for the supervisor to stop
            if (req.Graceful)
            {
                // Give it more time for graceful shutdown
                await Task.Delay(1000, ct);
            }
            else
            {
                await Task.Delay(500, ct);
            }
            
            // Get new status
            var newStatus = _supervisorManager.GetTaskStatus(req.TaskId.ToString());
            
            _logger.LogInformation("Successfully stopped task {TaskId}. Status: {Status}", req.TaskId, newStatus);

            return new StopTaskResponse
            {
                TaskId = req.TaskId,
                Success = true,
                Message = "Task stopped successfully",
                Status = newStatus?.ToString() ?? "Stopped"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop task {TaskId}", req.TaskId);
            
            return new StopTaskResponse
            {
                TaskId = req.TaskId,
                Success = false,
                Message = $"Failed to stop task: {ex.Message}",
                Status = currentStatus?.ToString() ?? "Unknown"
            };
        }
    }
}