using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
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
            Console.WriteLine("CronPlus started. Press any key to exit.");

            List<TaskConfig> configs = LoadConfig("Config.json");

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
                // Ensure the file exists before attempting to print
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }
                
                // Check the operating system and use the appropriate printing method
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    PrintFileWindows(filePath, printerName);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    PrintFileLinux(filePath, printerName);
                }
                else
                {
                    throw new PlatformNotSupportedException("Printing is only supported on Windows and Linux.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error printing file {filePath}: {ex.Message}");
                throw;
            }
        }
        
        private void PrintFileLinux(string filePath, string printerName)
        {
            // Use CUPS printing system via lp command on Linux
            // Properly quote the file path to handle spaces in filenames
            Process process = new Process();
            process.StartInfo.FileName = "lp";
            process.StartInfo.Arguments = $"-d {printerName} \"{filePath}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            
            Console.WriteLine($"Executing Linux print command: lp -d {printerName} \"{filePath}\"");
            process.Start();
            
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            
            process.WaitForExit();
            
            if (process.ExitCode != 0)
            {
                Console.WriteLine($"Print command error output: {error}");
                throw new Exception($"Printing failed with exit code {process.ExitCode}");
            }
            
            Console.WriteLine($"Print job submitted successfully: {output}");
        }
        
        private void PrintFileWindows(string filePath, string printerName)
        {
            // For Windows, we'll use a combination of approaches depending on file type
            string extension = Path.GetExtension(filePath).ToLower();
            
            // For most file types, use the ShellExecute approach
            Process process = new Process();
            process.StartInfo.FileName = filePath;
            process.StartInfo.Verb = "print";
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.CreateNoWindow = true;
            
            if (!string.IsNullOrEmpty(printerName))
            {
                // Set the default printer for the process
                SetDefaultPrinter(printerName);
            }
            
            Console.WriteLine($"Executing Windows print command for file: {filePath} to printer: {printerName}");
            process.Start();
            
            // Wait a bit to allow the print job to be submitted
            Task.Delay(2000).Wait();
            
            Console.WriteLine("Print job submitted to Windows print spooler.");
        }
        
        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDefaultPrinter(string printerName);
    }
}
