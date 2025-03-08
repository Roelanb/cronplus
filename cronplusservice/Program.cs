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
#if WINDOWS
using System.Drawing;
using System.Drawing.Printing;
#endif

namespace CronPlus;

class Program
{
    static void Main(string[] args)
    {
        string configPath = args.Length > 0 ? args[0] : "Config.json";

        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Config file not found: {configPath}");
            return;
        }

        Console.WriteLine("CronPlus started. Press any key to exit.");
        Console.WriteLine($"Loading config from: {configPath}");

        List<TaskConfig> configs = LoadConfig(configPath);
        
        // Group configs by directory for file system triggers
        var directoryConfigs = new Dictionary<string, List<TaskConfig>>();
        
        foreach (var config in configs)
        {
            // Handle file system triggers (file created or renamed)
            if (config.TriggerType == "fileCreated" || config.TriggerType == "fileRenamed")
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
            
            var fileSystemTrigger = new FileSystemTaskTrigger(directory, dirConfigs);
            fileSystemTrigger.Start();
        }

        Console.ReadKey();
    }

    static List<TaskConfig> LoadConfig(string filePath)
    {
        string json = File.ReadAllText(filePath);
        var configs = JsonConvert.DeserializeObject<List<TaskConfig>>(json);
        return configs ?? new List<TaskConfig>();
    }
}

