using System.Text.RegularExpressions;
using FluentValidation;

namespace Cronplus.Api.Domain.Models;

/// <summary>
/// Value object for file watch configuration
/// </summary>
public class WatchConfiguration : IEquatable<WatchConfiguration>
{
    public string Directory { get; }
    public string GlobPattern { get; }
    public bool IncludeSubdirectories { get; }
    public int DebounceMilliseconds { get; }
    public int StabilizationMilliseconds { get; }
    public List<string> ExcludePatterns { get; }
    public List<string> IncludeExtensions { get; }
    public FileChangeTypes WatchedChangeTypes { get; }
    public long? MinFileSizeBytes { get; }
    public long? MaxFileSizeBytes { get; }
    
    public WatchConfiguration(
        string directory,
        string globPattern = "*",
        bool includeSubdirectories = false,
        int debounceMilliseconds = 500,
        int stabilizationMilliseconds = 1000,
        List<string>? excludePatterns = null,
        List<string>? includeExtensions = null,
        FileChangeTypes? watchedChangeTypes = null,
        long? minFileSizeBytes = null,
        long? maxFileSizeBytes = null)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory cannot be empty", nameof(directory));
        
        if (string.IsNullOrWhiteSpace(globPattern))
            throw new ArgumentException("Glob pattern cannot be empty", nameof(globPattern));
        
        if (debounceMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(debounceMilliseconds), "Debounce time cannot be negative");
        
        if (stabilizationMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(stabilizationMilliseconds), "Stabilization time cannot be negative");
        
        if (minFileSizeBytes.HasValue && minFileSizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(minFileSizeBytes), "Minimum file size cannot be negative");
        
