using System;
using System.Threading.Tasks;
using CronPlus.Models;
using CronPlus.Storage;

namespace CronPlus.Tasks;

/// <summary>
/// Base class for all task implementations
/// </summary>
public abstract class BaseTask : ITask
{
    protected readonly TaskConfig _config;

    protected BaseTask(TaskConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Execute the task on the given file path
    /// </summary>
    /// <param name="filePath">The path to the file that triggered the task</param>
    /// <param name="dataStore">The data store for logging and configuration</param>
    public abstract Task Execute(string filePath, DataStore dataStore);

    /// <summary>
    /// The type of task, used for display and logging
    /// </summary>
    protected abstract string TaskType { get; }
}
