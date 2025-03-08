using System;
using System.IO;

namespace CronPlus.Tasks;

/// <summary>
/// Task for moving files from one location to another
/// </summary>
public class MoveTask : BaseTask
{
    public MoveTask(TaskConfig config) : base(config)
    {
    }

    public override void Execute(string filePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(_config.DestinationFile))
            {
                Console.WriteLine($"Moving file from {filePath} to {_config.DestinationFile}");
                File.Move(filePath, _config.DestinationFile);
                Console.WriteLine("File moved successfully");
            }
            else
            {
                Console.WriteLine("Destination file not specified for move operation.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error moving file {filePath}: {ex.Message}");
        }
    }
}
