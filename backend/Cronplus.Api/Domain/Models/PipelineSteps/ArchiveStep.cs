using System.IO.Compression;
using FluentValidation;
using FluentValidation.Results;

namespace Cronplus.Api.Domain.Models.PipelineSteps;

/// <summary>
/// Pipeline step for archiving files
/// </summary>
public class ArchiveStep : PipelineStepBase
{
    public override string StepType => "archive";
    
    public string ArchivePath { get; set; } = string.Empty;
    public ArchiveFormat Format { get; set; } = ArchiveFormat.Zip;
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;
    public bool DeleteOriginal { get; set; } = false;
    public bool AppendToExisting { get; set; } = true;
    public string? ArchiveNamePattern { get; set; } // e.g., "archive_{date:yyyyMMdd}.zip"
    public bool PreservePath { get; set; } = false; // Store with full path in archive
    public string? Password { get; set; } // For future implementation
    public ConflictStrategy ConflictStrategy { get; set; } = ConflictStrategy.Rename;
    public long? MaxArchiveSize { get; set; } // Max size in bytes before creating new archive
    public bool CreateIndexFile { get; set; } = false; // Create an index file listing archive contents
    public bool VerifyArchive { get; set; } = true; // Verify archive integrity after creation
    
    public override async Task<StepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            context.Logger?.LogInformation("Executing ArchiveStep: {Name} for file {FilePath}", Name, context.FilePath);
            
            if (!File.Exists(context.FilePath))
            {
                return StepResult.FailureResult($"Source file not found: {context.FilePath}");
            }
            
            // Resolve archive path with variables
            var resolvedArchivePath = ResolveVariables(ArchivePath, context);
            
            // Apply archive name pattern if specified
            if (!string.IsNullOrEmpty(ArchiveNamePattern))
            {
                var archiveName = ApplyArchiveNamePattern(ArchiveNamePattern, context);
                if (Directory.Exists(resolvedArchivePath))
                {
                    resolvedArchivePath = Path.Combine(resolvedArchivePath, archiveName);
                }
                else
                {
                    resolvedArchivePath = Path.Combine(Path.GetDirectoryName(resolvedArchivePath) ?? "", archiveName);
                }
            }
            
            // Ensure archive has correct extension
            resolvedArchivePath = EnsureArchiveExtension(resolvedArchivePath, Format);
            
            // Create directory if needed
            var archiveDir = Path.GetDirectoryName(resolvedArchivePath);
            if (!string.IsNullOrEmpty(archiveDir) && !Directory.Exists(archiveDir))
            {
                Directory.CreateDirectory(archiveDir);
            }
            
            // Archive the file
            var archiveResult = await ArchiveFileAsync(context, resolvedArchivePath, cancellationToken);
            
            if (!archiveResult.Success)
            {
                return archiveResult;
            }
            
            // Delete original if requested
            if (DeleteOriginal)
            {
                try
                {
                    File.Delete(context.FilePath);
                    context.Logger?.LogInformation("Deleted original file: {FilePath}", context.FilePath);
                }
                catch (Exception ex)
                {
                    context.Logger?.LogWarning(ex, "Failed to delete original file: {FilePath}", context.FilePath);
                    // Don't fail the step if deletion fails
                }
            }
            
            stopwatch.Stop();
            
            var outputs = new Dictionary<string, object>
            {
                ["ArchivePath"] = resolvedArchivePath,
                ["ArchiveSize"] = new FileInfo(resolvedArchivePath).Length,
                ["OriginalDeleted"] = DeleteOriginal && !File.Exists(context.FilePath)
            };
            
