using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Services.TaskSupervision;

namespace Cronplus.Api.Features.Tasks;

public class UpdateTaskRequest
{
    public string Id { get; set; } = string.Empty;
    public bool? Enabled { get; set; }
    public string? WatchDirectory { get; set; }
    public string? GlobPattern { get; set; }
    public int? DebounceMs { get; set; }
    public int? StabilizationMs { get; set; }
    public string? Description { get; set; }
}

public class UpdateTaskResponse
{
    public string Id { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool SupervisorReloaded { get; set; }
}

public class UpdateTaskValidator : Validator<UpdateTaskRequest>
{
    public UpdateTaskValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.WatchDirectory)
            .NotEmpty()
            .MaximumLength(500)
            .When(x => x.WatchDirectory != null);

        RuleFor(x => x.GlobPattern)
            .NotEmpty()
            .MaximumLength(100)
            .When(x => x.GlobPattern != null);

        RuleFor(x => x.DebounceMs)
            .InclusiveBetween(0, 60000)
            .When(x => x.DebounceMs.HasValue)
            .WithMessage("Debounce must be between 0 and 60000 milliseconds");

        RuleFor(x => x.StabilizationMs)
            .InclusiveBetween(0, 300000)
            .When(x => x.StabilizationMs.HasValue)
            .WithMessage("Stabilization must be between 0 and 300000 milliseconds");

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description != null);
    }
}

public class UpdateTaskEndpoint : Endpoint<UpdateTaskRequest, UpdateTaskResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskSupervisorManager _supervisorManager;
    private readonly ILogger<UpdateTaskEndpoint> _logger;

    public UpdateTaskEndpoint(
        IUnitOfWork unitOfWork,
        ITaskSupervisorManager supervisorManager,
        ILogger<UpdateTaskEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _supervisorManager = supervisorManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Put("/api/tasks/{id}");
        AllowAnonymous();
        Description(b => b
            .Produces<UpdateTaskResponse>(200)
            .ProducesProblemFE(404)
            .ProducesProblemFE(400)
            .WithTags("Tasks"));
        Summary(s =>
        {
            s.Summary = "Update an existing task";
            s.Description = "Updates task configuration. Only provided fields will be updated.";
        });
    }

    public override async Task<UpdateTaskResponse> ExecuteAsync(UpdateTaskRequest req, CancellationToken ct)
    {
        _logger.LogInformation("Updating task {TaskId}", req.Id);

        // Get existing task
        var task = await _unitOfWork.Tasks.GetByIdAsync(req.Id);
        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", req.Id);
            ThrowError("Resource not found", 404);
            return new UpdateTaskResponse();
        }

        var wasEnabled = task.Enabled;
        var hasChanges = false;

        // Update only provided fields
        if (req.Enabled.HasValue && task.Enabled != req.Enabled.Value)
        {
            task.Enabled = req.Enabled.Value;
            hasChanges = true;
        }

        if (!string.IsNullOrEmpty(req.WatchDirectory) && task.WatchDirectory != req.WatchDirectory)
        {
            task.WatchDirectory = req.WatchDirectory;
            hasChanges = true;
        }

        if (!string.IsNullOrEmpty(req.GlobPattern) && task.GlobPattern != req.GlobPattern)
        {
            task.GlobPattern = req.GlobPattern;
            hasChanges = true;
        }

        if (req.DebounceMs.HasValue && task.DebounceMs != req.DebounceMs.Value)
        {
            task.DebounceMs = req.DebounceMs.Value;
            hasChanges = true;
        }

        if (req.StabilizationMs.HasValue && task.StabilizationMs != req.StabilizationMs.Value)
        {
            task.StabilizationMs = req.StabilizationMs.Value;
            hasChanges = true;
        }

        if (req.Description != null && task.Description != req.Description)
        {
            task.Description = req.Description;
            hasChanges = true;
        }

        if (!hasChanges)
        {
            _logger.LogInformation("No changes detected for task {TaskId}", req.Id);
            return new UpdateTaskResponse
            {
                Id = req.Id,
                Success = true,
                Message = "No changes detected",
                SupervisorReloaded = false
            };
        }

        try
        {
            // Update task in database
            var updated = await _unitOfWork.Tasks.UpdateAsync(task);
            
            if (!updated)
            {
                _logger.LogError("Failed to update task {TaskId} in database", req.Id);
                return new UpdateTaskResponse
                {
                    Id = req.Id,
                    Success = false,
                    Message = "Failed to update task in database",
                    SupervisorReloaded = false
                };
            }

            // Handle supervisor reload if needed
            var supervisorReloaded = false;
            
            // If task was disabled and now enabled, start supervisor
            if (!wasEnabled && task.Enabled)
            {
                await _supervisorManager.ReloadTaskAsync(req.Id.ToString(), ct);
                supervisorReloaded = true;
                _logger.LogInformation("Started supervisor for newly enabled task {TaskId}", req.Id);
            }
            // If task was enabled and now disabled, stop supervisor
            else if (wasEnabled && !task.Enabled)
            {
                await _supervisorManager.StopTaskAsync(req.Id, ct);
                supervisorReloaded = true;
                _logger.LogInformation("Stopped supervisor for disabled task {TaskId}", req.Id);
            }
            // If task is enabled and configuration changed, reload supervisor
            else if (task.Enabled && hasChanges)
            {
                await _supervisorManager.ReloadTaskAsync(req.Id.ToString(), ct);
                supervisorReloaded = true;
                _logger.LogInformation("Reloaded supervisor for updated task {TaskId}", req.Id);
            }

            _logger.LogInformation("Successfully updated task {TaskId}", req.Id);

            return new UpdateTaskResponse
            {
                Id = req.Id,
                Success = true,
                Message = "Task updated successfully",
                SupervisorReloaded = supervisorReloaded
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update task {TaskId}", req.Id);
            
            return new UpdateTaskResponse
            {
                Id = req.Id,
                Success = false,
                Message = $"Failed to update task: {ex.Message}",
                SupervisorReloaded = false
            };
        }
    }
}