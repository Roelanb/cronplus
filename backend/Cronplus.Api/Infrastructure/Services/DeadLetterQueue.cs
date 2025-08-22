using System.Collections.Concurrent;
using System.Text.Json;
using Cronplus.Api.Infrastructure.Database;
using Dapper;

namespace Cronplus.Api.Infrastructure.Services;

/// <summary>
/// Dead letter queue for storing failed pipeline executions
/// </summary>
public interface IDeadLetterQueue
{
    Task EnqueueAsync(DeadLetterItem item, CancellationToken cancellationToken = default);
    Task<IEnumerable<DeadLetterItem>> GetItemsAsync(string? taskId = null, int limit = 100, CancellationToken cancellationToken = default);
    Task<DeadLetterItem?> GetItemAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> RetryItemAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteItemAsync(int id, CancellationToken cancellationToken = default);
    Task<int> DeleteOldItemsAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
    Task<DeadLetterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

public class DeadLetterQueue : IDeadLetterQueue, IDisposable
{
    private readonly ILogger<DeadLetterQueue> _logger;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IPipelineExecutor? _pipelineExecutor;
    private readonly ConcurrentQueue<DeadLetterItem> _memoryQueue = new();
    private readonly SemaphoreSlim _persistenceLock = new(1, 1);
    private Timer? _persistenceTimer;
    private bool _disposed;

    public DeadLetterQueue(
        ILogger<DeadLetterQueue> logger,
        IDbConnectionFactory connectionFactory,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _connectionFactory = connectionFactory;
        
        // Avoid circular dependency by lazy loading
        try
        {
            _pipelineExecutor = serviceProvider.GetService<IPipelineExecutor>();
        }
        catch
        {
            // Pipeline executor might not be available during startup
        }

        InitializeDatabase().GetAwaiter().GetResult();
        StartPersistenceTimer();
    }

    private async Task InitializeDatabase()
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();
        
        var createTableSql = @"
            CREATE TABLE IF NOT EXISTS dead_letter_queue (
                id INTEGER PRIMARY KEY,
                task_id TEXT NOT NULL,
                file_path TEXT NOT NULL,
                step_name TEXT,
                error TEXT NOT NULL,
                execution_result TEXT,
                retry_count INTEGER DEFAULT 0,
                max_retries INTEGER DEFAULT 3,
                next_retry_at TIMESTAMP,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                status TEXT DEFAULT 'failed'
            );
            
            CREATE INDEX IF NOT EXISTS idx_dlq_task_id ON dead_letter_queue(task_id);
            CREATE INDEX IF NOT EXISTS idx_dlq_status ON dead_letter_queue(status);
            CREATE INDEX IF NOT EXISTS idx_dlq_created_at ON dead_letter_queue(created_at);
            CREATE INDEX IF NOT EXISTS idx_dlq_next_retry ON dead_letter_queue(next_retry_at);
        ";
        
        await connection.ExecuteAsync(createTableSql);
    }

    private void StartPersistenceTimer()
    {
        _persistenceTimer = new Timer(
            async _ => await PersistQueuedItems(),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
    }

    public async Task EnqueueAsync(DeadLetterItem item, CancellationToken cancellationToken = default)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        item.CreatedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        item.Status = DeadLetterStatus.Failed;

        // Calculate next retry time if applicable
        if (item.RetryCount < item.MaxRetries)
        {
            var backoffSeconds = Math.Pow(2, item.RetryCount) * 10; // Exponential backoff
            item.NextRetryAt = DateTime.UtcNow.AddSeconds(backoffSeconds);
        }

        _logger.LogInformation(
            "Enqueueing failed item to dead letter queue: TaskId={TaskId}, FilePath={FilePath}, Error={Error}",
            item.TaskId, item.FilePath, item.Error);

        // Add to memory queue for batch persistence
        _memoryQueue.Enqueue(item);

        // If queue is getting large, persist immediately
        if (_memoryQueue.Count > 10)
        {
            await PersistQueuedItems();
        }
    }

