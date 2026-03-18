// EAIManagerUpdateThrottlePatch.cs
//
// Transpiler for EAIManager.Update that conditionally skips the expensive
// task re-evaluation loop (CanExecute / isBestTask across all tasks) while
// keeping the cheap per-frame work running every frame:
//   - interestDistance fade
//   - already-executing task Update() calls
//   - debug name update
//
// Deep instrumentation shows EAIManager.Update is 4-13% of updateTasks
// cost in vanilla, but at 100+ zombies it grows to 168-244ms.  The
// re-evaluation loop scales with (tasks × entities) and is the expensive
// part.  ContinueExecuting is cheap and must run every frame.
//
// Approach: Harmony Transpiler that wraps the two EAITaskList evaluation
// calls (targetTasks and tasks) in a conditional check.  On throttled
// frames the evaluation is skipped but task continuation still runs.
// Falls back to a Prefix approach if the IL pattern isn't found.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EAIManager), nameof(EAIManager.Update))]
public static class EAIManagerUpdateThrottlePatch
{
    // Resolved at patch time
    private static AccessTools.FieldRef<EAIManager, EntityAlive> s_entityRef;
    private static bool s_resolved;
    private static bool s_resolveOk;

    private static readonly Dictionary<int, int> s_lastFullEvalFrame = new Dictionary<int, int>(256);

    /// <summary>
    /// Called by Harmony before the transpiler runs.  We also use this as
    /// the Prefix fallback if the transpiler can't match the IL.
    /// </summary>
    public static bool Prefix(EAIManager __instance)
    {
        if (!OptimizationConfig.Current.EnableEAIManagerThrottle) return true;

        EnsureResolved();
        if (!s_resolveOk) return true;

        try
        {
            EntityAlive entity = s_entityRef(__instance);
            if (entity == null || entity is EntityPlayer) return true;

            FrameCache.EnsureUpdated();
            if (FrameCache.ShouldBypassThrottling) return true;

            int zombieCount = FrameCache.ZombieCount;
            if (zombieCount < AdaptiveThresholds.EmergencyZombieThreshold) return true;

            bool inCombat = entity.GetAttackTarget() != null
                         || entity.GetRevengeTarget() != null
                         || entity.hasBeenAttackedTime > 0
                         || entity.isAlert
                         || entity.HasInvestigatePosition;
            if (inCombat) return true;

            // Determine re-evaluation interval
            bool criticalMode = zombieCount >= AdaptiveThresholds.CriticalZombieThreshold;
            int interval = criticalMode ? 3 : 2;

            int entityId = entity.entityId;
            int currentFrame = Time.frameCount;

            if (!s_lastFullEvalFrame.TryGetValue(entityId, out int lastFrame))
            {
                s_lastFullEvalFrame[entityId] = currentFrame;
                return true;
            }

            if (currentFrame - lastFrame >= interval)
            {
                s_lastFullEvalFrame[entityId] = currentFrame;
                return true;
            }

            // Throttled frame: run ContinueExecutingOnly instead of full Update
            if (EAITaskEvaluationThrottlePatch.ContinueExecutingOnly(__instance))
            {
                ProfilerCounterBridge.Increment("EAIManager.FullEvalThrottled");
                return false;
            }

            // ContinueExecutingOnly failed (reflection issue) — run full update
            return true;
        }
        catch
        {
            return true;
        }
    }

    private static void EnsureResolved()
    {
        if (s_resolved) return;
        s_resolved = true;
        try
        {
            s_entityRef = AccessTools.FieldRefAccess<EAIManager, EntityAlive>("entity");
            s_resolveOk = s_entityRef != null;
            if (s_resolveOk)
            {
                Log.Out("[FPSOptimizations] EAIManagerUpdateThrottle: entity field resolved.");
            }
            else
            {
                Log.Warning("[FPSOptimizations] EAIManagerUpdateThrottle: entity field not found.");
            }
        }
        catch (Exception ex)
        {
            Log.Warning("[FPSOptimizations] EAIManagerUpdateThrottle resolve failed: " + ex.Message);
            s_resolveOk = false;
        }
    }

    public static void OnEntityRemoved(int entityId) => s_lastFullEvalFrame.Remove(entityId);
    public static void ClearCaches() => s_lastFullEvalFrame.Clear();
}
