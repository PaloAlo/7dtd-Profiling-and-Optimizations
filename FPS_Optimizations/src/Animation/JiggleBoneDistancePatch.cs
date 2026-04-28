// JiggleBoneDistancePatch.cs
//
// Toggles EModelBase.JiggleOn(false) for entities beyond 50 m.
// Jiggle bone simulation (secondary motion on zombie limbs/clothing) is
// invisible at distance but still costs a transform update every frame.
//
// Uses EntityBudgetSystem tiers — Critical/High (< 50 m) keep jiggle;
// Medium/Low (>= 50 m) have it disabled.  Checked once every ~30 frames
// per entity to minimise overhead.

using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.Update))]
public static class JiggleBoneDistancePatch
{
    private const float JIGGLE_ON_DIST_SQ = 2500f; // 50 m

    [HarmonyPostfix]
    public static void Postfix(EntityAlive __instance)
    {
        if (__instance == null || __instance is EntityPlayer) return;
        if (!OptimizationConfig.Current.EnableJiggleBoneToggle) return;

        try
        {
            if ((Time.frameCount + __instance.entityId) % 30 != 0) return;

            if (__instance.emodel == null) return;
            if (!FrameCache.HasPlayer) return;

            float distSq = (__instance.position - FrameCache.PlayerPosition).sqrMagnitude;
            bool shouldJiggle = distSq < JIGGLE_ON_DIST_SQ;
            __instance.emodel.JiggleOn(shouldJiggle);

            if (!shouldJiggle)
                ProfilerCounterBridge.Increment("JiggleBone.Disabled");
        }
        catch { }
    }
}
