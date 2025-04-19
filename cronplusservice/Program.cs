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
using CronPlus.Storage;
#if WINDOWS
using System.Drawing;
using System.Drawing.Printing;
#endif

namespace CronPlus;

class Program
{
    static async Task Main(string[] args)
    {
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
                Console.WriteLine($"Invalid TaskConfig for directory '{config.Directory}':");
                foreach (var error in errors)
                    Console.WriteLine("  - " + error);
                continue;
            }
            validConfigs.Add(config);
        }
        
        ConsoleDumper.DumpTaskConfigsWithValidity(configs, validationResults);
        
        // Group configs by directory for file system triggers
        var directoryConfigs = new Dictionary<string, List<TaskConfig>>();
        
        foreach (var config in validConfigs)
        {
            // Handle file system triggers (file created or renamed)
            if (config.TriggerType == TriggerType.FileCreated || config.TriggerType == TriggerType.FileRenamed)
            {
                if (!directoryConfigs.ContainsKey(config.Directory))
                {
                    directoryConfigs[config.Directory] = new List<TaskConfig>();
                }
                
                directoryConfigs[config.Directory].Add(config);
            }
            // Add other trigger types here when implemented
        }

        // Create file system triggers for each directory
        foreach (var entry in directoryConfigs)
        {
            var directory = entry.Key;
            var dirConfigs = entry.Value;
            
            var fileSystemTrigger = new FileSystemTaskTrigger(directory, dirConfigs, dataStore);
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
