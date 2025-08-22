using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Domain.Interfaces;

namespace Cronplus.Api.Features.Variables;

public class DeleteVariableRequest
{
    public int Id { get; set; }
}

public class DeleteVariableResponse
{
    public int Id { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class DeleteVariableValidator : Validator<DeleteVariableRequest>
{
    public DeleteVariableValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Variable ID must be a positive integer");
    }
}

public class DeleteVariableEndpoint : Endpoint<DeleteVariableRequest, DeleteVariableResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteVariableEndpoint> _logger;

    public DeleteVariableEndpoint(
        IUnitOfWork unitOfWork,
        ILogger<DeleteVariableEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public override void Configure()
    {
        Delete("/api/variables/{Id}");
        AllowAnonymous();
        Description(b => b
            .Produces<DeleteVariableResponse>(200)
            .ProducesProblemFE(404)
            .ProducesProblemFE(500)
            .WithTags("Variables"));
        Summary(s =>
        {
            s.Summary = "Delete a variable";
            s.Description = "Removes a variable from a task";
        });
    }

    public override async Task<DeleteVariableResponse> ExecuteAsync(DeleteVariableRequest req, CancellationToken ct)
    {
        _logger.LogInformation("Deleting variable {VariableId}", req.Id);

        // Get existing variable
        var variable = await _unitOfWork.TaskVariables.GetByIdAsync(req.Id);
        if (variable == null)
        {
            _logger.LogWarning("Variable {VariableId} not found", req.Id);
            ThrowError("Variable not found", 404);
        }

        try
        {
            // Delete the variable
            await _unitOfWork.TaskVariables.DeleteAsync(req.Id);
            _unitOfWork.Commit();

            _logger.LogInformation("Successfully deleted variable {VariableId} from task {TaskId}", 
                req.Id, variable.TaskId);

            return new DeleteVariableResponse
            {
                Id = req.Id,
                Success = true,
                Message = "Variable deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete variable {VariableId}", req.Id);
            ThrowError($"Failed to delete variable: {ex.Message}", 500);
            
            // This won't be reached due to ThrowError
            return new DeleteVariableResponse
            {
                Id = req.Id,
                Success = false,
                Message = "Failed to delete variable"
            };
        }
    }
}