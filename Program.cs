using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Newtonsoft.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
#if WINDOWS
using System.Drawing;
using System.Drawing.Printing;
#endif

namespace CronPlus
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Please provide the path to config.json as an argument.");
                Console.WriteLine("Usage: cronplus <path-to-config.json>");
                return;
            }

            string configPath = args[0];
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Config file not found: {configPath}");
                return;
            }

            Console.WriteLine("CronPlus started. Press any key to exit.");

            Console.WriteLine($"Loading config from: {configPath}");
            
            List<TaskConfig> configs = LoadConfig(configPath);

            foreach (var config in configs)
            {
                if (config.TriggerType == "fileCreated" || config.TriggerType == "fileRenamed")
                {
                    FileSystemTaskTrigger fileSystemTaskTrigger = new FileSystemTaskTrigger(config.Directory, new List<TaskConfig> { config });
                    fileSystemTaskTrigger.Start();
                }
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

    public class FileSystemTaskTrigger
    {
        private string _directory;
        private List<TaskConfig> _configs;
        private FileSystemWatcher _watcher;

        public FileSystemTaskTrigger(string directory, List<TaskConfig> configs)
        {
            _directory = directory;
            _configs = configs;
            _watcher = new FileSystemWatcher(_directory);
            _watcher.Created += OnFileCreated;
            _watcher.Renamed += OnFileRenamed;
            _watcher.EnableRaisingEvents = false; // Start disabled, will enable in Start()
        }

        public void Start()
        {
            _watcher.EnableRaisingEvents = true;
            _watcher.IncludeSubdirectories = false;
            Console.WriteLine($"Watching directory: {_directory}");
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"File created: {e.FullPath}");
            ExecuteTasks("fileCreated", e.FullPath);
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            Console.WriteLine($"File renamed: {e.OldFullPath} to {e.FullPath}");
            ExecuteTasks("fileRenamed", e.FullPath);
        }

        private void ExecuteTasks(string triggerType, string filePath)
        {
            foreach (var config in _configs)
            {
                if (config.TriggerType == triggerType)
                {
                    Console.WriteLine($"Executing task: {config.TaskType} for file: {filePath}");
                    
                    switch (config.TaskType.ToLower())
                    {
                        case "print":
                            PrintAndArchiveFile(filePath, config);
                            break;
                        case "copy":
                            if (!string.IsNullOrEmpty(config.DestinationFile))
                            {
                                File.Copy(filePath, config.DestinationFile, true);
                            }
                            else
                            {
                                Console.WriteLine("Destination file not specified for copy operation.");
                            }
                            break;
                        case "move":
                            if (!string.IsNullOrEmpty(config.DestinationFile))
                            {
                                File.Move(filePath, config.DestinationFile);
                            }
                            else
                            {
                                Console.WriteLine("Destination file not specified for move operation.");
                            }
                            break;
                        default:
                            Console.WriteLine($"Unknown task type: {config.TaskType}");
                            break;
                    }
                }
            }
        }
        
        private void PrintAndArchiveFile(string filePath, TaskConfig config)
        {
            try
            {
                // Wait a moment to ensure the file is fully written
                Task.Delay(1000).Wait();
                
                // Print the file
                if (!string.IsNullOrEmpty(config.PrinterName))
                {
                    Console.WriteLine($"Printing file: {filePath} to printer: {config.PrinterName}");
                    PrintFile(filePath, config.PrinterName);
                }
                else
                {
                    Console.WriteLine("No printer specified in configuration.");
                }
                
                // Archive the file to a monthly folder
                if (!string.IsNullOrEmpty(config.ArchiveDirectory))
                {
                    string monthlyFolder = Path.Combine(
                        config.ArchiveDirectory,
                        DateTime.Now.ToString("yyyy-MM")
                    );
                    
                    // Create the monthly folder if it doesn't exist
                    if (!Directory.Exists(monthlyFolder))
                    {
                        Directory.CreateDirectory(monthlyFolder);
                    }
                    
                    string fileName = Path.GetFileName(filePath);
                    string archiveFilePath = Path.Combine(monthlyFolder, fileName);
                    
                    // If a file with the same name already exists in the archive, add a timestamp
                    if (File.Exists(archiveFilePath))
                    {
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        string extension = Path.GetExtension(fileName);
                        string timestampedFileName = $"{fileNameWithoutExt}_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}{extension}";
                        archiveFilePath = Path.Combine(monthlyFolder, timestampedFileName);
                    }
                    
                    Console.WriteLine($"Archiving file to: {archiveFilePath}");
                    File.Move(filePath, archiveFilePath);
                }
                else
                {
                    Console.WriteLine("No archive directory specified in configuration.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file {filePath}: {ex.Message}");
            }
        }
        
        private void PrintFile(string filePath, string printerName)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"File not found: {filePath}");
                    return;
                }

                if (Path.GetExtension(filePath).ToLower() == ".pdf")
                {
                    #if WINDOWS
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        PrintPdfWindows(filePath, printerName);
                    }
                    else
                    {
                        PrintPdfLinux(filePath, printerName);
                    }
                    #else
                    PrintPdfLinux(filePath, printerName);
                    #endif
                }
                else
                {
                    // For non-PDF files, use the existing print command
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "print",
                        Arguments = $"\"{filePath}\" \"{printerName}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(startInfo))
                    {
                        if (process != null)
                        {
                            process.WaitForExit();
                            Console.WriteLine($"File {filePath} sent to printer {printerName}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error printing file {filePath}: {ex.Message}");
            }
        }

        private void PrintPdfLinux(string filePath, string printerName)
        {
            try
            {
                // On Linux, we can use lpr command to print PDF files directly
                var startInfo = new ProcessStartInfo
                {
                    FileName = "lpr",
                    Arguments = $"-P \"{printerName}\" \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit();
                        if (process.ExitCode == 0)
                        {
                            Console.WriteLine($"PDF file {filePath} sent to printer {printerName}");
                        }
                        else
                        {
                            var error = process.StandardError.ReadToEnd();
                            Console.WriteLine($"Error printing PDF file: {error}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error printing PDF file on Linux: {ex.Message}");
            }
        }

        #if WINDOWS
        [SupportedOSPlatform("windows")]
        private void PrintPdfWindows(string filePath, string printerName)
        {
            using (var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly))
            {
                var printDocument = new PrintDocument();
                printDocument.PrinterSettings.PrinterName = printerName;
                
                if (!printDocument.PrinterSettings.IsValid)
                {
                    Console.WriteLine($"Printer {printerName} is not valid.");
                    return;
                }

                // Wait a moment to ensure the file is fully accessible
                Task.Delay(1000).Wait();

                try
                {
                    printDocument.Print();
                    Console.WriteLine($"PDF file {filePath} sent to printer {printerName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error printing PDF file {filePath}: {ex.Message}");
                }
            }
        }
        #endif
    }
}
