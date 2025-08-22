using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Interfaces;
using System.Text;
using System.Text.Json;

namespace Cronplus.Api.Infrastructure.Database.Repositories;

public class TaskRepository : ITaskRepository
{
    private readonly IDatabaseContext _context;
    private readonly ILogger<TaskRepository> _logger;

    public TaskRepository(IDatabaseContext context, ILogger<TaskRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<TaskEntity?> GetByIdAsync(object id)
    {
        var sql = @"
            SELECT id, enabled, watch_directory, glob_pattern, debounce_ms, 
                   stabilization_ms, created_at, updated_at, description
            FROM tasks 
            WHERE id = $id";
        
        return await _context.QuerySingleOrDefaultAsync<TaskEntity>(sql, new { id });
    }

    public async Task<TaskEntity?> GetByIdWithDetailsAsync(string id)
    {
        var task = await GetByIdAsync(id);
        if (task == null) return null;

        // Load pipeline steps
        var stepsSql = @"
            SELECT id, task_id, step_order, type, configuration, 
                   retry_max, retry_backoff_ms, created_at
            FROM pipeline_steps 
            WHERE task_id = $taskId 
            ORDER BY step_order";
        
        var steps = await _context.QueryAsync<PipelineStep>(stepsSql, new { taskId = id });
        task.PipelineSteps = steps.ToList();

        // Load variables
        var variablesSql = @"
            SELECT id, task_id, name, type, value, created_at, updated_at
            FROM task_variables 
            WHERE task_id = $taskId";
        
        var variables = await _context.QueryAsync<TaskVariable>(variablesSql, new { taskId = id });
        task.Variables = variables.ToList();

        return task;
    }

    public async Task<IEnumerable<TaskEntity>> GetAllAsync()
    {
        var sql = @"
            SELECT id, enabled, watch_directory, glob_pattern, debounce_ms, 
                   stabilization_ms, created_at, updated_at, description
            FROM tasks 
            ORDER BY created_at DESC";
        
        return await _context.QueryAsync<TaskEntity>(sql);
    }

    public async Task<IEnumerable<TaskEntity>> GetEnabledTasksAsync()
    {
        var sql = @"
            SELECT id, enabled, watch_directory, glob_pattern, debounce_ms, 
                   stabilization_ms, created_at, updated_at, description
            FROM tasks 
            WHERE enabled = true
            ORDER BY created_at DESC";
        
        return await _context.QueryAsync<TaskEntity>(sql);
    }

    public async Task<IEnumerable<TaskEntity>> FindAsync(string whereClause, object? parameters = null)
    {
        var sql = $@"
            SELECT id, enabled, watch_directory, glob_pattern, debounce_ms, 
                   stabilization_ms, created_at, updated_at, description
            FROM tasks 
            WHERE {whereClause}
            ORDER BY created_at DESC";
        
        return await _context.QueryAsync<TaskEntity>(sql, parameters);
    }

    public async Task<int> AddAsync(TaskEntity entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        var sql = @"
            INSERT INTO tasks (id, enabled, watch_directory, glob_pattern, 
                             debounce_ms, stabilization_ms, created_at, updated_at, description)
            VALUES ($id, $enabled, $watchDirectory, $globPattern, 
                    $debounceMs, $stabilizationMs, $createdAt, $updatedAt, $description)";
        
        return await _context.ExecuteAsync(sql, new
        {
            id = entity.Id,
            enabled = entity.Enabled,
            watchDirectory = entity.WatchDirectory,
            globPattern = entity.GlobPattern,
            debounceMs = entity.DebounceMs,
            stabilizationMs = entity.StabilizationMs,
            createdAt = entity.CreatedAt,
            updatedAt = entity.UpdatedAt,
            description = entity.Description
        });
    }

    public async Task<bool> UpdateAsync(TaskEntity entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;

        var sql = @"
            UPDATE tasks 
            SET enabled = $enabled,
                watch_directory = $watchDirectory,
                glob_pattern = $globPattern,
                debounce_ms = $debounceMs,
                stabilization_ms = $stabilizationMs,
                updated_at = $updatedAt,
                description = $description
            WHERE id = $id";
        
        var result = await _context.ExecuteAsync(sql, new
        {
            id = entity.Id,
            enabled = entity.Enabled,
            watchDirectory = entity.WatchDirectory,
            globPattern = entity.GlobPattern,
            debounceMs = entity.DebounceMs,
            stabilizationMs = entity.StabilizationMs,
            updatedAt = entity.UpdatedAt,
            description = entity.Description
        });

        return result > 0;
    }

    public async Task<bool> DeleteAsync(object id)
    {
        // First delete related records
        await _context.ExecuteAsync("DELETE FROM execution_logs WHERE task_id = $id", new { id });
        await _context.ExecuteAsync("DELETE FROM task_variables WHERE task_id = $id", new { id });
        await _context.ExecuteAsync("DELETE FROM step_conditions WHERE step_id IN (SELECT id FROM pipeline_steps WHERE task_id = $id)", new { id });
        await _context.ExecuteAsync("DELETE FROM pipeline_steps WHERE task_id = $id", new { id });
        
        // Then delete the task
        var sql = "DELETE FROM tasks WHERE id = $id";
        var result = await _context.ExecuteAsync(sql, new { id });
        
        return result > 0;
    }

    public async Task<int> CountAsync(string? whereClause = null, object? parameters = null)
    {
        var sql = "SELECT COUNT(*) FROM tasks";
        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }
        
        return await _context.QuerySingleOrDefaultAsync<int>(sql, parameters);
    }

    public async Task<bool> TaskExistsAsync(string id)
    {
        var sql = "SELECT COUNT(*) FROM tasks WHERE id = $id";
        var count = await _context.QuerySingleOrDefaultAsync<int>(sql, new { id });
        return count > 0;
    }
}