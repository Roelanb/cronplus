using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CronPlus.Models;
using CronPlus.Tasks;
using CronPlus.Storage;

namespace CronPlus;

/// <summary>
/// Monitors a directory for file system events and triggers tasks based on configuration
/// </summary>
public class FileSystemTaskTrigger : IDisposable
{
    private readonly string _directory;
    private readonly List<TaskConfig> _configs;
    private readonly FileSystemWatcher _watcher;
    private readonly CronPlus.Storage.DataStore _dataStore;

    public FileSystemTaskTrigger(string sourceFolder, List<TaskConfig> configs, CronPlus.Storage.DataStore dataStore)
    {
        _directory = sourceFolder;
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
            if (config.triggerType == triggerType)
            {
                Console.WriteLine($"Executing task: {config.taskType} for file: {filePath}");
                var log = new TaskLogging {
                    TaskType = config.taskType,
                    TriggerType = config.triggerType,
                    SourceFolder = config.sourceFolder,
                    DestinationFolder = config.destinationFolder,
                    FilePath = filePath,
                    PrinterName = config.printerName,
                    ArchiveDirectory = config.archiveDirectory,
                    TriggeredAt = DateTime.UtcNow
                };
                try
                {
                    // Use the factory to create an appropriate task instance
                    ITask task = Tasks.TaskFactory.CreateTask(config);
                    await task.Execute(filePath, _dataStore);
                    log.Result = "Success";
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine(ex.Message);
                    log.Result = $"Failure: {ex.Message}";
                }
                await _dataStore.SaveTaskLog(log);
            }
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
    }
}
