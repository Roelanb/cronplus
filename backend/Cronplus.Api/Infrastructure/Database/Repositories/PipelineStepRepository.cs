using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Interfaces;
using System.Text.Json;

namespace Cronplus.Api.Infrastructure.Database.Repositories;

public class PipelineStepRepository : IPipelineStepRepository
{
    private readonly IDatabaseContext _context;
    private readonly ILogger<PipelineStepRepository> _logger;

    public PipelineStepRepository(IDatabaseContext context, ILogger<PipelineStepRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PipelineStep?> GetByIdAsync(object id)
    {
        var sql = @"
            SELECT id, task_id, step_order, type, configuration::VARCHAR as configuration, 
                   retry_max, retry_backoff_ms, created_at
            FROM pipeline_steps 
            WHERE id = $id";
        
        var step = await _context.QuerySingleOrDefaultAsync<PipelineStep>(sql, new { id });
        if (step != null && step.Configuration != null)
        {
            // Parse JSON configuration
            var configJson = step.Configuration.RootElement.GetRawText();
            step.Configuration = JsonDocument.Parse(configJson);
        }
        return step;
    }

    public async Task<IEnumerable<PipelineStep>> GetAllAsync()
    {
        var sql = @"
            SELECT id, task_id, step_order, type, configuration::VARCHAR as configuration, 
                   retry_max, retry_backoff_ms, created_at
            FROM pipeline_steps 
            ORDER BY task_id, step_order";
        
        var steps = await _context.QueryAsync<PipelineStep>(sql);
        return ParseConfigurations(steps);
    }

    public async Task<IEnumerable<PipelineStep>> GetByTaskIdAsync(string taskId)
    {
        var sql = @"
            SELECT id, task_id, step_order, type, configuration::VARCHAR as configuration, 
                   retry_max, retry_backoff_ms, created_at
            FROM pipeline_steps 
            WHERE task_id = $taskId
            ORDER BY step_order";
        
        var steps = await _context.QueryAsync<PipelineStep>(sql, new { taskId });
        return ParseConfigurations(steps);
    }

    public async Task<IEnumerable<PipelineStep>> FindAsync(string whereClause, object? parameters = null)
    {
        var sql = $@"
            SELECT id, task_id, step_order, type, configuration::VARCHAR as configuration, 
                   retry_max, retry_backoff_ms, created_at
            FROM pipeline_steps 
            WHERE {whereClause}
            ORDER BY task_id, step_order";
        
        var steps = await _context.QueryAsync<PipelineStep>(sql, parameters);
        return ParseConfigurations(steps);
    }

    public async Task<int> AddAsync(PipelineStep entity)
    {
        entity.CreatedAt = DateTime.UtcNow;

        var sql = @"
            INSERT INTO pipeline_steps (task_id, step_order, type, configuration, 
                                      retry_max, retry_backoff_ms, created_at)
            VALUES ($taskId, $stepOrder, $type, $configuration::JSON, 
                    $retryMax, $retryBackoffMs, $createdAt)
            RETURNING id";
        
        var configJson = entity.Configuration?.RootElement.GetRawText() ?? "{}";
        
        var id = await _context.QuerySingleOrDefaultAsync<int>(sql, new
        {
            taskId = entity.TaskId,
            stepOrder = entity.StepOrder,
            type = entity.Type,
            configuration = configJson,
            retryMax = entity.RetryMax,
            retryBackoffMs = entity.RetryBackoffMs,
            createdAt = entity.CreatedAt
        });

        entity.Id = id;
        return 1;
    }

    public async Task<bool> UpdateAsync(PipelineStep entity)
    {
        var sql = @"
            UPDATE pipeline_steps 
            SET task_id = $taskId,
                step_order = $stepOrder,
                type = $type,
                configuration = $configuration::JSON,
                retry_max = $retryMax,
                retry_backoff_ms = $retryBackoffMs
            WHERE id = $id";
        
        var configJson = entity.Configuration?.RootElement.GetRawText() ?? "{}";
        
        var result = await _context.ExecuteAsync(sql, new
        {
            id = entity.Id,
            taskId = entity.TaskId,
            stepOrder = entity.StepOrder,
            type = entity.Type,
            configuration = configJson,
            retryMax = entity.RetryMax,
            retryBackoffMs = entity.RetryBackoffMs
        });

        return result > 0;
    }

    public async Task<bool> DeleteAsync(object id)
    {
        // First delete related condition
        await _context.ExecuteAsync("DELETE FROM step_conditions WHERE step_id = $id", new { id });
        
        // Then delete the step
        var sql = "DELETE FROM pipeline_steps WHERE id = $id";
        var result = await _context.ExecuteAsync(sql, new { id });
        
        return result > 0;
    }

    public async Task<int> CountAsync(string? whereClause = null, object? parameters = null)
    {
        var sql = "SELECT COUNT(*) FROM pipeline_steps";
        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }
        
        return await _context.QuerySingleOrDefaultAsync<int>(sql, parameters);
    }

    public async Task<int> GetMaxStepOrderAsync(string taskId)
    {
        var sql = "SELECT COALESCE(MAX(step_order), 0) FROM pipeline_steps WHERE task_id = $taskId";
        return await _context.QuerySingleOrDefaultAsync<int>(sql, new { taskId });
    }

    public async Task<bool> ReorderStepsAsync(string taskId, List<(int stepId, int newOrder)> reorderList)
    {
        _context.BeginTransaction();
        try
        {
            foreach (var (stepId, newOrder) in reorderList)
            {
                var sql = @"
                    UPDATE pipeline_steps 
                    SET step_order = $newOrder 
                    WHERE id = $stepId AND task_id = $taskId";
                
                await _context.ExecuteAsync(sql, new { stepId, newOrder, taskId });
            }
            
            _context.Commit();
            return true;
        }
        catch
        {
            _context.Rollback();
            throw;
        }
    }

    private IEnumerable<PipelineStep> ParseConfigurations(IEnumerable<PipelineStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Configuration != null)
            {
                var configJson = step.Configuration.RootElement.GetRawText();
                step.Configuration = JsonDocument.Parse(configJson);
            }
        }
        return steps;
    }
}