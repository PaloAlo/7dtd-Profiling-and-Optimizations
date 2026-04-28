// XUiThrottlePatch.cs
//
// Throttles XUiUpdater.Update() to a configurable FPS cap (default 45).
// The HUD/inventory UI (health bar, hotbar, compass, etc.) is imperceptible
// above ~30 fps; running it every game frame at 60-120 fps wastes CPU on
// DOM dirtying and layout recalculation.
//
// Bypass conditions (full-speed passthrough):
//   - Cursor is visible (menu open)
//   - A "timer" window is open (countdown UI needs real-time updates)
//   - A "focusedBlockHealth" window is open (block-damage feedback)
//   - The XUi update queue is empty
//
// Adapted from Redbeardt's Afterlife mod (XUiUpdaterThrottling.cs), rewritten
// to use OptimizationConfig, avoid the manual foreach loop duplication, and
// hook cleanly into the existing config/hot-reload system.

using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(XUiUpdater), nameof(XUiUpdater.Update))]
public static class XUiThrottlePatch
{
    private static float s_lastUpdateTime;

    public static bool Prefix()
    {
        if (!OptimizationConfig.Current.EnableXUiThrottle) return true;
        if (XUiUpdater.uiToUpdate.Count <= 0) return false;

        LocalPlayerUI ui = LocalPlayerUI.GetUIForPrimaryPlayer();
        if (ui == null) return true;

        // Always run at full speed when cursor/menu is visible or a reactive
        // window is open — player is actively interacting with UI.
        if (GameManager.Instance.bCursorVisible) return true;
        if (ui.windowManager.IsWindowOpen("timer")) return true;
        if (ui.windowManager.IsWindowOpen("focusedBlockHealth")) return true;

        float interval = 1f / OptimizationConfig.Current.XUiThrottleFPS;
        float elapsed = Time.time - s_lastUpdateTime;
        if (elapsed < interval) return false;

        s_lastUpdateTime = Time.time;
        return true;
    }

    public static void ClearCaches() => s_lastUpdateTime = 0f;
}
