using System.Text.Json;
using Cronplus.Api.Domain.Models;
using Dapper;

namespace Cronplus.Api.Infrastructure.Database.Repositories;

public interface IVariableRepository
{
    Task<IEnumerable<VariableModel>> GetByTaskIdAsync(string taskId, CancellationToken cancellationToken = default);
    Task<VariableModel?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<VariableModel?> GetByNameAsync(string taskId, string name, CancellationToken cancellationToken = default);
    Task<int> CreateAsync(VariableModel variable, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(VariableModel variable, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteByTaskIdAsync(string taskId, CancellationToken cancellationToken = default);
}

public class VariableRepository : IVariableRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<VariableRepository> _logger;

    public VariableRepository(IDbConnectionFactory connectionFactory, ILogger<VariableRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        InitializeTable().GetAwaiter().GetResult();
    }

    private async Task InitializeTable()
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();
        
        var createTableSql = @"
            CREATE TABLE IF NOT EXISTS variables (
                id INTEGER PRIMARY KEY,
                task_id TEXT NOT NULL,
                name TEXT NOT NULL,
                type TEXT NOT NULL,
                value TEXT NOT NULL,
                description TEXT,
                is_constant BOOLEAN DEFAULT FALSE,
                is_required BOOLEAN DEFAULT FALSE,
                default_value TEXT,
                scope TEXT DEFAULT 'Task',
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(task_id, name)
            );
            
            CREATE INDEX IF NOT EXISTS idx_variables_task_id ON variables(task_id);
            CREATE INDEX IF NOT EXISTS idx_variables_name ON variables(name);
        ";
        
        await connection.ExecuteAsync(createTableSql);
    }

    public async Task<IEnumerable<VariableModel>> GetByTaskIdAsync(string taskId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = @"
            SELECT 
                id as Id,
                task_id as TaskId,
                name as Name,
                type as TypeString,
                value as ValueJson,
                description as Description,
                is_constant as IsConstant,
                is_required as IsRequired,
                default_value as DefaultValueJson,
                scope as ScopeString,
                created_at as CreatedAt,
                updated_at as UpdatedAt
            FROM variables
            WHERE task_id = @TaskId
            ORDER BY name";

        var dtos = await connection.QueryAsync<VariableDto>(sql, new { TaskId = taskId });
        return dtos.Select(dto => dto.ToVariable());
    }

    public async Task<VariableModel?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = @"
            SELECT 
                id as Id,
                task_id as TaskId,
                name as Name,
                type as TypeString,
                value as ValueJson,
                description as Description,
                is_constant as IsConstant,
                is_required as IsRequired,
                default_value as DefaultValueJson,
                scope as ScopeString,
                created_at as CreatedAt,
                updated_at as UpdatedAt
            FROM variables
            WHERE id = @Id";

