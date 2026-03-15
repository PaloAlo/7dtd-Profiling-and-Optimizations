// MoveHelperThrottlePatch.cs
//
// Throttles obstacle-checking inside EntityMoveHelper.UpdateMoveHelper.
// Runs full checks when entity appears stuck; lighter checks when moving freely.
// Combat-engaged entities always get full updates.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityMoveHelper), nameof(EntityMoveHelper.UpdateMoveHelper))]
public static class MoveHelperThrottlePatch
{
    private static readonly Dictionary<int, EntityMoveData> s_moveData = new(256);

    private const float STUCK_THRESHOLD_SQ = 0.01f;
    private const int CHECK_INTERVAL_MOVING = 20;
    private const int CHECK_INTERVAL_STUCK = 4;

    private struct EntityMoveData
    {
        public Vector3 LastPosition;
        public int LastCheckFrame;
        public int StuckFrames;
    }

    public static bool Prefix(EntityMoveHelper __instance, EntityAlive ___entity)
    {
        if (!OptimizationConfig.Current.EnableMoveLOD) return true;
        if (___entity == null) return true;
        if (___entity is EntityPlayer) return true;
        if (!__instance.IsActive) return true;

        FrameCache.EnsureUpdated();
        if (FrameCache.ShouldBypassThrottling) return true;

        if (IsCombatOrStateActive(___entity)) return true;

        try
        {
            int entityId = ___entity.entityId;
            int currentFrame = Time.frameCount;
            int zombieCount = FrameCache.ZombieCount;

            if (!s_moveData.TryGetValue(entityId, out var data))
            {
                data = new EntityMoveData
                {
                    LastPosition = ___entity.position,
                    LastCheckFrame = currentFrame,
                    StuckFrames = 0
                };
                s_moveData[entityId] = data;
                return true;
            }

            Vector3 currentPos = ___entity.position;
            float movedSq = (currentPos - data.LastPosition).sqrMagnitude;
            int framesSinceCheck = currentFrame - data.LastCheckFrame;

            bool isStuck = movedSq < STUCK_THRESHOLD_SQ * framesSinceCheck;

            if (isStuck)
                data.StuckFrames++;
            else
                data.StuckFrames = 0;

            int checkInterval;
            if (data.StuckFrames > 3)
            {
                checkInterval = CHECK_INTERVAL_STUCK;
            }
            else if (zombieCount >= AdaptiveThresholds.CriticalZombieThreshold)
            {
                checkInterval = CHECK_INTERVAL_MOVING * 2;
            }
            else if (zombieCount >= AdaptiveThresholds.EmergencyZombieThreshold)
            {
                checkInterval = CHECK_INTERVAL_MOVING;
            }
            else
            {
                checkInterval = CHECK_INTERVAL_MOVING / 2;
            }

            bool shouldRun = framesSinceCheck >= checkInterval || data.StuckFrames > 3;

            if (shouldRun)
            {
                data.LastPosition = currentPos;
                data.LastCheckFrame = currentFrame;
                s_moveData[entityId] = data;
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
        if (entity.HasInvestigatePosition) return true;
        return false;
    }

    public static void OnEntityRemoved(int entityId) => s_moveData.Remove(entityId);
    public static void ClearCaches() => s_moveData.Clear();
}
