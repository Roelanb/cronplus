in the cronplusui application, add the database connection code. Use the https://surrealdb.com/docs/sdk/javascript documentation, the database to use is defined in the docker-compose.yaml file.
We would ant to store TaskConfig records in the database. The model of the TaskConfig can be found in the cronplusservice application.

Update the schedules page to show the TaskConfig records from the database. Call the Page Tasks.

Add the functionality to create, update and delete TaskConfig records.

can you use the mcpbrowser tool to add a record.

can you use the mcpbrowser tool to update a record.

can you use the mcpbrowser tool to delete a record.

load the TaskConfig records from the database, namespace = cronplus, database = cronplus
Use the root username, password = root
The model of the taskconfig records look like this:

public class TaskConfig
{
// Default constructor required for JSON deserialization
public TaskConfig()
{
// Initialize default values
TriggerType = string.Empty;
Directory = string.Empty;
TaskType = string.Empty;
}

    public string TriggerType { get; set; }
    public string Directory { get; set; }
    public string TaskType { get; set; }
    public string? SourceFile { get; set; }
    public string? DestinationFile { get; set; }
    public string? Time { get; set; }
    public int Interval { get; set; }
    public string? PrinterName { get; set; }
    public string? ArchiveDirectory { get; set; }

}

show the taskconfig list on the schedules page

On the TaskConfigForm page, can you add file/folder dialog boxes for the source and destination files

The deleting and updating of TaskConfig records does not work, imlement it correctly
