// ThreadPoolConsolidationPatch.cs
//
// Redirects bursty game threads from dedicated OS threads onto the .NET
// thread pool.  This avoids the overhead of persistent threads that sit
// idle between bursts and lets the CLR scheduler balance work across
// available cores.
//
// The original ThreadManager.StartThread has a _useRealThread parameter;
// setting it to false makes the call use ThreadPool.QueueUserWorkItem
// instead of creating a new Thread.
//
// Consolidated threads (all bursty compute or I/O):
//   ChunkCalc                     – chunk mesh calculation
//   ChunkMeshBake                 – chunk mesh baking
//   ChunkRegeneration             – chunk regeneration
//   GenerateChunks                – progressive world generation
//   SaveChunks                    – chunk data saving (I/O burst)
//   WaterSimulationApplyChanges   – water flow simulation
//
// Excluded (latency-sensitive network I/O — must keep dedicated threads):
//   NCS_Reader_ / NCS_Writer_
//   NCSteam_Reader_ / NCSteam_Writer_
//   SteamNetworkingClient / SteamNetworkingServer

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

[HarmonyPatch]
public static class ThreadPoolConsolidationPatch
{
    // Threads that benefit from pooling (bursty compute / I/O with idle gaps).
    // "SaveChunks " has a trailing space in the game code — we use StartsWith
    // so both "SaveChunks" and "SaveChunks " match safely.
    private static readonly HashSet<string> s_exactPoolTargets = new HashSet<string>
    {
        "ChunkCalc",
        "ChunkMeshBake",
        "ChunkRegeneration",
        "GenerateChunks",
        "WaterSimulationApplyChanges",
    };

    static MethodBase TargetMethod()
    {
        // All 6 target threads call the 8-parameter overload:
        //   StartThread(string, ThreadFunctionDelegate, ThreadFunctionLoopDelegate,
        //               ThreadFunctionEndDelegate, object, ExitCallbackThread, bool, bool)
        // The 6-param and 9-param overloads ultimately funnel to the same private
        // startThread, but patching the 8-param public entry point is sufficient
        // because every target thread's call site uses this overload directly.
        foreach (var m in AccessTools.GetDeclaredMethods(typeof(ThreadManager)))
        {
            if (m.Name == "StartThread")
            {
                var parms = m.GetParameters();
                if (parms.Length == 8 && parms[0].ParameterType == typeof(string))
                    return m;
            }
        }

        Log.Warning("[FPSOptimizations] ThreadPoolConsolidation: StartThread(8) not found — patch inactive");
        return null;
    }

    public static void Prefix(string _name, ref bool _useRealThread)
    {
        if (!OptimizationConfig.Current.EnableThreadPoolConsolidation) return;
        if (!_useRealThread) return; // already pooled, nothing to do

        if (s_exactPoolTargets.Contains(_name) || _name.StartsWith("SaveChunks"))
        {
            _useRealThread = false;
            ProfilerCounterBridge.Increment("ThreadPool.Consolidated");
        }
    }
}
