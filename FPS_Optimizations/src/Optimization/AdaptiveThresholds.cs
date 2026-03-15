// AdaptiveThresholds.cs
//
// Dynamically adjusts emergency/critical zombie thresholds based on current
// FPS relative to baseline.  Uses sqrt scaling so high-FPS systems don't push
// thresholds absurdly high.

using UnityEngine;

public static class AdaptiveThresholds
{
    public static int EmergencyZombieThreshold { get; private set; } = 32;
    public static int CriticalZombieThreshold { get; private set; } = 64;

    private const int MIN_EMERGENCY = 20;
    private const int MAX_EMERGENCY = 48;
    private const int MIN_CRITICAL = 40;
    private const int MAX_CRITICAL = 80;

    private const float TARGET_FPS_FLOOR = 30f;
    private const float TARGET_FPS_COMFORTABLE = 50f;

    private static float s_baselineFps = 60f;
    private static float s_lastUpdateTime = -999f;
    private const float UPDATE_INTERVAL = 5f;

    public static void SetBaselineFps(float baseline)
    {
        if (baseline > 20f)
            s_baselineFps = baseline;
    }

    public static void Update(float currentFps, int currentZombieCount)
    {
        float now = Time.realtimeSinceStartup;
        bool normalTick = (now - s_lastUpdateTime >= UPDATE_INTERVAL);
        bool fpsEmergency = currentFps < TARGET_FPS_FLOOR && currentZombieCount > 15;

        if (!normalTick && !fpsEmergency) return;
        s_lastUpdateTime = now;

        float headroom = s_baselineFps - TARGET_FPS_FLOOR;
        if (headroom <= 0)
        {
            EmergencyZombieThreshold = MIN_EMERGENCY;
            CriticalZombieThreshold = MIN_CRITICAL;
            return;
        }

        float ratio = Mathf.Sqrt(headroom / 30f);

        int emergencyTarget = Mathf.RoundToInt(32f * ratio);
        int criticalTarget = Mathf.RoundToInt(64f * ratio);

        if (currentFps < TARGET_FPS_COMFORTABLE && currentZombieCount > 15)
        {
            float stressFactor = Mathf.Clamp01(currentFps / TARGET_FPS_COMFORTABLE);
            stressFactor *= stressFactor;
            emergencyTarget = Mathf.RoundToInt(emergencyTarget * stressFactor);
            criticalTarget = Mathf.RoundToInt(criticalTarget * stressFactor);
        }

        EmergencyZombieThreshold = Mathf.Clamp(emergencyTarget, MIN_EMERGENCY, MAX_EMERGENCY);
        CriticalZombieThreshold = Mathf.Clamp(criticalTarget, MIN_CRITICAL, MAX_CRITICAL);
    }
}
