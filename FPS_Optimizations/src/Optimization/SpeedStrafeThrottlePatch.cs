// SpeedStrafeThrottlePatch.cs
//
// Throttles EntityAlive.updateSpeedForwardAndStrafe for distant non-combat
// entities.  Combat-engaged entities always get fresh heading recalculation.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), "updateSpeedForwardAndStrafe")]
public static class SpeedStrafeThrottlePatch
{
    private static readonly Dictionary<int, int> s_lastFrame = new Dictionary<int, int>(256);

    private const float ALWAYS_RUN_DIST_SQ = 900f;  // 30 m
    private const float MID_DIST_SQ = 2500f;        // 50 m
    private const float FAR_DIST_SQ = 6400f;        // 80 m

    public static bool Prefix(EntityAlive __instance)
    {
        if (!OptimizationConfig.Current.EnableMoveLOD) return true;
        if (__instance == null) return true;
        if (__instance is EntityPlayer) return true;

        FrameCache.EnsureUpdated();
        if (FrameCache.ShouldBypassThrottling) return true;

        if (IsCombatOrStateActive(__instance)) return true;

        try
        {
            int zombieCount = FrameCache.ZombieCount;
            if (zombieCount < AdaptiveThresholds.EmergencyZombieThreshold) return true;

            float distSq = (__instance.position - FrameCache.PlayerPosition).sqrMagnitude;
            if (distSq < ALWAYS_RUN_DIST_SQ) return true;

            bool criticalMode = zombieCount >= AdaptiveThresholds.CriticalZombieThreshold;

            int skipInterval;
            if (distSq < MID_DIST_SQ)
                skipInterval = criticalMode ? 2 : 1;
            else if (distSq < FAR_DIST_SQ)
                skipInterval = criticalMode ? 3 : 2;
            else
                skipInterval = criticalMode ? 4 : 3;

            if (skipInterval <= 1) return true;

            int entityId = __instance.entityId;
            int frameSlot = Time.frameCount % skipInterval;
            int entitySlot = (entityId & 0x7FFFFFFF) % skipInterval;

            if (frameSlot == entitySlot)
            {
                s_lastFrame[entityId] = Time.frameCount;
                return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    public static void OnEntityRemoved(int entityId) => s_lastFrame.Remove(entityId);
    public static void ClearCaches() => s_lastFrame.Clear();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCombatOrStateActive(EntityAlive entity)
    {
        if (entity.GetAttackTarget() != null) return true;
        if (entity.GetRevengeTarget() != null) return true;
        if (entity.hasBeenAttackedTime > 0) return true;
        if (entity.isAlert) return true;
        if (entity.HasInvestigatePosition) return true;
        return false;
    }
}
