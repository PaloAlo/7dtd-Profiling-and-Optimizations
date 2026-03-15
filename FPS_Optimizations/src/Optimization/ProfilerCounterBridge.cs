// ProfilerCounterBridge.cs
//
// Reflection-based bridge to the profiler mod's PerFrameCounters.
// Allows the optimization mod to report skip/cache-hit counts without
// a compile-time reference to the Profiler assembly.
// If the profiler mod is not loaded, all calls are no-ops.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;

public static class ProfilerCounterBridge
{
    private static Action<string, long> s_increment;
    private static bool s_resolved;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Increment(string key, long amount = 1)
    {
        if (!s_resolved) Resolve();
        s_increment?.Invoke(key, amount);
    }

    private static void Resolve()
    {
        s_resolved = true;
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

            if (profilingType == null) return;

            var nested = profilingType.GetNestedType("PerFrameCounters",
                BindingFlags.Public | BindingFlags.Static);
            if (nested == null) return;

            var method = nested.GetMethod("Increment",
                BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(long) }, null);
            if (method == null) return;

            s_increment = (Action<string, long>)Delegate.CreateDelegate(
                typeof(Action<string, long>), method);
        }
        catch (Exception ex)
        {
            Log.Warning($"[FPSOptimizations] ProfilerCounterBridge resolve failed: {ex.Message}");
        }
    }
}
