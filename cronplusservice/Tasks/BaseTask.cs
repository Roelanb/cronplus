using System;

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
    public abstract void Execute(string filePath);
}
