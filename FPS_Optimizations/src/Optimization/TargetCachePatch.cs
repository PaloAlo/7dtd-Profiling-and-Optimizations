// TargetCachePatch.cs
//
// Caches GetAttackTarget / GetRevengeTarget per entity per frame.
// These are called many times per frame from multiple patches and from
// IsCombatOrStateActive checks.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.GetAttackTarget))]
static class GetAttackTargetCachePatch
{
    private static readonly Dictionary<int, (int frame, EntityAlive target)> s_cache = new(256);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Prefix(EntityAlive __instance, ref EntityAlive __result)
    {
        if (!OptimizationConfig.Current.EnableTargetCache) return true;
        if (FrameCache.ShouldBypassThrottling) return true;

        int frame = Time.frameCount;
        int id = __instance.entityId;

        if (s_cache.TryGetValue(id, out var cached) && cached.frame == frame)
        {
            __result = cached.target;
            ProfilerCounterBridge.Increment("GetAttackTarget.CacheHit");
            return false;
        }

        return true;
    }

    public static void Postfix(EntityAlive __instance, EntityAlive __result)
    {
        if (!OptimizationConfig.Current.EnableTargetCache) return;
        s_cache[__instance.entityId] = (Time.frameCount, __result);
    }

    public static void OnEntityRemoved(int entityId) => s_cache.Remove(entityId);
}

[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.GetRevengeTarget))]
static class GetRevengeTargetCachePatch
{
    private static readonly Dictionary<int, (int frame, EntityAlive target)> s_cache = new(256);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Prefix(EntityAlive __instance, ref EntityAlive __result)
    {
        if (!OptimizationConfig.Current.EnableTargetCache) return true;
        if (FrameCache.ShouldBypassThrottling) return true;

        int frame = Time.frameCount;
        int id = __instance.entityId;

        if (s_cache.TryGetValue(id, out var cached) && cached.frame == frame)
        {
            __result = cached.target;
            ProfilerCounterBridge.Increment("GetRevengeTarget.CacheHit");
            return false;
        }

        return true;
    }

    public static void Postfix(EntityAlive __instance, EntityAlive __result)
    {
        if (!OptimizationConfig.Current.EnableTargetCache) return;
        s_cache[__instance.entityId] = (Time.frameCount, __result);
    }

    public static void OnEntityRemoved(int entityId) => s_cache.Remove(entityId);
}