    private async Task PersistQueuedItems()
    {
        if (_memoryQueue.IsEmpty)
            return;

        await _persistenceLock.WaitAsync();
        try
        {
            var items = new List<DeadLetterItem>();
            while (_memoryQueue.TryDequeue(out var item))
            {
                items.Add(item);
            }

            if (items.Any())
            {
                using var connection = await _connectionFactory.CreateConnectionAsync();
                using var transaction = connection.BeginTransaction();

                var insertSql = @"
                    INSERT INTO dead_letter_queue 
                    (task_id, file_path, step_name, error, execution_result, retry_count, max_retries, next_retry_at, created_at, updated_at, status)
                    VALUES 
                    (@TaskId, @FilePath, @StepName, @Error, @ExecutionResultJson, @RetryCount, @MaxRetries, @NextRetryAt, @CreatedAt, @UpdatedAt, @Status)";

                foreach (var item in items)
                {
                    await connection.ExecuteAsync(insertSql, new
                    {
                        item.TaskId,
                        item.FilePath,
                        item.StepName,
                        item.Error,
                        ExecutionResultJson = item.ExecutionResult != null 
                            ? JsonSerializer.Serialize(item.ExecutionResult) 
                            : null,
                        item.RetryCount,
                        item.MaxRetries,
                        item.NextRetryAt,
                        item.CreatedAt,
                        item.UpdatedAt,
                        Status = item.Status.ToString()
                    }, transaction);
                }

                transaction.Commit();
                _logger.LogDebug("Persisted {Count} items to dead letter queue", items.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist dead letter queue items");
            
            // Re-queue items that failed to persist
            foreach (var item in _memoryQueue.ToArray())
            {
                _memoryQueue.Enqueue(item);
            }
        }
        finally
        {
            _persistenceLock.Release();
        }
    }

    public async Task<IEnumerable<DeadLetterItem>> GetItemsAsync(string? taskId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = @"
            SELECT 
                id as Id,
                task_id as TaskId,
                file_path as FilePath,
                step_name as StepName,
                error as Error,
                execution_result as ExecutionResultJson,
                retry_count as RetryCount,
                max_retries as MaxRetries,
                next_retry_at as NextRetryAt,
                created_at as CreatedAt,
                updated_at as UpdatedAt,
                status as StatusString
            FROM dead_letter_queue
            WHERE 1=1";

        if (!string.IsNullOrEmpty(taskId))
        {
            sql += " AND task_id = @TaskId";
        }

        sql += " ORDER BY created_at DESC LIMIT @Limit";

        var results = await connection.QueryAsync<DeadLetterItemDto>(sql, new { TaskId = taskId, Limit = limit });
        
        return results.Select(dto => dto.ToDeadLetterItem());
    }

    public async Task<DeadLetterItem?> GetItemAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = @"
            SELECT 
                id as Id,
                task_id as TaskId,
                file_path as FilePath,
                step_name as StepName,
                error as Error,
                execution_result as ExecutionResultJson,
                retry_count as RetryCount,
                max_retries as MaxRetries,
                next_retry_at as NextRetryAt,
                created_at as CreatedAt,
                updated_at as UpdatedAt,
                status as StatusString
            FROM dead_letter_queue
            WHERE id = @Id";

        var dto = await connection.QuerySingleOrDefaultAsync<DeadLetterItemDto>(sql, new { Id = id });
        
        return dto?.ToDeadLetterItem();
    }

    public async Task<bool> RetryItemAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await GetItemAsync(id, cancellationToken);
        if (item == null)
        {
            _logger.LogWarning("Dead letter item {Id} not found for retry", id);
            return false;
        }

        if (_pipelineExecutor == null)
        {
            _logger.LogError("Pipeline executor not available for retry");
            return false;
        }

        _logger.LogInformation("Retrying dead letter item {Id} for task {TaskId}", id, item.TaskId);

        try
        {
            // Execute the pipeline
            var result = await _pipelineExecutor.ExecuteAsync(item.TaskId, item.FilePath, cancellationToken);
            
            if (result.Success)
            {
                // Mark as resolved
                await UpdateItemStatusAsync(id, DeadLetterStatus.Resolved, cancellationToken);
                _logger.LogInformation("Successfully retried dead letter item {Id}", id);
                return true;
            }
            else
            {
                // Update retry count and status
                item.RetryCount++;
                item.UpdatedAt = DateTime.UtcNow;
                
                if (item.RetryCount >= item.MaxRetries)
                {
                    item.Status = DeadLetterStatus.MaxRetriesExceeded;
                    await UpdateItemAsync(item, cancellationToken);
                    _logger.LogWarning("Dead letter item {Id} exceeded max retries", id);
                }
                else
                {
                    // Calculate next retry time
                    var backoffSeconds = Math.Pow(2, item.RetryCount) * 10;
                    item.NextRetryAt = DateTime.UtcNow.AddSeconds(backoffSeconds);
                    item.Status = DeadLetterStatus.Failed;
                    await UpdateItemAsync(item, cancellationToken);
                    _logger.LogInformation("Dead letter item {Id} will be retried at {NextRetryAt}", id, item.NextRetryAt);
                }
                
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry dead letter item {Id}", id);
            
            // Update retry count
            item.RetryCount++;
            item.UpdatedAt = DateTime.UtcNow;
            item.Error = ex.ToString();
            
            if (item.RetryCount >= item.MaxRetries)
            {
                item.Status = DeadLetterStatus.MaxRetriesExceeded;
            }
            
            await UpdateItemAsync(item, cancellationToken);
            return false;
        }
    }

    private async Task UpdateItemAsync(DeadLetterItem item, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = @"
            UPDATE dead_letter_queue
            SET 
                retry_count = @RetryCount,
                next_retry_at = @NextRetryAt,
                updated_at = @UpdatedAt,
                status = @Status,
                error = @Error
            WHERE id = @Id";

        await connection.ExecuteAsync(sql, new
        {
            item.Id,
            item.RetryCount,
            item.NextRetryAt,
            item.UpdatedAt,
            Status = item.Status.ToString(),
            item.Error
        });
    }

    private async Task UpdateItemStatusAsync(int id, DeadLetterStatus status, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = @"
            UPDATE dead_letter_queue
            SET 
                status = @Status,
                updated_at = @UpdatedAt
            WHERE id = @Id";

        await connection.ExecuteAsync(sql, new
        {
            Id = id,
            Status = status.ToString(),
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<bool> DeleteItemAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = "DELETE FROM dead_letter_queue WHERE id = @Id";
        var affected = await connection.ExecuteAsync(sql, new { Id = id });
        
        return affected > 0;
    }

    public async Task<int> DeleteOldItemsAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var cutoffDate = DateTime.UtcNow - olderThan;
        var sql = "DELETE FROM dead_letter_queue WHERE created_at < @CutoffDate";
        
        var affected = await connection.ExecuteAsync(sql, new { CutoffDate = cutoffDate });
        
        _logger.LogInformation("Deleted {Count} old items from dead letter queue", affected);
        return affected;
    }

    public async Task<DeadLetterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = @"
            SELECT 
                COUNT(*) as TotalItems,
                COUNT(CASE WHEN status = 'Failed' THEN 1 END) as FailedItems,
                COUNT(CASE WHEN status = 'Resolved' THEN 1 END) as ResolvedItems,
                COUNT(CASE WHEN status = 'MaxRetriesExceeded' THEN 1 END) as MaxRetriesExceededItems,
                COUNT(DISTINCT task_id) as UniqueTasksAffected,
                MIN(created_at) as OldestItemDate,
                MAX(created_at) as NewestItemDate
            FROM dead_letter_queue";

        var stats = await connection.QuerySingleAsync<DeadLetterStatistics>(sql);
        
        // Get items pending retry
        var pendingRetrySql = @"
            SELECT COUNT(*) 
            FROM dead_letter_queue 
            WHERE status = 'Failed' 
            AND retry_count < max_retries 
            AND next_retry_at <= @Now";
        
        stats.ItemsPendingRetry = await connection.QuerySingleAsync<int>(pendingRetrySql, new { Now = DateTime.UtcNow });
        
        return stats;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _persistenceTimer?.Dispose();
            
            // Persist any remaining items
            try
            {
                PersistQueuedItems().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error persisting queued items during disposal");
            }
            
            _persistenceLock?.Dispose();
        }

        _disposed = true;
    }
}

