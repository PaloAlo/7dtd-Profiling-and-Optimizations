// SpatialGridManager.cs
// Manages the spatial hash grid for all EntityAlive instances
// Updates on entity position changes, provides fast neighbor queries
// (A) Populates grid immediately on Init so queries work from first frame

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public static class SpatialGridManager
{
    private static SpatialHashGrid<int> s_entityGrid;
    private static readonly Dictionary<int, Vector3> s_lastPositions = new(256);
    private static readonly List<int> s_queryBuffer = new(64);
    private static bool s_initialized;
    private static int s_lastUpdateFrame = -1;

    public static bool IsEnabled => ProfilerConfig.Current?.EnableSpatialGrid ?? false;
    public static int EntityCount => s_entityGrid?.EntityCount ?? 0;
    public static int CellCount => s_entityGrid?.CellCount ?? 0;

    public static void Init()
    {
        if (s_initialized) return;
        s_initialized = true;

        float cellSize = ProfilerConfig.Current?.SpatialGridCellSize ?? 8f;
        s_entityGrid = new SpatialHashGrid<int>(cellSize);
        s_lastPositions.Clear();

        // (A) Populate grid immediately with existing entities so queries work right away
        try
        {
            var world = GameManager.Instance?.World;
            if (world != null)
            {
                // Use Entities.list which contains all entities
                var entities = world.Entities.list;
                int count = 0;
                for (int i = 0; i < entities.Count; i++)
                {
                    var entity = entities[i] as EntityAlive;
                    if (entity == null) continue;
                    int id = entity.entityId;
                    Vector3 pos = entity.position;
                    s_entityGrid.Add(id, pos);
                    s_lastPositions[id] = pos;
                    count++;
                }
                Log.Out($"[Profiler] SpatialGrid initialized with {count} entities");
            }
        }
        catch (System.Exception ex)
        {
            // Defensive: if population fails, keep grid empty and allow fallback behavior
            Log.Warning($"[Profiler] SpatialGrid population failed: {ex.Message}");
            s_entityGrid?.Clear();
            s_lastPositions.Clear();
        }
    }

    public static void Shutdown()
    {
        s_entityGrid?.Clear();
        s_lastPositions.Clear();
        s_initialized = false;
        Log.Out("[Profiler] SpatialGrid shutdown");
    }

    /// <summary>
    /// Call once per frame to update entity positions in the grid
    /// Called from SpatialGridTickPatch (World.TickEntities Prefix)
    /// </summary>
    public static void UpdateFrame()
    {
        if (!IsEnabled || !s_initialized) return;

        int frame = Time.frameCount;
        if (frame == s_lastUpdateFrame) return;
        s_lastUpdateFrame = frame;

        var world = GameManager.Instance?.World;
        if (world == null) return;

        // Update all entity positions using Entities.list
        var entities = world.Entities.list;
        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i] as EntityAlive;
            if (entity == null) continue;

            int id = entity.entityId;
            Vector3 pos = entity.position;

            if (s_lastPositions.TryGetValue(id, out var lastPos))
            {
                // Only update grid if moved more than 1 unit
                if ((pos - lastPos).sqrMagnitude > 1f)
                {
                    s_entityGrid.Update(id, pos);
                    s_lastPositions[id] = pos;
                }
            }
            else
            {
                // New entity not in our tracking
                s_entityGrid.Add(id, pos);
                s_lastPositions[id] = pos;
            }
        }

        ProfilingUtils.PerFrameCounters.SetGauge("SpatialGrid.Entities", s_entityGrid.EntityCount);
        ProfilingUtils.PerFrameCounters.SetGauge("SpatialGrid.Cells", s_entityGrid.CellCount);
    }

    /// <summary>
    /// Add entity to grid (called when entity spawns)
    /// </summary>
    public static void OnEntityAdded(EntityAlive entity)
    {
        if (!IsEnabled || !s_initialized || entity == null) return;

        int id = entity.entityId;
        Vector3 pos = entity.position;

        if (!s_entityGrid.Contains(id))
        {
            s_entityGrid.Add(id, pos);
            s_lastPositions[id] = pos;
        }
    }

    /// <summary>
    /// Remove entity from grid (called when entity despawns)
    /// </summary>
    public static void OnEntityRemoved(int entityId)
    {
        if (!s_initialized) return;

        s_entityGrid?.Remove(entityId);
        s_lastPositions.Remove(entityId);
    }

    /// <summary>
    /// Get all entity IDs within radius of position
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static List<int> QueryNearby(Vector3 position, float radius)
    {
        // (C) Fallback: return empty if grid not ready or empty
        if (!IsEnabled || !s_initialized || s_entityGrid == null || s_entityGrid.EntityCount == 0)
            return s_queryBuffer; // Return empty buffer

        return s_entityGrid.QueryRadius(position, radius);
    }

    /// <summary>
    /// Get all entity IDs within radius, output to provided list
    /// </summary>
    public static void QueryNearby(Vector3 position, float radius, List<int> results)
    {
        // (C) Fallback: clear results if grid not ready or empty
        if (!IsEnabled || !s_initialized || s_entityGrid == null || s_entityGrid.EntityCount == 0)
        {
            results.Clear();
            return;
        }

        s_entityGrid.QueryRadius(position, radius, results);
    }

    /// <summary>
    /// Get entities within radius that match a filter predicate
    /// </summary>
    public static void QueryNearbyFiltered(
        Vector3 position, 
        float radius, 
        List<EntityAlive> results,
        System.Func<EntityAlive, bool> filter = null)
    {
        results.Clear();

        // (C) Fallback when grid not ready
        if (!IsEnabled || !s_initialized || s_entityGrid == null || s_entityGrid.EntityCount == 0) 
            return;

        var world = GameManager.Instance?.World;
        if (world == null) return;

        var nearbyIds = QueryNearby(position, radius);
        float radiusSq = radius * radius;

        for (int i = 0; i < nearbyIds.Count; i++)
        {
            int id = nearbyIds[i];
            var entity = world.GetEntity(id) as EntityAlive;

            if (entity == null) continue;

            // Distance check (grid returns cell-based approximation)
            if ((entity.position - position).sqrMagnitude > radiusSq) continue;

            // Apply custom filter
            if (filter != null && !filter(entity)) continue;

            results.Add(entity);
        }
    }

    /// <summary>
    /// Find closest EntityAlive within radius that matches filter
    /// </summary>
    public static EntityAlive FindClosest(
        Vector3 position,
        float maxRadius,
        System.Func<EntityAlive, bool> filter = null)
    {
        // (C) Fallback when grid not ready
        if (!IsEnabled || !s_initialized || s_entityGrid == null || s_entityGrid.EntityCount == 0) 
            return null;

        var world = GameManager.Instance?.World;
        if (world == null) return null;

        var nearbyIds = QueryNearby(position, maxRadius);
        float maxRadiusSq = maxRadius * maxRadius;

        EntityAlive closest = null;
        float closestDistSq = float.MaxValue;

        for (int i = 0; i < nearbyIds.Count; i++)
        {
            int id = nearbyIds[i];
            var entity = world.GetEntity(id) as EntityAlive;

            if (entity == null) continue;

            float distSq = (entity.position - position).sqrMagnitude;
            if (distSq > maxRadiusSq) continue;
            if (distSq >= closestDistSq) continue;

            if (filter != null && !filter(entity)) continue;

            closest = entity;
            closestDistSq = distSq;
        }

        return closest;
    }
}