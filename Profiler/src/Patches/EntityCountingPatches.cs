// EntityCountingPatches.cs
// Lightweight Harmony prefixes that increment counters for entity update calls.
// Safe: no postfixs, no behavior changes.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

[HarmonyPatch]
static class EntityCountingPatches
{
    // Target Entity.Update
    static MethodBase TargetMethod_Entity_Update()
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
        {
            try { return string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });
        if (asm == null) return null;
        var t = asm.GetType("Entity") ?? asm.GetType("Entity, Assembly-CSharp");
        return t?.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    static void Prefix_Entity_Update()
    {
        try { ProfilingUtils.PerFrameCounters.Increment("Entity.Update.calls", 1); }
        catch { }
    }

    // Target EntityAlive.Update
    static MethodBase TargetMethod_EntityAlive_Update()
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
        {
            try { return string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });
        if (asm == null) return null;
        var t = asm.GetType("EntityAlive") ?? asm.GetType("EntityAlive, Assembly-CSharp");
        return t?.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    static void Prefix_EntityAlive_Update()
    {
        try { ProfilingUtils.PerFrameCounters.Increment("EntityAlive.Update.calls", 1); }
        catch { }
    }
}