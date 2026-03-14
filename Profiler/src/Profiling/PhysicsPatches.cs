// PhysicsPatches.cs
// Patch UnityEngine.Physics query methods (Raycast / Overlap / Cast) to increment per-frame counters.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

public static class PhysicsPatches
{
    public static void Apply(Harmony harmony)
    {
        if (harmony == null) return;

        Type physicsType = typeof(UnityEngine.Physics);
        if (physicsType == null) return;

        var names = new[]
        {
            "Raycast", "RaycastAll", "RaycastNonAlloc",
            "SphereCast", "SphereCastAll", "SphereCastNonAlloc",
            "CapsuleCast", "CapsuleCastAll",
            "BoxCast", "BoxCastAll",
            "OverlapSphere", "OverlapSphereNonAlloc",
            "OverlapBox", "OverlapCapsule"
        };

        var allMethods = physicsType.GetMethods(BindingFlags.Public | BindingFlags.Static);
        var prefix = new HarmonyMethod(typeof(PhysicsPatches).GetMethod(nameof(PhysicsPrefix), BindingFlags.Static | BindingFlags.NonPublic));

        foreach (var m in allMethods.Where(m => names.Contains(m.Name)))
        {
            try
            {
                harmony.Patch(m, prefix, null);
                LogUtil.Debug($"PhysicsPatches: patched UnityEngine.Physics.{m.Name} (params={m.GetParameters().Length})");
            }
            catch (Exception ex)
            {
                LogUtil.Warn($"PhysicsPatches: failed to patch {m.Name}: {ex.Message}");
            }
        }
    }

    // Fixed: use __originalMethod so Harmony can bind MethodBase successfully
    static void PhysicsPrefix(MethodBase __originalMethod)
    {
        try
        {
            var name = (__originalMethod?.Name ?? "").ToLowerInvariant();
            if (name.Contains("raycast"))
                ProfilingUtils.PerFrameCounters.Increment("raycasts");
            else if (name.Contains("overlap"))
                ProfilingUtils.PerFrameCounters.Increment("overlaps");
            else if (name.Contains("cast"))
                ProfilingUtils.PerFrameCounters.Increment("casts");
            else
                ProfilingUtils.PerFrameCounters.Increment("physics_queries");
        }
        catch
        {
            // swallow to avoid affecting game behavior
        }
    }
}