        if (maxFileSizeBytes.HasValue && maxFileSizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxFileSizeBytes), "Maximum file size cannot be negative");
        
        if (minFileSizeBytes.HasValue && maxFileSizeBytes.HasValue && minFileSizeBytes > maxFileSizeBytes)
            throw new ArgumentException("Minimum file size cannot be greater than maximum file size");
        
        Directory = Path.GetFullPath(directory);
        GlobPattern = globPattern;
        IncludeSubdirectories = includeSubdirectories;
        DebounceMilliseconds = debounceMilliseconds;
        StabilizationMilliseconds = stabilizationMilliseconds;
        ExcludePatterns = excludePatterns ?? new List<string>();
        IncludeExtensions = includeExtensions ?? new List<string>();
        WatchedChangeTypes = watchedChangeTypes ?? FileChangeTypes.All;
        MinFileSizeBytes = minFileSizeBytes;
        MaxFileSizeBytes = maxFileSizeBytes;
    }
    
    /// <summary>
    /// Check if a file path matches the watch configuration
    /// </summary>
    public bool IsFileMatch(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;
        
        var fileName = Path.GetFileName(filePath);
        var fileDir = Path.GetDirectoryName(filePath) ?? "";
        
        // Check if file is in the watched directory
        if (!IncludeSubdirectories)
        {
            if (!string.Equals(fileDir, Directory, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        else
        {
            if (!fileDir.StartsWith(Directory, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        
        // Check glob pattern
        if (!MatchesGlobPattern(fileName, GlobPattern))
            return false;
        
        // Check exclude patterns
        foreach (var excludePattern in ExcludePatterns)
        {
            if (MatchesGlobPattern(fileName, excludePattern))
                return false;
        }
        
        // Check extensions
        if (IncludeExtensions.Any())
        {
            var extension = Path.GetExtension(fileName);
            if (!IncludeExtensions.Any(ext => 
                string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        
        // Check file size if file exists
        if (File.Exists(filePath))
        {
            var fileInfo = new FileInfo(filePath);
            
            if (MinFileSizeBytes.HasValue && fileInfo.Length < MinFileSizeBytes.Value)
                return false;
            
            if (MaxFileSizeBytes.HasValue && fileInfo.Length > MaxFileSizeBytes.Value)
                return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Check if a file change type should be watched
    /// </summary>
    public bool ShouldWatchChangeType(FileChangeTypes changeType)
    {
        return (WatchedChangeTypes & changeType) == changeType;
    }
    
    private bool MatchesGlobPattern(string fileName, string pattern)
    {
        // Convert glob pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".")
            + "$";
        
        return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
    }
    
    public bool Equals(WatchConfiguration? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        
        return Directory == other.Directory &&
               GlobPattern == other.GlobPattern &&
               IncludeSubdirectories == other.IncludeSubdirectories &&
               DebounceMilliseconds == other.DebounceMilliseconds &&
               StabilizationMilliseconds == other.StabilizationMilliseconds &&
               ExcludePatterns.SequenceEqual(other.ExcludePatterns) &&
               IncludeExtensions.SequenceEqual(other.IncludeExtensions) &&
               WatchedChangeTypes == other.WatchedChangeTypes &&
               MinFileSizeBytes == other.MinFileSizeBytes &&
               MaxFileSizeBytes == other.MaxFileSizeBytes;
    }
    
    public override bool Equals(object? obj)
    {
        return Equals(obj as WatchConfiguration);
    }
    
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Directory);
        hash.Add(GlobPattern);
        hash.Add(IncludeSubdirectories);
        hash.Add(DebounceMilliseconds);
        hash.Add(StabilizationMilliseconds);
        hash.Add(WatchedChangeTypes);
        hash.Add(MinFileSizeBytes);
        hash.Add(MaxFileSizeBytes);
        
        foreach (var pattern in ExcludePatterns)
            hash.Add(pattern);
        
        foreach (var ext in IncludeExtensions)
            hash.Add(ext);
        
        return hash.ToHashCode();
    }
    
    public static bool operator ==(WatchConfiguration? left, WatchConfiguration? right)
    {
        return Equals(left, right);
    }
    
    public static bool operator !=(WatchConfiguration? left, WatchConfiguration? right)
    {
        return !Equals(left, right);
    }
    
    public override string ToString()
    {
        return $"Watch: {Directory}/{GlobPattern} (Subdirs: {IncludeSubdirectories}, Debounce: {DebounceMilliseconds}ms)";
    }
}

/// <summary>
/// File change types to watch
/// </summary>
[Flags]
public enum FileChangeTypes
{
    None = 0,
    Created = 1,
    Changed = 2,
    Deleted = 4,
    Renamed = 8,
    All = Created | Changed | Deleted | Renamed
}

/// <summary>
/// Builder for WatchConfiguration
/// </summary>
public class WatchConfigurationBuilder
{
    private string _directory = "";
    private string _globPattern = "*";
    private bool _includeSubdirectories = false;
    private int _debounceMilliseconds = 500;
    private int _stabilizationMilliseconds = 1000;
    private List<string> _excludePatterns = new();
    private List<string> _includeExtensions = new();
    private FileChangeTypes _watchedChangeTypes = FileChangeTypes.All;
    private long? _minFileSizeBytes;
    private long? _maxFileSizeBytes;
    
    public WatchConfigurationBuilder WithDirectory(string directory)
    {
        _directory = directory;
        return this;
    }
    
    public WatchConfigurationBuilder WithGlobPattern(string pattern)
    {
        _globPattern = pattern;
        return this;
    }
    
    public WatchConfigurationBuilder WithSubdirectories(bool include = true)
    {
        _includeSubdirectories = include;
        return this;
    }
    
    public WatchConfigurationBuilder WithDebounce(int milliseconds)
    {
        _debounceMilliseconds = milliseconds;
        return this;
    }
    
    public WatchConfigurationBuilder WithStabilization(int milliseconds)
    {
        _stabilizationMilliseconds = milliseconds;
        return this;
    }
    
    public WatchConfigurationBuilder ExcludePattern(string pattern)
    {
        _excludePatterns.Add(pattern);
        return this;
    }
    
    public WatchConfigurationBuilder IncludeExtension(string extension)
    {
        if (!extension.StartsWith("."))
            extension = "." + extension;
        
        _includeExtensions.Add(extension);
        return this;
    }
    
    public WatchConfigurationBuilder WatchChangeTypes(FileChangeTypes types)
    {
        _watchedChangeTypes = types;
        return this;
    }
    
    public WatchConfigurationBuilder WithFileSizeRange(long? min, long? max)
    {
        _minFileSizeBytes = min;
        _maxFileSizeBytes = max;
        return this;
    }
    
    public WatchConfiguration Build()
    {
        return new WatchConfiguration(
            _directory,
            _globPattern,
            _includeSubdirectories,
            _debounceMilliseconds,
            _stabilizationMilliseconds,
            _excludePatterns,
            _includeExtensions,
            _watchedChangeTypes,
            _minFileSizeBytes,
            _maxFileSizeBytes);
    }
}

/// <summary>
/// Validator for WatchConfiguration
/// </summary>
public class WatchConfigurationValidator : AbstractValidator<WatchConfiguration>
{
    public WatchConfigurationValidator()
    {
        RuleFor(x => x.Directory)
            .NotEmpty().WithMessage("Directory is required")
            .Must(BeAValidDirectory).WithMessage("Directory must be a valid path");
        
        RuleFor(x => x.GlobPattern)
            .NotEmpty().WithMessage("Glob pattern is required");
        
        RuleFor(x => x.DebounceMilliseconds)
            .InclusiveBetween(0, 60000).WithMessage("Debounce must be between 0 and 60000 ms");
        
        RuleFor(x => x.StabilizationMilliseconds)
            .InclusiveBetween(0, 300000).WithMessage("Stabilization must be between 0 and 300000 ms");
        
        RuleFor(x => x.MinFileSizeBytes)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinFileSizeBytes.HasValue)
            .WithMessage("Minimum file size must be non-negative");
        
        RuleFor(x => x.MaxFileSizeBytes)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MaxFileSizeBytes.HasValue)
            .WithMessage("Maximum file size must be non-negative");
        
        RuleFor(x => x)
            .Must(x => !x.MinFileSizeBytes.HasValue || 
                      !x.MaxFileSizeBytes.HasValue || 
                      x.MinFileSizeBytes <= x.MaxFileSizeBytes)
            .WithMessage("Minimum file size cannot be greater than maximum file size");
    }
    
    private bool BeAValidDirectory(string directory)
    {
        try
        {
            Path.GetFullPath(directory);
            return true;
        }
        catch
        {
            return false;
        }
    }
}