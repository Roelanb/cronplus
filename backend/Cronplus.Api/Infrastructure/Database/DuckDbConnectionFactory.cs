using System.Data;
using DuckDB.NET.Data;
using Microsoft.Extensions.Options;

namespace Cronplus.Api.Infrastructure.Database;

/// <summary>
/// Factory for creating DuckDB connections
/// </summary>
public interface IDbConnectionFactory
{
    Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}

public class DuckDbConnectionFactory : IDbConnectionFactory
{
    private readonly DatabaseSettings _settings;
    private readonly ILogger<DuckDbConnectionFactory> _logger;

    public DuckDbConnectionFactory(IOptions<DatabaseSettings> settings, ILogger<DuckDbConnectionFactory> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = _settings.ConnectionString;
        var connection = new DuckDBConnection(connectionString);
        
        try
        {
            connection.Open();
            _logger.LogDebug("Created new DuckDB connection");
            return Task.FromResult<IDbConnection>(connection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create DuckDB connection");
            connection.Dispose();
            throw;
        }
    }
}