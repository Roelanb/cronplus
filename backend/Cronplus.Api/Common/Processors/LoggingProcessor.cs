using FastEndpoints;
using System.Diagnostics;

namespace Cronplus.Api.Common.Processors;

/// <summary>
/// Pre-processor that logs incoming requests with timing
/// </summary>
public class RequestLoggingPreProcessor<TRequest> : IPreProcessor<TRequest>
{
    private readonly ILogger<RequestLoggingPreProcessor<TRequest>> _logger;

    public RequestLoggingPreProcessor(ILogger<RequestLoggingPreProcessor<TRequest>> logger)
    {
        _logger = logger;
    }

    public Task PreProcessAsync(IPreProcessorContext<TRequest> context, CancellationToken ct)
    {
        // Store request start time for duration calculation
        context.HttpContext.Items["RequestStartTime"] = Stopwatch.GetTimestamp();
        
        _logger.LogInformation("Processing {EndpointName} request from {RemoteIp} with correlation ID {CorrelationId}",
            context.HttpContext.GetEndpoint()?.DisplayName ?? "Unknown",
            context.HttpContext.Connection.RemoteIpAddress,
            context.HttpContext.TraceIdentifier);
            
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Request details: {@Request}", context.Request);
        }
        
        return Task.CompletedTask;
    }
}

/// <summary>
/// Post-processor that logs response times and results
/// </summary>
public class ResponseLoggingPostProcessor<TRequest, TResponse> : IPostProcessor<TRequest, TResponse>
{
    private readonly ILogger<ResponseLoggingPostProcessor<TRequest, TResponse>> _logger;

    public ResponseLoggingPostProcessor(ILogger<ResponseLoggingPostProcessor<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public Task PostProcessAsync(IPostProcessorContext<TRequest, TResponse> context, CancellationToken ct)
    {
        var startTime = context.HttpContext.Items["RequestStartTime"];
        if (startTime != null && startTime is long startTimestamp)
        {
            var elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            
            _logger.LogInformation("Completed {EndpointName} request in {ElapsedMs:F2}ms with status {StatusCode}",
                context.HttpContext.GetEndpoint()?.DisplayName ?? "Unknown",
                elapsedMs,
                context.HttpContext.Response.StatusCode);
        }
        
        if (_logger.IsEnabled(LogLevel.Debug) && context.Response != null)
        {
            _logger.LogDebug("Response details: {@Response}", context.Response);
        }
        
        return Task.CompletedTask;
    }
}

/// <summary>
/// Pre-processor that validates request consistency
/// </summary>
public class ValidationPreProcessor<TRequest> : IPreProcessor<TRequest>
{
    private readonly ILogger<ValidationPreProcessor<TRequest>> _logger;

    public ValidationPreProcessor(ILogger<ValidationPreProcessor<TRequest>> logger)
    {
        _logger = logger;
    }

    public Task PreProcessAsync(IPreProcessorContext<TRequest> context, CancellationToken ct)
    {
        // Add correlation ID if not present
        if (!context.HttpContext.Request.Headers.ContainsKey("X-Correlation-Id"))
        {
            context.HttpContext.Request.Headers["X-Correlation-Id"] = Guid.NewGuid().ToString();
        }
        
        // Log validation errors if any
        if (context.ValidationFailures.Any())
        {
            _logger.LogWarning("Validation failed for {EndpointName}: {@Errors}",
                context.HttpContext.GetEndpoint()?.DisplayName ?? "Unknown",
                context.ValidationFailures.Select(f => new { f.PropertyName, f.ErrorMessage }));
        }
        
        return Task.CompletedTask;
    }
}