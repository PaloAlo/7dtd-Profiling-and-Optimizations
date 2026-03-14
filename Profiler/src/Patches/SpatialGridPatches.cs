// SpatialGridPatches.cs
// Patches to integrate SpatialGridManager with game lifecycle

using HarmonyLib;

/// <summary>
/// Initialize grid when world starts
/// </summary>
[HarmonyPatch(typeof(World), "Init", new System.Type[] { typeof(IGameManager), typeof(WorldBiomes) })]
static class SpatialGridWorldInitPatch
{
    static void Postfix()
    {
        SpatialGridManager.Init();
    }
}

/// <summary>
/// Clean up grid when world unloads
/// </summary>
[HarmonyPatch(typeof(World), nameof(World.Cleanup))]
static class SpatialGridWorldCleanupPatch
{
    static void Prefix()
    {
        SpatialGridManager.Shutdown();
    }
}

/// <summary>
/// Add entities to grid when spawned
/// </summary>
[HarmonyPatch(typeof(World), nameof(World.SpawnEntityInWorld))]
static class SpatialGridEntitySpawnPatch
{
    static void Postfix(Entity _entity)
    {
        if (_entity is EntityAlive entityAlive)
        {
            SpatialGridManager.OnEntityAdded(entityAlive);
        }
    }
}

/// <summary>
/// Remove entities from grid when unloaded
/// </summary>
[HarmonyPatch(typeof(World), nameof(World.RemoveEntity))]
static class SpatialGridEntityRemovePatch
{
    static void Prefix(int _entityId)
    {
        SpatialGridManager.OnEntityRemoved(_entityId);
    }
}

/// <summary>
/// Update grid each frame during entity ticking
/// </summary>
[HarmonyPatch(typeof(World), nameof(World.TickEntities))]
static class SpatialGridTickPatch
{
    static void Prefix()
    {
        SpatialGridManager.UpdateFrame();
    }
}