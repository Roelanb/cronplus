using System;

namespace CronPlus.Tasks;

/// <summary>
/// Interface for all task implementations
/// </summary>
public interface ITask
{
    /// <summary>
    /// Execute the task on the given file path
    /// </summary>
    /// <param name="filePath">The path to the file that triggered the task</param>
    void Execute(string filePath);
}
