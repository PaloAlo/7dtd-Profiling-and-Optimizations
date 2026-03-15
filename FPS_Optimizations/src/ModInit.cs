// ModInit.cs — FPS Optimizations mod entry point

using System.Reflection;
using HarmonyLib;

public class ModInit : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        OptimizationConfig.Load();

        var harmony = new Harmony("com.PaLoALo.fpsoptimizations.7dtd");
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        Log.Out($"[FPSOptimizations] Loaded. MoveLOD={OptimizationConfig.Current.EnableMoveLOD}, TargetCache={OptimizationConfig.Current.EnableTargetCache}");
    }
}
