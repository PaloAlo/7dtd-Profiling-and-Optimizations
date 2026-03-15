// DeepEntityInstrumentation.cs
//
// Deep instrumentation to identify exactly what sub-operations are causing
// EntityPlayerLocal.Update and EntityAlive.Update to scale poorly with zombie count.
// 
// Key insight from profiling: EntityPlayerLocal.Update dominates (50%+) and scales
// with zombie count, which is unexpected. We need to find what inside that method
// is iterating over entities.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

/// <summary>
/// Deep instrumentation patches for EntityPlayerLocal and EntityAlive sub-methods.
/// These patches identify which internal operations scale with entity count.
/// </summary>
public static class DeepEntityInstrumentation
{
    private static bool _applied = false;

    /// <summary>
    /// Apply deep instrumentation patches. Call this from ProfilerRunner after base patches.
    /// </summary>
    public static void Apply(Harmony harmony)
    {
        if (_applied || harmony == null) return;

        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase));
        if (asm == null)
        {
            LogUtil.Warn("Assembly-CSharp not found.");
            return;
        }

        // Target methods inside EntityPlayerLocal that might iterate entities
        var playerLocalTargets = new[]
        {
            "updateLocalPlayerTickData",
            "updateMoveInputFromInput", 
            "updateCameraPosition",
            "updateStats",
            "updateStatsLocal",
            "updateBuffs",
            "updateBuffsLocal",
            "updateLocalPlayer",
            "onUpdateLive",
            "UpdatePlayerTick",
            "CheckCollisionWithEntities",
            "updateNearbyEntities",
            "BlockRadiusEffectsTick",
            "ShelterFrameUpdate",
            "GetNearbyEntities",
            "GetEntitiesInRadius",
            "UpdateStatusEffects",
            // Additional player sub-methods that may scale with entity count
            "updateSenses",
            "updateCurrentMovementStyle",
            "updateAnimation",
            "updatePhysics",
            "updateStepSound",
            "MoveEntityHeaded",
            "updateSpeedForwardAndStrafe",
            "updateCurrentBlockPosAndValue",
            "checkAutoAim",
            "updateFootSound"
        };

        // Target methods inside EntityAlive that could be expensive
        var entityAliveTargets = new[]
        {
            "onUpdateLive",
            "updateCurrentMovementStyle",
            "updateStepSound",
            "updateFootSound",
            "updateSenses",
            "updateStatus",
            "updateAnimation",
            "updatePhysics",
            "updateBuffs",
            "GetAttackTarget",
            "GetRevengeTarget",
            "CanSee",
            "CheckForDamage",
            "updateDismember",
            "MoveEntityHeaded", // ADDED: instrument movement core
            "updateTasks"       // ensure updateTasks also instrumented if present
        };

        // Target methods in EAIManager/AI system
        var aiTargets = new[]
        {
            ("EAIManager", "ExecuteTasks"),
            ("EAIManager", "UpdateTasks"),
            ("EAIManager", "FindTask"),
            ("EAISetNearestEntityAsTarget", "CanExecute"),
            ("EAISetNearestEntityAsTarget", "Execute"),
            ("EAIApproachAndAttackTarget", "Execute"),
            ("EAIApproachAndAttackTarget", "CanExecute"),
            ("EAIRunawayWhenHurt", "Execute"),
            ("EAIBreakBlock", "Execute"),
            ("EAIFollowTarget", "Execute"),
            ("EAIWander", "Execute")
        };

        var prefix = new HarmonyMethod(typeof(DeepEntityInstrumentation).GetMethod(nameof(GenericTimingPrefix), BindingFlags.Public | BindingFlags.Static));
        var postfix = new HarmonyMethod(typeof(DeepEntityInstrumentation).GetMethod(nameof(GenericTimingPostfix), BindingFlags.Public | BindingFlags.Static));

        int patchCount = 0;

        // Patch EntityPlayerLocal methods
        var playerLocalType = asm.GetType("EntityPlayerLocal");
        if (playerLocalType != null)
        {
            patchCount += PatchMethods(harmony, playerLocalType, playerLocalTargets, prefix, postfix, "PlayerLocal");
        }

        // Patch EntityAlive methods  
        var entityAliveType = asm.GetType("EntityAlive");
        if (entityAliveType != null)
        {
            patchCount += PatchMethods(harmony, entityAliveType, entityAliveTargets, prefix, postfix, "EntityAlive");
        }

        // Patch AI system methods
        foreach (var (typeName, methodName) in aiTargets)
        {
            var t = asm.GetType(typeName) ?? FindTypeByShortName(asm, typeName);
            if (t != null)
            {
                patchCount += PatchMethods(harmony, t, new[] { methodName }, prefix, postfix, typeName);
            }
        }

        _applied = true;
        LogUtil.Info($"Applied {patchCount} deep instrumentation patches.");
    }

    private static int PatchMethods(Harmony harmony, Type type, string[] methodNames, HarmonyMethod prefix, HarmonyMethod postfix, string tagPrefix)
    {
        int count = 0;
        foreach (var methodName in methodNames)
        {
            try
            {
                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == methodName && !m.IsAbstract && m.DeclaringType == type)
                    .ToArray();

                foreach (var method in methods)
                {
                    try
                    {
                        // Store the tag for this method so postfix knows what to record
                        var tag = $"{tagPrefix}.{methodName}";
                        _methodTags[method] = tag;
                        
                        harmony.Patch(method, prefix, postfix);
                        count++;
                        LogUtil.Debug($"Patched {tag}");
                    }
                    catch (Exception ex)
                    {
                        LogUtil.Debug($"Failed to patch {type.Name}.{methodName}: {ex.Message}");
                    }
                }
            }
            catch { }
        }
        return count;
    }

    private static Type FindTypeByShortName(Assembly asm, string shortName)
    {
        try
        {
            return asm.GetTypes().FirstOrDefault(t => t.Name == shortName);
        }
        catch { return null; }
    }

    // Store method -> tag mapping for postfix lookup
    private static readonly Dictionary<MethodBase, string> _methodTags = new Dictionary<MethodBase, string>();

    public static void GenericTimingPrefix(MethodBase __originalMethod, out long __state)
    {
        __state = 0;
        if (!ProfilerConfig.Current.EnableProfiling) return;
        try
        {
            __state = System.Diagnostics.Stopwatch.GetTimestamp();

            // Track call counts for correlation analysis
            if (_methodTags.TryGetValue(__originalMethod, out var tag))
            {
                ProfilingUtils.PerFrameCounters.Increment($"{tag}.calls");
            }
        }
        catch { }
    }

    public static void GenericTimingPostfix(MethodBase __originalMethod, long __state)
    {
        if (__state == 0) return;
        try
        {
            if (_methodTags.TryGetValue(__originalMethod, out var tag))
            {
                ProfilingUtils.EndSample(tag, __state);
            }
        }
        catch { }
    }
}