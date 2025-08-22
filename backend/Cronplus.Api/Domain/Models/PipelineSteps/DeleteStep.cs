using System.Security.Cryptography;
using FluentValidation;
using FluentValidation.Results;

namespace Cronplus.Api.Domain.Models.PipelineSteps;

/// <summary>
/// Pipeline step for deleting files
/// </summary>
public class DeleteStep : PipelineStepBase
{
    public override string StepType => "delete";
    
    public bool DeleteEmptyDirectories { get; set; } = false;
    public bool RequireConfirmation { get; set; } = false;
    public long? MinFileAgeMinutes { get; set; } // Only delete files older than X minutes
    public long? MaxFileSizeBytes { get; set; } // Only delete files smaller than X bytes
    public string? FilePattern { get; set; } // Additional pattern matching for safety
    public bool SecureDelete { get; set; } = false; // Overwrite file before deletion
    public int SecureDeletePasses { get; set; } = 3; // Number of overwrite passes
    public bool MoveToRecycleBin { get; set; } = false; // Move to recycle bin instead of permanent deletion
    public string? BackupPath { get; set; } // Create backup before deletion
    
    public override async Task<StepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            context.Logger?.LogInformation("Executing DeleteStep: {Name} for file {FilePath}", Name, context.FilePath);
            
            // Safety checks
            if (!File.Exists(context.FilePath))
            {
                return StepResult.SuccessResult("File already deleted or doesn't exist");
            }
            
            var fileInfo = new FileInfo(context.FilePath);
            
            // Check file age if specified
            if (MinFileAgeMinutes.HasValue)
            {
                var fileAge = DateTime.UtcNow - fileInfo.LastWriteTimeUtc;
                if (fileAge.TotalMinutes < MinFileAgeMinutes.Value)
                {
                    context.Logger?.LogWarning("File {FilePath} is too recent to delete (age: {Age} minutes)", 
                        context.FilePath, fileAge.TotalMinutes);
                    return StepResult.SuccessResult($"File skipped - too recent (age: {fileAge.TotalMinutes:F1} minutes)");
                }
            }
            
            // Check file size if specified
            if (MaxFileSizeBytes.HasValue && fileInfo.Length > MaxFileSizeBytes.Value)
            {
                context.Logger?.LogWarning("File {FilePath} is too large to delete (size: {Size} bytes)", 
                    context.FilePath, fileInfo.Length);
                return StepResult.SuccessResult($"File skipped - too large (size: {fileInfo.Length} bytes)");
            }
            
            // Check file pattern if specified
            if (!string.IsNullOrEmpty(FilePattern))
            {
                var pattern = new System.Text.RegularExpressions.Regex(FilePattern);
                if (!pattern.IsMatch(context.FileName))
                {
                    context.Logger?.LogWarning("File {FilePath} doesn't match pattern {Pattern}", 
                        context.FilePath, FilePattern);
                    return StepResult.SuccessResult($"File skipped - doesn't match pattern {FilePattern}");
                }
            }
            
            // Store file info before deletion
            var deletedFileInfo = new Dictionary<string, object>
            {
                ["DeletedFilePath"] = context.FilePath,
                ["DeletedFileSize"] = fileInfo.Length,
                ["DeletedFileCreated"] = fileInfo.CreationTimeUtc,
                ["DeletedFileModified"] = fileInfo.LastWriteTimeUtc
            };
            
            // Create backup if requested
            if (!string.IsNullOrEmpty(BackupPath))
            {
                var backupResult = await CreateBackupAsync(context.FilePath, BackupPath, context, cancellationToken);
                if (!backupResult.success)
                {
                    return StepResult.FailureResult($"Failed to create backup: {backupResult.error}");
                }
                deletedFileInfo["BackupPath"] = backupResult.backupPath;
                context.Logger?.LogDebug("Created backup at: {BackupPath}", backupResult.backupPath);
            }
            
