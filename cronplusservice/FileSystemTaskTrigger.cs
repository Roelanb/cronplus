using System;
using System.Collections.Generic;
using System.IO;
using CronPlus.Tasks;

namespace CronPlus;

/// <summary>
/// Monitors a directory for file system events and triggers tasks based on configuration
/// </summary>
public class FileSystemTaskTrigger
{
    private readonly string _directory;
    private readonly List<TaskConfig> _configs;
    private readonly FileSystemWatcher _watcher;

    public FileSystemTaskTrigger(string directory, List<TaskConfig> configs)
    {
        _directory = directory;
        _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        _watcher = new FileSystemWatcher(_directory);
        _watcher.Created += OnFileCreated;
        _watcher.Renamed += OnFileRenamed;
        _watcher.EnableRaisingEvents = false; // Start disabled, will enable in Start()
    }

    /// <summary>
    /// Start monitoring the directory for file events
    /// </summary>
    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
        _watcher.IncludeSubdirectories = false;
        Console.WriteLine($"Watching directory: {_directory}");
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"File created: {e.FullPath}");
        ExecuteTasks("fileCreated", e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        Console.WriteLine($"File renamed: {e.OldFullPath} to {e.FullPath}");
        ExecuteTasks("fileRenamed", e.FullPath);
    }

    private void ExecuteTasks(string triggerType, string filePath)
    {
        foreach (var config in _configs)
        {
            if (config.TriggerType == triggerType)
            {
                Console.WriteLine($"Executing task: {config.TaskType} for file: {filePath}");
                
                try
                {
                    // Use the factory to create an appropriate task instance
                    ITask task = Tasks.TaskFactory.CreateTask(config);
                    task.Execute(filePath);
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
    }

}

