using FastEndpoints;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cronplus.Api.Common.Processors;

/// <summary>
/// Attribute to mark endpoints for caching
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CacheableAttribute : Attribute
{
    public int DurationInSeconds { get; }
    public bool VaryByUser { get; }
    public string[]? VaryByQueryKeys { get; }

    public CacheableAttribute(int durationInSeconds = 60, bool varyByUser = false, string[]? varyByQueryKeys = null)
    {
        DurationInSeconds = durationInSeconds;
        VaryByUser = varyByUser;
        VaryByQueryKeys = varyByQueryKeys;
    }
}

/// <summary>
/// Pre-processor that checks cache before processing
/// </summary>
public class CacheCheckPreProcessor<TRequest> : IPreProcessor<TRequest>
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheCheckPreProcessor<TRequest>> _logger;

    public CacheCheckPreProcessor(IMemoryCache cache, ILogger<CacheCheckPreProcessor<TRequest>> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task PreProcessAsync(IPreProcessorContext<TRequest> context, CancellationToken ct)
    {
        var endpoint = context.HttpContext.GetEndpoint();
        var cacheAttribute = endpoint?.Metadata.GetMetadata<CacheableAttribute>();
        
        if (cacheAttribute == null)
        {
            return; // Not cacheable
        }

        var cacheKey = GenerateCacheKey(context, cacheAttribute);
        
        if (_cache.TryGetValue<object>(cacheKey, out var cachedResponse) && cachedResponse != null)
        {
            _logger.LogDebug("Cache hit for key: {CacheKey}", cacheKey);
            
            // Set response from cache
            context.HttpContext.Items["CachedResponse"] = cachedResponse;
            context.HttpContext.Items["CacheHit"] = true;
            
            // Send cached response directly
            await context.HttpContext.Response.SendAsync(cachedResponse, 200);
            
            // Short-circuit the pipeline
            await context.HttpContext.Response.CompleteAsync();
        }
        else
        {
            _logger.LogDebug("Cache miss for key: {CacheKey}", cacheKey);
            context.HttpContext.Items["CacheKey"] = cacheKey;
            context.HttpContext.Items["CacheAttribute"] = cacheAttribute;
        }
    }

    private string GenerateCacheKey<T>(IPreProcessorContext<T> context, CacheableAttribute attribute)
    {
        var keyBuilder = new StringBuilder();
        keyBuilder.Append($"cache:{context.HttpContext.Request.Path}");

        if (attribute.VaryByUser)
        {
            var userId = context.HttpContext.User?.Identity?.Name ?? "anonymous";
            keyBuilder.Append($":user:{userId}");
        }

        if (attribute.VaryByQueryKeys != null && attribute.VaryByQueryKeys.Length > 0)
        {
            foreach (var key in attribute.VaryByQueryKeys)
            {
                if (context.HttpContext.Request.Query.TryGetValue(key, out var value))
                {
                    keyBuilder.Append($":{key}:{value}");
                }
            }
        }

        // Add request body to cache key if present
        if (context.Request != null)
        {
            var requestJson = JsonSerializer.Serialize(context.Request);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(requestJson));
            keyBuilder.Append($":body:{Convert.ToBase64String(hash)}");
        }

        return keyBuilder.ToString();
    }
}

/// <summary>
/// Post-processor that stores response in cache
/// </summary>
public class CacheStorePostProcessor<TRequest, TResponse> : IPostProcessor<TRequest, TResponse>
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheStorePostProcessor<TRequest, TResponse>> _logger;

    public CacheStorePostProcessor(IMemoryCache cache, ILogger<CacheStorePostProcessor<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task PostProcessAsync(IPostProcessorContext<TRequest, TResponse> context, CancellationToken ct)
    {
        // Don't cache if there was an error or if it was a cache hit
        if (context.HasValidationFailures || 
            context.HasExceptionOccurred || 
            context.HttpContext.Items.ContainsKey("CacheHit"))
        {
            return Task.CompletedTask;
        }

        var cacheKey = context.HttpContext.Items["CacheKey"] as string;
        var cacheAttribute = context.HttpContext.Items["CacheAttribute"] as CacheableAttribute;

        if (!string.IsNullOrEmpty(cacheKey) && cacheAttribute != null && context.Response != null)
        {
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(cacheAttribute.DurationInSeconds),
                SlidingExpiration = TimeSpan.FromSeconds(cacheAttribute.DurationInSeconds / 2)
            };

            _cache.Set(cacheKey, context.Response, cacheOptions);
            
            _logger.LogDebug("Cached response for key: {CacheKey} with duration: {Duration}s", 
                cacheKey, cacheAttribute.DurationInSeconds);
                
            // Add cache headers to response
            context.HttpContext.Response.Headers["X-Cache"] = "MISS";
            context.HttpContext.Response.Headers["Cache-Control"] = $"public, max-age={cacheAttribute.DurationInSeconds}";
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Extension methods for configuring caching
/// </summary>
public static class CachingExtensions
{
    public static IServiceCollection AddEndpointCaching(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton(typeof(CacheCheckPreProcessor<>));
        services.AddSingleton(typeof(CacheStorePostProcessor<,>));
        return services;
    }
}