            var result = StepResult.SuccessResult($"File archived to {resolvedArchivePath}", outputs);
            result.ExecutionTime = stopwatch.Elapsed;
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            context.Logger?.LogError(ex, "Failed to archive file in step: {Name}", Name);
            var result = StepResult.FailureResult($"Archive failed: {ex.Message}", ex);
            result.ExecutionTime = stopwatch.Elapsed;
            return result;
        }
    }
    
    public override ValidationResult Validate()
    {
        var validator = new ArchiveStepValidator();
        return validator.Validate(this);
    }
    
    private async Task<StepResult> ArchiveFileAsync(ExecutionContext context, string archivePath, CancellationToken cancellationToken)
    {
        try
        {
            switch (Format)
            {
                case ArchiveFormat.Zip:
                    await AddToZipArchiveAsync(context, archivePath, cancellationToken);
                    break;
                    
                case ArchiveFormat.GZip:
                    await CreateGZipArchiveAsync(context, archivePath, cancellationToken);
                    break;
                    
                case ArchiveFormat.Tar:
                    return StepResult.FailureResult("TAR format not yet implemented");
                    
                default:
                    return StepResult.FailureResult($"Unsupported archive format: {Format}");
            }
            
            return StepResult.SuccessResult();
        }
        catch (Exception ex)
        {
            return StepResult.FailureResult($"Archive operation failed: {ex.Message}", ex);
        }
    }
    
    private async Task AddToZipArchiveAsync(ExecutionContext context, string archivePath, CancellationToken cancellationToken)
    {
        // Check if we need to create a new archive due to size limit
        if (MaxArchiveSize.HasValue && File.Exists(archivePath))
        {
            var archiveInfo = new FileInfo(archivePath);
            if (archiveInfo.Length >= MaxArchiveSize.Value)
            {
                // Create a new archive with incrementing number
                archivePath = GetNextArchivePath(archivePath);
                context.Logger?.LogInformation("Archive size limit reached, creating new archive: {ArchivePath}", archivePath);
            }
        }
        
        var mode = (AppendToExisting && File.Exists(archivePath)) ? ZipArchiveMode.Update : ZipArchiveMode.Create;
        
        using var fileStream = new FileStream(archivePath, 
            mode == ZipArchiveMode.Update ? FileMode.Open : FileMode.Create,
            FileAccess.ReadWrite);
        
        using var archive = new ZipArchive(fileStream, mode, leaveOpen: false);
        
        var entryName = PreservePath 
            ? context.FilePath.Replace(Path.GetPathRoot(context.FilePath) ?? "", "").TrimStart('\\', '/')
            : context.FileName;
        
        // Check if entry already exists and handle conflicts
        var existingEntry = archive.GetEntry(entryName);
        if (existingEntry != null)
        {
            switch (ConflictStrategy)
            {
                case ConflictStrategy.Skip:
                    context.Logger?.LogWarning("Entry {EntryName} already exists in archive, skipping", entryName);
                    return;
                    
                case ConflictStrategy.Replace:
                    existingEntry.Delete();
                    context.Logger?.LogDebug("Replaced existing entry {EntryName} in archive", entryName);
                    break;
                    
                case ConflictStrategy.Rename:
                    // Add timestamp to make unique
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(entryName);
                    var ext = Path.GetExtension(entryName);
                    entryName = $"{nameWithoutExt}_{timestamp}{ext}";
                    context.Logger?.LogDebug("Renamed entry to {EntryName} to avoid conflict", entryName);
                    break;
                    
                case ConflictStrategy.IncrementNumber:
                    entryName = GetIncrementedEntryName(archive, entryName);
                    context.Logger?.LogDebug("Renamed entry to {EntryName} with incremented number", entryName);
                    break;
            }
        }
        
        var entry = archive.CreateEntry(entryName, CompressionLevel);
        
        using var entryStream = entry.Open();
        using var sourceStream = new FileStream(context.FilePath, FileMode.Open, FileAccess.Read);
        
        await sourceStream.CopyToAsync(entryStream, 81920, cancellationToken);
        
        // Create index file if requested
        if (CreateIndexFile)
        {
            await CreateArchiveIndexAsync(archivePath, archive, context);
        }
        
        // Verify archive if requested
        if (VerifyArchive)
        {
            await VerifyArchiveIntegrityAsync(archivePath, context, cancellationToken);
        }
    }
    
    private string GetIncrementedEntryName(ZipArchive archive, string baseName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(baseName);
        var ext = Path.GetExtension(baseName);
        var counter = 1;
        var newName = baseName;
        
        while (archive.GetEntry(newName) != null)
        {
            newName = $"{nameWithoutExt}_{counter}{ext}";
            counter++;
        }
        
        return newName;
    }
    
    private string GetNextArchivePath(string basePath)
    {
        var dir = Path.GetDirectoryName(basePath) ?? "";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);
        var counter = 1;
        var newPath = basePath;
        
        while (File.Exists(newPath))
        {
            newPath = Path.Combine(dir, $"{nameWithoutExt}_{counter}{ext}");
            counter++;
        }
        
        return newPath;
    }
    
    private async Task CreateArchiveIndexAsync(string archivePath, ZipArchive archive, ExecutionContext context)
    {
        try
        {
            var indexPath = Path.ChangeExtension(archivePath, ".index.txt");
            var indexContent = new System.Text.StringBuilder();
            
            indexContent.AppendLine($"Archive: {Path.GetFileName(archivePath)}");
            indexContent.AppendLine($"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            indexContent.AppendLine($"Entries: {archive.Entries.Count}");
            indexContent.AppendLine(new string('-', 50));
            
            foreach (var entry in archive.Entries.OrderBy(e => e.FullName))
            {
                indexContent.AppendLine($"{entry.FullName}\t{entry.Length}\t{entry.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            }
            
            await File.WriteAllTextAsync(indexPath, indexContent.ToString());
            context.Logger?.LogDebug("Created archive index at {IndexPath}", indexPath);
        }
        catch (Exception ex)
        {
            context.Logger?.LogWarning(ex, "Failed to create archive index");
        }
    }
    
    private async Task VerifyArchiveIntegrityAsync(string archivePath, ExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read);
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);
                
                foreach (var entry in archive.Entries)
                {
                    // Try to read each entry to verify it's not corrupted
                    using var entryStream = entry.Open();
                    var buffer = new byte[1024];
                    while (entryStream.Read(buffer, 0, buffer.Length) > 0)
                    {
                        // Just read through the entry
                    }
                }
            }, cancellationToken);
            
            context.Logger?.LogDebug("Archive integrity verified successfully");
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "Archive integrity verification failed");
            throw new InvalidOperationException("Archive integrity verification failed", ex);
        }
    }
    
    private async Task CreateGZipArchiveAsync(ExecutionContext context, string archivePath, CancellationToken cancellationToken)
    {
        // GZip is single-file compression
        if (AppendToExisting && File.Exists(archivePath))
        {
            // For GZip, we'll create a new file with timestamp
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var nameWithoutExt = Path.GetFileNameWithoutExtension(archivePath);
            var dir = Path.GetDirectoryName(archivePath) ?? "";
            archivePath = Path.Combine(dir, $"{nameWithoutExt}_{timestamp}.gz");
        }
        
        using var sourceStream = new FileStream(context.FilePath, FileMode.Open, FileAccess.Read);
        using var destinationStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write);
        using var gzipStream = new GZipStream(destinationStream, CompressionLevel);
        
        await sourceStream.CopyToAsync(gzipStream, 81920, cancellationToken);
    }
    
    private string ResolveVariables(string path, ExecutionContext context)
    {
        var result = path;
        
        // Replace built-in variables
        result = result.Replace("{fileName}", context.FileName);
        result = result.Replace("{fileNameWithoutExt}", context.FileNameWithoutExtension);
        result = result.Replace("{fileExt}", context.FileExtension);
        result = result.Replace("{fileDir}", context.FileDirectory);
        result = result.Replace("{date}", DateTime.Now.ToString("yyyyMMdd"));
        result = result.Replace("{time}", DateTime.Now.ToString("HHmmss"));
        result = result.Replace("{datetime}", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        
        // Replace custom variables
        foreach (var variable in context.Variables)
        {
            result = result.Replace($"{{{variable.Key}}}", variable.Value?.ToString() ?? string.Empty);
        }
        
        return result;
    }
    
    private string ApplyArchiveNamePattern(string pattern, ExecutionContext context)
    {
        return ResolveVariables(pattern, context);
    }
    
    private string EnsureArchiveExtension(string path, ArchiveFormat format)
    {
        var extension = format switch
        {
            ArchiveFormat.Zip => ".zip",
            ArchiveFormat.GZip => ".gz",
            ArchiveFormat.Tar => ".tar",
            _ => ".zip"
        };
        
        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return path + extension;
        }
        
        return path;
    }
}

