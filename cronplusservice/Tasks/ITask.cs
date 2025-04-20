using System;
using System.Threading.Tasks;
using CronPlus.Storage;

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
    /// <param name="dataStore">The data store for logging and configuration</param>
    Task Execute(string filePath, DataStore dataStore);
}
