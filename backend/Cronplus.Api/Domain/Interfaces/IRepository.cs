namespace Cronplus.Api.Domain.Interfaces;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<IEnumerable<T>> FindAsync(string whereClause, object? parameters = null);
    Task<int> AddAsync(T entity);
    Task<bool> UpdateAsync(T entity);
    Task<bool> DeleteAsync(object id);
    Task<int> CountAsync(string? whereClause = null, object? parameters = null);
}

public interface ITaskRepository : IRepository<Entities.TaskEntity>
{
    Task<Entities.TaskEntity?> GetByIdWithDetailsAsync(string id);
    Task<IEnumerable<Entities.TaskEntity>> GetEnabledTasksAsync();
    Task<bool> TaskExistsAsync(string id);
}

public interface IPipelineStepRepository : IRepository<Entities.PipelineStep>
{
    Task<IEnumerable<Entities.PipelineStep>> GetByTaskIdAsync(string taskId);
    Task<int> GetMaxStepOrderAsync(string taskId);
    Task<bool> ReorderStepsAsync(string taskId, List<(int stepId, int newOrder)> reorderList);
}

public interface ITaskVariableRepository : IRepository<Entities.TaskVariable>
{
    Task<IEnumerable<Entities.TaskVariable>> GetByTaskIdAsync(string taskId);
    Task<Entities.TaskVariable?> GetByTaskIdAndNameAsync(string taskId, string name);
    Task<bool> VariableExistsAsync(string taskId, string name);
}

public interface IExecutionLogRepository : IRepository<Entities.ExecutionLog>
{
    Task<IEnumerable<Entities.ExecutionLog>> GetByTaskIdAsync(string taskId, int limit = 100);
    Task<IEnumerable<Entities.ExecutionLog>> GetRecentLogsAsync(int limit = 100);
    Task<Entities.ExecutionLog?> GetLatestByTaskIdAsync(string taskId);
    Task<int> CleanupOldLogsAsync(int daysToKeep);
}