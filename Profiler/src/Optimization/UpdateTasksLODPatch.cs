// UpdateTasksLODPatch.cs
//
// LOD-based throttling for EntityAlive.updateTasks.
// Now includes emergency throttling when zombie count is high (blood moon).
//
// On throttled frames RunMinimalMaintenance handles only lightweight
// housekeeping (despawn check, seeCache expiry).  Movement is NOT
// maintained here — it runs through its own vanilla call-sites and our
// separate throttle patches (MoveEntityHeaded, MoveHelper, SpeedStrafe).

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), "updateTasks")]
public static class UpdateTasksLODPatch
{
    // Track last full AI update per entity
    private static readonly Dictionary<int, int> s_lastAIUpdateFrame = new Dictionary<int, int>(256);

    // Distance thresholds
    private const float CLOSE_DIST_SQ = 400f;    // 20m - very close, minimal throttling
    private const float MID_DIST_SQ = 900f;      // 30m - medium distance
    private const float FAR_DIST_SQ = 2500f;     // 50m - far
    private const float VERY_FAR_DIST_SQ = 6400f; // 80m - very far

    public static bool Prefix(EntityAlive __instance)
    {
        // Feature toggle
        if (!ProfilerConfig.Current.EnableStaggeredUpdate) return true;

        // Safety checks
        if (__instance == null) return true;

        // NEVER throttle players
        if (__instance is EntityPlayer) return true;

        try
        {
            // ALWAYS let combat-engaged or state-critical entities through
            // This must run regardless of zombie count to prevent idle-while-close bugs
            if (IsCombatOrStateActive(__instance)) return true;

            int entityId = __instance.entityId;
            int currentFrame = Time.frameCount;

            UpdateCaches(currentFrame);
            int zombieCount = FrameCache.ZombieCount;

            // Check if we're in emergency mode (many zombies) — adaptive thresholds
            bool emergencyMode = zombieCount >= AdaptiveThresholds.EmergencyZombieThreshold;
            bool criticalMode = zombieCount >= AdaptiveThresholds.CriticalZombieThreshold;

            // Calculate distance to player
            float distSq = (__instance.position - FrameCache.PlayerPosition).sqrMagnitude;

            // Very close entities - ALWAYS full update (never reduce this threshold)
            if (distSq < CLOSE_DIST_SQ)
            {
                return true;
            }

            // Calculate skip interval based on distance AND zombie count
            int interval;
            if (distSq < MID_DIST_SQ)
            {
                // 20-30m: light throttling
                interval = emergencyMode ? 2 : 1;
            }
            else if (distSq < FAR_DIST_SQ)
            {
                // 30-50m: medium throttling
                interval = criticalMode ? 3 : (emergencyMode ? 2 : 1);
            }
            else if (distSq < VERY_FAR_DIST_SQ)
            {
                // 50-80m: heavy throttling
                interval = criticalMode ? 5 : (emergencyMode ? 4 : 2);
            }
            else
            {
                // >80m: very heavy throttling
                interval = criticalMode ? 8 : (emergencyMode ? 6 : 4);
            }

            // Skip if interval is 1 (no throttling)
            if (interval <= 1) return true;

            // Check if enough frames have passed
            if (!s_lastAIUpdateFrame.TryGetValue(entityId, out int lastFrame))
            {
                // First time - run update
                s_lastAIUpdateFrame[entityId] = currentFrame;
                return true;
            }

            int framesSince = currentFrame - lastFrame;
            if (framesSince >= interval)
            {
                // Time for full update
                s_lastAIUpdateFrame[entityId] = currentFrame;
                return true;
            }

            RunMinimalMaintenance(__instance);

            ProfilingUtils.PerFrameCounters.Increment("updateTasks.LODSkipped");
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateCaches(int frame)
    {
        FrameCache.EnsureUpdated();

        int zc = FrameCache.ZombieCount;
        if (zc >= AdaptiveThresholds.CriticalZombieThreshold)
            ProfilingUtils.PerFrameCounters.SetGauge("UpdateTasks.CriticalMode", 1);
        else if (zc >= AdaptiveThresholds.EmergencyZombieThreshold)
            ProfilingUtils.PerFrameCounters.SetGauge("UpdateTasks.EmergencyMode", 1);
    }

    private static void RunMinimalMaintenance(EntityAlive entity)
    {
        try
        {
            entity.CheckDespawn();
            entity.seeCache?.ClearIfExpired();
        }
        catch
        {
        }
    }

    public static void OnEntityRemoved(int entityId)
    {
        s_lastAIUpdateFrame.Remove(entityId);
    }

    public static void ClearCaches()
    {
        s_lastAIUpdateFrame.Clear();
    }
}