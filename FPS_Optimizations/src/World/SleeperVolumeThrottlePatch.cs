// SleeperVolumeThrottlePatch.cs
//
// Throttles World.TickSleeperVolumes to run every 4th tick instead of
// every tick.  Sleeper volumes only need periodic checks (player
// proximity triggers), so running at quarter-rate is invisible.

using HarmonyLib;

[HarmonyPatch(typeof(World), "TickSleeperVolumes")]
public static class SleeperVolumeThrottlePatch
{
    private static int s_tickCounter;

    public static bool Prefix()
    {
        if (!OptimizationConfig.Current.EnableSleeperVolumeThrottle) return true;

        s_tickCounter++;
        if (s_tickCounter >= 4)
        {
            s_tickCounter = 0;
            return true;
        }

        ProfilerCounterBridge.Increment("SleeperVolume.Skipped");
        return false;
    }
}
