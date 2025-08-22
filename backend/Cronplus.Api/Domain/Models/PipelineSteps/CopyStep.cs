using System.Security.Cryptography;
using FluentValidation;
using FluentValidation.Results;

namespace Cronplus.Api.Domain.Models.PipelineSteps;

/// <summary>
/// Pipeline step for copying files
/// </summary>
public class CopyStep : PipelineStepBase
{
    public override string StepType => "copy";
    
    public string DestinationPath { get; set; } = string.Empty;
    public bool Overwrite { get; set; } = false;
    public bool CreateDirectories { get; set; } = true;
    public bool PreserveTimestamps { get; set; } = true;
    public string? RenamePattern { get; set; } // e.g., "{name}_copy{ext}", "{name}_{date:yyyyMMdd}{ext}"
    public bool UseAtomicMove { get; set; } = true; // Use atomic operations when possible
    public bool VerifyChecksum { get; set; } = true; // Verify file integrity after copy
    public bool DeleteSourceAfterCopy { get; set; } = false; // Move instead of copy
    public int MaxRetries { get; set; } = 3; // Retry count for transient failures
    
    public override async Task<StepResult> ExecuteAsync(ExecutionContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            context.Logger?.LogInformation("Executing CopyStep: {Name} for file {FilePath}", Name, context.FilePath);
            
            // Validate source file exists
            if (!File.Exists(context.FilePath))
            {
                return StepResult.FailureResult($"Source file does not exist: {context.FilePath}");
            }
            
            // Resolve destination path with variables
            var resolvedDestination = ResolveVariables(DestinationPath, context);
            
            // Apply rename pattern if specified
            if (!string.IsNullOrEmpty(RenamePattern))
            {
                var fileName = ApplyRenamePattern(context.FileName, context);
                resolvedDestination = Path.Combine(resolvedDestination, fileName);
            }
            else if (Directory.Exists(resolvedDestination))
            {
                // If destination is a directory, append the original filename
                resolvedDestination = Path.Combine(resolvedDestination, context.FileName);
            }
            
            // Create directories if needed
            if (CreateDirectories)
            {
                var destDir = Path.GetDirectoryName(resolvedDestination);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                    context.Logger?.LogDebug("Created directory: {Directory}", destDir);
                }
            }
            
            // Check if file exists and handle overwrite
            if (File.Exists(resolvedDestination) && !Overwrite)
            {
                return StepResult.FailureResult($"Destination file already exists: {resolvedDestination}");
            }
            
            // Calculate source checksum if verification is enabled
            string? sourceChecksum = null;
            if (VerifyChecksum)
            {
                sourceChecksum = await CalculateChecksumAsync(context.FilePath, cancellationToken);
                context.Logger?.LogDebug("Source file checksum: {Checksum}", sourceChecksum);
            }
            
            // Perform the copy/move operation with retry logic
            var success = await ExecuteWithRetryAsync(async () =>
            {
                if (DeleteSourceAfterCopy && UseAtomicMove && CanPerformAtomicMove(context.FilePath, resolvedDestination))
                {
                    // Attempt atomic move
                    await PerformAtomicMoveAsync(context.FilePath, resolvedDestination, cancellationToken);
                    context.Logger?.LogDebug("Performed atomic move to: {Destination}", resolvedDestination);
                }
                else
                {
                    // Copy the file
                    await CopyFileWithProgressAsync(context.FilePath, resolvedDestination, context, cancellationToken);
                    
                    // Verify checksum if enabled
                    if (VerifyChecksum && sourceChecksum != null)
                    {
                        var destChecksum = await CalculateChecksumAsync(resolvedDestination, cancellationToken);
                        if (sourceChecksum != destChecksum)
                        {
                            // Delete corrupted copy
                            File.Delete(resolvedDestination);
                            throw new InvalidOperationException($"Checksum verification failed. Source: {sourceChecksum}, Destination: {destChecksum}");
                        }
                        context.Logger?.LogDebug("Checksum verification successful");
                    }
                    
                    // Delete source if move operation
                    if (DeleteSourceAfterCopy)
                    {
                        File.Delete(context.FilePath);
                        context.Logger?.LogDebug("Deleted source file after successful copy");
                    }
                }
                
                return true;
            }, MaxRetries, context.Logger);
            
            if (!success)
            {
                return StepResult.FailureResult($"Failed to copy/move file after {MaxRetries} retries");
            }
            
            // Preserve timestamps if requested
            if (PreserveTimestamps && File.Exists(resolvedDestination) && !DeleteSourceAfterCopy)
            {
                try
                {
                    var sourceInfo = new FileInfo(context.FilePath);
                    var destInfo = new FileInfo(resolvedDestination);
                    destInfo.CreationTimeUtc = sourceInfo.CreationTimeUtc;
                    destInfo.LastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;
                    destInfo.LastAccessTimeUtc = sourceInfo.LastAccessTimeUtc;
                }
                catch (Exception ex)
                {
                    context.Logger?.LogWarning(ex, "Failed to preserve timestamps");
                }
            }
            
