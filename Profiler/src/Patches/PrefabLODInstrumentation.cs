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
        if (!ProfilerCounterBridge.IsProfilerAvailable) return;
        ProfilerCounterBridge.Increment("PrefabLOD.FrameUpdate.calls");
        t_startTimestamp = Stopwatch.GetTimestamp();
    }

    public static void Postfix()
    {
        if (!ProfilerCounterBridge.IsProfilerAvailable) return;
        long elapsed = Stopwatch.GetTimestamp() - t_startTimestamp;
        double ms = elapsed * 1000.0 / Stopwatch.Frequency;
        if (ms > 0.001)
        {
            ProfilerCounterBridge.RecordTiming("PrefabLOD.FrameUpdate", ms);
        }
    }
}