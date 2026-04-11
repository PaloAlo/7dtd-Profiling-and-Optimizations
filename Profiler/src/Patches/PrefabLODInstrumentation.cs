// PrefabLODInstrumentation.cs
//
// Lightweight instrumentation for PrefabLODManager.FrameUpdate()
// - records timing (ms) into profiler timing entries (appears in periodic CSV)
// - records call count into profiler per-frame counters
// - guarded by ProfilerCounterBridge availability to be zero-cost when profiler absent
//
// Only instrumentation (no throttling) as requested.

using System.Diagnostics;
using HarmonyLib;

[HarmonyPatch(typeof(PrefabLODManager), "FrameUpdate")]
public static class PrefabLODInstrumentation
{
    [System.ThreadStatic]
    private static long t_startTimestamp;

    public static void Prefix()
    {
        // Feature-guard so this is effectively zero-cost when disabled
        if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation)
        {
            t_startTimestamp = 0;
            return;
        }

        // Count the call (aggregates for high-load dumps)
        ProfilingUtils.PerFrameCounters.Increment("PrefabLOD.FrameUpdate.calls");

        // Start the profiler sample (no allocations)
        t_startTimestamp = ProfilingUtils.BeginSample();
    }

    public static void Postfix()
    {
        if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) return;
        if (t_startTimestamp == 0) return;

        // Finish the sample and record the timing under a descriptive tag
        ProfilingUtils.EndSample("PrefabLOD.FrameUpdate", t_startTimestamp);

        // Clear thread-local state
        t_startTimestamp = 0;
    }
}