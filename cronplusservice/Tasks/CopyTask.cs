using System;
using System.IO;
using CronPlus.Models;

namespace CronPlus.Tasks;

/// <summary>
/// Task for copying files from one location to another
/// </summary>
public class CopyTask : BaseTask
{
    public CopyTask(TaskConfig config) : base(config)
    {
    }

    public override void Execute(string filePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(_config.DestinationFile))
            {
                Console.WriteLine($"Copying file from {filePath} to {_config.DestinationFile}");
                File.Copy(filePath, _config.DestinationFile, true);
                Console.WriteLine("File copied successfully");
            }
            else
            {
                Console.WriteLine("Destination file not specified for copy operation.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error copying file {filePath}: {ex.Message}");
        }
    }
}