            stopwatch.Stop();
            
            // Set output variables
            var outputs = new Dictionary<string, object>
            {
                ["DestinationPath"] = resolvedDestination,
                ["FileSize"] = new FileInfo(resolvedDestination).Length,
                ["Operation"] = DeleteSourceAfterCopy ? "Move" : "Copy"
            };
            
            if (VerifyChecksum && sourceChecksum != null)
            {
                outputs["Checksum"] = sourceChecksum;
            }
            
            var operation = DeleteSourceAfterCopy ? "moved" : "copied";
            context.Logger?.LogInformation("Successfully {Operation} file to: {Destination}", operation, resolvedDestination);
            
            var result = StepResult.SuccessResult($"File {operation} to {resolvedDestination}", outputs);
            result.ExecutionTime = stopwatch.Elapsed;
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            context.Logger?.LogError(ex, "Failed to copy/move file in step: {Name}", Name);
            var result = StepResult.FailureResult($"Copy/move failed: {ex.Message}", ex);
            result.ExecutionTime = stopwatch.Elapsed;
            return result;
        }
    }
    
    public override ValidationResult Validate()
    {
        var validator = new CopyStepValidator();
        return validator.Validate(this);
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
    
    private string ApplyRenamePattern(string originalFileName, ExecutionContext context)
    {
        if (string.IsNullOrEmpty(RenamePattern))
            return originalFileName;
        
        var nameWithoutExt = Path.GetFileNameWithoutExtension(originalFileName);
        var ext = Path.GetExtension(originalFileName);
        
        var result = RenamePattern;
        result = result.Replace("{name}", nameWithoutExt);
        result = result.Replace("{ext}", ext);
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
    
    private async Task<string> CalculateChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        
        var buffer = new byte[8192];
        int bytesRead;
        
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            sha256.TransformBlock(buffer, 0, bytesRead, buffer, 0);
        }
        
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return BitConverter.ToString(sha256.Hash!).Replace("-", "").ToLowerInvariant();
    }
    
    private bool CanPerformAtomicMove(string source, string destination)
    {
        try
        {
            // Atomic move is only possible on the same volume/drive
            var sourceDrive = Path.GetPathRoot(Path.GetFullPath(source));
            var destDrive = Path.GetPathRoot(Path.GetFullPath(destination));
            return string.Equals(sourceDrive, destDrive, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    
    private async Task PerformAtomicMoveAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            // Delete existing file if overwrite is enabled
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
            
            // Perform atomic move
            File.Move(source, destination);
        }, cancellationToken);
    }
    
    private async Task CopyFileWithProgressAsync(string source, string destination, ExecutionContext context, CancellationToken cancellationToken)
    {
        const int bufferSize = 1024 * 1024; // 1MB buffer for better performance
        var fileInfo = new FileInfo(source);
        var totalBytes = fileInfo.Length;
        long bytesCopied = 0;
        var lastProgressReport = DateTime.UtcNow;
        
        using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        using var destinationStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        
        var buffer = new byte[bufferSize];
        int bytesRead;
        
        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await destinationStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            bytesCopied += bytesRead;
            
            // Report progress every second for large files
            if (totalBytes > 10 * 1024 * 1024 && (DateTime.UtcNow - lastProgressReport).TotalSeconds >= 1)
            {
                var percentComplete = (int)((bytesCopied * 100) / totalBytes);
                context.Logger?.LogDebug("Copy progress: {Percent}% ({BytesCopied}/{TotalBytes} bytes)", 
                    percentComplete, bytesCopied, totalBytes);
                lastProgressReport = DateTime.UtcNow;
            }
        }
        
        await destinationStream.FlushAsync(cancellationToken);
    }
    
    private async Task<bool> ExecuteWithRetryAsync(Func<Task<bool>> operation, int maxRetries, ILogger? logger)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // Exponential backoff
                logger?.LogWarning(ex, "Attempt {Attempt}/{MaxRetries} failed. Retrying in {Delay}ms", 
                    attempt, maxRetries, delay.TotalMilliseconds);
                await Task.Delay(delay);
            }
        }
        
        return false;
    }
}

/// <summary>
/// Validator for CopyStep
/// </summary>
public class CopyStepValidator : AbstractValidator<CopyStep>
{
    public CopyStepValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Step name is required");
        
        RuleFor(x => x.DestinationPath)
            .NotEmpty().WithMessage("Destination path is required")
            .Must(BeAValidPathPattern).WithMessage("Invalid destination path pattern");
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