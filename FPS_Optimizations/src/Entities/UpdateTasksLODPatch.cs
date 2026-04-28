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

using System;
using System.Collections.Generic;
using GamePath;
using HarmonyLib;
using UAI;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), "updateTasks")]
public static class UpdateTasksLODPatch
{
    private static readonly Dictionary<int, int> s_lastFullUpdateFrame = new Dictionary<int, int>(256);

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
            int zombieCount = FrameCache.ZombieCount;

            // Use centralized entity budget classification
            if (!EntityBudgetSystem.TryGetInfo(entityId, out var budgetInfo))
                return true;

            // Critical tier = close or combat → always full updateTasks
            if (budgetInfo.Tier == EntityBudgetSystem.Tier.Critical) return true;

            // High tier runs full updateTasks unless zombie count is extreme (100+).
            // At z<100, High tier always gets full AI re-evaluation.
            if (budgetInfo.Tier == EntityBudgetSystem.Tier.High
                && zombieCount < 100)
                return true;

            // Use budget system's recommended interval
            int interval = EntityBudgetSystem.GetRecommendedInterval(budgetInfo.Tier, zombieCount);
            if (interval <= 1) return true;

            int currentFrame = Time.frameCount;
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
            //    Skip the full CanExecute/Continue re-evaluation loop when possible.
            if (entity.aiManager != null)
            {
                bool useAIPackages = EntityClass.list[entity.entityClass].UseAIPackages;
                if (!useAIPackages)
                {
                    bool contOk = false;
                    try
                    {
                        // Try the safe continue-only path. It returns true when it
                        // successfully performed the safe per-task Continue/Update logic.
                        contOk = EAITaskEvaluationThrottlePatch.ContinueExecutingOnly(entity.aiManager);
                    }
                    catch (Exception ex)
                    {
                        // Defensive: ContinueExecutingOnly should not throw, but if it does,
                        // log and fall back to the full update below (inside its own try/catch).
                        Log.Warning("[FPSOptimizations] ContinueExecutingOnly threw: " + ex.Message);
                        contOk = false;
                    }

                    if (!contOk)
                    {
                        // Reflection failed or ContinueExecutingOnly couldn't run safely.
                        // Fall back to full aiManager.Update(), but DO NOT allow exceptions
                        // to escape — catch and log them so the world tick continues.
                        try
                        {
                            entity.aiManager.Update();
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("[FPSOptimizations] aiManager.Update() threw during fallback: " + ex.Message);
                            // swallow exception to avoid top-level crash; state may be inconsistent
                            // but this prevents the whole server/game from crashing.
                        }
                    }
                    else
                    {
                        ProfilerCounterBridge.Increment("EAIManager.EvalThrottled");
                    }
                }
                // UAI entities -> full update (rare)
                else if (entity.utilityAIContext != null)
                {
                    try { UAIBase.Update(entity.utilityAIContext); }
                    catch (Exception ex)
                    {
                        Log.Warning("[FPSOptimizations] UAIBase.Update() threw: " + ex.Message);
                    }
                }
            }

            // 3. Pick up completed paths (cheap thread result check)
            PathInfo path = PathFinderThread.Instance.GetPath(entity.entityId);
            if (path.path != null)
            {
                bool useAIPackages2 = EntityClass.list[entity.entityClass].UseAIPackages;
                bool accept = true;
                if (!useAIPackages2 && entity.aiManager != null)
                {
                    try { accept = entity.aiManager.CheckPath(path); }
                    catch { accept = false; }
                }
                if (accept)
                {
                    entity.navigator.SetPath(path, path.speed);
                }
            }

            // 4. CRITICAL: navigation + movement + look (every frame for smooth motion)
            //    MoveHelperThrottlePatch handles any Low-tier throttling separately.
            entity.navigator.UpdateNavigation();
            entity.moveHelper.UpdateMoveHelper();
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
        catch
        {
            // Protect the world tick — any unexpected exception in maintenance should not crash the game.
        }
    }

    public static void OnEntityRemoved(int entityId) => s_lastFullUpdateFrame.Remove(entityId);
    public static void ClearCaches() => s_lastFullUpdateFrame.Clear();
}
