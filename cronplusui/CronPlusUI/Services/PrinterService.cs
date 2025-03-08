using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CronPlusUI.Services;

public class PrinterService
{
    /// <summary>
    /// Get available printers on the current platform
    /// </summary>
    /// <returns>List of printer names</returns>
    public async Task<List<string>> GetAvailablePrintersAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetWindowsPrinters();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return await GetLinuxPrintersAsync();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return await GetMacPrintersAsync();
        }
        
        return new List<string>();
    }
    
    /// <summary>
    /// Get the default printer on the current platform
    /// </summary>
    /// <returns>Name of the default printer, or null if none is available</returns>
    public async Task<string?> GetDefaultPrinterAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetWindowsDefaultPrinter();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return await GetLinuxDefaultPrinterAsync();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return await GetMacDefaultPrinterAsync();
        }
        
        return null;
    }
    
    // Windows printer methods
    private List<string> GetWindowsPrinters()
    {
        var printers = new List<string>();
        
#if WINDOWS
        foreach (string printer in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
        {
            printers.Add(printer);
        }
#endif
        
        return printers;
    }
    
    private string? GetWindowsDefaultPrinter()
    {
#if WINDOWS
        var settings = new System.Drawing.Printing.PrinterSettings();
        return settings.IsDefaultPrinter ? settings.PrinterName : null;
#else
        return null;
#endif
    }
    
    // Linux printer methods
    private async Task<List<string>> GetLinuxPrintersAsync()
    {
        var printers = new List<string>();
        
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "lpstat",
                Arguments = "-p",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return printers;
            }
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0)
            {
                using StringReader reader = new StringReader(output);
                string? line;
                
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("printer"))
                    {
                        // Extract printer name from "printer {name} is"
                        var parts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            printers.Add(parts[1]);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting Linux printers: {ex.Message}");
        }
        
        return printers;
    }
    
    private async Task<string?> GetLinuxDefaultPrinterAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "lpstat",
                Arguments = "-d",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                // Output format: "system default destination: printername"
                var parts = output.Split(":", StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    return parts[1].Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting Linux default printer: {ex.Message}");
        }
        
        return null;
    }
    
    // macOS printer methods (for completeness)
    private async Task<List<string>> GetMacPrintersAsync()
    {
        var printers = new List<string>();
        
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "lpstat",
                Arguments = "-p",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return printers;
            }
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0)
            {
                // Format is similar to Linux
                using StringReader reader = new StringReader(output);
                string? line;
                
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("printer"))
                    {
                        var parts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            printers.Add(parts[1]);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting macOS printers: {ex.Message}");
        }
        
        return printers;
    }
    
    private async Task<string?> GetMacDefaultPrinterAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "lpstat",
                Arguments = "-d",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var parts = output.Split(":", StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    return parts[1].Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting macOS default printer: {ex.Message}");
        }
        
        return null;
    }
}
