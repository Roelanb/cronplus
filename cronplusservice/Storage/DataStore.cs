using System.Runtime.CompilerServices;
using CronPlus.Models;
using SurrealDb.Net;
using SurrealDb.Net.Models;
using SurrealDb.Net.Models.Auth;

namespace CronPlus.Storage;

public class DataStore
{
    public async Task<TaskConfig> SaveConfig()
    {
        var db = new SurrealDbClient("ws://127.0.0.1:8000/rpc");
        await db.SignIn(new RootAuth { Username = "root", Password = "root" });
        await db.Use("cronplus","cronplus");

        const string TABLE = "TaskConfig";

        var config = new TaskConfig
        {
            TriggerType = TriggerType.FileCreated,
            Directory = "/media/bart/Data1/Projects/cronplus/cronplusservice/watchpathprint",
            TaskType = "print",
            SourceFile = null,
            DestinationFile = null,
            Time = null,
            Interval = 0,
            PrinterName = "Brother_DCP_J4120DW",
            ArchiveDirectory = "/media/bart/Data1/Projects/cronplus/cronplusservice/archive"
        };

        var created = await db.Create(TABLE, config);

        return created;
    }

    public async Task<List<TaskConfig>> GetConfigs()
    {
        var db = new SurrealDbClient("ws://127.0.0.1:8000/rpc");
        await db.SignIn(new RootAuth { Username = "root", Password = "root" });
        await db.Use("cronplus","cronplus");

        const string TABLE = "TaskConfig";

        var configs = await db.Select<TaskConfig>(TABLE);

        return configs.ToList();
    }

    public async Task LogTaskAsync(TaskLogging log)
    {
        var db = new SurrealDbClient("ws://127.0.0.1:8000/rpc");
        await db.SignIn(new RootAuth { Username = "root", Password = "root" });
        await db.Use("cronplus","cronplus");
        const string TABLE = "TaskLogging";
        await db.Create(TABLE, log);
    }
}