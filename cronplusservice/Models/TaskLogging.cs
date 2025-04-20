using System;
using System.Text.Json.Serialization;
using CronPlus.Models;
using SurrealDb.Net.Models;

namespace CronPlus.Models;

public class TaskLogging : Record
{
    public string TaskType { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TriggerType TriggerType { get; set; }
    public string Directory { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string? PrinterName { get; set; }
    public string? ArchiveDirectory { get; set; }
    public string SourceFolder { get; set; } = string.Empty;
    public string DestinationFolder { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
    public string Result { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
}
