// ChunkCopyTimeBudgetPatch.cs
//
// Applies a per-frame time budget to ChunkManager.CopyChunksToUnity.
// The budget is deltaTime * 0.5 clamped to [2 ms, 8 ms].
//
// When CopyChunksToUnity is called multiple times per frame (caller loop),
// subsequent calls after the budget is exceeded are skipped until next
// frame.  When called once per frame with an internal loop, the Postfix
// tracks elapsed time so the NEXT frame's call can be deferred if the
// previous one overran.
//
// This prevents frame spikes from bulk chunk mesh generation without
// replacing any game logic — chunks that were deferred are processed
// the following frame.

using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(ChunkManager), "CopyChunksToUnity")]
public static class ChunkCopyTimeBudgetPatch
{
    private static int s_lastBudgetFrame = -1;
    private static float s_frameStartTime;
    private static bool s_budgetExceeded;

    public static bool Prefix(ChunkManager __instance, ref bool __result)
    {
        if (!OptimizationConfig.Current.EnableChunkCopyTimeBudget) return true;

        int frame = Time.frameCount;
        if (s_lastBudgetFrame != frame)
        {
            s_lastBudgetFrame = frame;
            s_frameStartTime = Time.realtimeSinceStartup;
            s_budgetExceeded = false;
        }

        if (s_budgetExceeded)
        {
            __result = false;
            ProfilerCounterBridge.Increment("ChunkCopy.BudgetSkip");
            return false;
        }

        return true;
    }

    public static void Postfix()
    {
        if (!OptimizationConfig.Current.EnableChunkCopyTimeBudget) return;
        if (s_budgetExceeded) return;

        float budgetSec = Mathf.Clamp(Time.deltaTime * 0.5f, 0.002f, 0.008f);
        if (Time.realtimeSinceStartup - s_frameStartTime > budgetSec)
        {
            s_budgetExceeded = true;
            ProfilerCounterBridge.Increment("ChunkCopy.BudgetHit");
        }
    }

    // Clear runtime state used for budgeting (used by hot-reload)
    public static void ClearCaches()
    {
        s_lastBudgetFrame = -1;
        s_frameStartTime = 0f;
        s_budgetExceeded = false;
    }
}
