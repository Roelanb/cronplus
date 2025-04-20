using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PdfSharp.Pdf.IO;
using System.Drawing.Printing;
using CronPlus.Models;
using CronPlus.Helpers;
using CronPlus.Storage;

namespace CronPlus.Tasks;

/// <summary>
/// Task for printing files and optionally archiving them
/// </summary>
public class PrintTask : BaseTask
{
    public PrintTask(TaskConfig config) : base(config)
    {
    }

    public override async Task Execute(string filePath, DataStore dataStore)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Wait a moment to ensure the file is fully written
            await Task.Delay(1000);

            Console.WriteLine($"Executing task: {TaskType} for file: {filePath}");

            // Print the file
            if (!string.IsNullOrEmpty(_config.printerName))
            {
                Console.WriteLine($"Printing file: {filePath} to printer: {_config.printerName}");
                await PrintFile(filePath, _config.printerName);
                Console.WriteLine("File printed successfully");
            }
            else
            {
                Console.WriteLine("No printer configured for print task");
            }

            // Archive the file to a monthly folder
            string archivedPath = string.Empty;
            if (!string.IsNullOrEmpty(_config.archiveDirectory))
            {
                string monthlyFolder = Path.Combine(
                    _config.archiveDirectory,
                    DateTime.Now.ToString("yyyy-MM")
                );

                if (!Directory.Exists(monthlyFolder))
                    Directory.CreateDirectory(monthlyFolder);

                string destinationPath = FilenameHelper.TranslateFilename(
                    filePath, 
                    "*.*", 
                    monthlyFolder
                );
                
                Console.WriteLine($"Archiving file from {filePath} to {destinationPath}");
                File.Move(filePath, destinationPath);
                Console.WriteLine("File archived successfully");
                archivedPath = destinationPath;
            }

            // Log the successful execution
            var log = new TaskLogging
            {
                TaskType = TaskType,
                TriggerType = _config.triggerType,
                FilePath = filePath,
                PrinterName = _config.printerName,
                ArchiveDirectory = _config.archiveDirectory,
                Result = !string.IsNullOrEmpty(_config.archiveDirectory) ? $"File printed and archived to {archivedPath}" : "File printed",
                TriggeredAt = DateTime.UtcNow,
                Duration = stopwatch.Elapsed
            };
            await dataStore.SaveTaskLog(log);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error printing file: {ex.Message}");

            // Log the error
            var log = new TaskLogging
            {
                TaskType = TaskType,
                TriggerType = _config.triggerType,
                FilePath = filePath,
                PrinterName = _config.printerName,
                ArchiveDirectory = _config.archiveDirectory,
                Result = $"Error: {ex.Message}",
                TriggeredAt = DateTime.UtcNow,
                Duration = stopwatch.Elapsed
            };
            await dataStore.SaveTaskLog(log);
        }
    }

    protected override string TaskType => "Print";

    private async Task PrintFile(string filePath, string printerName)
    {
        var stopwatch = Stopwatch.StartNew();
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
                    await PrintPdfWindows(filePath, printerName);
                }
                else
                {
                    await PrintPdfLinux(filePath, printerName);
                }
#else
                await PrintPdfLinux(filePath, printerName);
#endif
            }
            else
            {
                // For non-PDF files, we'll need a different approach
                var startInfo = new ProcessStartInfo
                {
                    FileName = "print",
                    Arguments = $"/D:{printerName} \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        Console.WriteLine($"File {filePath} sent to printer {printerName}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error printing file {filePath}: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task PrintPdfWindows(string filePath, string printerName)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using (var document = PdfReader.Open(filePath, PdfDocumentOpenMode.Import))
            {
                var printDocument = new PrintDocument();
                printDocument.PrinterSettings.PrinterName = printerName;
                
                if (!printDocument.PrinterSettings.IsValid)
                {
                    Console.WriteLine($"Printer {printerName} is not valid.");
                    return;
                }

                // Wait a moment to ensure the file is fully accessible
                await Task.Delay(1000);

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
        catch (Exception ex)
        {
            Console.WriteLine($"Error printing PDF file on Windows: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    private async Task PrintPdfLinux(string filePath, string printerName)
    {
        var stopwatch = Stopwatch.StartNew();
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
                    await process.WaitForExitAsync();
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
        finally
        {
            stopwatch.Stop();
        }
    }
}
