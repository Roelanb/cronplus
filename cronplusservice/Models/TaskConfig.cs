#if WINDOWS
using System.Drawing;
using System.Drawing.Printing;
#endif
using System;
using System.IO;
using SurrealDb.Net.Models;
using System.Text.Json.Serialization;

namespace CronPlus.Models;

public enum TriggerType
{
    FileCreated,
    FileRenamed,
    Time,
    Interval
}

public class TaskConfig : Record
{
    // Default constructor required for JSON deserialization
    public TaskConfig()
    {
        // Initialize default values
        triggerType = TriggerType.FileCreated;
        sourceFolder = string.Empty;
        taskType = string.Empty;
        destinationFolder = string.Empty;
        createdAt = DateTime.UtcNow;
        updatedAt = DateTime.UtcNow;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TriggerType triggerType { get; set; }
    
    public string? sourceFolder { get; set; }
    
    public string? destinationFolder { get; set; }
    
    public string? taskType { get; set; }
    
    public string? sourceFile { get; set; }
    
    public string? destinationFile { get; set; }
    
    public string? time { get; set; }
    
    public int interval { get; set; }
    
    public string? printerName { get; set; }
    
    public string? archiveDirectory { get; set; }

    public DateTime? createdAt { get; set; }

    public DateTime? updatedAt { get; set; }
}
