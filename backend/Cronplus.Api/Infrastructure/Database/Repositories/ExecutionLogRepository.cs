using Cronplus.Api.Domain.Entities;
using Cronplus.Api.Domain.Interfaces;
using System.Text.Json;

namespace Cronplus.Api.Infrastructure.Database.Repositories;

public class ExecutionLogRepository : IExecutionLogRepository
{
    private readonly IDatabaseContext _context;
    private readonly ILogger<ExecutionLogRepository> _logger;

    public ExecutionLogRepository(IDatabaseContext context, ILogger<ExecutionLogRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ExecutionLog?> GetByIdAsync(object id)
    {
        var sql = @"
            SELECT id, task_id, file_path, status, started_at, completed_at, 
                   error_message, execution_details::VARCHAR as execution_details
            FROM execution_logs 
            WHERE id = $id";
        
        var log = await _context.QuerySingleOrDefaultAsync<ExecutionLog>(sql, new { id });
        if (log != null && log.ExecutionDetails != null)
        {
            var detailsJson = log.ExecutionDetails.RootElement.GetRawText();
            log.ExecutionDetails = JsonDocument.Parse(detailsJson);
        }
        return log;
    }

    public async Task<IEnumerable<ExecutionLog>> GetAllAsync()
    {
        var sql = @"
            SELECT id, task_id, file_path, status, started_at, completed_at, 
                   error_message, execution_details::VARCHAR as execution_details
            FROM execution_logs 
            ORDER BY started_at DESC
            LIMIT 1000";
        
        var logs = await _context.QueryAsync<ExecutionLog>(sql);
        return ParseExecutionDetails(logs);
    }

    public async Task<IEnumerable<ExecutionLog>> GetByTaskIdAsync(string taskId, int limit = 100)
    {
        var sql = @"
            SELECT id, task_id, file_path, status, started_at, completed_at, 
                   error_message, execution_details::VARCHAR as execution_details
            FROM execution_logs 
            WHERE task_id = $taskId
            ORDER BY started_at DESC
            LIMIT $limit";
        
        var logs = await _context.QueryAsync<ExecutionLog>(sql, new { taskId, limit });
        return ParseExecutionDetails(logs);
    }

    public async Task<IEnumerable<ExecutionLog>> GetRecentLogsAsync(int limit = 100)
    {
        var sql = @"
            SELECT id, task_id, file_path, status, started_at, completed_at, 
                   error_message, execution_details::VARCHAR as execution_details
            FROM execution_logs 
            ORDER BY started_at DESC
            LIMIT $limit";
        
        var logs = await _context.QueryAsync<ExecutionLog>(sql, new { limit });
        return ParseExecutionDetails(logs);
    }

    public async Task<ExecutionLog?> GetLatestByTaskIdAsync(string taskId)
    {
        var sql = @"
            SELECT id, task_id, file_path, status, started_at, completed_at, 
                   error_message, execution_details::VARCHAR as execution_details
            FROM execution_logs 
            WHERE task_id = $taskId
            ORDER BY started_at DESC
            LIMIT 1";
        
        var log = await _context.QuerySingleOrDefaultAsync<ExecutionLog>(sql, new { taskId });
        if (log != null && log.ExecutionDetails != null)
        {
            var detailsJson = log.ExecutionDetails.RootElement.GetRawText();
            log.ExecutionDetails = JsonDocument.Parse(detailsJson);
        }
        return log;
    }

    public async Task<IEnumerable<ExecutionLog>> FindAsync(string whereClause, object? parameters = null)
    {
        var sql = $@"
            SELECT id, task_id, file_path, status, started_at, completed_at, 
                   error_message, execution_details::VARCHAR as execution_details
            FROM execution_logs 
            WHERE {whereClause}
            ORDER BY started_at DESC";
        
        var logs = await _context.QueryAsync<ExecutionLog>(sql, parameters);
        return ParseExecutionDetails(logs);
    }

    public async Task<int> AddAsync(ExecutionLog entity)
    {
        entity.StartedAt = DateTime.UtcNow;

        var sql = @"
            INSERT INTO execution_logs (task_id, file_path, status, started_at, 
                                      completed_at, error_message, execution_details)
            VALUES ($taskId, $filePath, $status, $startedAt, 
                    $completedAt, $errorMessage, $executionDetails::JSON)
            RETURNING id";
        
        var detailsJson = entity.ExecutionDetails?.RootElement.GetRawText() ?? "{}";
        
        var id = await _context.QuerySingleOrDefaultAsync<long>(sql, new
        {
            taskId = entity.TaskId,
            filePath = entity.FilePath,
            status = entity.Status,
            startedAt = entity.StartedAt,
            completedAt = entity.CompletedAt,
            errorMessage = entity.ErrorMessage,
            executionDetails = detailsJson
        });

        entity.Id = id;
        return 1;
    }

    public async Task<bool> UpdateAsync(ExecutionLog entity)
    {
        var sql = @"
            UPDATE execution_logs 
            SET status = $status,
                completed_at = $completedAt,
                error_message = $errorMessage,
                execution_details = $executionDetails::JSON
            WHERE id = $id";
        
        var detailsJson = entity.ExecutionDetails?.RootElement.GetRawText() ?? "{}";
        
        var result = await _context.ExecuteAsync(sql, new
        {
            id = entity.Id,
            status = entity.Status,
            completedAt = entity.CompletedAt,
            errorMessage = entity.ErrorMessage,
            executionDetails = detailsJson
        });

        return result > 0;
    }

    public async Task<bool> DeleteAsync(object id)
    {
        var sql = "DELETE FROM execution_logs WHERE id = $id";
        var result = await _context.ExecuteAsync(sql, new { id });
        
        return result > 0;
    }

    public async Task<int> CountAsync(string? whereClause = null, object? parameters = null)
    {
        var sql = "SELECT COUNT(*) FROM execution_logs";
        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }
        
        return await _context.QuerySingleOrDefaultAsync<int>(sql, parameters);
    }

    public async Task<int> CleanupOldLogsAsync(int daysToKeep)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
        var sql = "DELETE FROM execution_logs WHERE started_at < $cutoffDate";
        
        return await _context.ExecuteAsync(sql, new { cutoffDate });
    }

    private IEnumerable<ExecutionLog> ParseExecutionDetails(IEnumerable<ExecutionLog> logs)
    {
        foreach (var log in logs)
        {
            if (log.ExecutionDetails != null)
            {
                var detailsJson = log.ExecutionDetails.RootElement.GetRawText();
                log.ExecutionDetails = JsonDocument.Parse(detailsJson);
            }
        }
        return logs;
    }
}