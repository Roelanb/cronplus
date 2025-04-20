using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SurrealDb.Net;
using SurrealDb.Net.Models.Auth;
using CronPlus.Models;

namespace CronPlus.Storage;

public class DataStore
{
    private readonly SurrealDbClient _db;
    private const string CONFIG_TABLE = "taskconfig";
    private const string LOGS_TABLE = "tasklogs";

    public DataStore()
    {
        _db = new SurrealDbClient("ws://127.0.0.1:8000/rpc");
        InitializeDbConnection().GetAwaiter().GetResult();
    }

    private async Task InitializeDbConnection()
    {
        try
        {
            await _db.Connect();
            await _db.SignIn(new RootAuth { Username = "root", Password = "root" });
            await _db.Use("cronplus","cronplus");
            Console.WriteLine("Database connection initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing database connection: {ex.Message}");
            throw;
        }
    }

    public async Task<TaskConfig> SaveConfig(TaskConfig config)
    {
        if (!config.createdAt.HasValue)
            config.createdAt = DateTime.UtcNow;

        config.updatedAt = DateTime.UtcNow;

        var created = await _db.Create(CONFIG_TABLE, config);
        return created;
    }

    public async Task<List<TaskConfig>> GetConfigs()
    {
        try
        {
            var configs = await _db.Select<TaskConfig>(CONFIG_TABLE);
            var result = configs.ToList();

            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching configs: {ex.Message}");
            Console.WriteLine("Creating fresh config table...");
            
            try
            {
                try 
                {
                    await _db.Query($"REMOVE TABLE {CONFIG_TABLE}");
                }
                catch (Exception dropEx)
                {
                    Console.WriteLine($"Table may not exist, continuing: {dropEx.Message}");
                }

                try
                {
                    await _db.Query($"DEFINE TABLE {CONFIG_TABLE}");
                }
                catch (Exception createEx)
                {
                    Console.WriteLine($"Error creating table: {createEx.Message}");
                }
                
                return new List<TaskConfig>();
            }
            catch (Exception recreateEx)
            {
                Console.WriteLine($"Error recreating config table: {recreateEx.Message}");
                return new List<TaskConfig>();
            }
        }
    }


    public async Task LogTaskAsync(TaskLogging log)
    {
        await _db.Create(LOGS_TABLE, log);
    }

    public async Task SaveTaskLog(TaskLogging log)
    {
        await _db.Create(LOGS_TABLE, log);
    }

    public async Task<List<TaskLogging>> GetTaskLogs()
    {
        try
        {
            var result = await _db.Select<TaskLogging>(LOGS_TABLE);
            return result.Where(l => l != null).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching task logs: {ex.Message}");
            return new List<TaskLogging>();
        }
    }
}