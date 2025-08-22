using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Interfaces;

namespace Cronplus.Api.Features.Variables;

public class AddVariableRequest
{
    public string TaskId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string Value { get; set; } = string.Empty;
}

public class AddVariableResponse
{
    public int Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class AddVariableValidator : Validator<AddVariableRequest>
{
    public AddVariableValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEmpty()
            .MaximumLength(100)
            .WithMessage("TaskId is required and must not exceed 100 characters");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]*$")
            .WithMessage("Variable name must start with a letter or underscore and contain only letters, numbers, and underscores");

        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(type => new[] { "string", "number", "boolean", "json", "date" }.Contains(type.ToLower()))
            .WithMessage("Type must be one of: string, number, boolean, json, date");

        RuleFor(x => x.Value)
            .NotEmpty()
            .MaximumLength(5000);

    }
}

public class AddVariableEndpoint : Endpoint<AddVariableRequest, AddVariableResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AddVariableEndpoint> _logger;

    public AddVariableEndpoint(
        IUnitOfWork unitOfWork,
        ILogger<AddVariableEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/api/tasks/{TaskId}/variables");
        AllowAnonymous();
        Description(b => b
            .Produces<AddVariableResponse>(201)
            .ProducesProblemFE(400)
            .ProducesProblemFE(404)
            .ProducesProblemFE(409)
            .WithTags("Variables"));
        Summary(s =>
        {
            s.Summary = "Add a new variable to a task";
            s.Description = "Creates a new variable for the specified task";
        });
    }

    public override async Task<AddVariableResponse> ExecuteAsync(AddVariableRequest req, CancellationToken ct)
    {
        _logger.LogInformation("Adding variable {VariableName} to task {TaskId}", req.Name, req.TaskId);

        // Check if task exists
        var task = await _unitOfWork.Tasks.GetByIdAsync(req.TaskId);
        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", req.TaskId);
            ThrowError("Task not found", 404);
        }

        // Check if variable with same name already exists
        var existingVariables = await _unitOfWork.TaskVariables.GetByTaskIdAsync(req.TaskId);
        if (existingVariables.Any(v => v.Name.Equals(req.Name, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Variable {VariableName} already exists for task {TaskId}", req.Name, req.TaskId);
            ThrowError($"Variable '{req.Name}' already exists for this task", 409);
        }

        // Validate value based on type
        if (!ValidateVariableValue(req.Type, req.Value))
        {
            _logger.LogWarning("Invalid value for variable type {Type}: {Value}", req.Type, req.Value);
            ThrowError($"Invalid value for type '{req.Type}'", 400);
        }

        // Create variable
        var variable = new TaskVariable
        {
            TaskId = req.TaskId,
            Name = req.Name,
            Type = req.Type.ToLower(),
            Value = req.Value
        };

        await _unitOfWork.TaskVariables.AddAsync(variable);
        _unitOfWork.Commit();

        _logger.LogInformation("Successfully added variable {VariableName} to task {TaskId}", req.Name, req.TaskId);

        return new AddVariableResponse
        {
            Id = variable.Id,
            TaskId = req.TaskId,
            Name = req.Name,
            Success = true,
            Message = "Variable added successfully"
        };
    }

    private bool ValidateVariableValue(string type, string value)
    {
        return type.ToLower() switch
        {
            "number" => double.TryParse(value, out _),
            "boolean" => bool.TryParse(value, out _),
            "date" => DateTime.TryParse(value, out _),
            "json" => IsValidJson(value),
            _ => true // string and others are always valid
        };
    }

    private bool IsValidJson(string value)
    {
        try
        {
            System.Text.Json.JsonDocument.Parse(value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}