        var dto = await connection.QuerySingleOrDefaultAsync<VariableDto>(sql, new { Id = id });
        return dto?.ToVariable();
    }

    public async Task<VariableModel?> GetByNameAsync(string taskId, string name, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = @"
            SELECT 
                id as Id,
                task_id as TaskId,
                name as Name,
                type as TypeString,
                value as ValueJson,
                description as Description,
                is_constant as IsConstant,
                is_required as IsRequired,
                default_value as DefaultValueJson,
                scope as ScopeString,
                created_at as CreatedAt,
                updated_at as UpdatedAt
            FROM variables
            WHERE task_id = @TaskId AND name = @Name";

        var dto = await connection.QuerySingleOrDefaultAsync<VariableDto>(sql, new { TaskId = taskId, Name = name });
        return dto?.ToVariable();
    }

    public async Task<int> CreateAsync(VariableModel variable, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = @"
            INSERT INTO variables 
            (task_id, name, type, value, description, is_constant, is_required, default_value, scope, created_at, updated_at)
            VALUES 
            (@TaskId, @Name, @Type, @Value, @Description, @IsConstant, @IsRequired, @DefaultValue, @Scope, @CreatedAt, @UpdatedAt)
            RETURNING id";

        var valueJson = SerializeValue(variable.Value, variable.Type);
        var defaultValueJson = variable.DefaultValue != null ? SerializeValue(variable.DefaultValue, variable.Type) : null;
        
        var id = await connection.QuerySingleAsync<int>(sql, new
        {
            variable.TaskId,
            variable.Name,
            Type = variable.Type.ToString(),
            Value = valueJson,
            variable.Description,
            variable.IsConstant,
            variable.IsRequired,
            DefaultValue = defaultValueJson,
            Scope = variable.Scope.ToString(),
            variable.CreatedAt,
            variable.UpdatedAt
        });

        variable.Id = id;
        _logger.LogInformation("Created variable {Name} for task {TaskId}", variable.Name, variable.TaskId);
        
        return id;
    }

    public async Task<bool> UpdateAsync(VariableModel variable, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = @"
            UPDATE variables
            SET 
                name = @Name,
                type = @Type,
                value = @Value,
                description = @Description,
                is_constant = @IsConstant,
                is_required = @IsRequired,
                default_value = @DefaultValue,
                scope = @Scope,
                updated_at = @UpdatedAt
            WHERE id = @Id";

        var valueJson = SerializeValue(variable.Value, variable.Type);
        var defaultValueJson = variable.DefaultValue != null ? SerializeValue(variable.DefaultValue, variable.Type) : null;
        variable.UpdatedAt = DateTime.UtcNow;
        
        var affected = await connection.ExecuteAsync(sql, new
        {
            variable.Id,
            variable.Name,
            Type = variable.Type.ToString(),
            Value = valueJson,
            variable.Description,
            variable.IsConstant,
            variable.IsRequired,
            DefaultValue = defaultValueJson,
            Scope = variable.Scope.ToString(),
            variable.UpdatedAt
        });

        if (affected > 0)
        {
            _logger.LogInformation("Updated variable {Name} for task {TaskId}", variable.Name, variable.TaskId);
        }
        
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = "DELETE FROM variables WHERE id = @Id";
        var affected = await connection.ExecuteAsync(sql, new { Id = id });
        
        if (affected > 0)
        {
            _logger.LogInformation("Deleted variable with id {Id}", id);
        }
        
        return affected > 0;
    }

    public async Task<bool> DeleteByTaskIdAsync(string taskId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        
        var sql = "DELETE FROM variables WHERE task_id = @TaskId";
        var affected = await connection.ExecuteAsync(sql, new { TaskId = taskId });
        
        if (affected > 0)
        {
            _logger.LogInformation("Deleted {Count} variables for task {TaskId}", affected, taskId);
        }
        
        return affected > 0;
    }

    private string SerializeValue(object value, VariableType type)
    {
        if (value == null)
            return "null";

        return type switch
        {
            VariableType.String => value.ToString() ?? string.Empty,
            VariableType.Integer or VariableType.Decimal or VariableType.Boolean => value.ToString() ?? "0",
            VariableType.DateTime => value is DateTime dt ? dt.ToString("O") : value.ToString() ?? "",
            _ => JsonSerializer.Serialize(value)
        };
    }

    private object DeserializeValue(string json, VariableType type)
    {
        return DeserializeValueStatic(json, type);
    }

    private static object DeserializeValueStatic(string json, VariableType type)
    {
        if (string.IsNullOrEmpty(json) || json == "null")
        {
            return type switch
            {
                VariableType.String => string.Empty,
                VariableType.Integer => 0L,
                VariableType.Decimal => 0m,
                VariableType.Boolean => false,
                VariableType.DateTime => DateTime.UtcNow,
                VariableType.Json => new { },
                VariableType.List => new List<object>(),
                VariableType.Dictionary => new Dictionary<string, object>(),
                _ => string.Empty
            };
        }

        try
        {
            return type switch
            {
                VariableType.String => json,
                VariableType.Integer => long.Parse(json),
                VariableType.Decimal => decimal.Parse(json),
                VariableType.Boolean => bool.Parse(json),
                VariableType.DateTime => DateTime.Parse(json),
                VariableType.Json => JsonSerializer.Deserialize<object>(json) ?? new { },
                VariableType.List => JsonSerializer.Deserialize<List<object>>(json) ?? new List<object>(),
                VariableType.Dictionary => JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>(),
                _ => json
            };
        }
        catch
        {
            // Return json as fallback if deserialization fails
            return json;
        }
    }

    /// <summary>
    /// DTO for database mapping
    /// </summary>
    private class VariableDto
    {
        public int Id { get; set; }
        public string TaskId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeString { get; set; } = string.Empty;
        public string ValueJson { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsConstant { get; set; }
        public bool IsRequired { get; set; }
        public string? DefaultValueJson { get; set; }
        public string ScopeString { get; set; } = "Task";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public VariableModel ToVariable()
        {
            var type = Enum.TryParse<VariableType>(TypeString, out var varType) 
                ? varType 
                : VariableType.String;

            var value = DeserializeValueStatic(ValueJson, type);
            var defaultValue = !string.IsNullOrEmpty(DefaultValueJson) 
                ? DeserializeValueStatic(DefaultValueJson, type) 
                : null;
            var scope = Enum.TryParse<VariableScope>(ScopeString, out var varScope)
                ? varScope
                : VariableScope.Task;

            return new VariableModel
            {
                Id = Id,
                TaskId = TaskId,
                Name = Name,
                Type = type,
                Value = value,
                Description = Description,
                IsConstant = IsConstant,
                IsRequired = IsRequired,
                DefaultValue = defaultValue,
                Scope = scope,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt
            };
        }
    }
}