using DuckDB.NET.Data;
using Microsoft.Extensions.Options;
using System.Data;

namespace Cronplus.Api.Infrastructure.Database;

public interface IDatabaseInitializer
{
    Task InitializeAsync();
    Task SeedDataAsync();
    Task MigrateAsync();
}

public class DatabaseInitializer : IDatabaseInitializer
{
    private readonly DatabaseSettings _settings;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly IServiceProvider _serviceProvider;

    public DatabaseInitializer(
        IOptions<DatabaseSettings> settings, 
        ILogger<DatabaseInitializer> logger,
        IServiceProvider serviceProvider)
    {
        _settings = settings.Value;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing database...");
        
        try
        {
            await CreateDatabaseIfNotExistsAsync();
            await MigrateAsync();
            
            // Only seed in development
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (environment == "Development")
            {
                await SeedDataAsync();
            }
            
            _logger.LogInformation("Database initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database initialization failed");
            throw;
        }
    }

    private async Task CreateDatabaseIfNotExistsAsync()
    {
        if (!_settings.CreateIfNotExists) return;
        
        // DuckDB creates the database file automatically on connection
        // We just need to ensure the directory exists
        var dbPath = _settings.ConnectionString.Replace("DataSource=", "");
        var directory = Path.GetDirectoryName(dbPath);
        
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogInformation("Created database directory: {Directory}", directory);
        }
        
