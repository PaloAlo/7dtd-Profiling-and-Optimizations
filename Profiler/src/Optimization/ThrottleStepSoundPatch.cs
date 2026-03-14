// ThrottleStepSoundPatch.cs

using System.Collections.Concurrent;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), "updateStepSound")]
public static class ThrottleStepSoundPatch
{
    // Per-entity last-play time (seconds)
    private static readonly ConcurrentDictionary<int, float> LastPlayed = new ConcurrentDictionary<int, float>();

    // Minimum seconds between step sound calls for the same entity.
    // Tune as needed (0.05 = 20Hz per entity).
    private const float CooldownSeconds = 0.05f;

    // Prefix: return false to skip original method when within cooldown.
    public static bool Prefix(EntityAlive __instance)
    {
        if (__instance == null)
            return true; // nothing to do; allow original to run just in case

        int id;
        try
        {
            id = __instance.entityId;
        }
        catch
        {
            // If the field can't be accessed for some reason, don't interfere.
            return true;
        }

        float now = Time.time;
        if (LastPlayed.TryGetValue(id, out float last) && (now - last) < CooldownSeconds)
        {
            // Throttle: skip original method
            return false;
        }

        LastPlayed[id] = now;
        return true; // allow original updateStepSound to run
    }
}