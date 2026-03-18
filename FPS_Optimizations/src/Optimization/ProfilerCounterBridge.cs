// ProfilerCounterBridge.cs
//
// Reflection-based bridge to the profiler mod's PerFrameCounters.
// Allows the optimization mod to report skip/cache-hit counts without
// a compile-time reference to the Profiler assembly.
// If the profiler mod is not loaded, all calls are no-ops.
//
// NOTE: Resolution is deferred — if the profiler assembly isn't loaded
// yet (mod load order), we keep retrying for up to MaxResolveAttempts
// so alphabetical mod folder ordering doesn't permanently kill the bridge.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;

public static class ProfilerCounterBridge
{
    private static Action<string, long> s_increment;
    private static bool s_resolved;
    private static int s_resolveAttempts;
    private const int MaxResolveAttempts = 20;

    public static bool IsProfilerAvailable
    {
        get
        {
            if (!s_resolved) Resolve();
            return s_increment != null;
        }
    }

    public static void EnsureResolved()
    {
        if (!s_resolved) Resolve();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Increment(string key, long amount = 1)
    {
        if (!s_resolved) Resolve();
        s_increment?.Invoke(key, amount);
    }

    private static void Resolve()
    {
        try
        {
            Type profilingType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.GetName().Name == "Profiler")
                    {
                        profilingType = asm.GetType("ProfilingUtils");
                        break;
                    }
                }
                catch { }
            }

            if (profilingType == null)
            {
                // Profiler assembly not loaded yet — retry later unless we've
                // exhausted attempts (profiler genuinely not installed).
                s_resolveAttempts++;
                if (s_resolveAttempts >= MaxResolveAttempts)
                {
                    s_resolved = true;
                    Log.Out("[FPSOptimizations] ProfilerCounterBridge: profiler not found after "
                          + MaxResolveAttempts + " attempts — counters disabled.");
                }
                return;
            }

            var nested = profilingType.GetNestedType("PerFrameCounters",
                BindingFlags.Public | BindingFlags.Static);
            if (nested == null)
            {
                s_resolved = true;
                Log.Warning("[FPSOptimizations] ProfilerCounterBridge: PerFrameCounters type not found.");
                return;
            }

            var method = nested.GetMethod("Increment",
                BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(long) }, null);
            if (method == null)
            {
                s_resolved = true;
                Log.Warning("[FPSOptimizations] ProfilerCounterBridge: Increment method not found.");
                return;
            }

            s_increment = (Action<string, long>)Delegate.CreateDelegate(
                typeof(Action<string, long>), method);
            s_resolved = true;
            Log.Out("[FPSOptimizations] ProfilerCounterBridge: connected to profiler successfully.");
        }
        catch (Exception ex)
        {
            s_resolved = true;
            Log.Warning($"[FPSOptimizations] ProfilerCounterBridge resolve failed: {ex.Message}");
        }
    }
}
