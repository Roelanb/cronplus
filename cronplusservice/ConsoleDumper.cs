using System;
using System.Collections.Generic;
using System.Linq;
using ConsoleTables;
using CronPlus.Models;

public static class ConsoleDumper
{
    public static void DumpTaskConfigsWithValidity(IEnumerable<TaskConfig> configs, Dictionary<TaskConfig, List<string>> validationResults)
    {
        var table = new ConsoleTable(
            "Directory",
            "TaskType",
            "TriggerType",
            "PrinterName",
            "ArchiveDirectory",
            "Valid",
            "Errors"
        );

        foreach (var config in configs)
        {
            validationResults.TryGetValue(config, out var errors);
            var isValid = errors == null || errors.Count == 0;
            table.AddRow(
                config.Directory,
                config.TaskType,
                config.TriggerType,
                config.PrinterName ?? "-",
                config.ArchiveDirectory ?? "-",
                isValid ? "Yes" : "No",
                isValid ? "-" : string.Join("; ", errors?.Where(e => !string.IsNullOrWhiteSpace(e)) ?? Array.Empty<string>())
            );
        }

        table.Write(Format.Alternative);
    }
}