/// <summary>
/// Dead letter queue item
/// </summary>
public class DeadLetterItem
{
    public int Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? StepName { get; set; }
    public string Error { get; set; } = string.Empty;
    public PipelineExecutionResult? ExecutionResult { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public DateTime? NextRetryAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime Timestamp { get; set; }
    public DeadLetterStatus Status { get; set; }
}

/// <summary>
/// Dead letter item status
/// </summary>
public enum DeadLetterStatus
{
    Failed,
    Resolved,
    MaxRetriesExceeded,
    Ignored
}

/// <summary>
/// Dead letter queue statistics
/// </summary>
public class DeadLetterStatistics
{
    public int TotalItems { get; set; }
    public int FailedItems { get; set; }
    public int ResolvedItems { get; set; }
    public int MaxRetriesExceededItems { get; set; }
    public int ItemsPendingRetry { get; set; }
    public int UniqueTasksAffected { get; set; }
    public DateTime? OldestItemDate { get; set; }
    public DateTime? NewestItemDate { get; set; }
}

/// <summary>
/// DTO for database mapping
/// </summary>
internal class DeadLetterItemDto
{
    public int Id { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? StepName { get; set; }
    public string Error { get; set; } = string.Empty;
    public string? ExecutionResultJson { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string StatusString { get; set; } = string.Empty;

    public DeadLetterItem ToDeadLetterItem()
    {
        return new DeadLetterItem
        {
            Id = Id,
            TaskId = TaskId,
            FilePath = FilePath,
            StepName = StepName,
            Error = Error,
            ExecutionResult = !string.IsNullOrEmpty(ExecutionResultJson)
                ? JsonSerializer.Deserialize<PipelineExecutionResult>(ExecutionResultJson)
                : null,
            RetryCount = RetryCount,
            MaxRetries = MaxRetries,
            NextRetryAt = NextRetryAt,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Status = Enum.TryParse<DeadLetterStatus>(StatusString, out var status) 
                ? status 
                : DeadLetterStatus.Failed
        };
    }
}