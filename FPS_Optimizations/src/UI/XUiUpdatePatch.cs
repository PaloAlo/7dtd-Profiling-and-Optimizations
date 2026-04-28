// XUiUpdatePatch.cs
//
// Reduces unnecessary per-frame XUi controller work.
//
// XUiUpdater calls AlwaysUpdate() on each controller every frame to decide
// whether to run Update() when the window is not open.  Vanilla returns true
// for XUiC_Toolbelt (hotbar) and XUiC_Radial (hold-E menu), meaning they get
// a full DOM rebuild / input-poll every frame regardless of visible state.
//
// XUiC_Toolbelt — returning false is safe; it only needs to rebuild when item
//   data changes, which happens via the normal dirty-state system.
//
// XUiC_CompassWindow — AlwaysUpdate() already returns false in vanilla
//   (base XUiController default), so no patch is needed.
//
// XUiC_Radial — must NOT have AlwaysUpdate() → false because its Update()
//   does time-sensitive input polling (button-release detection, open-delay
//   timer).  Instead we throttle Update() itself to every 2 frames via a
//   Prefix patch.  At 60fps that is 30 polls/sec — still imperceptible for
//   hold-E detection and avoids touching 50% of frames.
//


using HarmonyLib;

[HarmonyPatch(typeof(XUiC_Toolbelt), "Update")]
public static class XUiToolbeltAlwaysUpdatePatch
{
    private static int s_frame;

    [HarmonyPrefix]
    public static bool Prefix()
    {
        if (!OptimizationConfig.Current.EnableXUiThrottle) return true;
        return (++s_frame & 1) == 0;   // run on even frames only
    }
}

[HarmonyPatch(typeof(XUiC_CompassWindow), "Update")]
public static class XUiCompassAlwaysUpdatePatch
{
    private static int s_frame;

    [HarmonyPrefix]
    public static bool Prefix()
    {
        if (!OptimizationConfig.Current.EnableXUiThrottle) return true;
        return (++s_frame & 1) == 0;   // run on even frames only
    }
}

[HarmonyPatch(typeof(XUiC_Radial), "Update")]
public static class XUiRadialUpdateThrottlePatch
{
    private static int s_frame;

    [HarmonyPrefix]
    public static bool Prefix()
    {
        if (!OptimizationConfig.Current.EnableXUiThrottle) return true;
        return (++s_frame & 1) == 0;   // run on even frames only
    }
}