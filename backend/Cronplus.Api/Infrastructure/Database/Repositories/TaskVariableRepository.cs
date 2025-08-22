using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Interfaces;

namespace Cronplus.Api.Infrastructure.Database.Repositories;

public class TaskVariableRepository : ITaskVariableRepository
{
    private readonly IDatabaseContext _context;
    private readonly ILogger<TaskVariableRepository> _logger;

    public TaskVariableRepository(IDatabaseContext context, ILogger<TaskVariableRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<TaskVariable?> GetByIdAsync(object id)
    {
        var sql = @"
            SELECT id, task_id, name, type, value, created_at, updated_at
            FROM task_variables 
            WHERE id = $id";
        
        return await _context.QuerySingleOrDefaultAsync<TaskVariable>(sql, new { id });
    }

    public async Task<IEnumerable<TaskVariable>> GetAllAsync()
    {
        var sql = @"
            SELECT id, task_id, name, type, value, created_at, updated_at
            FROM task_variables 
            ORDER BY task_id, name";
        
        return await _context.QueryAsync<TaskVariable>(sql);
    }

    public async Task<IEnumerable<TaskVariable>> GetByTaskIdAsync(string taskId)
    {
        var sql = @"
            SELECT id, task_id, name, type, value, created_at, updated_at
            FROM task_variables 
            WHERE task_id = $taskId
            ORDER BY name";
        
        return await _context.QueryAsync<TaskVariable>(sql, new { taskId });
    }

    public async Task<TaskVariable?> GetByTaskIdAndNameAsync(string taskId, string name)
    {
        var sql = @"
            SELECT id, task_id, name, type, value, created_at, updated_at
            FROM task_variables 
            WHERE task_id = $taskId AND name = $name";
        
        return await _context.QuerySingleOrDefaultAsync<TaskVariable>(sql, new { taskId, name });
    }

    public async Task<IEnumerable<TaskVariable>> FindAsync(string whereClause, object? parameters = null)
    {
        var sql = $@"
            SELECT id, task_id, name, type, value, created_at, updated_at
            FROM task_variables 
            WHERE {whereClause}
            ORDER BY task_id, name";
        
        return await _context.QueryAsync<TaskVariable>(sql, parameters);
    }

    public async Task<int> AddAsync(TaskVariable entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        var sql = @"
            INSERT INTO task_variables (task_id, name, type, value, created_at, updated_at)
            VALUES ($taskId, $name, $type, $value, $createdAt, $updatedAt)
            RETURNING id";
        
        var id = await _context.QuerySingleOrDefaultAsync<int>(sql, new
        {
            taskId = entity.TaskId,
            name = entity.Name,
            type = entity.Type,
            value = entity.Value,
            createdAt = entity.CreatedAt,
            updatedAt = entity.UpdatedAt
        });

        entity.Id = id;
        return 1;
    }

    public async Task<bool> UpdateAsync(TaskVariable entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;

        var sql = @"
            UPDATE task_variables 
            SET name = $name,
                type = $type,
                value = $value,
                updated_at = $updatedAt
            WHERE id = $id";
        
        var result = await _context.ExecuteAsync(sql, new
        {
            id = entity.Id,
            name = entity.Name,
            type = entity.Type,
            value = entity.Value,
            updatedAt = entity.UpdatedAt
        });

        return result > 0;
    }

    public async Task<bool> DeleteAsync(object id)
    {
        var sql = "DELETE FROM task_variables WHERE id = $id";
        var result = await _context.ExecuteAsync(sql, new { id });
        
        return result > 0;
    }

    public async Task<int> CountAsync(string? whereClause = null, object? parameters = null)
    {
        var sql = "SELECT COUNT(*) FROM task_variables";
        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }
        
        return await _context.QuerySingleOrDefaultAsync<int>(sql, parameters);
    }

    public async Task<bool> VariableExistsAsync(string taskId, string name)
    {
        var sql = "SELECT COUNT(*) FROM task_variables WHERE task_id = $taskId AND name = $name";
        var count = await _context.QuerySingleOrDefaultAsync<int>(sql, new { taskId, name });
        return count > 0;
    }
}