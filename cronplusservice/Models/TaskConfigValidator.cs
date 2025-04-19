using System;
using System.Collections.Generic;
using System.IO;
#if WINDOWS
using System.Drawing.Printing;
#endif

namespace CronPlus.Models;

public static class TaskConfigValidator
{
    public static List<string> Validate(TaskConfig config)
    {
        var errors = new List<string>();
        // Directory exists
        if (string.IsNullOrWhiteSpace(config.Directory) || !Directory.Exists(config.Directory))
            errors.Add($"Directory does not exist: {config.Directory}");

        // Archive directory's parent exists
        if (!string.IsNullOrWhiteSpace(config.ArchiveDirectory))
        {
            var parent = Path.GetDirectoryName(config.ArchiveDirectory);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
                errors.Add($"Archive directory parent does not exist: {parent}");
        }

        // Printer exists (Windows only)
        #if WINDOWS
        if (!string.IsNullOrWhiteSpace(config.PrinterName))
        {
            var installedPrinters = PrinterSettings.InstalledPrinters;
            bool found = false;
            foreach (string printer in installedPrinters)
            {
                if (string.Equals(printer, config.PrinterName, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                errors.Add($"Printer does not exist: {config.PrinterName}");
        }
        #endif

        // TriggerType-specific checks
        switch (config.TriggerType)
        {
            case TriggerType.Time:
                if (string.IsNullOrWhiteSpace(config.Time))
                    errors.Add("Time must be set for TriggerType.Time");
                break;
            case TriggerType.Interval:
                if (config.Interval <= 0)
                    errors.Add("Interval must be positive for TriggerType.Interval");
                break;
        }
        return errors;
    }
}
