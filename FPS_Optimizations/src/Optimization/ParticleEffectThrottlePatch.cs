// ParticleEffectThrottlePatch.cs
//
// Reduces particle effect overhead during high-zombie-count scenarios.
//
// Problem: The game's ParticleEffect.SpawnParticleEffect is called
// periodically by buff-driven effects (e.g. p_bleeding every 4 buff ticks).
// Each call enters one of two paths:
//   Reuse:  GetComponentsInChildren<ParticleSystem>()  (GC alloc)
//           + GetComponentsInChildren<TemporaryObject>() (GC alloc)
//           + Clear()/Play() restart on every child system
//   New:    Object.Instantiate (deep copy of prefab hierarchy)
//           + GetComponentsInChildren<Renderer>() (GC alloc)
//
// During horde combat the player is usually bleeding (p_bleeding attached)
// at the exact moment distance-based throttles are least effective because
// zombies are in the Critical tier.  This is the "double whammy."
//
// Optimisations:
//   1. Skip redundant respawns — if the entity already has an active
//      instance of the same particle type, short-circuit the call entirely.
//      The existing particles keep playing; no allocations, no restarts.
//   2. Emission scaling — when a NEW particle IS spawned during load,
//      scale down emission rate and max-particles so the GPU renders
//      fewer sprites (the dominant rendering cost for near-camera effects).
//
// Both layers are gated on EnableParticleThrottle + a zombie-count
// threshold, so they only engage when the player is under real stress.

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(ParticleEffect), nameof(ParticleEffect.SpawnParticleEffect))]
public static class ParticleEffectThrottlePatch
{
    // Track last-spawn frame per (entity, particleId) to avoid rapid respawns
    private static readonly Dictionary<long, int> s_lastSpawnFrame = new Dictionary<long, int>();
    private static int s_cleanupFrame;

    [HarmonyPrefix]
    public static bool Prefix(
        ParticleEffect _pe,
        int _entityThatCausedIt,
        bool _forceCreation,
        ref Transform __result)
    {
        if (!OptimizationConfig.Current.EnableParticleThrottle) return true;
        if (_forceCreation) return true;
        if (_entityThatCausedIt == -1) return true;
        if (_pe.ParticleId == 0) return true;

        try
        {
            FrameCache.EnsureUpdated();
            int zombieCount = FrameCache.ZombieCount;
            if (zombieCount < OptimizationConfig.Current.ParticleThrottleZombieThreshold)
                return true;

            // Don't skip particles that carry a sound — the sound still needs to play
            if (!string.IsNullOrEmpty(_pe.soundName)) return true;

            // Check entityParticles to see if this entity already has an
            // active (non-destroyed) instance of this particle type.
            var entityParticles = ParticleEffect.entityParticles;
            if (entityParticles != null &&
                entityParticles.TryGetValue(_entityThatCausedIt, out var list))
            {
                bool hasActive = false;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var ed = list[i];
                    if (ed.id == _pe.ParticleId && ed.t)
                    {
                        hasActive = true;
                        break;
                    }
                }

                if (hasActive)
                {
                    // Throttle: skip this respawn — existing particles are still running
                    long key = ((long)_entityThatCausedIt << 32) | (uint)_pe.ParticleId;
                    int frame = Time.frameCount;

                    // Allow through at a reduced rate instead of blocking entirely.
                    // At 60 FPS the buff fires roughly once/4 sec.  We allow every
                    // other respawn (~8 sec cadence) so the effect refreshes visually
                    // but at half the frequency.
                    int minGapFrames = OptimizationConfig.Current.ParticleThrottleMinGapFrames;

                    if (s_lastSpawnFrame.TryGetValue(key, out int lastFrame) &&
                        frame - lastFrame < minGapFrames)
                    {
                        ProfilerCounterBridge.Increment("Particle.SkippedRespawn");
                        __result = null;
                        return false;
                    }

                    s_lastSpawnFrame[key] = frame;
                }
            }

            PeriodicCleanup();
        }
        catch
        {
            // Safety: never break vanilla on unexpected errors
        }

        return true;
    }

    [HarmonyPostfix]
    public static void Postfix(ParticleEffect _pe, ref Transform __result)
    {
        if (__result == null) return;
        if (!OptimizationConfig.Current.EnableParticleThrottle) return;

        try
        {
            FrameCache.EnsureUpdated();
            int zombieCount = FrameCache.ZombieCount;
            int threshold = OptimizationConfig.Current.ParticleThrottleZombieThreshold;
            if (zombieCount < threshold) return;

            float minScale = OptimizationConfig.Current.ParticleEmissionScaleMin;
            if (minScale >= 1f) return;

            // Interpolate: at threshold → scale=1.0, at 2×threshold → scale=minScale
            float t = Mathf.Clamp01((float)(zombieCount - threshold) / threshold);
            float scale = Mathf.Lerp(1f, minScale, t);
            if (scale >= 0.99f) return;

            var systems = __result.GetComponentsInChildren<ParticleSystem>();
            for (int i = 0; i < systems.Length; i++)
            {
                var ps = systems[i];

                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= scale;
                emission.rateOverDistanceMultiplier *= scale;

                var main = ps.main;
                int scaledMax = Mathf.Max(1, (int)(main.maxParticles * scale));
                main.maxParticles = scaledMax;
            }

            ProfilerCounterBridge.Increment("Particle.EmissionScaled");
        }
        catch { }
    }

    private static void PeriodicCleanup()
    {
        int frame = Time.frameCount;
        if (frame - s_cleanupFrame < 600) return; // ~10 sec at 60 fps
        s_cleanupFrame = frame;

        var toRemove = new List<long>();
        foreach (var kvp in s_lastSpawnFrame)
        {
            if (frame - kvp.Value > 1200) // stale after ~20 sec
                toRemove.Add(kvp.Key);
        }
        for (int i = 0; i < toRemove.Count; i++)
            s_lastSpawnFrame.Remove(toRemove[i]);
    }

    public static void ClearCaches()
    {
        s_lastSpawnFrame.Clear();
    }

    public static void OnEntityRemoved(int entityId)
    {
        // Remove all entries for this entity (upper 32 bits of key)
        var toRemove = new List<long>();
        foreach (var kvp in s_lastSpawnFrame)
        {
            if ((int)(kvp.Key >> 32) == entityId)
                toRemove.Add(kvp.Key);
        }
        for (int i = 0; i < toRemove.Count; i++)
            s_lastSpawnFrame.Remove(toRemove[i]);
    }
}
