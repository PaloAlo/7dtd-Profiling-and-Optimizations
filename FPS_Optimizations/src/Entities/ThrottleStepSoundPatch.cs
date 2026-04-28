// ThrottleStepSoundPatch.cs
//
// Simple cooldown on updateStepSound — prevents redundant sound triggers
// within 0.05 s for the same entity.

using System.Collections.Concurrent;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), "updateStepSound")]
public static class ThrottleStepSoundPatch
{
    private static readonly ConcurrentDictionary<int, float> LastPlayed = new ConcurrentDictionary<int, float>();
    private const float CooldownSeconds = 0.05f;

    public static bool Prefix(EntityAlive __instance)
    {
        if (__instance == null) return true;

        int id;
        try { id = __instance.entityId; }
        catch { return true; }

        float now = Time.time;
        if (LastPlayed.TryGetValue(id, out float last) && (now - last) < CooldownSeconds)
            return false;

        LastPlayed[id] = now;
        return true;
    }

    public static void ClearCaches() => LastPlayed.Clear();
}
