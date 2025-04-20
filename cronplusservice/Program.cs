using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Newtonsoft.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using CronPlus.Tasks;
using CronPlus.Storage;
using CronPlus.Models;
#if WINDOWS
using System.Drawing;
using System.Drawing.Printing;
#endif

namespace CronPlus;

class Program
{
    public const string Version = "1.0.0";

    static async Task Main(string[] args)
    {
        Console.WriteLine($"CronPlus Backend Service v{Version}");
        var dataStore = new DataStore();
        
        var configs = await LoadConfig(dataStore);
        var validConfigs = new List<TaskConfig>();
        var validationResults = new Dictionary<TaskConfig, List<string>>();
        
        foreach (var config in configs)
        {
            var errors = TaskConfigValidator.Validate(config);
            validationResults[config] = errors;
            if (errors.Count > 0)
            {
                Console.WriteLine($"Invalid TaskConfig for source folder '{config.sourceFolder}':");
                foreach (var error in errors)
                    Console.WriteLine("  - " + error);
                continue;
            }
            validConfigs.Add(config);
        }
        
        ConsoleDumper.DumpTaskConfigsWithValidity(configs, validationResults);
        
        // Group configs by source folder for file system triggers
        var sourceFolderConfigs = new Dictionary<string, List<TaskConfig>>();
        
        foreach (var config in validConfigs)
        {
            // Handle file system triggers (file created or renamed)
            if (config.triggerType == TriggerType.FileCreated || config.triggerType == TriggerType.FileRenamed)
            {
                if (!sourceFolderConfigs.ContainsKey(config.sourceFolder))
                {
                    sourceFolderConfigs[config.sourceFolder] = new List<TaskConfig>();
                }
                
                sourceFolderConfigs[config.sourceFolder].Add(config);
            }
            // Add other trigger types here when implemented
        }

        // Create file system triggers for each source folder
        foreach (var entry in sourceFolderConfigs)
        {
            var sourceFolder = entry.Key;
            var dirConfigs = entry.Value;
            
            var fileSystemTrigger = new FileSystemTaskTrigger(sourceFolder, dirConfigs, dataStore);
            fileSystemTrigger.Start();
        }

        Console.ReadKey();
    }

    static async Task<List<TaskConfig>> LoadConfig(DataStore dataStore)
    {
        var result = await dataStore.GetConfigs();
        
        return result;
    }
}
