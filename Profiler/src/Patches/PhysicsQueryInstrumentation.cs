// PhysicsQueryInstrumentation.cs
//
// Instrumentation for per-entity physics query methods to measure their
// cumulative cost and identify optimization candidates.
//
// Measured methods:
//   - EntityMoveHelper.CheckWorldBlocked  — Voxel.Raycast every 4 ticks per entity
//   - EntityMoveHelper.CheckEntityBlocked — entity proximity checks every 4 ticks
//   - World.GetEntitiesInBounds           — spatial query used by AI, ThreatLevel, etc.
//   - Voxel.Raycast                       — core physics raycast
//
// All timing goes to ProfilerCounterBridge.RecordTiming so it appears in
// the periodic CSV top-methods list alongside other timing entries.
// Call counts go to PerFrameCounters for highload dump analysis.
//
// Guarded by ProfilerCounterBridge.IsProfilerAvailable — zero cost when
// the profiler mod is not installed.
//
// NOTE: Uses attribute-based Harmony patches for methods with unique
// signatures, and deferred runtime patching for overloaded methods
// (GetEntitiesInBounds, Voxel.Raycast) to avoid ambiguous match failures.

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;

/// <summary>
/// Instruments EntityMoveHelper.CheckWorldBlocked — does Voxel.Raycast calls
/// to detect block obstructions.  Runs every 4 ticks per entity.
/// </summary>
[HarmonyPatch(typeof(EntityMoveHelper), nameof(EntityMoveHelper.CheckWorldBlocked))]
public static class CheckWorldBlockedInstrumentation
{
    [System.ThreadStatic]
    private static long t_start;

    public static void Prefix()
    {
        if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) return;
        t_start = ProfilingUtils.BeginSample();
    }

    public static void Postfix()
    {
        if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) return;
        if (t_start == 0) return;
        ProfilingUtils.EndSample("MoveHelper.CheckWorldBlocked", t_start);
        ProfilingUtils.PerFrameCounters.Increment("MoveHelper.CheckWorldBlocked.calls");
        t_start = 0;
    }
}

/// <summary>
/// Instruments EntityMoveHelper.CheckEntityBlocked — checks proximity to
/// other entities to determine if movement is blocked.
/// </summary>
[HarmonyPatch(typeof(EntityMoveHelper), nameof(EntityMoveHelper.CheckEntityBlocked))]
public static class CheckEntityBlockedInstrumentation
{
    [System.ThreadStatic]
    private static long t_start;

    public static void Prefix()
    {
        if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) return;
        t_start = ProfilingUtils.BeginSample();
    }

    public static void Postfix()
    {
        if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) return;
        if (t_start == 0) return;
        ProfilingUtils.EndSample("MoveHelper.CheckEntityBlocked", t_start);
        ProfilingUtils.PerFrameCounters.Increment("MoveHelper.CheckEntityBlocked.calls");
        t_start = 0;
    }
}

/// <summary>
/// Instruments EntityMoveHelper.CheckBlocked — per-height-layer raycast
/// for obstacle detection.
/// </summary>
[HarmonyPatch(typeof(EntityMoveHelper), nameof(EntityMoveHelper.CheckBlocked))]
public static class CheckBlockedInstrumentation
{
    [System.ThreadStatic]
    private static long t_start;

    public static void Prefix()
    {
        if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) return;
        t_start = ProfilingUtils.BeginSample();
    }

    public static void Postfix()
    {
        if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) return;
        if (t_start == 0) return;
        ProfilingUtils.EndSample("MoveHelper.CheckBlocked", t_start);
        ProfilingUtils.PerFrameCounters.Increment("MoveHelper.CheckBlocked.calls");
        t_start = 0;
    }
}

/// <summary>
/// Deferred runtime patches for overloaded methods that can't use
/// attribute-based patching (would cause ambiguous match).
/// Called from ModInit after Harmony.PatchAll().
/// </summary>
public static class PhysicsQueryDeferredPatches
{
    private static bool _applied;

    /// <summary>
    /// Apply deferred patches for overloaded physics methods.
    /// Safe to call multiple times — applies only once.
    /// </summary>
    public static void Apply(Harmony harmony)
    {
        if (_applied || harmony == null) return;
        _applied = true;

        if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) return;

        var prefix = new HarmonyMethod(typeof(PhysicsQueryDeferredPatches)
            .GetMethod(nameof(TimingPrefix), BindingFlags.Public | BindingFlags.Static));
        var postfix = new HarmonyMethod(typeof(PhysicsQueryDeferredPatches)
            .GetMethod(nameof(TimingPostfix), BindingFlags.Public | BindingFlags.Static));

        // World.GetEntitiesInBounds — patch ALL overloads to capture total cost
        PatchAllOverloads(harmony, typeof(World), "GetEntitiesInBounds",
            "World.GetEntitiesInBounds", prefix, postfix);

        // Voxel.Raycast — patch ALL overloads
        PatchAllOverloads(harmony, typeof(Voxel), "Raycast",
            "Voxel.Raycast", prefix, postfix);
    }

    private static void PatchAllOverloads(Harmony harmony, Type type, string methodName,
        string tag, HarmonyMethod prefix, HarmonyMethod postfix)
    {
        try
        {
            var methods = type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly)
                .Where(m => m.Name == methodName && !m.IsAbstract && !m.ContainsGenericParameters)
                .ToArray();

            int count = 0;
            foreach (var m in methods)
            {
                try
                {
                    _currentTag = tag;
                    harmony.Patch(m, prefix, postfix);
                    count++;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[FPSOptimizations] PhysicsQuery: failed to patch {type.Name}.{methodName} overload: {ex.Message}");
                }
            }

            if (count > 0)
                Log.Out($"[FPSOptimizations] PhysicsQuery: patched {count} overload(s) of {type.Name}.{methodName}");
            else
                Log.Warning($"[FPSOptimizations] PhysicsQuery: no patchable overloads found for {type.Name}.{methodName}");
        }
        catch (Exception ex)
        {
            Log.Warning($"[FPSOptimizations] PhysicsQuery: error patching {type.Name}.{methodName}: {ex.Message}");
        }
    }

    // The tag for the current batch of overload patches.
    // Set before harmony.Patch() so the prefix/postfix know what to record as.
    // This is safe because PatchAll runs synchronously on the main thread.
    private static string _currentTag = "";

    // Per-thread timing storage keyed by call depth to handle nesting
    [ThreadStatic] private static long t_start;
    [ThreadStatic] private static int t_depth;

    public static void TimingPrefix(MethodBase __originalMethod, out string __state)
    {
        __state = null;
        if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) { __state = null; return; }

        // Derive tag from the method's declaring type and name
        string tag = $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}";
        __state = tag;

        // Maintain nesting depth and only start timing at outermost call
        t_depth++;
        if (t_depth == 1)
        {
            t_start = ProfilingUtils.BeginSample();
        }
    }

    public static void TimingPostfix(string __state)
    {
        if (__state == null) return;

        t_depth--;
        if (t_depth == 0)
        {
            // Counters are recorded via ProfilingUtils.PerFrameCounters (below).
            if (t_start == 0) return;
            ProfilingUtils.EndSample(__state, t_start);
            ProfilingUtils.PerFrameCounters.Increment(__state + ".calls");
            t_start = 0;
        }
    }
}
