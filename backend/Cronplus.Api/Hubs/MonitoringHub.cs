using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Cronplus.Api.Hubs;

/// <summary>
/// SignalR hub for real-time system monitoring and metrics
/// </summary>
[Authorize]
public class MonitoringHub : Hub
{
    private readonly ILogger<MonitoringHub> _logger;
    private readonly IHostEnvironment _environment;
    private static readonly Process _currentProcess = Process.GetCurrentProcess();

    public MonitoringHub(
        ILogger<MonitoringHub> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Monitoring client connected: {ConnectionId}", Context.ConnectionId);
        
        // Add to monitoring group
        await Groups.AddToGroupAsync(Context.ConnectionId, "Monitoring");
        
        // Send initial system info
        await Clients.Caller.SendAsync("SystemInfo", GetSystemInfo());
        
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Monitoring client disconnected: {ConnectionId}", Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Monitoring");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Get current system metrics
    /// </summary>
    public async Task<SystemMetrics> GetMetrics()
    {
        return await Task.FromResult(CollectMetrics());
    }

    /// <summary>
    /// Subscribe to metric updates at specified interval
    /// </summary>
    public async Task SubscribeToMetrics(int intervalSeconds = 5)
    {
        if (intervalSeconds < 1 || intervalSeconds > 60)
        {
            await Clients.Caller.SendAsync("Error", new { 
                Message = "Interval must be between 1 and 60 seconds" 
            });
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"Metrics_{intervalSeconds}");
        _logger.LogDebug("Client {ConnectionId} subscribed to metrics with {Interval}s interval", 
            Context.ConnectionId, intervalSeconds);
    }

    /// <summary>
    /// Unsubscribe from metric updates
    /// </summary>
    public async Task UnsubscribeFromMetrics(int intervalSeconds = 5)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Metrics_{intervalSeconds}");
        _logger.LogDebug("Client {ConnectionId} unsubscribed from metrics", Context.ConnectionId);
    }

    /// <summary>
    /// Get application logs (requires admin permission)
    /// </summary>
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IEnumerable<LogEntry>> GetRecentLogs(int count = 100)
    {
        // This would typically fetch from a log store
        // For now, returning empty as implementation depends on logging setup
        return await Task.FromResult(new List<LogEntry>());
    }

    // Helper methods
    private SystemInfo GetSystemInfo()
    {
        return new SystemInfo
        {
            MachineName = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            DotNetVersion = RuntimeInformation.FrameworkDescription,
            ApplicationName = _environment.ApplicationName,
            EnvironmentName = _environment.EnvironmentName,
            StartTime = _currentProcess.StartTime,
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0"
        };
    }

    private SystemMetrics CollectMetrics()
    {
        _currentProcess.Refresh();
        
        var workingSet = _currentProcess.WorkingSet64;
        var privateMemory = _currentProcess.PrivateMemorySize64;
        var virtualMemory = _currentProcess.VirtualMemorySize64;
        var gcTotalMemory = GC.GetTotalMemory(false);
        
        return new SystemMetrics
        {
            Timestamp = DateTime.UtcNow,
            
            // CPU Metrics
            ProcessorTime = _currentProcess.TotalProcessorTime.TotalMilliseconds,
            ThreadCount = _currentProcess.Threads.Count,
            
            // Memory Metrics
            WorkingSetMB = workingSet / (1024.0 * 1024.0),
            PrivateMemoryMB = privateMemory / (1024.0 * 1024.0),
            VirtualMemoryMB = virtualMemory / (1024.0 * 1024.0),
            GCHeapSizeMB = gcTotalMemory / (1024.0 * 1024.0),
            GCFragmentedMB = 0,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            
            // Application Metrics
            HandleCount = _currentProcess.HandleCount,
            UptimeHours = (DateTime.UtcNow - _currentProcess.StartTime).TotalHours
        };
    }
}

/// <summary>
/// System information DTO
/// </summary>
public class SystemInfo
{
    public string MachineName { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public string DotNetVersion { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// System metrics DTO
/// </summary>
public class SystemMetrics
{
    public DateTime Timestamp { get; set; }
    
    // CPU Metrics
    public double ProcessorTime { get; set; }
    public int ThreadCount { get; set; }
    
    // Memory Metrics
    public double WorkingSetMB { get; set; }
    public double PrivateMemoryMB { get; set; }
    public double VirtualMemoryMB { get; set; }
    public double GCHeapSizeMB { get; set; }
    public double GCFragmentedMB { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    
    // Application Metrics
    public int HandleCount { get; set; }
    public double UptimeHours { get; set; }
}

/// <summary>
/// Log entry DTO
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public Dictionary<string, object>? Properties { get; set; }
}

/// <summary>
/// Background service to broadcast metrics periodically
/// </summary>
public class MetricsBroadcastService : BackgroundService
{
    private readonly IHubContext<MonitoringHub> _hubContext;
    private readonly ILogger<MetricsBroadcastService> _logger;
    private readonly Dictionary<int, Timer> _intervalTimers = new();

    public MetricsBroadcastService(
        IHubContext<MonitoringHub> hubContext,
        ILogger<MetricsBroadcastService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Create timers for common intervals
        var intervals = new[] { 5, 10, 30, 60 }; // seconds
        
        foreach (var interval in intervals)
        {
            var timer = new Timer(
                async _ => await BroadcastMetrics(interval),
                null,
                TimeSpan.FromSeconds(interval),
                TimeSpan.FromSeconds(interval));
            
            _intervalTimers[interval] = timer;
        }

        _logger.LogInformation("Metrics broadcast service started with intervals: {Intervals}", intervals);
        
        return Task.CompletedTask;
    }

    private async Task BroadcastMetrics(int intervalSeconds)
    {
        try
        {
            var metrics = CollectMetrics();
            await _hubContext.Clients
                .Group($"Metrics_{intervalSeconds}")
                .SendAsync("MetricsUpdate", metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting metrics for interval {Interval}s", intervalSeconds);
        }
    }

    private SystemMetrics CollectMetrics()
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();
        
        var gcTotalMemory = GC.GetTotalMemory(false);
        
        return new SystemMetrics
        {
            Timestamp = DateTime.UtcNow,
            ProcessorTime = process.TotalProcessorTime.TotalMilliseconds,
            ThreadCount = process.Threads.Count,
            WorkingSetMB = process.WorkingSet64 / (1024.0 * 1024.0),
            PrivateMemoryMB = process.PrivateMemorySize64 / (1024.0 * 1024.0),
            VirtualMemoryMB = process.VirtualMemorySize64 / (1024.0 * 1024.0),
            GCHeapSizeMB = gcTotalMemory / (1024.0 * 1024.0),
            GCFragmentedMB = 0,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            HandleCount = process.HandleCount,
            UptimeHours = (DateTime.UtcNow - process.StartTime).TotalHours
        };
    }

    public override void Dispose()
    {
        foreach (var timer in _intervalTimers.Values)
        {
            timer?.Dispose();
        }
        _intervalTimers.Clear();
        base.Dispose();
    }
}