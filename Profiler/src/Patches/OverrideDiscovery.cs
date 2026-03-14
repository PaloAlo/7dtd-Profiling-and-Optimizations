// OverrideDiscovery.cs
//
// Minimal helper to discover EntityAlive-derived types that declare overrides
// for ProcessDamageResponseLocal or DamageEntity(DamageSource,int,bool,float).
// Call OverrideDiscovery.FindEntityOverrideMethods() once at ModInit (after
// assemblies are loaded) to log and return the set of override types.

using System;
using System.Collections.Generic;
using System.Reflection;

internal static class OverrideDiscovery
{
    /// <summary>
    /// Scan loaded assemblies for types deriving from EntityAlive that declare
    /// an override of ProcessDamageResponseLocal or the 4-arg DamageEntity signature.
    /// Logs findings via LogUtil and returns a map of type -> list of overridden method names.
    /// </summary>
    public static Dictionary<Type, List<string>> FindEntityOverrideMethods()
    {
        var found = new Dictionary<Type, List<string>>();
        var baseType = typeof(EntityAlive);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }

            foreach (var t in types)
            {
                if (t == null) continue;
                if (!baseType.IsAssignableFrom(t)) continue;
                if (t == baseType) continue;

                var overrides = new List<string>();

                // Declared-only ProcessDamageResponseLocal override (any visibility)
                var proc = t.GetMethod(
                    "ProcessDamageResponseLocal",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (proc != null)
                    overrides.Add("ProcessDamageResponseLocal");

                // Declared-only DamageEntity with vanilla signature
                var dmg = t.GetMethod(
                    "DamageEntity",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    new Type[] { typeof(DamageSource), typeof(int), typeof(bool), typeof(float) },
                    null);
                if (dmg != null)
                    overrides.Add("DamageEntity(DamageSource,int,bool,float)");

                if (overrides.Count > 0)
                {
                    found[t] = overrides;
                    LogUtil.Info($"OverrideDiscovery: {t.FullName} overrides: {string.Join(", ", overrides)}");
                }
            }
        }

        if (found.Count == 0)
            LogUtil.Info("OverrideDiscovery: no EntityAlive-derived types with these overrides were found.");

        return found;
    }
}