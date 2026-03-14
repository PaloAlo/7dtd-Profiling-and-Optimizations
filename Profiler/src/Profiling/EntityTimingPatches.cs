// EntityTimingPatches.cs:
// Lightweight timing patches for EntityAlive hot paths not already covered.
// Records exclusive timings via ProfilingUtils.BeginSample/EndSample and increments simple per-frame counters.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;


/// <summary>
/// Lightweight timing patches for EntityAlive hot paths not already covered.
/// Records exclusive timings via ProfilingUtils.BeginSample/EndSample and increments simple per-frame counters.
/// </summary>
[HarmonyPatch]
static class EntityTimingPatches
{
    // Helper: find Assembly-CSharp type by short name
    private static Type FindType(string shortName)
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));
        if (asm == null) return null;
        try { return asm.GetType(shortName) ?? asm.GetTypes().FirstOrDefault(t => t.Name == shortName); } catch { return null; }
    }

    // Target: EntityAlive.MoveEntityHeaded
    [HarmonyPatch]
    static class Patch_MoveEntityHeaded
    {
        static MethodBase TargetMethod()
        {
            var t = FindType("EntityAlive");
            if (t == null) return null;
            return t.GetMethod("MoveEntityHeaded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        static void Prefix(out long __state)
        {
            try
            {
                ProfilingUtils.PerFrameCounters.Increment("MoveEntityHeaded.calls");
                __state = ProfilingUtils.BeginSample();
            }
            catch { __state = 0; }
        }

        static void Postfix(long __state)
        {
            try
            {
                if (__state != 0) ProfilingUtils.EndSample("EntityAlive.MoveEntityHeaded", __state);
            }
            catch { }
        }
    }

    // Target: EntityAlive.updateSpeedForwardAndStrafe
    [HarmonyPatch]
    static class Patch_updateSpeedForwardAndStrafe
    {
        static MethodBase TargetMethod()
        {
            var t = FindType("EntityAlive");
            if (t == null) return null;
            // name seen in decompilation: "updateSpeedForwardAndStrafe"
            var m = t.GetMethod("updateSpeedForwardAndStrafe", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null) return m;
            // fallback: find any method with that name
            return t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(mi => mi.Name.Equals("updateSpeedForwardAndStrafe", StringComparison.Ordinal));
        }

        static void Prefix(out long __state)
        {
            try
            {
                ProfilingUtils.PerFrameCounters.Increment("updateSpeedForwardAndStrafe.calls");
                __state = ProfilingUtils.BeginSample();
            }
            catch { __state = 0; }
        }

        static void Postfix(long __state)
        {
            try
            {
                if (__state != 0) ProfilingUtils.EndSample("EntityAlive.updateSpeedForwardAndStrafe", __state);
            }
            catch { }
        }
    }
}