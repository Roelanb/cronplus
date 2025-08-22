using FastEndpoints;
using FastEndpoints.Security;
using FluentValidation;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Cronplus.Api.Common.Security;

namespace Cronplus.Api.Features.Auth;

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public UserInfo User { get; set; } = new();
}

public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
}

public class LoginValidator : Validator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required")
            .MinimumLength(3).WithMessage("Username must be at least 3 characters");
        
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters");
    }
}

public class LoginEndpoint : Endpoint<LoginRequest, LoginResponse>
{
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<LoginEndpoint> _logger;

    public LoginEndpoint(JwtSettings jwtSettings, ILogger<LoginEndpoint> logger)
    {
        _jwtSettings = jwtSettings;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/auth/login");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Authenticate user and receive JWT token";
            s.Description = "Validates user credentials and returns access and refresh tokens";
            s.ExampleRequest = new LoginRequest 
            { 
                Username = "admin", 
                Password = "password123" 
            };
        });
    }

    public override async Task<LoginResponse> ExecuteAsync(LoginRequest req, CancellationToken ct)
    {
        // TODO: Replace with actual user validation from database
        // For demo purposes, using hardcoded validation
        if (!IsValidUser(req.Username, req.Password))
        {
            ThrowError("Invalid username or password", 401);
        }

        // Get user info (would come from database)
        var user = GetUserInfo(req.Username);
        
        // Generate tokens
        var (accessToken, expiresIn) = GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken();
        
        // TODO: Store refresh token in database
        
        _logger.LogInformation("User {Username} logged in successfully", req.Username);
        
        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = expiresIn,
            User = user
        };
    }

    private bool IsValidUser(string username, string password)
    {
        // TODO: Implement actual password verification with hashing
        // This is for demo purposes only
        return username switch
        {
            "admin" when password == "admin123" => true,
            "user" when password == "user123" => true,
            "viewer" when password == "viewer123" => true,
            _ => false
        };
    }

    private UserInfo GetUserInfo(string username)
    {
        // TODO: Fetch from database
        return username switch
        {
            "admin" => new UserInfo
            {
                Id = "1",
                Username = username,
                Email = "admin@cronplus.local",
                Roles = new List<string> { "Admin", "User" },
                Permissions = new List<string> 
                { 
                    "tasks:read", "tasks:write", 
                    "system:manage", "logs:read" 
                }
            },
            "user" => new UserInfo
            {
                Id = "2",
                Username = username,
                Email = "user@cronplus.local",
                Roles = new List<string> { "User" },
                Permissions = new List<string> 
                { 
                    "tasks:read", "tasks:write" 
                }
            },
            "viewer" => new UserInfo
            {
                Id = "3",
                Username = username,
                Email = "viewer@cronplus.local",
                Roles = new List<string> { "Viewer" },
                Permissions = new List<string> 
                { 
                    "tasks:read" 
                }
            },
            _ => new UserInfo()
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
        
        // Add roles
        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }
        
        // Add permissions
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