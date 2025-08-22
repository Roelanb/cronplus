using System.ComponentModel.DataAnnotations;
using FluentValidation;

namespace Cronplus.Api.Domain.Models;

/// <summary>
/// Enhanced Task model with validation attributes
/// </summary>
public class TaskModel
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    [RegularExpression(@"^[a-z0-9-]+$", ErrorMessage = "Task ID must contain only lowercase letters, numbers, and hyphens")]
    public string Id { get; set; } = string.Empty;
    
    public bool Enabled { get; set; }
    
    [Required]
    [StringLength(500)]
    public string WatchDirectory { get; set; } = string.Empty;
    
    [Required]
    [StringLength(200)]
    public string GlobPattern { get; set; } = "*";
    
    [Range(100, 60000)]
    public int DebounceMs { get; set; } = 500;
    
    [Range(100, 120000)]
    public int StabilizationMs { get; set; } = 1000;
    
    [StringLength(1000)]
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Additional properties for runtime
    public TaskStatus Status { get; set; } = TaskStatus.Idle;
    public DateTime? LastTriggeredAt { get; set; }
    public int ExecutionCount { get; set; }
    public int FailureCount { get; set; }
}

/// <summary>
/// Task status enumeration
/// </summary>
public enum TaskStatus
{
    Idle,
    Watching,
    Processing,
    Failed,
    Disabled,
    Paused
}

/// <summary>
/// Fluent validation for Task model
/// </summary>
public class TaskModelValidator : AbstractValidator<TaskModel>
{
    public TaskModelValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Task ID is required")
            .Length(3, 100).WithMessage("Task ID must be between 3 and 100 characters")
            .Matches(@"^[a-z0-9-]+$").WithMessage("Task ID must contain only lowercase letters, numbers, and hyphens");
        
        RuleFor(x => x.WatchDirectory)
            .NotEmpty().WithMessage("Watch directory is required")
            .Must(BeAValidPath).WithMessage("Watch directory must be a valid path");
        
        RuleFor(x => x.GlobPattern)
            .NotEmpty().WithMessage("Glob pattern is required")
            .Must(BeAValidGlobPattern).WithMessage("Invalid glob pattern");
        
        RuleFor(x => x.DebounceMs)
            .InclusiveBetween(100, 60000).WithMessage("Debounce must be between 100ms and 60 seconds");
        
        RuleFor(x => x.StabilizationMs)
            .InclusiveBetween(100, 120000).WithMessage("Stabilization must be between 100ms and 2 minutes");
        
        RuleFor(x => x.StabilizationMs)
            .GreaterThanOrEqualTo(x => x.DebounceMs)
            .WithMessage("Stabilization time must be greater than or equal to debounce time");
    }
    
    private bool BeAValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        
        try
        {
            // Check if path is valid (doesn't have to exist)
            var fullPath = Path.GetFullPath(path);
            return !string.IsNullOrEmpty(fullPath);
        }
        catch
        {
            return false;
        }
    }
    
    private bool BeAValidGlobPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;
        
        // Basic glob pattern validation
        var invalidChars = Path.GetInvalidFileNameChars()
            .Where(c => c != '*' && c != '?' && c != '[' && c != ']' && c != '/')
            .ToArray();
        
        return !pattern.Any(c => invalidChars.Contains(c));
    }
}