using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Interfaces;

namespace Cronplus.Api.Features.Variables;

public class UpdateVariableRequest
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Value { get; set; }
}

public class UpdateVariableResponse
{
    public int Id { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class UpdateVariableValidator : Validator<UpdateVariableRequest>
{
    public UpdateVariableValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Variable ID must be a positive integer");

        RuleFor(x => x.Name)
            .MaximumLength(100)
            .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]*$")
            .When(x => !string.IsNullOrEmpty(x.Name))
            .WithMessage("Variable name must start with a letter or underscore and contain only letters, numbers, and underscores");

        RuleFor(x => x.Type)
            .Must(type => new[] { "string", "number", "boolean", "json", "date" }.Contains(type!.ToLower()))
            .When(x => !string.IsNullOrEmpty(x.Type))
            .WithMessage("Type must be one of: string, number, boolean, json, date");

        RuleFor(x => x.Value)
            .MaximumLength(5000)
            .When(x => x.Value != null);

    }
}

public class UpdateVariableEndpoint : Endpoint<UpdateVariableRequest, UpdateVariableResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateVariableEndpoint> _logger;

    public UpdateVariableEndpoint(
        IUnitOfWork unitOfWork,
        ILogger<UpdateVariableEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public override void Configure()
    {
        Put("/api/variables/{Id}");
        AllowAnonymous();
        Description(b => b
            .Produces<UpdateVariableResponse>(200)
            .ProducesProblemFE(400)
            .ProducesProblemFE(404)
            .WithTags("Variables"));
        Summary(s =>
        {
            s.Summary = "Update a variable";
            s.Description = "Updates an existing variable's properties";
        });
    }

    public override async Task<UpdateVariableResponse> ExecuteAsync(UpdateVariableRequest req, CancellationToken ct)
    {
        _logger.LogInformation("Updating variable {VariableId}", req.Id);

        // Get existing variable
        var variable = await _unitOfWork.TaskVariables.GetByIdAsync(req.Id);
        if (variable == null)
        {
            _logger.LogWarning("Variable {VariableId} not found", req.Id);
            ThrowError("Variable not found", 404);
        }

        var hasChanges = false;

        // Update only provided fields
        if (!string.IsNullOrEmpty(req.Name) && variable.Name != req.Name)
        {
            // Check if new name conflicts with existing variable
            var existingVariables = await _unitOfWork.TaskVariables.GetByTaskIdAsync(variable.TaskId);
            if (existingVariables.Any(v => v.Id != req.Id && v.Name.Equals(req.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Variable name {VariableName} already exists for task {TaskId}", req.Name, variable.TaskId);
                ThrowError($"Variable '{req.Name}' already exists for this task", 409);
            }
            
            variable.Name = req.Name;
            hasChanges = true;
        }

        if (!string.IsNullOrEmpty(req.Type) && variable.Type != req.Type.ToLower())
        {
            // If type is changing, validate the current or new value
            var valueToValidate = req.Value ?? variable.Value;
            if (!ValidateVariableValue(req.Type, valueToValidate))
            {
                _logger.LogWarning("Invalid value for new type {Type}: {Value}", req.Type, valueToValidate);
                ThrowError($"Current value is invalid for type '{req.Type}'", 400);
            }
            
            variable.Type = req.Type.ToLower();
            hasChanges = true;
        }

        if (req.Value != null && variable.Value != req.Value)
        {
            // Validate new value against type
            var typeToValidate = !string.IsNullOrEmpty(req.Type) ? req.Type : variable.Type;
            if (!ValidateVariableValue(typeToValidate, req.Value))
            {
                _logger.LogWarning("Invalid value for type {Type}: {Value}", typeToValidate, req.Value);
                ThrowError($"Invalid value for type '{typeToValidate}'", 400);
            }
            
            variable.Value = req.Value;
            hasChanges = true;
        }


        if (hasChanges)
        {
            variable.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.TaskVariables.UpdateAsync(variable);
            _unitOfWork.Commit();
            
            _logger.LogInformation("Successfully updated variable {VariableId}", req.Id);
        }
        else
        {
            _logger.LogInformation("No changes made to variable {VariableId}", req.Id);
        }

        return new UpdateVariableResponse
        {
            Id = req.Id,
            Success = true,
            Message = hasChanges ? "Variable updated successfully" : "No changes made"
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