        await Task.CompletedTask;
    }

    public async Task MigrateAsync()
    {
        _logger.LogInformation("Running database migrations...");
        
        using var connection = new DuckDBConnection(_settings.ConnectionString);
        await connection.OpenAsync();
        
        // Create migrations table if not exists
        await ExecuteNonQueryAsync(connection, @"
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                description VARCHAR(255),
                applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            )");
        
        // Get current version
        var currentVersion = await GetCurrentVersionAsync(connection);
        _logger.LogInformation("Current database version: {Version}", currentVersion);
        
        // Apply migrations
        var migrations = GetMigrations();
        foreach (var migration in migrations.Where(m => m.Version > currentVersion))
        {
            _logger.LogInformation("Applying migration {Version}: {Description}", 
                migration.Version, migration.Description);
            
            using var transaction = connection.BeginTransaction();
            try
            {
                // Execute migration SQL (no parameters needed for schema creation)
                using var migrationCmd = connection.CreateCommand();
                migrationCmd.CommandText = migration.Sql;
                migrationCmd.Transaction = transaction;
                await migrationCmd.ExecuteNonQueryAsync();
                
                // Record migration  
                using var recordCmd = connection.CreateCommand();
                recordCmd.CommandText = @"INSERT INTO schema_migrations (version, description) 
                                         VALUES (?, ?)";
                recordCmd.Transaction = transaction;
                
                var versionParam = recordCmd.CreateParameter();
                versionParam.Value = migration.Version;
                recordCmd.Parameters.Add(versionParam);
                
                var descParam = recordCmd.CreateParameter();
                descParam.Value = migration.Description;
                recordCmd.Parameters.Add(descParam);
                
                await recordCmd.ExecuteNonQueryAsync();
                
                transaction.Commit();
                _logger.LogInformation("Migration {Version} applied successfully", migration.Version);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Migration {Version} failed", migration.Version);
                throw;
            }
        }
        
        _logger.LogInformation("Database migrations completed");
    }

    private async Task<int> GetCurrentVersionAsync(DuckDBConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private List<Migration> GetMigrations()
    {
        return new List<Migration>
        {
            new Migration
            {
                Version = 1,
                Description = "Initial schema",
                Sql = @"
                    -- Tasks table
                    CREATE TABLE IF NOT EXISTS tasks (
                        id VARCHAR PRIMARY KEY,
                        enabled BOOLEAN NOT NULL DEFAULT false,
                        watch_directory VARCHAR NOT NULL,
                        glob_pattern VARCHAR NOT NULL DEFAULT '*',
                        debounce_ms INTEGER DEFAULT 500,
                        stabilization_ms INTEGER DEFAULT 1000,
                        description VARCHAR,
                        created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );

                    -- Create sequence for pipeline_steps first
                    CREATE SEQUENCE IF NOT EXISTS pipeline_steps_id_seq START 1;
                    
                    -- Pipeline steps table with auto-increment
                    CREATE TABLE IF NOT EXISTS pipeline_steps (
                        id INTEGER PRIMARY KEY DEFAULT nextval('pipeline_steps_id_seq'),
                        task_id VARCHAR NOT NULL REFERENCES tasks(id),
                        step_order INTEGER NOT NULL,
                        type VARCHAR NOT NULL,
                        configuration JSON,
                        retry_max INTEGER,
                        retry_backoff_ms INTEGER,
                        created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );

                    -- Create sequence for step_conditions
                    CREATE SEQUENCE IF NOT EXISTS step_conditions_id_seq START 1;
                    
                    -- Step conditions table
                    CREATE TABLE IF NOT EXISTS step_conditions (
                        id INTEGER PRIMARY KEY DEFAULT nextval('step_conditions_id_seq'),
                        step_id INTEGER NOT NULL REFERENCES pipeline_steps(id),
                        expression VARCHAR NOT NULL,
                        true_action VARCHAR DEFAULT 'continue',
                        false_action VARCHAR DEFAULT 'continue'
                    );

                    -- Create sequence for task_variables
                    CREATE SEQUENCE IF NOT EXISTS task_variables_id_seq START 1;
                    
                    -- Task variables table
                    CREATE TABLE IF NOT EXISTS task_variables (
                        id INTEGER PRIMARY KEY DEFAULT nextval('task_variables_id_seq'),
                        task_id VARCHAR NOT NULL REFERENCES tasks(id),
                        name VARCHAR NOT NULL,
                        type VARCHAR NOT NULL DEFAULT 'string',
                        value VARCHAR NOT NULL,
                        created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        UNIQUE(task_id, name)
                    );

                    -- Create sequence for execution_logs
                    CREATE SEQUENCE IF NOT EXISTS execution_logs_id_seq START 1;
                    
                    -- Execution logs table
                    CREATE TABLE IF NOT EXISTS execution_logs (
                        id BIGINT PRIMARY KEY DEFAULT nextval('execution_logs_id_seq'),
                        task_id VARCHAR NOT NULL REFERENCES tasks(id),
                        file_path VARCHAR NOT NULL,
                        status VARCHAR NOT NULL,
                        started_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        completed_at TIMESTAMP,
                        error_message VARCHAR,
                        execution_details JSON
                    );

                    -- Indexes
                    CREATE INDEX IF NOT EXISTS idx_pipeline_steps_task_id ON pipeline_steps(task_id);
                    CREATE INDEX IF NOT EXISTS idx_task_variables_task_id ON task_variables(task_id);
                    CREATE INDEX IF NOT EXISTS idx_execution_logs_task_id ON execution_logs(task_id);
                    CREATE INDEX IF NOT EXISTS idx_execution_logs_started_at ON execution_logs(started_at);
                "
            },
            new Migration
            {
                Version = 2,
                Description = "Placeholder migration - sequences already in Migration 1",
                Sql = @"
                    -- Sequences are now created as part of Migration 1
                    -- This migration is kept for version tracking
                    SELECT 1;
                "
            }
        };
    }

    public async Task SeedDataAsync()
    {
        _logger.LogInformation("Seeding development data...");
        
        using var scope = _serviceProvider.CreateScope();
        using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        
        // Check if data already exists
        var existingTasks = await unitOfWork.Tasks.GetAllAsync();
        if (existingTasks.Any())
        {
            _logger.LogInformation("Database already contains data, skipping seed");
            return;
        }
        
        // Create sample tasks
        var sampleTask1 = new Domain.Entities.TaskEntity
        {
            Id = "print-and-archive-demo",
            Enabled = true,
            WatchDirectory = "/tmp/cronplus/demo/incoming",
            GlobPattern = "*.pdf",
            DebounceMs = 500,
            StabilizationMs = 1000,
            Description = "Demo task for printing and archiving PDF files"
        };
        
        await unitOfWork.Tasks.AddAsync(sampleTask1);
        
        // Add pipeline steps
        await unitOfWork.PipelineSteps.AddAsync(new Domain.Entities.PipelineStep
        {
            TaskId = sampleTask1.Id,
            StepOrder = 1,
            Type = "archive",
            Configuration = System.Text.Json.JsonDocument.Parse(@"{
                ""destination"": ""/tmp/cronplus/demo/archive"",
                ""preserveSubdirs"": false,
                ""conflictStrategy"": ""rename""
            }")
        });
        
        // Add variables
        await unitOfWork.TaskVariables.AddAsync(new Domain.Entities.TaskVariable
        {
            TaskId = sampleTask1.Id,
            Name = "ARCHIVE_PATH",
            Type = "string",
            Value = "/tmp/cronplus/demo/archive"
        });
        
        var sampleTask2 = new Domain.Entities.TaskEntity
        {
            Id = "api-integration-demo",
            Enabled = false,
            WatchDirectory = "/tmp/cronplus/demo/api",
            GlobPattern = "*.json",
            DebounceMs = 1000,
            StabilizationMs = 2000,
            Description = "Demo task for REST API integration"
        };
        
        await unitOfWork.Tasks.AddAsync(sampleTask2);
        
        _logger.LogInformation("Development data seeded successfully");
    }

    private async Task ExecuteNonQueryAsync(DuckDBConnection connection, string sql, 
        IDbTransaction? transaction = null, params object[] parameters)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction as DuckDBTransaction;
        
        for (int i = 0; i < parameters.Length; i++)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = $"${i + 1}";
            param.Value = parameters[i] ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }
        
        await cmd.ExecuteNonQueryAsync();
    }

    private class Migration
    {
        public int Version { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
    }
}