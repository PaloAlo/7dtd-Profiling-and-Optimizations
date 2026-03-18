// EAITaskEvaluationThrottlePatch.cs
//
// Provides a lightweight alternative to full EAIManager.Update() for use on
// throttled frames.  Instead of running the full task re-evaluation loop
// (CanExecute / Continue / isBestTask on every task), this only calls
// Update() on already-executing tasks so the current AI behaviour continues
// (approach-and-attack keeps walking, look keeps tracking, etc.) without the
// expensive decision-making overhead.
//
// Used by UpdateTasksLODPatch.RunLiteMaintenance on combat-stagger and
// distance-LOD frames.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

public static class EAITaskEvaluationThrottlePatch
{
    private static readonly Dictionary<int, int> s_lastEvalFrame = new Dictionary<int, int>(256);

    // Fast ref accessors into EAIManager's private task lists.
    // These are cached at first use for zero-allocation per-call access.
    private static AccessTools.FieldRef<EAIManager, EAITaskList> s_tasksRef;
    private static AccessTools.FieldRef<EAIManager, EAITaskList> s_targetTasksRef;
    private static bool s_resolved;
    private static bool s_resolveOk;

    private static void EnsureResolved()
    {
        if (s_resolved) return;
        s_resolved = true;
        try
        {
            s_tasksRef = AccessTools.FieldRefAccess<EAIManager, EAITaskList>("tasks");
            s_targetTasksRef = AccessTools.FieldRefAccess<EAIManager, EAITaskList>("targetTasks");
            s_resolveOk = s_tasksRef != null && s_targetTasksRef != null;
        }
        catch (Exception ex)
        {
            Log.Warning("[FPSOptimizations] EAITaskEval field resolution failed: " + ex.Message);
            s_resolveOk = false;
        }
    }

    /// <summary>
    /// Lightweight update: continues executing tasks and updates interestDistance
    /// but skips the full CanExecute / Continue re-evaluation loop.
    /// Returns true if successful, false if reflection failed (caller should
    /// fall back to full aiManager.Update).
    /// </summary>
    public static bool ContinueExecutingOnly(EAIManager manager)
    {
        EnsureResolved();
        if (!s_resolveOk || manager == null) return false;

        try
        {
            // interestDistance fade — cheap, keeps state consistent
            manager.interestDistance = Utils.FastMoveTowards(manager.interestDistance, 10f, 1f / 120f);

            // Continue running already-executing tasks (the per-frame Update call)
            ContinueTaskList(s_targetTasksRef(manager));
            ContinueTaskList(s_tasksRef(manager));

            // Debug name update (only when debug UI is open — virtually free)
            manager.UpdateDebugName();

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ContinueTaskList(EAITaskList list)
    {
        if (list == null) return;
        var executing = list.GetExecutingTasks();
        for (int i = 0; i < executing.Count; i++)
        {
            executing[i].action.Update();
        }
    }

    public static void OnEntityRemoved(int entityId) => s_lastEvalFrame.Remove(entityId);
    public static void ClearCaches() => s_lastEvalFrame.Clear();
}
