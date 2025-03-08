using System;
using System.Collections.Generic;
using CronPlus;

namespace CronPlusUI.Models;

public class TaskModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public string TriggerType { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string? SourceFile { get; set; }
    public string? DestinationFile { get; set; }
    public string? Time { get; set; }
    public int Interval { get; set; }
    public string? PrinterName { get; set; }
    public string? ArchiveDirectory { get; set; }
    
    // Available options for UI dropdowns
    public static List<string> AvailableTriggerTypes => new() 
    { 
        "fileCreated", 
        "fileRenamed", 
        "time", 
        "interval" 
    };
    
    public static List<string> AvailableTaskTypes => new() 
    { 
        "print", 
        "copy", 
        "move" 
    };
    
    // Convert to/from TaskConfig for integration with the service
    public static TaskModel FromTaskConfig(TaskConfig config)
    {
        return new TaskModel
        {
            TriggerType = config.TriggerType,
            Directory = config.Directory,
            TaskType = config.TaskType,
            SourceFile = config.SourceFile,
            DestinationFile = config.DestinationFile,
            Time = config.Time,
            Interval = config.Interval,
            PrinterName = config.PrinterName,
            ArchiveDirectory = config.ArchiveDirectory
        };
    }
    
    public TaskConfig ToTaskConfig()
    {
        return new TaskConfig
        {
            TriggerType = TriggerType,
            Directory = Directory,
            TaskType = TaskType,
            SourceFile = SourceFile,
            DestinationFile = DestinationFile,
            Time = Time,
            Interval = Interval,
            PrinterName = PrinterName,
            ArchiveDirectory = ArchiveDirectory
        };
    }
}
