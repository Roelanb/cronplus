using FastEndpoints;
using FluentValidation;

namespace Cronplus.Api.Common.Endpoints;

/// <summary>
/// Base endpoint for all Cronplus API endpoints
/// Provides common configuration and patterns
/// </summary>
public abstract class BaseEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected new ILogger Logger { get; set; } = null!;
    
    public override void Configure()
    {
        // Call derived class configuration
        ConfigureEndpoint();
        
        // Apply common configuration
        ApplyCommonConfiguration();
    }
    
    /// <summary>
    /// Override this method to configure the specific endpoint
    /// </summary>
    protected abstract void ConfigureEndpoint();
    
    /// <summary>
    /// Apply common configuration to all endpoints
    /// </summary>
    private void ApplyCommonConfiguration()
    {
        // Add common response types
        Description(b => b
            .Produces<TResponse>(200)
            .ProducesProblemDetails(400)
            .ProducesProblemDetails(500));
            
        // Add operation ID for OpenAPI
        if (!string.IsNullOrEmpty(EndpointName))
        {
            Description(b => b.WithName(EndpointName));
        }
        
        // Add summary if provided
        if (!string.IsNullOrEmpty(EndpointSummary))
        {
            Description(b => b.WithSummary(EndpointSummary));
        }
    }
    
    protected virtual string? EndpointName => null;
    protected virtual string? EndpointSummary => null;
}

/// <summary>
/// Base endpoint for endpoints that don't require a request body
/// </summary>
public abstract class BaseEndpointWithoutRequest<TResponse> : EndpointWithoutRequest<TResponse>
    where TResponse : notnull
{
    protected new ILogger Logger { get; set; } = null!;
    
    public override void Configure()
    {
        ConfigureEndpoint();
        ApplyCommonConfiguration();
    }
    
    protected abstract void ConfigureEndpoint();
    
    private void ApplyCommonConfiguration()
    {
        Description(b => b
            .Produces<TResponse>(200)
            .ProducesProblemDetails(400)
            .ProducesProblemDetails(500));
            
        if (!string.IsNullOrEmpty(EndpointName))
        {
            Description(b => b.WithName(EndpointName));
        }
        
        if (!string.IsNullOrEmpty(EndpointSummary))
        {
            Description(b => b.WithSummary(EndpointSummary));
        }
    }
    
    protected virtual string? EndpointName => null;
    protected virtual string? EndpointSummary => null;
}

/// <summary>
/// Base endpoint for authenticated endpoints
/// </summary>
public abstract class SecureEndpoint<TRequest, TResponse> : BaseEndpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected override void ConfigureEndpoint()
    {
        // Configure specific endpoint
        ConfigureSecureEndpoint();
        
        // Apply security policies
        Policies("RequireAuthentication");
    }
    
    /// <summary>
    /// Override this to configure the secure endpoint
    /// </summary>
    protected abstract void ConfigureSecureEndpoint();
}