// ModInit.cs — FPS Optimizations mod entry point

using System.Reflection;
using HarmonyLib;

public class ModInit : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        OptimizationConfig.Load();

        // resolve profiler bridge early so we can log availability
        ProfilerCounterBridge.EnsureResolved();

        var harmony = new Harmony("com.PaLoALo.fps_optimizations.7dtd");
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        Log.Out($"[FPS_Optimizations] Loaded. MoveLOD={OptimizationConfig.Current.EnableMoveLOD}, TargetCache={OptimizationConfig.Current.EnableTargetCache}");
    }
}
