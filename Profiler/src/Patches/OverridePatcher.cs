// OverridePatcher.cs
//
// Dynamically patches declared overrides discovered by OverrideDiscovery.
// Safe: only patches methods that actually exist on the discovered types.

using System;
using System.Reflection;
using HarmonyLib;

internal static class OverridePatcher
{
    // Call this once from ModInit (after assemblies are loaded and Harmony created).
    public static void PatchDiscoveredOverrides(Harmony harmony)
    {
        var found = OverrideDiscovery.FindEntityOverrideMethods();
        if (found == null || found.Count == 0) return;

        var prefixMethod = new HarmonyMethod(typeof(OverridePatcher).GetMethod(
            nameof(GenericDamageEntityPrefix), BindingFlags.Static | BindingFlags.NonPublic));
        var postfixMethod = new HarmonyMethod(typeof(OverridePatcher).GetMethod(
            nameof(GenericProcessDamagePostfix), BindingFlags.Static | BindingFlags.NonPublic));

        foreach (var kv in found)
        {
            var t = kv.Key;
            var overrides = kv.Value;

            if (overrides.Contains("DamageEntity(DamageSource,int,bool,float)"))
            {
                var orig = t.GetMethod(
                    nameof(EntityAlive.DamageEntity),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    new Type[] { typeof(DamageSource), typeof(int), typeof(bool), typeof(float) },
                    null);
                if (orig != null)
                {
                    try
                    {
                        harmony.Patch(orig, prefix: prefixMethod);
                        LogUtil.Info($"OverridePatcher: patched {t.FullName}.DamageEntity (prefix)");
                    }
                    catch (Exception ex)
                    {
                        LogUtil.Warn($"OverridePatcher: failed to patch {t.FullName}.DamageEntity: {ex.Message}");
                    }
                }
            }

            if (overrides.Contains("ProcessDamageResponseLocal"))
            {
                var orig2 = t.GetMethod(
                    "ProcessDamageResponseLocal",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (orig2 != null)
                {
                    try
                    {
                        harmony.Patch(orig2, postfix: postfixMethod);
                        LogUtil.Info($"OverridePatcher: patched {t.FullName}.ProcessDamageResponseLocal (postfix)");
                    }
                    catch (Exception ex)
                    {
                        LogUtil.Warn($"OverridePatcher: failed to patch {t.FullName}.ProcessDamageResponseLocal: {ex.Message}");
                    }
                }
            }
        }
    }
}