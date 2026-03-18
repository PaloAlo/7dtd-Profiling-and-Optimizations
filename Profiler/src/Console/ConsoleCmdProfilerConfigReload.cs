// ConsoleCmdProfilerConfigReload.cs

using System;
using System.Collections.Generic;

// Console command to reload profiler config at runtime and apply side-effects
// Usage in-game (SdtdConsole): profiler-reload
public class ConsoleCmdProfilerConfigReload : ConsoleCmdAbstract
{
    public override string[] getCommands() => new[] { "profiler-reload", "pr-reload" };

    public override string getDescription() => "Reload profiler_config.json and apply runtime changes.";

    public override string getHelp() => "Usage: profiler-reload\r\nReloads the profiler JSON config and applies live changes.\r\nNo restart required for most settings.";
    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        try
        {
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Profiler: Reloading config...");
            ProfilerConfig.Load();

            var cfg = ProfilerConfig.Current;
            if (cfg == null)
            {
                SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Profiler: Failed to load config (null).");
                return;
            }

            SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Profiler: EnableProfiling={cfg.EnableProfiling}");

            if (cfg.EnableDeepPhysicsInstrumentation)
                SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Profiler: Deep physics instrumentation ENABLED.");
            else
                SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Profiler: Deep physics instrumentation DISABLED.");

            if (cfg.EnableDeepEntityInstrumentation)
                SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Profiler: Deep entity instrumentation ENABLED.");
            else
                SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Profiler: Deep entity instrumentation DISABLED.");

            SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Profiler: Reload complete.");
        }
        catch (Exception ex)
        {
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Profiler: Reload failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}