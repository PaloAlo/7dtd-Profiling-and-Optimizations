// UpdateTasksLODPatch.cs
//
// Distance-based throttling for EntityAlive.updateTasks (AI task evaluation).
// On throttled frames only lightweight housekeeping runs (despawn, seeCache).

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), "updateTasks")]
public static class UpdateTasksLODPatch
{
    private static readonly Dictionary<int, int> s_lastAIUpdateFrame = new Dictionary<int, int>(256);

    private const float CLOSE_DIST_SQ = 400f;     // 20 m
    private const float MID_DIST_SQ = 900f;       // 30 m
    private const float FAR_DIST_SQ = 2500f;      // 50 m
    private const float VERY_FAR_DIST_SQ = 6400f; // 80 m

    public static bool Prefix(EntityAlive __instance)
    {
        if (!OptimizationConfig.Current.EnableStaggeredUpdate) return true;
        if (__instance == null) return true;
        if (__instance is EntityPlayer) return true;

        FrameCache.EnsureUpdated();
        if (FrameCache.ShouldBypassThrottling) return true;

        try
        {
            if (IsCombatOrStateActive(__instance)) return true;

            int entityId = __instance.entityId;
            int currentFrame = Time.frameCount;
            int zombieCount = FrameCache.ZombieCount;

            bool emergencyMode = zombieCount >= AdaptiveThresholds.EmergencyZombieThreshold;
            bool criticalMode = zombieCount >= AdaptiveThresholds.CriticalZombieThreshold;

            float distSq = (__instance.position - FrameCache.PlayerPosition).sqrMagnitude;

            if (distSq < CLOSE_DIST_SQ) return true;

            int interval;
            if (distSq < MID_DIST_SQ)
            {
                interval = emergencyMode ? 2 : 1;
            }
            else if (distSq < FAR_DIST_SQ)
            {
                interval = criticalMode ? 3 : (emergencyMode ? 2 : 1);
            }
            else if (distSq < VERY_FAR_DIST_SQ)
            {
                interval = criticalMode ? 4 : (emergencyMode ? 3 : 2);
            }
            else
            {
                interval = criticalMode ? 6 : (emergencyMode ? 4 : 3);
            }

            if (interval <= 1) return true;

            if (!s_lastAIUpdateFrame.TryGetValue(entityId, out int lastFrame))
            {
                s_lastAIUpdateFrame[entityId] = currentFrame;
                return true;
            }

            if (currentFrame - lastFrame >= interval)
            {
                s_lastAIUpdateFrame[entityId] = currentFrame;
                return true;
            }

            RunMinimalMaintenance(__instance);
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

    private static void RunMinimalMaintenance(EntityAlive entity)
    {
        try
        {
            entity.CheckDespawn();
            entity.seeCache?.ClearIfExpired();
        }
        catch { }
    }

    public static void OnEntityRemoved(int entityId) => s_lastAIUpdateFrame.Remove(entityId);
    public static void ClearCaches() => s_lastAIUpdateFrame.Clear();
}
