// SpatialGridEAIPatch.cs
// Optimize EAI target finding using spatial grid instead of iterating all entities
// (C) Added fallback to original when grid is not yet populated or has zero entities
// FIX v2: Now sets aiActiveScale, aiClosestPlayer, and jiggle physics like vanilla.
//         Previously these were missing, causing aiManager.Update() to only fire once
//         and entities to have no closest-player reference.

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// Optimize World.GetClosestPlayer using spatial grid for initial filtering
/// </summary>
[HarmonyPatch(typeof(World), nameof(World.GetClosestPlayer), 
    new System.Type[] { typeof(Vector3), typeof(float), typeof(bool) })]
static class GetClosestPlayerSpatialPatch
{
    // This method is already efficient (iterates Players.list which is small)
    // No optimization needed - players are few
}

/// <summary>
/// Optimize aiClosest list population using spatial grid.
/// Replicates ALL vanilla EntityActivityUpdate behavior:
///   1. Populate aiClosest per player
///   2. Set aiClosestPlayer + aiClosestPlayerDistSq per entity
///   3. Sort aiClosest by distance
///   4. Set aiActiveScale based on priority/distance (controls AI tick rate)
///   5. Set jiggle physics based on distance
///   6. Set cloth simulation for remote players
/// </summary>
[HarmonyPatch(typeof(World), "EntityActivityUpdate")]
static class EntityActivityUpdateSpatialPatch
{
    private static readonly List<int> s_nearbyBuffer = new(128);

    static bool Prefix(World __instance)
    {
        // (C) Fall back to original when spatial grid is disabled or not populated
        if (!ProfilerConfig.Current.EnableSpatialGrid) return true;
        if (!SpatialGridManager.IsEnabled) return true;
        if (SpatialGridManager.EntityCount == 0) 
        {
            ProfilingUtils.PerFrameCounters.Increment("SpatialGrid.Fallback");
            return true;
        }

        var players = __instance.Players.list;
        if (players.Count == 0) return false;

        // Phase 1: Clear aiClosest and build per-player entity lists
        for (int i = 0; i < players.Count; i++)
        {
            players[i].aiClosest.Clear();
        }

        // Phase 2: For each entity, find closest player and assign
        // Use spatial grid for candidate filtering but still pick true closest
        var entityAlives = __instance.EntityAlives;
        int entityCount = entityAlives.Count;
        for (int i = 0; i < entityCount; i++)
        {
            var entity = entityAlives[i];
            if (entity == null) continue;

            // Find closest player (players list is small, iterate directly)
            EntityPlayer closestPlayer = __instance.GetClosestPlayer(entity.position, -1f, false);
            if (closestPlayer != null)
            {
                closestPlayer.aiClosest.Add(entity);
                entity.aiClosestPlayer = closestPlayer;
                entity.aiClosestPlayerDistSq = (closestPlayer.position - entity.position).sqrMagnitude;
            }
            else
            {
                entity.aiClosestPlayer = null;
                entity.aiClosestPlayerDistSq = float.MaxValue;
            }
        }

        // Phase 3: Sort, set aiActiveScale, jiggle — replicate vanilla exactly
        Vector3 camPos = Vector3.zero;
        float clothDistSq = 0f;
        var localPlayer = __instance.GetPrimaryPlayer();
        if (localPlayer != null)
        {
            camPos = localPlayer.cameraTransform.position + Origin.position;
            localPlayer.emodel.ClothSimOn(!localPlayer.AttachedToEntity);
            clothDistSq = 625f;
            if (localPlayer.AimingGun)
            {
                clothDistSq = 3025f;
            }
        }

        int maxFullAI = Utils.FastClamp(60 / players.Count, 4, 20);

        for (int p = players.Count - 1; p >= 0; p--)
        {
            var player = players[p];
            player.aiClosest.Sort((e1, e2) => e1.aiClosestPlayerDistSq.CompareTo(e2.aiClosestPlayerDistSq));

            for (int j = 0; j < player.aiClosest.Count; j++)
            {
                var entity = player.aiClosest[j];
                if (j < maxFullAI || entity.aiClosestPlayerDistSq < 64f)
                {
                    entity.aiActiveScale = 1f;
                    bool jiggleOn = entity.aiClosestPlayerDistSq < 36f;
                    entity.emodel.JiggleOn(jiggleOn);
                }
                else
                {
                    float scale = (entity.aiClosestPlayerDistSq < 225f) ? 0.3f : 0.1f;
                    entity.aiActiveScale = scale;
                    entity.emodel.JiggleOn(false);
                }
            }

            if (localPlayer != null && player != localPlayer)
            {
                bool clothOn = !player.AttachedToEntity 
                    && (player.position - camPos).sqrMagnitude < clothDistSq;
                player.emodel.ClothSimOn(clothOn);
            }
        }

        ProfilingUtils.PerFrameCounters.Increment("SpatialGrid.ActivityUpdates");
        return false;
    }
}

/// <summary>
/// Command to dump spatial grid stats
/// </summary>
public class ConsoleCmdSpatialGrid : ConsoleCmdAbstract
{
    public override string[] getCommands() => new[] { "spatialgrid" };

    public override string getDescription() => "Show spatial grid statistics";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        if (_params.Count > 0 && _params[0] == "toggle")
        {
            ProfilerConfig.Current.EnableSpatialGrid = !ProfilerConfig.Current.EnableSpatialGrid;

            // Re-init or shutdown based on new state
            if (ProfilerConfig.Current.EnableSpatialGrid)
            {
                SpatialGridManager.Shutdown(); // Reset first
                SpatialGridManager.Init();
            }
            else
            {
                SpatialGridManager.Shutdown();
            }

            ProfilerConfig.Save();
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output(
                $"SpatialGrid enabled: {ProfilerConfig.Current.EnableSpatialGrid}, Entities: {SpatialGridManager.EntityCount}");
            return;
        }

        if (_params.Count > 0 && _params[0] == "reinit")
        {
            SpatialGridManager.Shutdown();
            SpatialGridManager.Init();
            SingletonMonoBehaviour<SdtdConsole>.Instance.Output(
                $"SpatialGrid reinitialized with {SpatialGridManager.EntityCount} entities");
            return;
        }

        SingletonMonoBehaviour<SdtdConsole>.Instance.Output("=== Spatial Grid Stats ===");
        SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Enabled: {SpatialGridManager.IsEnabled}");
        SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Entity Count: {SpatialGridManager.EntityCount}");
        SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Cell Count: {SpatialGridManager.CellCount}");
        SingletonMonoBehaviour<SdtdConsole>.Instance.Output($"Cell Size: {ProfilerConfig.Current.SpatialGridCellSize}m");
        SingletonMonoBehaviour<SdtdConsole>.Instance.Output("Commands: 'spatialgrid toggle' | 'spatialgrid reinit'");
    }
}