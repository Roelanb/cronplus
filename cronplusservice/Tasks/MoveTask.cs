using System;
using System.IO;
using CronPlus.Models;
using CronPlus.Helpers;
using CronPlus.Storage;
using System.Diagnostics;

namespace CronPlus.Tasks;

/// <summary>
/// Task for moving files from one location to another
/// </summary>
public class MoveTask : BaseTask
{
    public MoveTask(TaskConfig config) : base(config)
    {
    }

    public override async Task Execute(string filePath, DataStore dataStore)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Console.WriteLine($"Executing task: {TaskType} for file: {filePath}");
            if (!string.IsNullOrEmpty(_config.destinationFile))
            {
                string destinationPath = FilenameHelper.TranslateFilename(filePath, _config.destinationFile, _config.destinationFolder ?? string.Empty);
                Console.WriteLine($"Moving file from {filePath} to {destinationPath}");

                // Wait a moment to ensure the file is fully written
                await Task.Delay(1000);

                // Ensure the destination directory exists
                string? destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                // Move the file
                File.Move(filePath, destinationPath, true);
                Console.WriteLine("File moved successfully");

                stopwatch.Stop();
                // Log the successful execution
                var log = new TaskLogging
                {
                    TaskType = TaskType,
                    TriggerType = _config.triggerType,
                    FilePath = filePath,
                    SourceFolder = _config.sourceFolder ?? string.Empty,
                    DestinationFolder = _config.destinationFolder ?? string.Empty,
                    Result = $"File moved to {destinationPath}",
                    TriggeredAt = DateTime.UtcNow,
                    Duration = stopwatch.Elapsed
                };
                await dataStore.SaveTaskLog(log);
            }
            else
            {
                Console.WriteLine("No destination file configured for move task");

                stopwatch.Stop();
                // Log the error
                var log = new TaskLogging
                {
                    TaskType = TaskType,
                    TriggerType = _config.triggerType,
                    FilePath = filePath,
                    SourceFolder = _config.sourceFolder ?? string.Empty,
                    DestinationFolder = _config.destinationFolder ?? string.Empty,
                    Result = "No destination file configured",
                    TriggeredAt = DateTime.UtcNow,
                    Duration = stopwatch.Elapsed
                };
                await dataStore.SaveTaskLog(log);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error moving file: {ex.Message}");

            stopwatch.Stop();
            // Log the error
            var log = new TaskLogging
            {
                TaskType = TaskType,
                TriggerType = _config.triggerType,
                FilePath = filePath,
                SourceFolder = _config.sourceFolder ?? string.Empty,
                DestinationFolder = _config.destinationFolder ?? string.Empty,
                Result = $"Error: {ex.Message}",
                TriggeredAt = DateTime.UtcNow,
                Duration = stopwatch.Elapsed
            };
            await dataStore.SaveTaskLog(log);
        }
    }

    protected override string TaskType => "Move";
}
