using FastEndpoints;
using FluentValidation;
using Cronplus.Api.Common.Models;

namespace Cronplus.Api.Common.Processors;

/// <summary>
/// Global error handler for all endpoints
/// </summary>
public class GlobalErrorHandler : IGlobalPostProcessor
{
    private readonly ILogger<GlobalErrorHandler> _logger;

    public GlobalErrorHandler(ILogger<GlobalErrorHandler> logger)
    {
        _logger = logger;
    }

    public async Task PostProcessAsync(IPostProcessorContext context, CancellationToken ct)
    {
        if (context.HasValidationFailures)
        {
            await HandleValidationErrors(context, ct);
        }
        else if (context.HasExceptionOccurred)
        {
            await HandleException(context, ct);
        }
    }

    private async Task HandleValidationErrors(IPostProcessorContext context, CancellationToken ct)
    {
        var errors = context.ValidationFailures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(f => f.ErrorMessage).ToArray()
            );

        var response = new Cronplus.Api.Common.Models.ErrorResponse
        {
            Message = "Validation failed",
            Details = "One or more validation errors occurred",
            Errors = errors,
            TraceId = context.HttpContext.TraceIdentifier
        };

        _logger.LogWarning("Validation failed for request {TraceId}: {@Errors}",
            context.HttpContext.TraceIdentifier, errors);

        await context.HttpContext.Response.SendAsync(response, 400);
    }

    private async Task HandleException(IPostProcessorContext context, CancellationToken ct)
    {
        var exception = context.ExceptionDispatchInfo?.SourceException;
        
        if (exception == null)
        {
            return;
        }

        var response = new Cronplus.Api.Common.Models.ErrorResponse
        {
            Message = "An error occurred while processing your request",
            TraceId = context.HttpContext.TraceIdentifier
        };

        var statusCode = exception switch
        {
            ValidationException => 400,
            UnauthorizedAccessException => 401,
            KeyNotFoundException => 404,
            InvalidOperationException => 400,
            NotImplementedException => 501,
            TimeoutException => 408,
            _ => 500
        };

        // In development, include exception details
        var isDevelopment = context.HttpContext.RequestServices
            .GetRequiredService<IWebHostEnvironment>()
            .IsDevelopment();

        if (isDevelopment)
        {
            response.Details = exception.ToString();
        }
        else
        {
            response.Details = exception.Message;
        }

        _logger.LogError(exception, "Unhandled exception in request {TraceId}", 
            context.HttpContext.TraceIdentifier);

        context.MarkExceptionAsHandled();
        await context.HttpContext.Response.SendAsync(response, statusCode);
    }
}

/// <summary>
/// Custom exception handler for specific exceptions
/// Note: Full IExceptionHandler implementation would require a separate middleware
/// This is a placeholder for when full exception handling is needed
/// </summary>
public class BusinessExceptionHandler
{
    private readonly ILogger<BusinessExceptionHandler> _logger;

    public BusinessExceptionHandler(ILogger<BusinessExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async Task<bool> HandleAsync(BusinessException exception, HttpContext context, CancellationToken ct)
    {
        _logger.LogWarning(exception, "Business exception occurred: {Message}", exception.Message);

        var response = new Cronplus.Api.Common.Models.ErrorResponse
        {
            Message = exception.Message,
            Details = exception.Details,
            TraceId = context.TraceIdentifier
        };

        await context.Response.SendAsync(response, exception.StatusCode);
        return true; // Exception is handled
    }
}

/// <summary>
/// Custom business exception
/// </summary>
public class BusinessException : Exception
{
    public int StatusCode { get; }
    public string? Details { get; }

    public BusinessException(string message, int statusCode = 400, string? details = null) 
        : base(message)
    {
        StatusCode = statusCode;
        Details = details;
    }
}