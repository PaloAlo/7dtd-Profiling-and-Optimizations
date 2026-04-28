// OcclusionLimitFix.cs
//
// Prevents the OcclusionManager pool from exhausting when many entities are
// active.  When freeEntries runs out the vanilla code silently stops
// registering new entities — those entities never get occlusion-culled and
// are rendered even when behind walls.  This patch recycles the oldest used
// entry so the pool never empties, keeping all entities in the occlusion
// system and maintaining GPU draw-call reduction during large hordes.
//
// Adapted from Redbeardt's Afterlife mod (OcclusionLimitFix.cs).

using System.Linq;
using HarmonyLib;

[HarmonyPatch(typeof(OcclusionManager))]
[HarmonyPatch("OnEnable")]
public static class OcclusionLimitFix_OnEnable
{
    [HarmonyPrefix]
    public static void Prefix() => OcclusionLimitFix.EnsureEntryAvailable();
}

[HarmonyPatch(typeof(OcclusionManager))]
[HarmonyPatch("AddEntity")]
public static class OcclusionLimitFix_AddEntity
{
    [HarmonyPrefix]
    public static void Prefix() => OcclusionLimitFix.EnsureEntryAvailable();
}

[HarmonyPatch(typeof(OcclusionManager))]
[HarmonyPatch("UpdateZoneRegistration")]
public static class OcclusionLimitFix_UpdateZoneRegistration
{
    [HarmonyPrefix]
    public static void Prefix() => OcclusionLimitFix.EnsureEntryAvailable();
}

public static class OcclusionLimitFix
{
    public static void EnsureEntryAvailable()
    {
        OcclusionManager mgr = OcclusionManager.Instance;
        if (mgr == null) return;
        if (mgr.freeEntries.Count > 0) return;

        // Pool exhausted — recycle the oldest used entry so new entities
        // can still be registered and properly occlusion-culled.
        OcclusionManager.OcclusionEntry oldest = mgr.usedEntries.FirstOrDefault();
        if (oldest == null) return;
        mgr.usedEntries.Remove(oldest);
        mgr.freeEntries.AddLast(oldest);
    }
}
