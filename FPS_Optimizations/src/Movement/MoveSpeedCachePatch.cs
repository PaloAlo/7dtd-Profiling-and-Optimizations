// MoveSpeedCachePatch.cs
//
// Caches EntityAlive.GetSpeedModifier() results per entity for ~1 second.
// Avoids repeated EffectManager.GetValue(PassiveEffects.RunSpeed) iterations.
// EntityPlayer overrides GetSpeedModifier independently — not affected.

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
    private const int CACHE_TTL_TICKS = 20;

    public static bool Prefix(EntityAlive __instance, ref float __result)
    {
        if (__instance == null) return true;
        if (__instance is EntityPlayer) return true;
        if (!OptimizationConfig.Current.EnableMoveLOD) return true;
        if (FrameCache.ShouldBypassThrottling) return true;

        try
        {
            int entityId = __instance.entityId;
            ulong currentTick = GameTimer.Instance.ticks;

            if (s_cache.TryGetValue(entityId, out var cached))
            {
                if (currentTick - cached.TickCached < CACHE_TTL_TICKS)
                {
                    __result = cached.Value;
                    ProfilerCounterBridge.Increment("MoveSpeed.CacheHit");
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
        if (!OptimizationConfig.Current.EnableMoveLOD) return;

        try
        {
            s_cache[__instance.entityId] = new CachedSpeed
            {
                Value = __result,
                TickCached = GameTimer.Instance.ticks
            };
        }
        catch { }
    }

    public static void OnEntityRemoved(int entityId) => s_cache.Remove(entityId);
    public static void ClearCaches() => s_cache.Clear();
}