/// <summary>
/// Archive format enumeration
/// </summary>
public enum ArchiveFormat
{
    Zip,
    GZip,
    Tar
}

/// <summary>
/// Strategy for handling conflicts when adding files to archives
/// </summary>
public enum ConflictStrategy
{
    Skip,           // Skip the file if it already exists
    Replace,        // Replace the existing file
    Rename,         // Rename with timestamp
    IncrementNumber // Rename with incrementing number
}

/// <summary>
/// Validator for ArchiveStep
/// </summary>
public class ArchiveStepValidator : AbstractValidator<ArchiveStep>
{
    public ArchiveStepValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Step name is required");
        
        RuleFor(x => x.ArchivePath)
            .NotEmpty().WithMessage("Archive path is required")
            .Must(BeAValidPathPattern).WithMessage("Invalid archive path pattern");
        
        RuleFor(x => x.Format)
            .IsInEnum().WithMessage("Invalid archive format");
        
        RuleFor(x => x.CompressionLevel)
            .IsInEnum().WithMessage("Invalid compression level");
    }
    
    private bool BeAValidPathPattern(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        
        // Allow variable placeholders
        var testPath = path.Replace("{fileName}", "test.txt")
                          .Replace("{fileNameWithoutExt}", "test")
                          .Replace("{fileExt}", ".txt")
                          .Replace("{fileDir}", "C:\\temp")
                          .Replace("{date}", "20240101")
                          .Replace("{time}", "120000")
                          .Replace("{datetime}", "20240101_120000");
        
        try
        {
            Path.GetFullPath(testPath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}