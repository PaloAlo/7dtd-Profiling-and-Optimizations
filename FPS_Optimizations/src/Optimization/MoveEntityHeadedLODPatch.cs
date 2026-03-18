// MoveEntityHeadedLODPatch.cs
//
// Distance-based throttling and speed-curve LOD for MoveEntityHeaded.
//
// V4 — Speed-Curve LOD:  Instead of frame-skipping combat/close entities
// (which breaks AI state machines, causes jerky movement, disappearing
// zombies, and path loss), we now reduce movement speed based on distance.
// Distant zombies move slower, creating natural stagger without any
// frame-skipping for combat-aware entities.
//
// - Close range (< 15m):  full speed, full update every frame
// - Mid range (15–80m):   speed scales from 100% down to 35%
// - Far non-combat:       distance LOD with lite motion (unchanged)
//
// All AI and physics still run every frame for combat entities.
// CharacterController.Move() still runs, just with a smaller direction
// vector, keeping entity behavior smooth and correct.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.MoveEntityHeaded))]
public static class MoveEntityHeadedLODPatch
{
    private static readonly Dictionary<int, int> s_lastUpdateFrame = new Dictionary<int, int>(256);

    public static bool Prefix(EntityAlive __instance, ref Vector3 _direction, bool _isDirAbsolute)
    {
        if (!OptimizationConfig.Current.EnableMoveLOD) return true;
        if (__instance == null) return true;
        if (__instance is EntityPlayer) return true;

        FrameCache.EnsureUpdated();
        if (FrameCache.ShouldBypassThrottling) return true;

        try
        {
            if (__instance.IsSleeping) return true;

            // Don't extrapolate entities with special movement modes
            if (__instance.AttachedToEntity != null) return true;
            if (__instance.jumpIsMoving) return true;
            if (__instance.IsFlyMode.Value) return true;

            int entityId = __instance.entityId;
            int zombieCount = FrameCache.ZombieCount;
            bool emergencyMode = zombieCount >= AdaptiveThresholds.EmergencyZombieThreshold;
            bool criticalMode = zombieCount >= AdaptiveThresholds.CriticalZombieThreshold;

            float distSq = (__instance.position - FrameCache.PlayerPosition).sqrMagnitude;

            float tier1Sq = OptimizationConfig.Current.MoveLODTier1DistSq;
            float tier2Sq = OptimizationConfig.Current.MoveLODTier2DistSq;
            float tier3Sq = OptimizationConfig.Current.MoveLODTier3DistSq;

            bool inCombat = __instance.GetAttackTarget() != null
                         || __instance.GetRevengeTarget() != null
                         || __instance.hasBeenAttackedTime > 0
                         || __instance.isAlert
                         || __instance.HasInvestigatePosition;

            // ── Speed-Curve LOD ───────────────────────────────────────
            // Reduce movement speed based on distance instead of skipping
            // frames.  All AI/physics still runs every frame — distant
            // zombies just move slower, creating a natural wave effect.
            var cfg = OptimizationConfig.Current;
            if (cfg.EnableSpeedCurveLOD
                && zombieCount >= cfg.SpeedCurveZombieThreshold
                && distSq > cfg.SpeedCurveCloseDistSq)
            {
                float t = Mathf.InverseLerp(cfg.SpeedCurveCloseDistSq, cfg.SpeedCurveFarDistSq, distSq);
                float speedMult = Mathf.Lerp(1f, cfg.SpeedCurveMinMult, t);
                _direction *= speedMult;
                ProfilerCounterBridge.Increment("MoveSpeed.Curved");
            }

            // ── Distance-based LOD (non-combat, outside tier1) ──
            if (distSq < tier1Sq) return true;
            if (inCombat) return true;

            // Scale distance LOD intervals — at extreme counts, even mid-range
            // entities get more aggressive throttling.
            bool siegeMode = zombieCount >= 100;
            int skipInterval;
            if (distSq < tier2Sq)
            {
                skipInterval = siegeMode ? 4 : (criticalMode ? 3 : (emergencyMode ? 2 : 1));
            }
            else if (distSq < tier3Sq)
            {
                skipInterval = siegeMode ? 6 : (criticalMode ? 4 : (emergencyMode ? 3 : 2));
            }
            else
            {
                skipInterval = siegeMode ? 8 : (criticalMode ? 6 : (emergencyMode ? 4 : 3));
            }

            if (skipInterval <= 1) return true;

            int frameSlot2 = Time.frameCount % skipInterval;
            int entitySlot2 = (entityId & 0x7FFFFFFF) % skipInterval;

            if (frameSlot2 == entitySlot2)
            {
                s_lastUpdateFrame[entityId] = Time.frameCount;
                return true;
            }

            ApplyLiteMotion(__instance);
            ProfilerCounterBridge.Increment("MoveEntityHeaded.LiteMotion");
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Lightweight motion extrapolation that skips CharacterController.Move()
    /// but keeps the entity moving smoothly.  Applies gravity + friction to
    /// the motion vector and extrapolates position, then syncs the physics
    /// transform so the next full-physics frame starts from the right spot.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyLiteMotion(EntityAlive entity)
    {
        try
        {
            float gravity = entity.world.Gravity;

            // Apply friction/damping to motion — mirrors DefaultMoveEntity
            if (entity.isSwimming)
            {
                entity.motion.x *= 0.91f;
                entity.motion.z *= 0.91f;
                // Keep swim buoyancy in motion for next full frame
                entity.motion.y -= gravity * 0.025f;
                entity.motion.y *= 0.91f;
            }
            else
            {
                // Ground friction (0.546f is vanilla's onGround friction)
                float friction = entity.onGround ? 0.546f : 0.91f;
                entity.motion.x *= friction;
                entity.motion.z *= friction;

                if (entity.onGround)
                {
                    // On-ground: reset Y motion so gravity doesn't accumulate
                    // across throttled frames and push the entity below
                    // floors/terrain — this was causing mass clip-through and
                    // despawning in multi-story POIs.
                    entity.motion.y = 0f;
                }
                else if (!entity.bInElevator)
                {
                    // Airborne: track gravity in motion for the next full frame
                    entity.motion.y -= gravity;
                    entity.motion.y *= 0.98f;
                }
            }

            // Extrapolate HORIZONTAL position only.
            // Never modify Y — CharacterController.Move() must handle all
            // vertical positioning to prevent entities clipping through
            // thin floors, terrain, or multi-story POI geometry.
            Vector3 delta = new Vector3(entity.motion.x, 0f, entity.motion.z);

            if (delta.x != 0f || delta.z != 0f)
            {
                entity.position += delta;

                // Keep bounding box in sync
                Bounds bb = entity.boundingBox;
                bb.center += delta;
                entity.boundingBox = bb;

                // Sync physics transform so CharacterController starts from
                // the correct position on the next full-physics frame
                Transform physT = entity.PhysicsTransform;
                if (physT != null)
                {
                    physT.position = entity.position - Origin.position;
                }
            }
        }
        catch { }
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
