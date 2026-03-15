// PhysicsTimingPatches.cs
//
// Targeted Harmony patches for common UnityEngine.Physics overloads to record timing.
// Guarded by ProfilerConfig.Current.EnableDeepPhysicsInstrumentation to avoid runtime overhead when disabled.

using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch]
static class PhysicsTimingPatches
{
    static MethodBase TargetMethod1()
    {
        var physics = typeof(Physics);
        var t = new Type[] { typeof(Vector3), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int) };
        return physics.GetMethod("Raycast", BindingFlags.Public | BindingFlags.Static, null, t, null);
    }

    static MethodBase TargetMethod2()
    {
        var physics = typeof(Physics);
        var t = new Type[] { typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int) };
        return physics.GetMethod("Raycast", BindingFlags.Public | BindingFlags.Static, null, t, null);
    }

    static MethodBase TargetMethod3()
    {
        var physics = typeof(Physics);
        var t = new Type[] { typeof(Vector3), typeof(Vector3), typeof(float), typeof(int), typeof(QueryTriggerInteraction) };
        return physics.GetMethod("Raycast", BindingFlags.Public | BindingFlags.Static, null, t, null);
    }

    static MethodBase TargetMethod4()
    {
        var physics = typeof(Physics);
        var t = new Type[] { typeof(Vector3), typeof(Vector3), typeof(float), typeof(int) };
        return physics.GetMethod("RaycastAll", BindingFlags.Public | BindingFlags.Static, null, t, null);
    }

    static MethodBase TargetMethod5()
    {
        var physics = typeof(Physics);
        var t = new Type[] { typeof(Vector3), typeof(float), typeof(int) };
        return physics.GetMethod("OverlapSphere", BindingFlags.Public | BindingFlags.Static, null, t, null);
    }

    // Each patch checks ProfilerConfig.Current.EnableDeepPhysicsInstrumentation and only samples when enabled.

    [HarmonyPatch]
    static class Patch1
    {
        static MethodBase TargetMethod() => TargetMethod1();
        static void Prefix(out long __state)
        {
            if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) { __state = 0; return; }
            ProfilingUtils.GenericPrefix(out __state);
        }
        static void Postfix(long __state, MethodBase __originalMethod)
        {
            if (__state == 0) return;
            ProfilingUtils.GenericPostfix(__state, __originalMethod);
        }
    }

    [HarmonyPatch]
    static class Patch2
    {
        static MethodBase TargetMethod() => TargetMethod2();
        static void Prefix(out long __state)
        {
            if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) { __state = 0; return; }
            ProfilingUtils.GenericPrefix(out __state);
        }
        static void Postfix(long __state, MethodBase __originalMethod)
        {
            if (__state == 0) return;
            ProfilingUtils.GenericPostfix(__state, __originalMethod);
        }
    }

    [HarmonyPatch]
    static class Patch3
    {
        static MethodBase TargetMethod() => TargetMethod3();
        static void Prefix(out long __state)
        {
            if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) { __state = 0; return; }
            ProfilingUtils.GenericPrefix(out __state);
        }
        static void Postfix(long __state, MethodBase __originalMethod)
        {
            if (__state == 0) return;
            ProfilingUtils.GenericPostfix(__state, __originalMethod);
        }
    }

    [HarmonyPatch]
    static class Patch4
    {
        static MethodBase TargetMethod() => TargetMethod4();
        static void Prefix(out long __state)
        {
            if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) { __state = 0; return; }
            ProfilingUtils.GenericPrefix(out __state);
        }
        static void Postfix(long __state, MethodBase __originalMethod)
        {
            if (__state == 0) return;
            ProfilingUtils.GenericPostfix(__state, __originalMethod);
        }
    }

    [HarmonyPatch]
    static class Patch5
    {
        static MethodBase TargetMethod() => TargetMethod5();
        static void Prefix(out long __state)
        {
            if (ProfilerConfig.Current == null || !ProfilerConfig.Current.EnableDeepPhysicsInstrumentation) { __state = 0; return; }
            ProfilingUtils.GenericPrefix(out __state);
        }
        static void Postfix(long __state, MethodBase __originalMethod)
        {
            if (__state == 0) return;
            ProfilingUtils.GenericPostfix(__state, __originalMethod);
        }
    }
}