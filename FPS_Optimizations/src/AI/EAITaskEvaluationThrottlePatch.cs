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
// Lightweight ContinueExecutingOnly using concrete types (no reflection).
// Per-action try/catch isolates misbehaving tasks without falling back to
// the full aiManager.Update() except when field resolution failed.

using System;
using System.Collections.Generic;
using HarmonyLib;

public static class EAITaskEvaluationThrottlePatch
{
    private static readonly Dictionary<int, int> s_lastEvalFrame = new Dictionary<int, int>(256);

    // Fast ref accessors into EAIManager's private task lists.
    // Cached at first use for zero-allocation per-call access.
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
    /// Lightweight update: calls Continue() on executing tasks and only Update()
    /// on tasks that should keep running. Tasks whose Continue() returns false
    /// are Reset() so the next full evaluation can pick a new one.
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
            try { manager.UpdateDebugName(); } catch { /* non-fatal */ }

            return true;
        }
        catch (Exception ex)
        {
            // If something truly unexpected happens here, fall back to full update.
            Log.Warning($"[FPSOptimizations] ContinueExecutingOnly top-level error: {ex.Message}");
            return false;
        }
    }

    // Use concrete typed path (no reflection) but protect each action with try/catch.
    private static void ContinueTaskList(EAITaskList list)
    {
        if (list == null) return;

        // GetExecutingTasks() is expected to return a list-like collection.
        var executing = list.GetExecutingTasks();
        if (executing == null) return;

        for (int i = executing.Count - 1; i >= 0; i--)
        {
            try
            {
                var entry = executing[i];
                if (entry == null) continue;

                var action = entry.action;
                if (action == null) continue;

                bool cont;
                try
                {
                    cont = action.Continue();
                }
                catch (Exception ex)
                {
                    Log.Warning("[FPSOptimizations] EAITask.Continue() threw: " + ex.Message);
                    try { action.Reset(); } catch { }
                    continue;
                }

                if (cont)
                {
                    try
                    {
                        action.Update();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[FPSOptimizations] EAITask.Update() threw: " + ex.Message);
                        try { action.Reset(); } catch { }
                    }
                }
                else
                {
                    try { action.Reset(); } catch { }
                }
            }
            catch (Exception ex)
            {
                // Protect the loop — a single bad executing entry must not abort work.
                Log.Warning("[FPSOptimizations] ContinueTaskList iteration error: " + ex.Message);
            }
        }
    }

    public static void OnEntityRemoved(int entityId) => s_lastEvalFrame.Remove(entityId);
    public static void ClearCaches() => s_lastEvalFrame.Clear();
}
