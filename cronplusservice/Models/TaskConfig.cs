#if WINDOWS
using System.Drawing;
using System.Drawing.Printing;
#endif
using System.Text.Json.Serialization;

namespace CronPlus.Models;

public enum TriggerType
{
    FileCreated,
    FileRenamed,
    Time,
    Interval
}

public class TaskConfig
{
    // Default constructor required for JSON deserialization
    public TaskConfig()
    {
        // Initialize default values
        TriggerType = TriggerType.FileCreated;
        Directory = string.Empty;
        TaskType = string.Empty;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TriggerType TriggerType { get; set; }
    public string Directory { get; set; }
    public string TaskType { get; set; }
    public string? SourceFile { get; set; }
    public string? DestinationFile { get; set; }
    public string? Time { get; set; }
    public int Interval { get; set; }
    public string? PrinterName { get; set; }
    public string? ArchiveDirectory { get; set; }
}
