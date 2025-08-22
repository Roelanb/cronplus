using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Services.TaskSupervision;

namespace Cronplus.Api.Features.Tasks;

public class DeleteTaskRequest
{
    public string Id { get; set; } = string.Empty;
}

public class DeleteTaskResponse
{
    public string Id { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class DeleteTaskValidator : Validator<DeleteTaskRequest>
{
    public DeleteTaskValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .MaximumLength(100);
    }
}

public class DeleteTaskEndpoint : Endpoint<DeleteTaskRequest, DeleteTaskResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskSupervisorManager _supervisorManager;
    private readonly ILogger<DeleteTaskEndpoint> _logger;

    public DeleteTaskEndpoint(
        IUnitOfWork unitOfWork,
        ITaskSupervisorManager supervisorManager,
        ILogger<DeleteTaskEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _supervisorManager = supervisorManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Delete("/api/tasks/{id}");
        AllowAnonymous();
        Description(b => b
            .Produces<DeleteTaskResponse>(200)
            .ProducesProblemFE(404)
            .ProducesProblemFE(400)
            .WithTags("Tasks"));
        Summary(s =>
        {
            s.Summary = "Delete a task";
            s.Description = "Deletes a task and all associated data (pipeline steps, variables, logs)";
        });
    }

    public override async Task<DeleteTaskResponse> ExecuteAsync(DeleteTaskRequest req, CancellationToken ct)
    {
        _logger.LogInformation("Deleting task {TaskId}", req.Id);

        // Check if task exists
        var task = await _unitOfWork.Tasks.GetByIdAsync(req.Id);
        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", req.Id);
            ThrowError("Resource not found", 404);
            return new DeleteTaskResponse();
        }

        try
        {
            // Stop supervisor if running
            if (task.Enabled)
            {
                await _supervisorManager.StopTaskAsync(req.Id, ct);
                _logger.LogInformation("Stopped supervisor for task {TaskId} before deletion", req.Id);
            }

            // Delete task (cascade will handle related records)
            var deleted = await _unitOfWork.Tasks.DeleteAsync(req.Id);
            
            if (!deleted)
            {
                _logger.LogError("Failed to delete task {TaskId} from database", req.Id);
                return new DeleteTaskResponse
                {
                    Id = req.Id,
                    Success = false,
                    Message = "Failed to delete task from database"
                };
            }

            _logger.LogInformation("Successfully deleted task {TaskId}", req.Id);

            return new DeleteTaskResponse
            {
                Id = req.Id,
                Success = true,
                Message = "Task deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete task {TaskId}", req.Id);
            
            return new DeleteTaskResponse
            {
                Id = req.Id,
                Success = false,
                Message = $"Failed to delete task: {ex.Message}"
            };
        }
    }
}