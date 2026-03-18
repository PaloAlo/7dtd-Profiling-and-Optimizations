// UpdateTasksLODPatch.cs
//
// Distance-based throttling for EntityAlive.updateTasks.
//
// V4 — Combat entities always get full updateTasks every frame (no stagger).
// Only non-combat, distant entities get LOD throttling with lite maintenance.
//
// What runs every frame on throttled (non-combat, distant) frames:
//   - CheckDespawn, seeCache.ClearIfExpired
//   - ContinueExecuting on already-running tasks (approach keeps walking)
//   - PathFinderThread.GetPath + navigator.SetPath (path pickup)
//   - navigator.UpdateNavigation  (smooth path following)
//   - moveHelper.UpdateMoveHelper (smooth obstacle avoidance)
//   - lookHelper.onUpdateLook     (smooth head tracking)
//
// What gets throttled (non-combat distant only):
//   - aiManager.Update full re-evaluation (CanExecute / Continue / isBestTask
//     loop across all tasks).  Only fires on full-update frames.

using System.Collections.Generic;
using GamePath;
using HarmonyLib;
using UAI;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), "updateTasks")]
public static class UpdateTasksLODPatch
{
    private static readonly Dictionary<int, int> s_lastFullUpdateFrame = new Dictionary<int, int>(256);

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
            if (__instance.IsSleeping) return true;

            int entityId = __instance.entityId;
            int currentFrame = Time.frameCount;
            int zombieCount = FrameCache.ZombieCount;

            bool emergencyMode = zombieCount >= AdaptiveThresholds.EmergencyZombieThreshold;
            bool criticalMode = zombieCount >= AdaptiveThresholds.CriticalZombieThreshold;

            float distSq = (__instance.position - FrameCache.PlayerPosition).sqrMagnitude;

            bool inCombat = __instance.GetAttackTarget() != null
                         || __instance.GetRevengeTarget() != null
                         || __instance.hasBeenAttackedTime > 0
                         || __instance.isAlert
                         || __instance.HasInvestigatePosition;

            // ── Distance-based LOD (non-combat, outside close range) ──
            if (inCombat) return true;
            if (distSq < CLOSE_DIST_SQ) return true;

            bool siegeMode = zombieCount >= 100;

            int interval;
            if (distSq < MID_DIST_SQ)
            {
                interval = emergencyMode ? 2 : 1;
            }
            else if (distSq < FAR_DIST_SQ)
            {
                interval = siegeMode ? 4 : (criticalMode ? 3 : (emergencyMode ? 2 : 1));
            }
            else if (distSq < VERY_FAR_DIST_SQ)
            {
                interval = siegeMode ? 6 : (criticalMode ? 4 : (emergencyMode ? 3 : 2));
            }
            else
            {
                interval = siegeMode ? 8 : (criticalMode ? 6 : (emergencyMode ? 4 : 3));
            }

            if (interval <= 1) return true;

            if (!s_lastFullUpdateFrame.TryGetValue(entityId, out int lastFrame2))
            {
                s_lastFullUpdateFrame[entityId] = currentFrame;
                return true;
            }

            if (currentFrame - lastFrame2 >= interval)
            {
                s_lastFullUpdateFrame[entityId] = currentFrame;
                return true;
            }

            // Distance-LOD throttled: run lite maintenance too
            RunLiteMaintenance(__instance);
            ProfilerCounterBridge.Increment("updateTasks.LiteRun");
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Runs everything in updateTasks EXCEPT the full aiManager.Update()
    /// re-evaluation.  This keeps movement smooth while skipping the
    /// expensive task decision-making on throttled frames.
    /// </summary>
    private static void RunLiteMaintenance(EntityAlive entity)
    {
        try
        {
            // 1. Lifecycle maintenance (same as vanilla)
            entity.CheckDespawn();
            entity.seeCache?.ClearIfExpired();

            // 2. Continue executing tasks (approach keeps walking, etc.)
            //    Skip the full CanExecute/Continue re-evaluation loop.
            if (entity.aiManager != null)
            {
                bool useAIPackages = EntityClass.list[entity.entityClass].UseAIPackages;
                if (!useAIPackages)
                {
                    if (!EAITaskEvaluationThrottlePatch.ContinueExecutingOnly(entity.aiManager))
                    {
                        // Reflection failed — fall through to full update
                        entity.aiManager.Update();
                    }
                    else
                    {
                        ProfilerCounterBridge.Increment("EAIManager.EvalThrottled");
                    }
                }
                // UAI entities (bandits/NPCs) get full update — they're rare
                else if (entity.utilityAIContext != null)
                {
                    UAIBase.Update(entity.utilityAIContext);
                }
            }

            // 3. Pick up completed paths (cheap thread result check)
            PathInfo path = PathFinderThread.Instance.GetPath(entity.entityId);
            if (path.path != null)
            {
                bool useAIPackages = EntityClass.list[entity.entityClass].UseAIPackages;
                bool accept = true;
                if (!useAIPackages && entity.aiManager != null)
                {
                    accept = entity.aiManager.CheckPath(path);
                }
                if (accept)
                {
                    entity.navigator.SetPath(path, path.speed);
                }
            }

            // 4. CRITICAL: navigation + movement + look (every frame for smooth motion)
            entity.navigator.UpdateNavigation();

            // At extreme zombie counts, moveHelper physics cost scales super-linearly
            // (500-800ms at 130+ entities).  Throttle on alternating throttled frames
            // to halve that cost while still keeping obstacle avoidance running frequently.
            int zCount = FrameCache.ZombieCount;
            if (zCount < 100 || ((entity.entityId + Time.frameCount) & 1) == 0)
            {
                entity.moveHelper.UpdateMoveHelper();
            }
            else
            {
                ProfilerCounterBridge.Increment("MoveHelper.Throttled");
            }

            entity.lookHelper.onUpdateLook();

            // 5. Distraction cleanup
            if (entity.distraction != null &&
                (entity.distraction.IsDead() || entity.distraction.IsMarkedForUnload()))
            {
                entity.distraction = null;
            }
            if (entity.pendingDistraction != null &&
                (entity.pendingDistraction.IsDead() || entity.pendingDistraction.IsMarkedForUnload()))
            {
                entity.pendingDistraction = null;
            }
        }
        catch { }
    }

    public static void OnEntityRemoved(int entityId) => s_lastFullUpdateFrame.Remove(entityId);
    public static void ClearCaches() => s_lastFullUpdateFrame.Clear();
}
