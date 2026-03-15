// MoveEntityHeadedLODPatch.cs
//
// Distance-based throttling for EntityAlive.MoveEntityHeaded — the single
// most expensive per-entity method.  Combat-engaged entities always run.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.MoveEntityHeaded))]
public static class MoveEntityHeadedLODPatch
{
    private static readonly Dictionary<int, int> s_lastUpdateFrame = new Dictionary<int, int>(256);

    public static bool Prefix(EntityAlive __instance, Vector3 _direction, bool _isDirAbsolute)
    {
        if (!OptimizationConfig.Current.EnableMoveLOD) return true;
        if (__instance == null) return true;
        if (__instance is EntityPlayer) return true;

        FrameCache.EnsureUpdated();
        if (FrameCache.ShouldBypassThrottling) return true;

        try
        {
            if (IsCombatOrStateActive(__instance)) return true;

            float distSq = (__instance.position - FrameCache.PlayerPosition).sqrMagnitude;

            float tier1Sq = OptimizationConfig.Current.MoveLODTier1DistSq;
            float tier2Sq = OptimizationConfig.Current.MoveLODTier2DistSq;
            float tier3Sq = OptimizationConfig.Current.MoveLODTier3DistSq;

            if (distSq < tier1Sq) return true;

            int zombieCount = FrameCache.ZombieCount;
            bool emergencyMode = zombieCount >= AdaptiveThresholds.EmergencyZombieThreshold;
            bool criticalMode = zombieCount >= AdaptiveThresholds.CriticalZombieThreshold;

            int skipInterval;
            if (distSq < tier2Sq)
            {
                skipInterval = criticalMode ? 3 : (emergencyMode ? 2 : 1);
            }
            else if (distSq < tier3Sq)
            {
                skipInterval = criticalMode ? 4 : (emergencyMode ? 3 : 2);
            }
            else
            {
                skipInterval = criticalMode ? 6 : (emergencyMode ? 4 : 3);
            }

            if (skipInterval <= 1) return true;

            int entityId = __instance.entityId;
            int frameSlot = Time.frameCount % skipInterval;
            int entitySlot = (entityId & 0x7FFFFFFF) % skipInterval;

            if (frameSlot == entitySlot)
            {
                s_lastUpdateFrame[entityId] = Time.frameCount;
                return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCombatOrStateActive(EntityAlive entity)
    {
        if (entity.GetAttackTarget() != null) return true;
        if (entity.GetRevengeTarget() != null) return true;
        if (entity.hasBeenAttackedTime > 0) return true;
        if (entity.isAlert) return true;
        if (entity.IsSleeping) return true;
        if (entity.HasInvestigatePosition) return true;
        return false;
    }

    public static void OnEntityRemoved(int entityId) => s_lastUpdateFrame.Remove(entityId);
    public static void ClearCaches() => s_lastUpdateFrame.Clear();
}

public static class EntityAliveExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasAttackTarget(this EntityAlive entity)
    {
        return entity.GetAttackTarget() != null;
    }
}