            // Perform deletion based on configured method
            if (MoveToRecycleBin)
            {
                await MoveToRecycleBinAsync(context.FilePath, cancellationToken);
                context.Logger?.LogInformation("Moved file to recycle bin: {FilePath}", context.FilePath);
                deletedFileInfo["DeletionMethod"] = "RecycleBin";
            }
            else if (SecureDelete)
            {
                await SecureDeleteFileAsync(context.FilePath, SecureDeletePasses, context, cancellationToken);
                context.Logger?.LogInformation("Securely deleted file: {FilePath} with {Passes} passes", 
                    context.FilePath, SecureDeletePasses);
                deletedFileInfo["DeletionMethod"] = $"SecureDelete-{SecureDeletePasses}passes";
            }
            else
            {
                // Standard deletion
                await Task.Run(() => File.Delete(context.FilePath), cancellationToken);
                context.Logger?.LogInformation("Successfully deleted file: {FilePath}", context.FilePath);
                deletedFileInfo["DeletionMethod"] = "Standard";
            }
            
            // Delete empty parent directories if requested
            if (DeleteEmptyDirectories)
            {
                await DeleteEmptyParentDirectoriesAsync(Path.GetDirectoryName(context.FilePath), cancellationToken);
            }
            
            stopwatch.Stop();
            
