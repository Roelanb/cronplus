using System;
using System.Collections.Generic;
using System.IO;
using CronPlus.Models;
using CronPlus.Tasks;
using CronPlus.Storage;

namespace CronPlus;

/// <summary>
/// Monitors a directory for file system events and triggers tasks based on configuration
/// </summary>
public class FileSystemTaskTrigger
{
    private readonly string _directory;
    private readonly List<TaskConfig> _configs;
    private readonly FileSystemWatcher _watcher;
    private readonly DataStore _dataStore;

    public FileSystemTaskTrigger(string directory, List<TaskConfig> configs, DataStore dataStore)
    {
        _directory = directory;
        _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
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
        ExecuteTasks(TriggerType.FileCreated, e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        Console.WriteLine($"File renamed: {e.OldFullPath} to {e.FullPath}");
        ExecuteTasks(TriggerType.FileRenamed, e.FullPath);
    }

    private async void ExecuteTasks(TriggerType triggerType, string filePath)
    {
        foreach (var config in _configs)
        {
            if (config.TriggerType == triggerType)
            {
                Console.WriteLine($"Executing task: {config.TaskType} for file: {filePath}");
                var log = new TaskLogging {
                    TaskType = config.TaskType,
                    TriggerType = config.TriggerType,
                    Directory = config.Directory,
                    FilePath = filePath,
                    PrinterName = config.PrinterName,
                    ArchiveDirectory = config.ArchiveDirectory,
                    TriggeredAt = DateTime.UtcNow
                };
                try
                {
                    // Use the factory to create an appropriate task instance
                    ITask task = Tasks.TaskFactory.CreateTask(config);
                    task.Execute(filePath);
                    log.Result = "Success";
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine(ex.Message);
                    log.Result = $"Failure: {ex.Message}";
                }
                await _dataStore.LogTaskAsync(log);
            }
        }
    }

}
