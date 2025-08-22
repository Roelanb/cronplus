using FastEndpoints;
using Cronplus.Api.Domain.Interfaces;
using Cronplus.Api.Services.TaskSupervision;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Cronplus.Api.Features.Monitoring;

public class MetricsRequest
{
    // Empty request for metrics endpoint
}

public class MetricsResponse
{
    public SystemMetrics System { get; set; } = new();
    public ApplicationMetrics Application { get; set; } = new();
    public TaskMetrics Tasks { get; set; } = new();
    public DatabaseMetrics Database { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class SystemMetrics
{
    public double CpuUsagePercent { get; set; }
    public long MemoryUsedMB { get; set; }
    public long MemoryAvailableMB { get; set; }
    public long DiskUsedGB { get; set; }
    public long DiskAvailableGB { get; set; }
    public string OperatingSystem { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
}

public class ApplicationMetrics
{
    public TimeSpan Uptime { get; set; }
    public long WorkingSetMB { get; set; }
    public long PrivateMemoryMB { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public long GCHeapMB { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
}

public class TaskMetrics
{
    public int TotalTasks { get; set; }
    public int EnabledTasks { get; set; }
    public int RunningTasks { get; set; }
    public int IdleTasks { get; set; }
    public int FailedTasks { get; set; }
    public long TotalFilesProcessed { get; set; }
    public long TotalFilesInLastHour { get; set; }
    public double AverageProcessingTimeMs { get; set; }
}

public class DatabaseMetrics
{
    public long DatabaseSizeMB { get; set; }
    public int TotalExecutionLogs { get; set; }
    public int LogsInLastHour { get; set; }
    public int TotalVariables { get; set; }
    public int TotalPipelineSteps { get; set; }
}

public class MetricsEndpoint : EndpointWithoutRequest<MetricsResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITaskSupervisorManager _supervisorManager;
    private readonly ILogger<MetricsEndpoint> _logger;
    private static readonly Process _currentProcess = Process.GetCurrentProcess();

    public MetricsEndpoint(
        IUnitOfWork unitOfWork,
        ITaskSupervisorManager supervisorManager,
        ILogger<MetricsEndpoint> logger)
    {
        _unitOfWork = unitOfWork;
        _supervisorManager = supervisorManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Get("/api/metrics");
        AllowAnonymous();
        Description(b => b
            .Produces<MetricsResponse>(200)
            .WithTags("Monitoring"));
        Summary(s =>
        {
            s.Summary = "Get system and application metrics";
            s.Description = "Retrieves current system, application, task, and database metrics";
        });
    }

    public override async Task<MetricsResponse> HandleAsync(CancellationToken ct)
    {
        _logger.LogDebug("Collecting system metrics");

        var response = new MetricsResponse
        {
            System = await CollectSystemMetrics(),
            Application = CollectApplicationMetrics(),
            Tasks = await CollectTaskMetrics(),
            Database = await CollectDatabaseMetrics()
        };

        return response;
    }

    private async Task<SystemMetrics> CollectSystemMetrics()
    {
        return await Task.FromResult(new SystemMetrics
        {
            CpuUsagePercent = GetCpuUsage(),
            MemoryUsedMB = GC.GetTotalMemory(false) / (1024 * 1024),
            MemoryAvailableMB = GetAvailableMemory(),
            DiskUsedGB = GetDiskUsage(),
            DiskAvailableGB = GetAvailableDisk(),
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount
        });
    }

    private ApplicationMetrics CollectApplicationMetrics()
    {
        _currentProcess.Refresh();
        
        return new ApplicationMetrics
        {
            Uptime = DateTime.UtcNow - _currentProcess.StartTime.ToUniversalTime(),
            WorkingSetMB = _currentProcess.WorkingSet64 / (1024 * 1024),
            PrivateMemoryMB = _currentProcess.PrivateMemorySize64 / (1024 * 1024),
            ThreadCount = _currentProcess.Threads.Count,
            HandleCount = _currentProcess.HandleCount,
            GCHeapMB = GC.GetTotalMemory(false) / (1024 * 1024),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }

    private async Task<TaskMetrics> CollectTaskMetrics()
    {
        var tasks = await _unitOfWork.Tasks.GetAllAsync();
        var taskStatuses = _supervisorManager.GetAllTaskStatuses();
        
        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var recentLogs = await _unitOfWork.ExecutionLogs.FindAsync(
            "started_at >= $startTime AND status = 'Success'",
            new Dictionary<string, object?> { ["startTime"] = oneHourAgo });

        var successfulLogs = recentLogs.ToList();
        var avgProcessingTime = 0.0;
        
        if (successfulLogs.Any(l => l.CompletedAt.HasValue))
        {
            avgProcessingTime = successfulLogs
                .Where(l => l.CompletedAt.HasValue)
                .Average(l => (l.CompletedAt!.Value - l.StartedAt).TotalMilliseconds);
        }

        return new TaskMetrics
        {
            TotalTasks = tasks.Count(),
            EnabledTasks = tasks.Count(t => t.Enabled),
            RunningTasks = taskStatuses.Count(s => s.State == TaskState.Processing || s.State == TaskState.Idle),
            IdleTasks = taskStatuses.Count(s => s.State == TaskState.Idle),
            FailedTasks = taskStatuses.Count(s => s.State == TaskState.Failed),
            TotalFilesProcessed = taskStatuses.Sum(s => s.ProcessedCount),
            TotalFilesInLastHour = successfulLogs.Count,
            AverageProcessingTimeMs = avgProcessingTime
        };
    }

    private async Task<DatabaseMetrics> CollectDatabaseMetrics()
    {
        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var allLogs = await _unitOfWork.ExecutionLogs.GetAllAsync();
        var recentLogs = allLogs.Where(l => l.StartedAt >= oneHourAgo);
        
        var allVariables = await _unitOfWork.TaskVariables.GetAllAsync();
        var allSteps = await _unitOfWork.PipelineSteps.GetAllAsync();

        return new DatabaseMetrics
        {
            DatabaseSizeMB = GetDatabaseSize(),
            TotalExecutionLogs = allLogs.Count(),
            LogsInLastHour = recentLogs.Count(),
            TotalVariables = allVariables.Count(),
            TotalPipelineSteps = allSteps.Count()
        };
    }

    private double GetCpuUsage()
    {
        try
        {
            return _currentProcess.TotalProcessorTime.TotalMilliseconds / Environment.TickCount * 100;
        }
        catch
        {
            return 0;
        }
    }

    private long GetAvailableMemory()
    {
        // This is a simplified implementation
        // In production, you might want to use platform-specific APIs
        return 1024; // Return 1GB as placeholder
    }

    private long GetDiskUsage()
    {
        try
        {
            var drive = new DriveInfo(Directory.GetCurrentDirectory());
            return (drive.TotalSize - drive.AvailableFreeSpace) / (1024 * 1024 * 1024);
        }
        catch
        {
            return 0;
        }
    }

    private long GetAvailableDisk()
    {
        try
        {
            var drive = new DriveInfo(Directory.GetCurrentDirectory());
            return drive.AvailableFreeSpace / (1024 * 1024 * 1024);
        }
        catch
        {
            return 0;
        }
    }

    private long GetDatabaseSize()
    {
        try
        {
            var dbPath = "cronplus.db"; // This should come from configuration
            if (File.Exists(dbPath))
            {
                var fileInfo = new FileInfo(dbPath);
                return fileInfo.Length / (1024 * 1024);
            }
        }
        catch
        {
            // Ignore errors
        }
        return 0;
    }
}