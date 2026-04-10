// ThreatLevelThrottlePatch.cs
//
// Throttles DynamicMusic.ThreatLevelUtility.GetThreatLevelOn to run at
// reduced frequency during high-zombie-count scenarios.
//
// Problem: GetThreatLevelOn is called EVERY FRAME from
// EntityPlayerLocal.Update().  Internally it:
//   1. World.GetEntitiesInBounds()  — scans chunks for all entities
//   2. zombiesContributingThreat()  — iterates entity list, checks IsAlive
//   3. EnemiesTargeting()           — iterates entity list again
//   4. isPlayerInUnclearedPOI()     — iterates sleeper volumes
//   5. IsPlayerHome() / IsPlayerInSpookyBiome() — environmental checks
//
// The result feeds into a Queue-based rolling average (LOOKBACK), so the
// music system already smooths over many frames.  Skipping computation
// for 30 frames (~0.5 sec at 60 fps) is completely invisible to the
// player but eliminates per-frame entity scanning during horde combat.

using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(DynamicMusic.ThreatLevelUtility), nameof(DynamicMusic.ThreatLevelUtility.GetThreatLevelOn))]
public static class ThreatLevelThrottlePatch
{
    private static float s_cachedResult;
    private static int s_lastComputeFrame = -9999;

    [HarmonyPrefix]
    public static bool Prefix(EntityPlayerLocal _player, ref float __result)
    {
        if (!OptimizationConfig.Current.EnableThreatLevelThrottle) return true;

        try
        {
            FrameCache.EnsureUpdated();

            if (FrameCache.ShouldBypassThrottling) return true;

            int zombieCount = FrameCache.ZombieCount;
            if (zombieCount < OptimizationConfig.Current.ThreatLevelThrottleZombieThreshold)
                return true;

            int frame = Time.frameCount;
            int gap = OptimizationConfig.Current.ThreatLevelThrottleFrames;

            if (frame - s_lastComputeFrame < gap)
            {
                __result = s_cachedResult;
                ProfilerCounterBridge.Increment("ThreatLevel.Throttled");
                return false;
            }
        }
        catch
        {
            return true;
        }

        return true;
    }

    [HarmonyPostfix]
    public static void Postfix(ref float __result)
    {
        if (!OptimizationConfig.Current.EnableThreatLevelThrottle) return;

        s_cachedResult = __result;
        s_lastComputeFrame = Time.frameCount;
    }

    public static void ClearCaches()
    {
        s_cachedResult = 0f;
        s_lastComputeFrame = -9999;
    }
}
