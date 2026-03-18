// EntityCleanupPatch.cs
//
// Cleans up all per-entity caches when entities are removed from the world.

using HarmonyLib;

[HarmonyPatch(typeof(World), nameof(World.RemoveEntity))]
static class EntityCleanupPatch
{
    static void Prefix(int _entityId)
    {
        ZombieFrameSkipPatch.OnEntityRemoved(_entityId);
        MoveEntityHeadedLODPatch.OnEntityRemoved(_entityId);
        UpdateTasksLODPatch.OnEntityRemoved(_entityId);
        GetAttackTargetCachePatch.OnEntityRemoved(_entityId);
        GetRevengeTargetCachePatch.OnEntityRemoved(_entityId);
        FindPathCachePatch.OnEntityRemoved(_entityId);
        MoveHelperThrottlePatch.OnEntityRemoved(_entityId);
        EntityCollisionThrottlePatch.OnEntityRemoved(_entityId);
        SpeedStrafeThrottlePatch.OnEntityRemoved(_entityId);
        EAITaskEvaluationThrottlePatch.OnEntityRemoved(_entityId);
        MoveSpeedCachePatch.OnEntityRemoved(_entityId);
        BlockPosUpdateThrottlePatch.OnEntityRemoved(_entityId);
        DefaultMoveEntityThrottlePatch.OnEntityRemoved(_entityId);
        EAIManagerUpdateThrottlePatch.OnEntityRemoved(_entityId);
    }
}
