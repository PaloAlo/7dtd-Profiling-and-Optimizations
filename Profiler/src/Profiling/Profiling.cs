// Profiling.cs
//
// Lightweight profiling utilities, per-frame counters and a small dynamic Harmony patcher.
// Minimal, allocation-friendly designs intended for Harmony prefix/postfix instrumentation.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using System.Linq;

public static class ProfilingUtils
{
    public static Mod ModInstance { get; set; }

    public static string ResolveOutputPath(string fileName)
    {
        try
        {
            var basePath = ModInstance?.Path;
            if (!string.IsNullOrEmpty(basePath))
                return Path.Combine(basePath, fileName);

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", fileName);
        }
        catch
        {
            return fileName;
        }
    }

    public static long GetTotalMemory(long memoryBefore)
    {
        return GC.GetTotalMemory(true) - memoryBefore;
    }

    public static string TotalMemoryMB(long memoryBefore)
    {
        return $"{GetTotalMemory(memoryBefore) / 1_048_576.0:N1}MB";
    }

    public static string TotalMemoryKB(long memoryBefore)
    {
        return $"{GetTotalMemory(memoryBefore) / 1024.0:N1}KB";
    }

    public static Stopwatch StartTimer()
    {
        var timer = new Stopwatch();
        timer.Start();
        return timer;
    }

    public static string TimeFormat(Stopwatch timer, string format = @"hh\:mm\:ss\.fff")
    {
        return timer?.Elapsed.ToString(format) ?? TimeSpan.Zero.ToString(format);
    }

    public static class PerFrameCounters
    {
        private static readonly ConcurrentDictionary<string, long> _counters = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);

        public static void Increment(string key, long amount = 1)
        {
            _counters.AddOrUpdate(key, amount, (_, old) => old + amount);
        }

        /// <summary>
        /// Add a value to a counter (alias for Increment with arbitrary amount)
        /// </summary>
        public static void Add(string key, long amount)
        {
            _counters.AddOrUpdate(key, amount, (_, old) => old + amount);
        }

        /// <summary>
        /// Set a gauge value (overwrites previous value, doesn't accumulate)
        /// </summary>
        public static void SetGauge(string key, float value)
        {
            _counters[key] = (long)(value * 1000); // Store as fixed-point for precision
        }

        /// <summary>
        /// Set a gauge value (integer version)
        /// </summary>
        public static void SetGauge(string key, int value)
        {
            _counters[key] = value;
        }

        public static long Get(string key) => _counters.TryGetValue(key, out var v) ? v : 0;

