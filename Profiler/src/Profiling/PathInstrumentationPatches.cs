// PathInstrumentationPatches.cs
// Lightweight prefix-only patches that increment per-frame counters (no behavior change).
// Added Timing prefixes/postfixes for updateTasks and its heavy subcalls so ProfilingUtils collects timing.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

[HarmonyPatch]
static class PathInstrumentationPatches
{
    [HarmonyPatch]
    static class Patch_FindPath
    {
        static MethodBase TargetMethod()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var a in assemblies)
            {
                try
                {
                    if (!string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase)) continue;
                    var t = a.GetType("PathFinderThread") ?? a.GetType("GamePath.PathFinderThread");
                    if (t == null) continue;
                    // find method named "FindPath" (any overload)
                    var m = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                             .FirstOrDefault(mi => string.Equals(mi.Name, "FindPath", StringComparison.Ordinal));
                    if (m != null) return m;
                }
                catch { }
            }
            return null;
        }

        static void Prefix() { try { ProfilingUtils.PerFrameCounters.Increment("pathRequests"); } catch { } }
    }

    [HarmonyPatch]
    static class Patch_SetPathOrUpdateNavigation
    {
        static MethodBase TargetMethod()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var a in assemblies)
            {
                try
                {
                    if (!string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase)) continue;
                    var t = a.GetType("PathNavigate") ?? a.GetType("GamePath.PathNavigate");
                    if (t == null) continue;

                    // Prefer SetPath, fallback to UpdateNavigation
                    var m = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                             .FirstOrDefault(mi => string.Equals(mi.Name, "SetPath", StringComparison.Ordinal));
                    if (m != null) return m;

                    m = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                         .FirstOrDefault(mi => string.Equals(mi.Name, "UpdateNavigation", StringComparison.Ordinal));
                    if (m != null) return m;
                }
                catch { }
            }
            return null;
        }

        static void Prefix() { try { ProfilingUtils.PerFrameCounters.Increment("navigatorSetPathCalls"); } catch { } }
    }

    [HarmonyPatch]
    static class Patch_PathFinderCalculate
    {
        static MethodBase TargetMethod()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var a in assemblies)
            {
                try
                {
                    if (!string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase)) continue;
                    var t = a.GetType("PathFinder") ?? a.GetType("GamePath.PathFinder");
                    if (t == null) continue;
                    var m = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                             .FirstOrDefault(mi => string.Equals(mi.Name, "Calculate", StringComparison.Ordinal));
                    if (m != null) return m;
                }
                catch { }
            }
            return null;
        }

        static void Prefix() { try { ProfilingUtils.PerFrameCounters.Increment("pathCalculations"); } catch { } }
    }

    //
    // New: Light timing instrumentation (BeginSample/EndSample) for updateTasks and heavy subcalls.
    // These use ProfilingUtils so results appear in the same CSV/summary mechanisms.
    //

    [HarmonyPatch]
    static class Patch_EntityAlive_updateTasks
    {
        static MethodBase TargetMethod()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));
                if (asm == null) return null;
                var t = asm.GetType("EntityAlive") ?? asm.GetType("GamePath.EntityAlive") ?? asm.GetTypes().FirstOrDefault(x => x.Name == "EntityAlive");
                if (t == null) return null;
                var m = t.GetMethod("updateTasks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                return m;
            }
            catch { return null; }
        }

        static void Prefix(out long __state)
        {
            try
            {
                ProfilingUtils.PerFrameCounters.Increment("updateTasks.calls");
                __state = ProfilingUtils.BeginSample();
            }
            catch { __state = 0; }
        }

        static void Postfix(long __state)
        {
            try
            {
                if (__state != 0) ProfilingUtils.EndSample("EntityAlive.updateTasks", __state);
            }
            catch { }
        }
    }

    [HarmonyPatch]
    static class Patch_EAIManager_Update
    {
        static MethodBase TargetMethod()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));
                if (asm == null) return null;
                var t = asm.GetType("EAIManager") ?? asm.GetTypes().FirstOrDefault(x => x.Name == "EAIManager");
                if (t == null) return null;
                return t.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch { return null; }
        }

        static void Prefix(out long __state)
        {
            try { __state = ProfilingUtils.BeginSample(); } catch { __state = 0; }
        }

        static void Postfix(long __state)
        {
            try { if (__state != 0) ProfilingUtils.EndSample("EAIManager.Update", __state); } catch { }
        }
    }

    [HarmonyPatch]
    static class Patch_UAIBase_Update
    {
        static MethodBase TargetMethod()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));
                if (asm == null) return null;
                // UAI path may be in a UAI namespace or class named UAIBase
                var t = asm.GetType("UAIBase") ?? asm.GetTypes().FirstOrDefault(x => x.Name == "UAIBase");
                if (t == null) return null;
                return t.GetMethod("Update", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) 
                       ?? t.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch { return null; }
        }

        static void Prefix(out long __state)
        {
            try { __state = ProfilingUtils.BeginSample(); } catch { __state = 0; }
        }

        static void Postfix(long __state)
        {
            try { if (__state != 0) ProfilingUtils.EndSample("UAIBase.Update", __state); } catch { }
        }
    }

    [HarmonyPatch]
    static class Patch_PathFinderThread_GetPath
    {
        static MethodBase TargetMethod()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));
                if (asm == null) return null;
                var t = asm.GetType("PathFinderThread") ?? asm.GetType("GamePath.PathFinderThread") ?? asm.GetTypes().FirstOrDefault(x => x.Name == "PathFinderThread");
                if (t == null) return null;
                // GetPath is often an instance method on the singleton instance
                var m = t.GetMethod("GetPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null) return m;
                return t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(mi => mi.Name == "GetPath");
            }
            catch { }
            return null;
        }

        static void Prefix(out long __state)
        {
            try { __state = ProfilingUtils.BeginSample(); } catch { __state = 0; }
        }

        static void Postfix(long __state)
        {
            try { if (__state != 0) ProfilingUtils.EndSample("PathFinderThread.GetPath", __state); } catch { }
        }
    }

    [HarmonyPatch]
    static class Patch_PathNavigate_UpdateNavigation
    {
        static MethodBase TargetMethod()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));
                if (asm == null) return null;
                var t = asm.GetType("PathNavigate") ?? asm.GetType("GamePath.PathNavigate") ?? asm.GetTypes().FirstOrDefault(x => x.Name == "PathNavigate");
                if (t == null) return null;
                var m = t.GetMethod("UpdateNavigation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return m;
            }
            catch { }
            return null;
        }

        static void Prefix(out long __state)
        {
            try { __state = ProfilingUtils.BeginSample(); } catch { __state = 0; }
        }

        static void Postfix(long __state)
        {
            try { if (__state != 0) ProfilingUtils.EndSample("PathNavigate.UpdateNavigation", __state); } catch { }
        }
    }

    [HarmonyPatch]
    static class Patch_PathNavigate_SetPath
    {
        static MethodBase TargetMethod()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));
                if (asm == null) return null;
                var t = asm.GetType("PathNavigate") ?? asm.GetType("GamePath.PathNavigate") ?? asm.GetTypes().FirstOrDefault(x => x.Name == "PathNavigate");
                if (t == null) return null;
                var m = t.GetMethod("SetPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return m;
            }
            catch { }
            return null;
        }

        static void Prefix(out long __state)
        {
            try { __state = ProfilingUtils.BeginSample(); } catch { __state = 0; }
        }

        static void Postfix(long __state)
        {
            try { if (__state != 0) ProfilingUtils.EndSample("PathNavigate.SetPath", __state); } catch { }
        }
    }

    [HarmonyPatch]
    static class Patch_EntityMoveHelper_UpdateMoveHelper
    {
        static MethodBase TargetMethod()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));
                if (asm == null) return null;
                var t = asm.GetType("EntityMoveHelper") ?? asm.GetTypes().FirstOrDefault(x => x.Name == "EntityMoveHelper");
                if (t == null) return null;
                return t.GetMethod("UpdateMoveHelper", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch { }
            return null;
        }

        static void Prefix(out long __state)
        {
            try { __state = ProfilingUtils.BeginSample(); } catch { __state = 0; }
        }

        static void Postfix(long __state)
        {
            try { if (__state != 0) ProfilingUtils.EndSample("EntityMoveHelper.UpdateMoveHelper", __state); } catch { }
        }
    }

    [HarmonyPatch]
    static class Patch_EntityLookHelper_onUpdateLook
    {
        static MethodBase TargetMethod()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));
                if (asm == null) return null;
                var t = asm.GetType("EntityLookHelper") ?? asm.GetTypes().FirstOrDefault(x => x.Name == "EntityLookHelper");
                if (t == null) return null;
                return t.GetMethod("onUpdateLook", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch { }
            return null;
        }

        static void Prefix(out long __state)
        {
            try { __state = ProfilingUtils.BeginSample(); } catch { __state = 0; }
        }

        static void Postfix(long __state)
        {
            try { if (__state != 0) ProfilingUtils.EndSample("EntityLookHelper.onUpdateLook", __state); } catch { }
        }
    }
}