// ModInit.cs — FPS Optimizations mod entry point

using System.Reflection;
using HarmonyLib;

public class ModInit : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        OptimizationConfig.Load();

        // Start file watcher for hot-reload (reads same folder used by Load)
        OptimizationConfig.StartFileWatcher();

        // resolve profiler bridge early so we can log availability
        ProfilerCounterBridge.EnsureResolved();

        var harmony = new Harmony("7dtd.PaLoALo.fps_optimizations");
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        var cfg = OptimizationConfig.Current;
        Log.Out($"[FPS_Optimizations] Loaded v{cfg.Version}. MoveLOD={cfg.EnableMoveLOD}, TargetCache={cfg.EnableTargetCache}, "
              + $"SleeperThrottle={cfg.EnableSleeperVolumeThrottle}, VehicleRBSleep={cfg.EnableVehicleRigidbodySleep}, "
              + $"JiggleBone={cfg.EnableJiggleBoneToggle}, ChunkBudget={cfg.EnableChunkCopyTimeBudget}, "
              + $"ChunkDir={cfg.EnableChunkDirectionalPriority}, ThreadPool={cfg.EnableThreadPoolConsolidation}, "
              + $"UAIThrottle={cfg.EnableUAIDecisionThrottle}, ParticleThrottle={cfg.EnableParticleThrottle}, "
              + $"ThreatLevelThrottle={cfg.EnableThreatLevelThrottle}, XUiThrottle={cfg.EnableXUiThrottle}@{cfg.XUiThrottleFPS}fps, "
              + $"AnimatorOptimize={cfg.EnableAnimatorOptimize}, LayerDistCull={cfg.EnableLayerDistanceCulling}");
    }
}
