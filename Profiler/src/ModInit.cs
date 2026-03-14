// ModInit.cs - Profiler mod initialization

using System.Reflection;
using HarmonyLib;

public class ModInit : IModApi
{
    public static string ModsFolderPath { get; set; }
    
    public void InitMod(Mod _modInstance)
    {
        var modName = _modInstance?.Name ?? "Profiler";
        LogUtil.Prefix = $"[{modName}]";

        Mod mod = ModManager.GetMod(modName);
        ModsFolderPath = mod.Path;
        
        // Load config
        ProfilerConfig.Load();
        ProfilingUtils.ModInstance = _modInstance;
        ProfilerRunner.Init(_modInstance);

        // Apply Harmony patches
        var harmony = new Harmony("com.PaLoALo.profiler.7dtd");
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        // Initialize systems
        SpatialGridManager.Init();

        Log.Out($"Profiler loaded. Profiling={ProfilerConfig.Current.EnableProfiling}, SpatialGrid={ProfilerConfig.Current.EnableSpatialGrid}");
        Log.Out($"MoveLOD={ProfilerConfig.Current.EnableMoveLOD}, TargetCache={ProfilerConfig.Current.EnableTargetCache}");
    }
}