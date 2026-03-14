// LogUtil.cs
//
// Single, robust logging helper for mods. Set LogUtil.Prefix at startup per-mod.

using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

public class CommandSetVerbose : ConsoleCmdAbstract
{
    public override string[] getCommands() => new[] { "profiler-setverbose" };
    public override string getDescription() => "Toggle verbose logging for the profiler mod (usage: profiler-setverbose on/off)";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        if (_params == null || _params.Count == 0)
        {
            SdtdConsole.Instance.Output($"VerboseLogging is currently {(LogUtil.VerboseLogging ? "ON" : "OFF")}");
            return;
        }

        string arg = _params[0].ToLowerInvariant();
        if (arg == "on" || arg == "true" || arg == "1")
        {
            LogUtil.VerboseLogging = true;
            SdtdConsole.Instance.Output($"{LogUtil.Prefix} Verbose logging ENABLED");
        }
        else if (arg == "off" || arg == "false" || arg == "0")
        {
            LogUtil.VerboseLogging = false;
            SdtdConsole.Instance.Output($"{LogUtil.Prefix} Verbose logging DISABLED");
        }
        else
        {
            SdtdConsole.Instance.Output("Usage: profiler-setverbose on/off");
        }
    }
}

internal static class LogUtil
{
    public static string Prefix { get; set; } = "[7dtd_Profiler]";

    // DISABLED by default - only enable via console command for diagnostics
    public static bool VerboseLogging { get; set; } = true;

    private static string ClassName(string callerFilePath)
    {
        if (!string.IsNullOrEmpty(callerFilePath))
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(callerFilePath);
                if (!string.IsNullOrEmpty(fileName))
                    return fileName;
            }
            catch { }
        }
        return "Unknown";
    }

    public static void Info(string message, [CallerFilePath] string callerFilePath = "")
    {
        Log.Out($"{Prefix} [{ClassName(callerFilePath)}] {message}");
    }

    public static void Warn(string message, [CallerFilePath] string callerFilePath = "")
    {
        Log.Warning($"{Prefix} [{ClassName(callerFilePath)}] {message}");
    }

    /// <summary>
    /// Debug logging - ONLY logs if VerboseLogging is enabled.
    /// Use sparingly in hot paths. Prefer not calling at all in per-frame code.
    /// </summary>
    public static void Debug(string message, [CallerFilePath] string callerFilePath = "")
    {
        // Early exit check BEFORE any string operations
        if (!VerboseLogging) return;

        Log.Out($"{Prefix} [{ClassName(callerFilePath)}] {message}");
    }
}