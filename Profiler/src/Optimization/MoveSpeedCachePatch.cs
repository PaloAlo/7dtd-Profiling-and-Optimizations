// MoveSpeedCachePatch.cs
//
// Caches EntityAlive.GetSpeedModifier() results for zombies.
// Each call invokes EffectManager.GetValue(PassiveEffects.RunSpeed) which
// iterates EntityClass.Effects, equipment slots, and Buffs.ModifyValue.
// With 80 zombies × 20 ticks/sec = 1,600+ calls/sec — but speed values
// only change when buffs are added/removed (rare).
//
// Cache strategy: store result per entity for 20 ticks (~1 second).
// EntityPlayer overrides GetSpeedModifier() independently, so this patch
// on EntityAlive.GetSpeedModifier won't affect players.

using System.Collections.Generic;
using HarmonyLib;

[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.GetSpeedModifier))]
public static class MoveSpeedCachePatch
{
    private struct CachedSpeed
    {
        public float Value;
        public ulong TickCached;
    }

    private static readonly Dictionary<int, CachedSpeed> s_cache = new Dictionary<int, CachedSpeed>(256);

    private const int CACHE_TTL_TICKS = 20;  // ~1 second at 20 ticks/sec

    public static bool Prefix(EntityAlive __instance, ref float __result)
    {
        if (__instance == null) return true;
        if (__instance is EntityPlayer) return true;
        if (!ProfilerConfig.Current.EnableMoveLOD) return true;

        try
        {
            int entityId = __instance.entityId;
            ulong currentTick = GameTimer.Instance.ticks;

            if (s_cache.TryGetValue(entityId, out var cached))
            {
                if (currentTick - cached.TickCached < CACHE_TTL_TICKS)
                {
                    __result = cached.Value;
                    ProfilingUtils.PerFrameCounters.Increment("MoveSpeed.CacheHit");
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return true;
        }
    }

    public static void Postfix(EntityAlive __instance, float __result)
    {
        if (__instance == null) return;
        if (__instance is EntityPlayer) return;
        if (!ProfilerConfig.Current.EnableMoveLOD) return;

        try
        {
            s_cache[__instance.entityId] = new CachedSpeed
            {
                Value = __result,
                TickCached = GameTimer.Instance.ticks
            };
        }
        catch
        {
            // defensive
        }
    }

    public static void OnEntityRemoved(int entityId)
    {
        s_cache.Remove(entityId);
    }
}
