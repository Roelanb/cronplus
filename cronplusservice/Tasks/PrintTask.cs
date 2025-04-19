using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PdfSharp.Pdf.IO;
using CronPlus.Models;
#if WINDOWS
using System.Drawing;
using System.Drawing.Printing;
#endif

namespace CronPlus.Tasks;

/// <summary>
/// Task for printing files and optionally archiving them
/// </summary>
public class PrintTask : BaseTask
{
    public PrintTask(TaskConfig config) : base(config)
    {
    }

    public override void Execute(string filePath)
    {
        try
        {
            // Wait a moment to ensure the file is fully written
            Task.Delay(1000).Wait();

            // Print the file
            if (!string.IsNullOrEmpty(_config.PrinterName))
            {
                Console.WriteLine($"Printing file: {filePath} to printer: {_config.PrinterName}");
                PrintFile(filePath, _config.PrinterName);
            }
            else
            {
                Console.WriteLine("No printer specified in configuration.");
            }

            // Archive the file to a monthly folder
            if (!string.IsNullOrEmpty(_config.ArchiveDirectory))
            {
                string monthlyFolder = Path.Combine(
                    _config.ArchiveDirectory,
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
