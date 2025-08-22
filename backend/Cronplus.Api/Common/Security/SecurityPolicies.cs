using FastEndpoints;
using FastEndpoints.Security;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Cronplus.Api.Common.Security;

/// <summary>
/// Security policies for the application
/// </summary>
public static class SecurityPolicies
{
    public const string RequireAuthentication = "RequireAuthentication";
    public const string RequireAdminRole = "RequireAdminRole";
    public const string RequireTaskReadPermission = "RequireTaskReadPermission";
    public const string RequireTaskWritePermission = "RequireTaskWritePermission";
    public const string RequireSystemPermission = "RequireSystemPermission";
}

/// <summary>
/// Custom authorization handler for task permissions
/// </summary>
public class TaskPermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public TaskPermissionRequirement(string permission)
    {
        Permission = permission;
    }
}

public class TaskPermissionHandler : AuthorizationHandler<TaskPermissionRequirement>
{
    private readonly ILogger<TaskPermissionHandler> _logger;

    public TaskPermissionHandler(ILogger<TaskPermissionHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TaskPermissionRequirement requirement)
    {
        var user = context.User;
        
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            _logger.LogWarning("Unauthenticated user attempted to access resource requiring {Permission}", 
                requirement.Permission);
            return Task.CompletedTask;
        }

        // Check if user has the required permission claim
        if (user.HasClaim("permission", requirement.Permission) ||
            user.HasClaim(ClaimTypes.Role, "Admin"))
        {
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning("User {User} lacks permission {Permission}", 
                user.Identity.Name, requirement.Permission);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// JWT configuration for authentication
/// </summary>
public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "Cronplus";
    public string Audience { get; set; } = "CronplusAPI";
    public int ExpirationMinutes { get; set; } = 60;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}

/// <summary>
/// Extension methods for configuring security
/// </summary>
public static class SecurityExtensions
{
    public static IServiceCollection AddCronplusSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure JWT settings
        var jwtSettings = new JwtSettings();
        configuration.GetSection("JwtSettings").Bind(jwtSettings);
        services.AddSingleton(jwtSettings);

        // Add authorization policies
        services.AddAuthorization(options =>
        {
            // Basic authentication policy
            options.AddPolicy(SecurityPolicies.RequireAuthentication, policy =>
                policy.RequireAuthenticatedUser());

            // Admin role policy
            options.AddPolicy(SecurityPolicies.RequireAdminRole, policy =>
                policy.RequireRole("Admin"));

            // Task read permission
            options.AddPolicy(SecurityPolicies.RequireTaskReadPermission, policy =>
                policy.AddRequirements(new TaskPermissionRequirement("tasks:read")));

            // Task write permission
            options.AddPolicy(SecurityPolicies.RequireTaskWritePermission, policy =>
                policy.AddRequirements(new TaskPermissionRequirement("tasks:write")));

            // System permission
            options.AddPolicy(SecurityPolicies.RequireSystemPermission, policy =>
                policy.AddRequirements(new TaskPermissionRequirement("system:manage")));
        });

        // Register authorization handlers
        services.AddSingleton<IAuthorizationHandler, TaskPermissionHandler>();

        // Configure JWT authentication (will be fully implemented when needed)
        // For now, we prepare the structure
        services.AddAuthentication()
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new()
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    // IssuerSigningKey will be set when we implement auth endpoints
                };
            });

        return services;
    }
}

/// <summary>
/// User context service for accessing current user information
/// </summary>
public interface IUserContext
{
    string? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    bool HasPermission(string permission);
    bool IsInRole(string role);
}

public class UserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string? UserId => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    public string? UserName => User?.Identity?.Name;
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public bool HasPermission(string permission)
    {
        return User?.HasClaim("permission", permission) ?? false;
    }

    public bool IsInRole(string role)
    {
        return User?.IsInRole(role) ?? false;
    }
}