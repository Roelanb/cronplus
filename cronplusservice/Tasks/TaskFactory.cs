using System;
using CronPlus.Models;

namespace CronPlus.Tasks;

/// <summary>
/// Factory class for creating task instances
/// </summary>
public static class TaskFactory
{
    /// <summary>
    /// Creates a task instance based on the task type in the config
    /// </summary>
    /// <param name="config">Task configuration</param>
    /// <returns>Task implementation</returns>
    public static ITask CreateTask(TaskConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.TaskType.ToLower() switch
        {
            "print" => new PrintTask(config),
            "copy" => new CopyTask(config),
            "move" => new MoveTask(config),
            _ => throw new ArgumentException($"Unknown task type: {config.TaskType}", nameof(config.TaskType)),
        };
    }
}
