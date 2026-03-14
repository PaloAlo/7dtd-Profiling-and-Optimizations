// ZombieFrameSkipPatch.cs
//
// Skips EntityAlive.Update() entirely for distant non-combat zombies.
// This is per-FRAME territory — Update() runs every render frame, not just
// every game tick. Skipping it for far-away zombies is visually imperceptible
// because the visual lerp catches up on the next processed frame.
//
// What gets skipped when Update() is skipped:
//   - base.Update() (Entity — updateTransform, animateYaw, audio monitoring)
//   - updateNetworkStats
//   - MinEventContext setup (includes FastTags.CombineTags allocation)
//   - Progression.Update (null for zombies, but still checked)
//   - Render fade logic
//
// EntityHuman does NOT override Update(), so Unity calls EntityAlive.Update()
// directly for all humanoid zombies — this single patch covers them all.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.Update))]
public static class ZombieFrameSkipPatch
{
    private static readonly Dictionary<int, int> s_lastUpdateFrame = new Dictionary<int, int>(256);

    // Distance thresholds (squared)
    private const float ALWAYS_UPDATE_DIST_SQ = 2500f;  // 50m — always run Update()
    private const float FAR_DIST_SQ = 4900f;            // 70m — more aggressive skip

    public static bool Prefix(EntityAlive __instance)
    {
        if (__instance == null) return true;
        if (__instance is EntityPlayer) return true;

        // AI Off: zero stale movement so entityCollision doesn't keep pushing the entity
        if (GamePrefs.GetBool(EnumGamePrefs.DebugStopEnemiesMoving))
        {
            __instance.motion = Vector3.zero;
            __instance.speedForward = 0f;
            __instance.speedStrafe = 0f;
            return true;
        }

        if (!ProfilerConfig.Current.EnableMoveLOD) return true;

        try
        {
            int currentFrame = Time.frameCount;
            FrameCache.EnsureUpdated();
            int zombieCount = FrameCache.ZombieCount;

            if (zombieCount < AdaptiveThresholds.EmergencyZombieThreshold)
                return true;

            // Combat-engaged entities always get full Update()
            if (IsCombatOrStateActive(__instance)) return true;

            int entityId = __instance.entityId;
            float distSq = (__instance.position - FrameCache.PlayerPosition).sqrMagnitude;

            if (distSq < ALWAYS_UPDATE_DIST_SQ)
                return true;

            bool criticalMode = zombieCount >= AdaptiveThresholds.CriticalZombieThreshold;

            int skipInterval;
            if (distSq < FAR_DIST_SQ)
            {
                // 50-70m: skip every 2nd frame in emergency, every 2nd in critical
                skipInterval = 2;
            }
            else
            {
                // 70m+: skip every 2nd frame in emergency, every 3rd in critical
                skipInterval = criticalMode ? 3 : 2;
            }

            // Frame slicing — distribute which entities skip which frames
            int frameSlot = currentFrame % skipInterval;
            int entitySlot = (entityId & 0x7FFFFFFF) % skipInterval;

            if (frameSlot == entitySlot)
            {
                s_lastUpdateFrame[entityId] = currentFrame;
                return true;
            }

            ProfilingUtils.PerFrameCounters.Increment("ZombieFrame.Skipped");
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
        if (entity.HasInvestigatePosition) return true;
        return false;
    }

    public static void OnEntityRemoved(int entityId)
    {
        s_lastUpdateFrame.Remove(entityId);
    }
}
