// EAITaskEvaluationThrottlePatch.cs
//
// Placeholder — full re-evaluation throttling is DISABLED because a Prefix on
// EAIManager.Update cannot selectively skip only the task re-evaluation phase;
// returning false skips the active task's ContinueExecuting() too, which breaks
// combat.  UpdateTasksLODPatch handles the outer throttle instead.

using System.Collections.Generic;
using HarmonyLib;

[HarmonyPatch(typeof(EAIManager), nameof(EAIManager.Update))]
public static class EAITaskEvaluationThrottlePatch
{
    private static readonly Dictionary<int, int> s_lastEvalFrame = new Dictionary<int, int>(256);

    public static bool Prefix(EAIManager __instance)
    {
        return true;
    }

    public static void OnEntityRemoved(int entityId) => s_lastEvalFrame.Remove(entityId);
}
