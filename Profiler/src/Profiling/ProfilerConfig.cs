// ProfilerConfig.cs

using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

[Serializable]
public class ProfilerConfig
{
    /// <summary>
    /// When false, ALL profiling instrumentation is disabled.
    /// </summary>
    public bool EnableProfiling = true;

    /// <summary>
    /// Enables deep physics instrumentation (adds overhead, use for targeted profiling only).
    /// </summary>
    public bool EnableDeepPhysicsInstrumentation = false;

    public const string ConfigFileName = "profiler_config.json";
    private const int ConfigVersion = 9;

    public int Version = ConfigVersion;

    public static ProfilerConfig Current { get; private set; } = new ProfilerConfig();

    public static void Load(string folder = null)
    {
        try
        {
            if (string.IsNullOrEmpty(folder))
            {
                folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
            string path = Path.Combine(folder, ConfigFileName);

            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<ProfilerConfig>(json);
                if (loaded != null)
                {
                    if (loaded.Version < ConfigVersion)
                    {
                        MigrateConfig(loaded);
                        Save(folder);
                    }
                    Current = loaded;
                    return;
                }
            }

            Current = new ProfilerConfig();
            Save(folder);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to load ProfilerConfig: {ex.Message}");
            Current = new ProfilerConfig();
        }
    }

    public static void Save(string folder = null)
    {
        try
        {
            if (string.IsNullOrEmpty(folder))
            {
                folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
            string path = Path.Combine(folder, ConfigFileName);
            string json = JsonConvert.SerializeObject(Current, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to save ProfilerConfig: {ex.Message}");
        }
    }

    private static void MigrateConfig(ProfilerConfig old)
    {
        old.Version = ConfigVersion;
        old.EnableDeepPhysicsInstrumentation = false;
        old.EnableProfiling = true;
    }
}