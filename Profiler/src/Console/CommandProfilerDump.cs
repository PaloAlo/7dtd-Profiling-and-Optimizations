// ConsoleProfileCommands.cs
// On-demand commands to dump Assembly types and to save the current profile CSV (detailed and summary).
using System;
using System.Collections.Generic;

public class CommandProfilerDump : ConsoleCmdAbstract
{
    public override string[] getCommands() => new[] { "profiler-dump-assembly", "profiler-save-now" };

    public override string getDescription()
    {
        return "profiler-dump-assembly <assemblyName> <outFile>  OR  profiler-save-now <detailsFile>";
    }

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        if (_params == null || _params.Count == 0)
        {
            SdtdConsole.Instance.Output("Usage:");
            SdtdConsole.Instance.Output("  profiler-dump-assembly <assemblyName> <outFile>");
            SdtdConsole.Instance.Output("  profiler-save-now <detailsFile>");
            return;
        }

        var cmd = _params[0].ToLowerInvariant();
        try
        {
            if (cmd == "profiler-dump-assembly" && _params.Count >= 3)
            {
                var asmName = _params[1];
                var outFile = _params[2];
                ProfilingUtils.DumpAssemblyTypes(asmName, outFile);
                SdtdConsole.Instance.Output($"{LogUtil.Prefix} Dumped assembly '{asmName}' to {outFile}");
                return;
            }

            if (cmd == "profiler-save-now" && _params.Count >= 2)
            {
                var detailsFile = _params[1];
                try
                {
                    // Save current aggregated CSV snapshot
                    var path = ProfilingUtils.ResolveOutputPath(detailsFile);
                    ProfilingUtils.SaveCsv(path);
                    SdtdConsole.Instance.Output($"{LogUtil.Prefix} Saved profile details to {path}");
                }
                catch (Exception ex)
                {
                    SdtdConsole.Instance.Output($"{LogUtil.Prefix} Save failed: {ex.Message}");
                }
                return;
            }

            // Backward compatibility: user called single-word commands
            if (_params[0].Equals("profiler-save-now", StringComparison.OrdinalIgnoreCase))
            {
                var fname = $"7dtd_profile_details_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
                ProfilingUtils.SaveCsv(ProfilingUtils.ResolveOutputPath(fname));
                SdtdConsole.Instance.Output($"{LogUtil.Prefix} Saved profile details to {fname}");
                return;
            }

            SdtdConsole.Instance.Output("Invalid parameters. See usage.");
        }
        catch (Exception ex)
        {
            SdtdConsole.Instance.Output($"{LogUtil.Prefix} Command failed: {ex.Message}");
        }
    }
}