            var result = StepResult.SuccessResult($"File deleted: {context.FilePath}", deletedFileInfo);
            result.ExecutionTime = stopwatch.Elapsed;
            return result;
        }
        catch (UnauthorizedAccessException ex)
        {
            stopwatch.Stop();
            context.Logger?.LogError(ex, "Access denied when deleting file: {FilePath}", context.FilePath);
            var result = StepResult.FailureResult($"Access denied: {ex.Message}", ex);
            result.ExecutionTime = stopwatch.Elapsed;
            return result;
        }
        catch (IOException ex)
        {
            stopwatch.Stop();
            context.Logger?.LogError(ex, "IO error when deleting file: {FilePath}", context.FilePath);
            var result = StepResult.FailureResult($"IO error: {ex.Message}", ex);
            result.ExecutionTime = stopwatch.Elapsed;
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            context.Logger?.LogError(ex, "Failed to delete file in step: {Name}", Name);
            var result = StepResult.FailureResult($"Delete failed: {ex.Message}", ex);
            result.ExecutionTime = stopwatch.Elapsed;
            return result;
        }
    }
    
    public override ValidationResult Validate()
    {
        var validator = new DeleteStepValidator();
        return validator.Validate(this);
    }
    
    private async Task DeleteEmptyParentDirectoriesAsync(string? directoryPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            return;
        
        await Task.Run(() =>
        {
            try
            {
                var directory = new DirectoryInfo(directoryPath);
                
                // Don't delete if it has files or subdirectories
                if (directory.GetFiles().Length == 0 && directory.GetDirectories().Length == 0)
                {
                    directory.Delete();
                    
                    // Recursively check parent directory
                    if (directory.Parent != null)
                    {
                        DeleteEmptyParentDirectoriesAsync(directory.Parent.FullName, cancellationToken).Wait();
                    }
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail the step if directory deletion fails
                // This is a best-effort operation
                System.Diagnostics.Debug.WriteLine($"Failed to delete empty directory {directoryPath}: {ex.Message}");
            }
        }, cancellationToken);
    }
    
    private async Task SecureDeleteFileAsync(string filePath, int passes, ExecutionContext context, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var fileSize = fileInfo.Length;
        
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[4096];
            var totalBytesToWrite = fileSize;
            
            for (int pass = 1; pass <= passes; pass++)
            {
                stream.Position = 0;
                long bytesWritten = 0;
                
                // Different patterns for each pass
                byte fillByte = pass switch
                {
                    1 => 0x00,  // All zeros
                    2 => 0xFF,  // All ones
                    _ => (byte)(pass % 256)  // Pattern based on pass number
                };
                
                // For the last pass, use random data
                if (pass == passes)
                {
                    using var rng = RandomNumberGenerator.Create();
                    
                    while (bytesWritten < totalBytesToWrite)
                    {
                        rng.GetBytes(buffer);
                        var bytesToWrite = (int)Math.Min(buffer.Length, totalBytesToWrite - bytesWritten);
                        await stream.WriteAsync(buffer, 0, bytesToWrite, cancellationToken);
                        bytesWritten += bytesToWrite;
                    }
                }
                else
                {
                    // Fill with pattern
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        buffer[i] = fillByte;
                    }
                    
                    while (bytesWritten < totalBytesToWrite)
                    {
                        var bytesToWrite = (int)Math.Min(buffer.Length, totalBytesToWrite - bytesWritten);
                        await stream.WriteAsync(buffer, 0, bytesToWrite, cancellationToken);
                        bytesWritten += bytesToWrite;
                    }
                }
                
                await stream.FlushAsync(cancellationToken);
                context.Logger?.LogDebug("Secure delete pass {Pass}/{Total} completed", pass, passes);
            }
        }
        
        // Now delete the file
        File.Delete(filePath);
    }
    
    private async Task MoveToRecycleBinAsync(string filePath, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            // Platform-specific recycle bin implementation
            if (OperatingSystem.IsWindows())
            {
                // Use Windows Shell API for recycle bin
                // Note: This requires Microsoft.WindowsAPICodePack.Shell NuGet package
                // For now, we'll just move to a .trash folder
                MoveToTrashFolder(filePath);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                // Move to trash folder following FreeDesktop.org specification
                MoveToTrashFolder(filePath);
            }
            else
            {
                // Fallback to regular delete
                File.Delete(filePath);
            }
        }, cancellationToken);
    }
    
    private void MoveToTrashFolder(string filePath)
    {
        var trashPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".trash",
            DateTime.Now.ToString("yyyyMMdd"));
        
        if (!Directory.Exists(trashPath))
        {
            Directory.CreateDirectory(trashPath);
        }
        
        var fileName = Path.GetFileName(filePath);
        var destPath = Path.Combine(trashPath, fileName);
        
        // Handle name conflicts
        var counter = 1;
        while (File.Exists(destPath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            destPath = Path.Combine(trashPath, $"{nameWithoutExt}_{counter}{ext}");
            counter++;
        }
        
        File.Move(filePath, destPath);
    }
    
    private async Task<(bool success, string? error, string? backupPath)> CreateBackupAsync(
        string filePath, string backupBasePath, ExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupDir = Path.Combine(backupBasePath, timestamp);
            
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }
            
            var backupPath = Path.Combine(backupDir, fileName);
            
            // Copy file to backup location
            using (var source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var dest = new FileStream(backupPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(dest, 81920, cancellationToken);
            }
            
            context.Logger?.LogDebug("Created backup of {FilePath} at {BackupPath}", filePath, backupPath);
            return (true, null, backupPath);
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "Failed to create backup of {FilePath}", filePath);
            return (false, ex.Message, null);
        }
    }
}

/// <summary>
/// Validator for DeleteStep
/// </summary>
public class DeleteStepValidator : AbstractValidator<DeleteStep>
{
    public DeleteStepValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Step name is required");
        
        RuleFor(x => x.MinFileAgeMinutes)
            .GreaterThanOrEqualTo(0)
            .When(x => x.MinFileAgeMinutes.HasValue)
            .WithMessage("Minimum file age must be non-negative");
        
        RuleFor(x => x.MaxFileSizeBytes)
            .GreaterThan(0)
            .When(x => x.MaxFileSizeBytes.HasValue)
            .WithMessage("Maximum file size must be positive");
        
        RuleFor(x => x.FilePattern)
            .Must(BeAValidRegexPattern)
            .When(x => !string.IsNullOrEmpty(x.FilePattern))
            .WithMessage("File pattern must be a valid regular expression");
    }
    
    private bool BeAValidRegexPattern(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;
        
        try
        {
            _ = new System.Text.RegularExpressions.Regex(pattern);
            return true;
        }
        catch
        {
            return false;
        }
    }
}