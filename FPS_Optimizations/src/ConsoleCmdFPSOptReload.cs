// ConsoleCmdFPSOptions.cs
//
// In-game console command to reload the FPS optimization JSON and print a changelog.
// Usage: fpsopt-reload

using System;
using System.Collections.Generic;

public class ConsoleCmdFPSOptShow : ConsoleCmdAbstract
{
    public override string[] getCommands() => new[] { "fpsopt-show", "fpsopt", "fpsopt-show-config" };

    public override string getDescription() => "Show FPS Optimizations important toggles and values.";

    public override string getHelp() => "Usage: fpsopt-show\r\nPrints a compact summary of key optimization toggles and LOD values.";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        try
        {
            var cfg = OptimizationConfig.Current;
            if (cfg == null)
            {
                SingletonMonoBehaviour<SdtdConsole>.Instance.Output("FPSOptimizations: config not loaded.");
                return;
            }

            SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"FPSOptimizations v{cfg.Version}:");
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"  MoveLOD={cfg.EnableMoveLOD}  SpeedCurve={cfg.EnableSpeedCurveLOD}  SpeedMinMult={cfg.SpeedCurveMinMult}");
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"  TargetCache={cfg.EnableTargetCache}  PathCache={cfg.EnablePathCache}  PathCacheTTL={cfg.PathCacheTTLSeconds}s");
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"  SleeperThrottle={cfg.EnableSleeperVolumeThrottle}  VehicleRBSleep={cfg.EnableVehicleRigidbodySleep}  JiggleBoneOff={cfg.EnableJiggleBoneToggle}");
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"  ChunkBudget={cfg.EnableChunkCopyTimeBudget}  ChunkDirPriority={cfg.EnableChunkDirectionalPriority}");
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"  ThreadPoolConsolidation={cfg.EnableThreadPoolConsolidation}");
        }
        catch (Exception ex)
        {
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"FPSOptimizations: show failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

public class ConsoleCmdFPSOptReload : ConsoleCmdAbstract
{
    public override string[] getCommands() => new[] { "fpsopt-reload", "fpsopt-reload-config", "fps-reload" };

    public override string getDescription() => "Reload fps_optimization_config.json and apply runtime changes.";

    public override string getHelp() => "Usage: fpsopt-reload\r\nReloads the FPS optimizations JSON config and prints a changelog. Safe for most runtime toggles.";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        try
        {
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output("FPSOptimizations: Reloading config...");
            string result = OptimizationConfig.ReloadAndReport();

            if (string.IsNullOrEmpty(result))
            {
                SingletonMonoBehaviour<SdtdConsole>.Instance.Output("FPSOptimizations: Reload complete (no output).");
            }
            else
            {
                var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 1 && lines[0] == "No changes detected.")
                {
                    SingletonMonoBehaviour<SdtdConsole>.Instance.Output("FPSOptimizations: Reload complete. No changes detected.");
                }
                else if (lines.Length == 1 && lines[0].StartsWith("Reload failed") || lines[0].StartsWith("Config file not found"))
                {
                    SingletonMonoBehaviour<SdtdConsole>.Instance.Output("FPSOptimizations: " + lines[0]);
                }
                else
                {
                    SingletonMonoBehaviour<SdtdConsole>.Instance.Output("FPSOptimizations: Reload complete. Changes:");
                    foreach (var line in lines)
                    {
                        SingletonMonoBehaviour<SdtdConsole>.Instance.Output("  " + line);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"FPSOptimizations: Reload failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}