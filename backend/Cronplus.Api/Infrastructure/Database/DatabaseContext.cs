using DuckDB.NET.Data;
using Microsoft.Extensions.Options;
using System.Data;

namespace Cronplus.Api.Infrastructure.Database;

public class DatabaseSettings
{
    public string ConnectionString { get; set; } = "DataSource=cronplus.db";
    public bool EnableWalMode { get; set; } = true;
    public bool CreateIfNotExists { get; set; } = true;
}

public interface IDatabaseContext : IDisposable
{
    IDbConnection Connection { get; }
    IDbTransaction? Transaction { get; }
    IDbTransaction BeginTransaction();
    void Commit();
    void Rollback();
    Task<int> ExecuteAsync(string sql, object? parameters = null);
    Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null);
    Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null);
}

public class DuckDbContext : IDatabaseContext
{
    private readonly DuckDBConnection _connection;
    private IDbTransaction? _transaction;
    private bool _disposed;
    private readonly ILogger<DuckDbContext> _logger;

    public IDbConnection Connection => _connection;
    public IDbTransaction? Transaction => _transaction;

    public DuckDbContext(IOptions<DatabaseSettings> settings, ILogger<DuckDbContext> logger)
    {
        _logger = logger;
        var connectionString = settings.Value.ConnectionString;
        
        _logger.LogDebug("Creating DuckDB connection with: {ConnectionString}", connectionString);
        
        _connection = new DuckDBConnection(connectionString);
        _connection.Open();
        
        // Note: DuckDB handles WAL mode differently than SQLite
        // Most PRAGMA settings are not needed or have different syntax
    }

    public IDbTransaction BeginTransaction()
    {
        if (_transaction != null)
            throw new InvalidOperationException("Transaction already in progress");
            
        _transaction = _connection.BeginTransaction();
        return _transaction;
    }

    public void Commit()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No transaction in progress");
            
        _transaction.Commit();
        _transaction.Dispose();
        _transaction = null;
    }

    public void Rollback()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No transaction in progress");
            
        _transaction.Rollback();
        _transaction.Dispose();
        _transaction = null;
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        using var cmd = _connection.CreateCommand();
        
        // Replace named parameters with positional parameters for DuckDB
        if (parameters != null)
        {
            var (convertedSql, paramValues) = ConvertToPositionalParameters(sql, parameters);
            cmd.CommandText = convertedSql;
            
            foreach (var value in paramValues)
            {
                var parameter = cmd.CreateParameter();
                parameter.Value = value ?? DBNull.Value;
                cmd.Parameters.Add(parameter);
            }
        }
        else
        {
            cmd.CommandText = sql;
        }
        
        cmd.Transaction = _transaction as DuckDBTransaction;
        return await Task.FromResult(cmd.ExecuteNonQuery());
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters = null)
    {
        var results = await QueryAsync<T>(sql, parameters);
        return results.FirstOrDefault();
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        using var cmd = _connection.CreateCommand();
        
        // Replace named parameters with positional parameters for DuckDB
        if (parameters != null)
        {
            var (convertedSql, paramValues) = ConvertToPositionalParameters(sql, parameters);
            cmd.CommandText = convertedSql;
            
            foreach (var value in paramValues)
            {
                var parameter = cmd.CreateParameter();
                parameter.Value = value ?? DBNull.Value;
                cmd.Parameters.Add(parameter);
            }
        }
        else
        {
            cmd.CommandText = sql;
        }
        
        cmd.Transaction = _transaction as DuckDBTransaction;
        
        var results = new List<T>();
        using var reader = await Task.FromResult(cmd.ExecuteReader());
        
        while (reader.Read())
        {
            if (typeof(T).IsPrimitive || typeof(T) == typeof(string) || typeof(T) == typeof(DateTime))
            {
                results.Add((T)Convert.ChangeType(reader[0], typeof(T)));
            }
            else
            {
                results.Add(MapReaderToObject<T>(reader));
            }
        }
        
        return results;
    }

    private (string sql, List<object?> values) ConvertToPositionalParameters(string sql, object parameters)
    {
        var properties = parameters.GetType().GetProperties();
        var paramValues = new List<object?>();
        var convertedSql = sql;
        
        // Create a dictionary of parameter names to values
        var paramDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in properties)
        {
            paramDict[prop.Name] = prop.GetValue(parameters);
        }
        
        // Find all parameter placeholders in order of appearance
        var paramPattern = new System.Text.RegularExpressions.Regex(@"\$([a-zA-Z_][a-zA-Z0-9_]*)");
        var matches = paramPattern.Matches(sql);
        
        // Collect unique parameters in order of first appearance
        var orderedParams = new List<(int index, string placeholder, string paramName)>();
        var seenParams = new HashSet<string>();
        
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var placeholder = match.Value;
            var paramName = match.Groups[1].Value;
            
            if (!seenParams.Contains(placeholder))
            {
                orderedParams.Add((match.Index, placeholder, paramName));
                seenParams.Add(placeholder);
            }
        }
        
        // Sort by index to maintain order
        orderedParams.Sort((a, b) => a.index.CompareTo(b.index));
        
        // Replace parameters with ? and collect values in order
        foreach (var (_, placeholder, paramName) in orderedParams)
        {
            if (paramDict.TryGetValue(paramName, out var value))
            {
                convertedSql = convertedSql.Replace(placeholder, "?");
                paramValues.Add(value);
            }
        }
        
        return (convertedSql, paramValues);
    }

    private T MapReaderToObject<T>(IDataReader reader)
    {
        var obj = Activator.CreateInstance<T>();
        var properties = typeof(T).GetProperties();
        
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            var property = properties.FirstOrDefault(p => 
                string.Equals(p.Name, columnName, StringComparison.OrdinalIgnoreCase));
                
            if (property != null && reader[i] != DBNull.Value)
            {
                var value = reader[i];
                if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
                {
                    property.SetValue(obj, DateTime.Parse(value.ToString()!));
                }
                else
                {
                    property.SetValue(obj, Convert.ChangeType(value, property.PropertyType));
                }
            }
        }
        
        return obj;
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _transaction?.Dispose();
        _connection?.Dispose();
        _disposed = true;
    }
}