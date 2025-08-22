using FastEndpoints;
using FluentValidation;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Cronplus.Api.Common.Security;

namespace Cronplus.Api.Features.Auth;

public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class RefreshTokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = "Bearer";
}

public class RefreshTokenValidator : Validator<RefreshTokenRequest>
{
    public RefreshTokenValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required");
    }
}

public class RefreshTokenEndpoint : Endpoint<RefreshTokenRequest, RefreshTokenResponse>
{
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<RefreshTokenEndpoint> _logger;

    public RefreshTokenEndpoint(JwtSettings jwtSettings, ILogger<RefreshTokenEndpoint> logger)
    {
        _jwtSettings = jwtSettings;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/auth/refresh");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Refresh access token using refresh token";
            s.Description = "Exchanges a valid refresh token for new access and refresh tokens";
        });
    }

    public override async Task<RefreshTokenResponse> ExecuteAsync(RefreshTokenRequest req, CancellationToken ct)
    {
        // TODO: Validate refresh token from database
        if (!IsValidRefreshToken(req.RefreshToken))
        {
            ThrowError("Invalid refresh token", 401);
        }

        // TODO: Get user info from refresh token in database
        var userId = GetUserIdFromRefreshToken(req.RefreshToken);
        var user = GetUserInfo(userId);
        
        // Generate new tokens
        var (accessToken, expiresIn) = GenerateAccessToken(user);
        var newRefreshToken = GenerateRefreshToken();
        
        // TODO: Update refresh token in database
        
        _logger.LogInformation("Refresh token used for user {UserId}", userId);
        
        return new RefreshTokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresIn = expiresIn
        };
    }

    private bool IsValidRefreshToken(string refreshToken)
    {
        // TODO: Implement actual refresh token validation from database
        // Check if token exists, not expired, not revoked
        return !string.IsNullOrEmpty(refreshToken) && refreshToken.Length > 20;
    }

    private string GetUserIdFromRefreshToken(string refreshToken)
    {
        // TODO: Get from database
        return "1"; // Demo user ID
    }

    private UserInfo GetUserInfo(string userId)
    {
        // TODO: Fetch from database
        return new UserInfo
        {
            Id = userId,
            Username = "admin",
            Email = "admin@cronplus.local",
            Roles = new List<string> { "Admin", "User" },
            Permissions = new List<string> 
            { 
                "tasks:read", "tasks:write", 
                "system:manage", "logs:read" 
            }
        };
    }

    private (string token, int expiresIn) GenerateAccessToken(UserInfo user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresIn = _jwtSettings.ExpirationMinutes * 60;
        
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("jti", Guid.NewGuid().ToString())
        };
        
        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        
        foreach (var permission in user.Permissions)
        {
            claims.Add(new Claim("permission", permission));
        }
        
        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: credentials
        );
        
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresIn);
    }

    private string GenerateRefreshToken()
    {
        var randomBytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}