        public static IDictionary<string, long> SnapshotAndReset()
        {
            var snapshot = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in _counters)
            {
                snapshot[kv.Key] = kv.Value;
                _counters[kv.Key] = 0;
            }
            return snapshot;
        }

        public static void ResetAll() => _counters.Clear();
    }

    private struct Entry { public long TotalTicks; public long Calls; }

    private static readonly ConcurrentDictionary<string, Entry> s_entries = new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);

    /// <summary>
    /// Record a timing sample in milliseconds for a given tag
    /// </summary>
    public static void RecordTiming(string tag, double milliseconds)
    {
        long ticks = (long)(milliseconds * Stopwatch.Frequency / 1000.0);
        s_entries.AddOrUpdate(tag,
            _ => new Entry { TotalTicks = ticks, Calls = 1 },
            (_, old) => new Entry { TotalTicks = old.TotalTicks + ticks, Calls = old.Calls + 1 });
    }

    public static long BeginSample() => Stopwatch.GetTimestamp();

    public static void EndSample(string tag, long startTimestamp)
    {
        var delta = Stopwatch.GetTimestamp() - startTimestamp;
        s_entries.AddOrUpdate(tag,
            _ => new Entry { TotalTicks = delta, Calls = 1 },
            (_, old) => new Entry { TotalTicks = old.TotalTicks + delta, Calls = old.Calls + 1 });
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    public static double GetTotalMs(string tag)
    {
        if (s_entries.TryGetValue(tag, out var e))
            return TicksToMs(e.TotalTicks);
        return 0.0;
    }

    public static long GetCalls(string tag)
    {
        if (s_entries.TryGetValue(tag, out var e))
            return e.Calls;
        return 0;
    }

    public static IDictionary<string, (double TotalMs, long Calls, double AvgMs)> SnapshotEntries()
    {
        var dict = new Dictionary<string, (double, long, double)>(StringComparer.Ordinal);
        foreach (var kvp in s_entries)
        {
            var tag = kvp.Key;
            var entry = kvp.Value;
            var totalMs = TicksToMs(entry.TotalTicks);
            var avgMs = entry.Calls == 0 ? 0.0 : totalMs / entry.Calls;
            dict[tag] = (totalMs, entry.Calls, avgMs);
        }
        return dict;
    }

    // New: expose current total ticks per tag so callers can calculate deltas between frames.
    public static IDictionary<string, long> SnapshotTicks()
    {
        var dict = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var kvp in s_entries)
        {
            dict[kvp.Key] = kvp.Value.TotalTicks;
        }
        return dict;
    }

    public static string BuildCsvDump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tag,TotalMs,Calls,AvgMs");
        foreach (var kvp in s_entries)
        {
            var tag = kvp.Key;
            var entry = kvp.Value;
            var totalMs = TicksToMs(entry.TotalTicks);
            var avgMs = entry.Calls == 0 ? 0.0 : totalMs / entry.Calls;
            sb.AppendLine($"{EscapeCsv(tag)},{totalMs:N4},{entry.Calls},{avgMs:N6}");
        }
        return sb.ToString();
    }

    public static void SaveCsv(string path) => File.WriteAllText(path, BuildCsvDump());

    public static void Clear() => s_entries.Clear();

    private static string EscapeCsv(string s)
    {
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    public static void ApplyDynamicPatches(Harmony harmony)
    {
        if (harmony == null) return;

        // Conservative target list based on your runtime dump and applied patches.
        var targets = new (string TypeName, string MethodName)[]
        {
            // Entity / AI
            ("Entity", "Update"),
            ("EntityAlive", "Update"),
            ("EntityAlive", "updateCurrentBlockPosAndValue"),
            ("EntityAlive", "updateTasks"),
            ("EntityAlive", "MoveEntityHeaded"),
            ("EntityAlive", "updateSpeedForwardAndStrafe"),
            ("EntityAlive", "FindPath"),
            ("EntityPlayerLocal", "Update"),
            ("EntityPlayer", "Update"),

            // Path / navigation (runtime names)
            ("PathFinder", "Calculate"),
            ("GamePath.PathFinder", "Calculate"),
            ("PathFinder", "Destruct"),
            ("PathNavigate", "UpdateNavigation"),
            ("GamePath.PathNavigate", "UpdateNavigation"),

            // Helpers / caches
            ("EntityMoveHelper", "UpdateMoveHelper"),
            ("EntityLookHelper", "onUpdateLook"),
            ("EntitySeeCache", "CanSee"),
            ("EntitySeeCache", "ClearIfExpired"),

            // Managers / UI observed in runtime
            ("EAIManager", "Update"),
            ("EAIBase", "Update"),
            ("BaseObjective", "Update"),
            ("GUIWindow", "Update"),
            ("EntityVehicle", "Update")
        };

        var prefixInfo = new HarmonyMethod(typeof(ProfilingUtils).GetMethod(nameof(GenericPrefix), BindingFlags.Public | BindingFlags.Static));
        var postfixInfo = new HarmonyMethod(typeof(ProfilingUtils).GetMethod(nameof(GenericPostfix), BindingFlags.Public | BindingFlags.Static));

        // Limit scan to the main game assembly only to avoid cross-assembly noise.
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        var asm = loadedAssemblies.FirstOrDefault(a =>
        {
            try { return string.Equals(a.GetName().Name, "Assembly-CSharp", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });

        if (asm == null)
        {
            LogUtil.Warn("Assembly-CSharp not loaded yet; dynamic patches deferred.");
            return;
        }

        foreach (var (typeName, methodName) in targets)
        {
            int patchedCountForTarget = 0;
            Type t = null;
            try
            {
                // try exact fullname then short-name lookup within Assembly-CSharp
                t = asm.GetType(typeName) ?? FindTypeByShortName(asm, typeName);
            }
            catch { t = null; }

            if (t == null)
            {
                LogUtil.Debug($"type '{typeName}' not found in Assembly-CSharp (skipping).");
                continue;
            }

            MethodInfo[] methods = null;
            try
            {
                var single = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (single != null) methods = new[] { single };
                else methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                 .Where(mi => mi.Name == methodName).ToArray();
            }
            catch { methods = null; }

            if (methods == null || methods.Length == 0)
            {
                LogUtil.Debug($"type '{t.FullName}' found but method '{methodName}' not present (skipping).");
                continue;
            }

            foreach (var m in methods)
            {
                try
                {
                    if (!m.IsAbstract && !m.ContainsGenericParameters && !(m.DeclaringType?.IsInterface ?? false))
                    {
                        harmony.Patch(m, prefixInfo, postfixInfo);
                        patchedCountForTarget++;
                        LogUtil.Debug($"Profiling: patched {m.DeclaringType?.FullName}.{m.Name}");
                        continue;
                    }

                    var baseDef = m.GetBaseDefinition();
                    if (baseDef != null && baseDef != m && !baseDef.IsAbstract && !baseDef.ContainsGenericParameters && !(baseDef.DeclaringType?.IsInterface ?? false))
                    {
                        try
                        {
                            harmony.Patch(baseDef, prefixInfo, postfixInfo);
                            patchedCountForTarget++;
                            LogUtil.Debug($"patched base {baseDef.DeclaringType?.FullName}.{baseDef.Name}");
                            continue;
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.Debug($"patch attempt failed for {m.DeclaringType?.FullName}.{m.Name}: {ex.Message}");
                }
            }

            if (patchedCountForTarget == 0)
                LogUtil.Warn($"no methods patched for {typeName}.{methodName} (type found but no concrete/implementable methods)");
            else
                LogUtil.Info($"patched {patchedCountForTarget} method(s) for {typeName}.{methodName}");
        }
    }

    private static Type FindTypeByShortName(Assembly asm, string shortName)
    {
        try
        {
            foreach (var t in asm.GetTypes())
                if (t.Name == shortName) return t;
        }
        catch { }
        return null;
    }


    // New: dump types and members of a specific assembly by name or file name
    public static void DumpAssemblyTypes(string assemblyNameOrFile, string fileName)
    {
        try
        {
            if (string.IsNullOrEmpty(assemblyNameOrFile)) { LogUtil.Warn("Profiling: DumpAssemblyTypes called with empty assembly name"); return; }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Assembly targetAsm = null;
            foreach (var asm in assemblies)
            {
                try
                {
                    var asmName = asm.GetName().Name;
                    var asmFile = "";
                    try { asmFile = Path.GetFileName(asm.Location); } catch { asmFile = ""; }

                    if (string.Equals(asmName, assemblyNameOrFile, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(asmFile, assemblyNameOrFile, StringComparison.OrdinalIgnoreCase))
                    {
                        targetAsm = asm;
                        break;
                    }
                }
                catch { continue; }
            }

            if (targetAsm == null)
            {
                LogUtil.Warn($"assembly '{assemblyNameOrFile}' not found among loaded assemblies");
                return;
            }

            var path = ResolveOutputPath(fileName);
            var sb = new StringBuilder();
            sb.AppendLine("Assembly,TypeFullName,MethodName,Modifiers,ReturnType,Parameters");

            Type[] types;
            try { types = targetAsm.GetTypes(); }
            catch (Exception ex) { LogUtil.Warn($"failed to enumerate types in {assemblyNameOrFile}: {ex.Message}"); return; }

            foreach (var t in types)
            {
                MethodInfo[] methods;
                try
                {
                    methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch { methods = Array.Empty<MethodInfo>(); }

                foreach (var m in methods)
                {
                    try
                    {
                        var mods = new List<string>();
                        if (m.IsPublic) mods.Add("public");
                        if (m.IsFamily) mods.Add("protected");
                        if (m.IsPrivate) mods.Add("private");
                        if (m.IsStatic) mods.Add("static");
                        if (m.IsAbstract) mods.Add("abstract");
                        if (m.IsVirtual && !m.IsAbstract) mods.Add("virtual");
                        string modifiers = string.Join(" ", mods);

                        var ret = m.ReturnType?.FullName ?? "void";
                        var ps = m.GetParameters();
                        var ptypes = string.Join(";", ps.Select(p => (p.ParameterType?.FullName ?? "unknown") + " " + p.Name));
                        string line = $"{EscapeCsv(targetAsm.GetName().Name)},{EscapeCsv(t.FullName)},{EscapeCsv(m.Name)},{EscapeCsv(modifiers)},{EscapeCsv(ret)},{EscapeCsv(ptypes)}";
                        sb.AppendLine(line);
                    }
                    catch { /* ignore individual method errors */ }
                }
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, sb.ToString());
            LogUtil.Info($"dumped assembly types for '{assemblyNameOrFile}' to {path}");
        }
        catch (Exception ex)
        {
            LogUtil.Warn($"DumpAssemblyTypes failed: {ex.Message}");
        }
    }

    public static void GenericPrefix(out long __state)
    {
        __state = ProfilerConfig.Current.EnableProfiling ? BeginSample() : 0;
    }

    // Fixed: use __originalMethod so Harmony can bind MethodBase correctly
    public static void GenericPostfix(long __state, MethodBase __originalMethod)
    {
        if (__state == 0) return;
        try
        {
            var tag = GetTag(__originalMethod);
            EndSample(tag, __state);
        }
        catch { }
    }

    private static string GetTag(MethodBase method)
    {
        if (method == null) return "Unknown";
        var typeName = method.DeclaringType?.FullName ?? method.DeclaringType?.Name ?? "UnknownType";
        return $"{typeName}.{method.Name}";
    }
}