using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Interfaces;

namespace Cronplus.Api.Features.Variables;

public class GetVariablesRequest
{
    public string TaskId { get; set; } = string.Empty;
}

public class GetVariablesResponse
{
    public List<VariableDto> Variables { get; set; } = new();
    public string TaskId { get; set; } = string.Empty;
}

public class VariableDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class GetVariablesValidator : Validator<GetVariablesRequest>
{
    public GetVariablesValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEmpty()
            .MaximumLength(100)
            .WithMessage("TaskId is required and must not exceed 100 characters");
    }
}

public class GetVariablesEndpoint : Endpoint<GetVariablesRequest, GetVariablesResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GetVariablesEndpoint> _logger;

    public GetVariablesEndpoint(
        IUnitOfWork unitOfWork,
        ILogger<GetVariablesEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public override void Configure()
    {
        Get("/api/tasks/{TaskId}/variables");
        AllowAnonymous();
        Description(b => b
            .Produces<GetVariablesResponse>(200)
            .ProducesProblemFE(400)
            .ProducesProblemFE(404)
            .WithTags("Variables"));
        Summary(s =>
        {
            s.Summary = "Get all variables for a task";
            s.Description = "Retrieves all variables associated with a specific task";
        });
    }

    public override async Task<GetVariablesResponse> ExecuteAsync(GetVariablesRequest req, CancellationToken ct)
    {
        _logger.LogDebug("Fetching variables for task {TaskId}", req.TaskId);

        // Check if task exists
        var task = await _unitOfWork.Tasks.GetByIdAsync(req.TaskId);
        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", req.TaskId);
            ThrowError("Task not found", 404);
        }

        // Get variables
        var variables = await _unitOfWork.TaskVariables.GetByTaskIdAsync(req.TaskId);

        var variableDtos = variables.Select(v => new VariableDto
        {
            Id = v.Id,
            Name = v.Name,
            Type = v.Type,
            Value = v.Value,
            CreatedAt = v.CreatedAt,
            UpdatedAt = v.UpdatedAt
        }).ToList();

        return new GetVariablesResponse
        {
            TaskId = req.TaskId,
            Variables = variableDtos
        };
    }
}