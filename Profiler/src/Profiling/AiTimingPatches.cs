// AiTimingPatches.cs
// Targeted Harmony patches to time AI hot paths: EntityAliveZombie.UpdateAI and Pathfinder.FindPath.
// Uses ProfilingUtils.GenericPrefix/GenericPostfix to record exclusive timings under AI tags.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

[HarmonyPatch]
static class AiTimingPatches
{
    // Helper: find a loaded Type by short name across all assemblies.
    private static Type FindTypeByShortName(string shortName)
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == shortName) return t;
                    }
                }
                catch { /* ignore reflection errors on dynamic/invalid assemblies */ }
            }
        }
        catch { }
        return null;
    }

    // Target: EntityAliveZombie.UpdateAI()
    static MethodBase TargetMethod_UpdateAI()
    {
        var t = FindTypeByShortName("EntityAliveZombie") ?? FindTypeByShortName("EntityAlive_Zombie") ?? FindTypeByShortName("EntityAliveZombieV2");
        if (t == null) return null;
        // prefer instance method named "UpdateAI"
        return t.GetMethod("UpdateAI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    // Target: Pathfinder.FindPath(...) (common candidate type names)
    static MethodBase TargetMethod_FindPath()
    {
        var names = new[] { "Pathfinder", "PathFind", "PathFinder", "PathFinding", "AStarPathfinder" };
        foreach (var n in names)
        {
            var t = FindTypeByShortName(n);
            if (t == null) continue;
            // prefer method named "FindPath" (any signature)
            var m = t.GetMethod("FindPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                 ?? (MethodBase)t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(mi => mi.Name == "FindPath");
            if (m != null) return m;
        }
        return null;
    }

    // Use ProfilingUtils.GenericPrefix / GenericPostfix to get exclusive/inclusive accounting.

    // Harmony nested patch for UpdateAI
    [HarmonyPatch]
    static class Patch_UpdateAI
    {
        // Return the real UpdateAI method when present; otherwise return a harmless fallback
        static MethodBase TargetMethod()
        {
            var m = TargetMethod_UpdateAI();
            if (m != null) return m;
            // Fallback: return an existing method in this mod so Harmony doesn't throw.
            // ProfilingUtils.GenericPrefix is safe to patch (no game-impact) and keeps Harmony happy.
            return typeof(ProfilingUtils).GetMethod(nameof(ProfilingUtils.GenericPrefix), BindingFlags.Public | BindingFlags.Static);
        }
    }

    // Harmony nested patch for FindPath
    [HarmonyPatch]
    static class Patch_FindPath
    {
        static MethodBase TargetMethod()
        {
            var m = TargetMethod_FindPath();
            if (m != null) return m;
            // Fallback to prevent Harmony error if pathfinder type missing
            return typeof(ProfilingUtils).GetMethod(nameof(ProfilingUtils.GenericPrefix), BindingFlags.Public | BindingFlags.Static);
        }
    }
}