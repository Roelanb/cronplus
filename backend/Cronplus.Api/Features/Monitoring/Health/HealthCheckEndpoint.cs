using Cronplus.Api.Domain.Interfaces;
using FastEndpoints;
using System.Diagnostics;
using Cronplus.Api.Common.Endpoints;

namespace Cronplus.Api.Features.Monitoring.Health;

public class HealthCheckRequest
{
    public bool Detailed { get; set; }
}

public class HealthCheckResponse
{
    public string Status { get; set; } = "Healthy";
    public string Version { get; set; } = "1.0.0";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Details { get; set; }
}

public class HealthCheckEndpoint : BaseEndpoint<HealthCheckRequest, HealthCheckResponse>
{
    protected override string EndpointName => "HealthCheck";
    protected override string EndpointSummary => "Check API health status";

    protected override void ConfigureEndpoint()
    {
        Get("/health/status");
        Group<MonitoringGroup>();
        AllowAnonymous();
        Description(b => b
            .WithDescription("Returns the current health status of the API including system information when detailed=true")
            .WithTags("Monitoring"));
    }

    public override Task HandleAsync(HealthCheckRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Health check requested with detailed: {Detailed}", req.Detailed);

        var response = new HealthCheckResponse();

        if (req.Detailed)
        {
            response.Details = new Dictionary<string, object>
            {
                ["environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                ["machineName"] = Environment.MachineName,
                ["processId"] = Environment.ProcessId,
                ["workingSet"] = Environment.WorkingSet,
                ["uptime"] = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                ["threadCount"] = Process.GetCurrentProcess().Threads.Count,
                ["osVersion"] = Environment.OSVersion.ToString(),
                ["is64BitProcess"] = Environment.Is64BitProcess,
                ["processorCount"] = Environment.ProcessorCount
            };
        }

        Response = response;
        return Task.CompletedTask;
    }
}