// ChunkDirectionalPriorityPatch.cs
//
// After DetermineChunksToLoad finishes, reorders the internal chunk copy
// queue (m_ChunksToCopy) so that chunks in the player's facing direction
// get their meshes generated first.  This reduces visible pop-in in the
// direction the player is looking.
//
// Uses AccessTools for private field access — if the field layout changes
// in a game update the patch gracefully disables itself.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(ChunkManager), "DetermineChunksToLoad")]
public static class ChunkDirectionalPriorityPatch
{
    private static FieldInfo s_fiChunksToCopy;
    private static bool s_initialized;
    private static bool s_available;

    private static void Init()
    {
        s_initialized = true;
        try
        {
            s_fiChunksToCopy = AccessTools.Field(typeof(ChunkManager), "m_ChunksToCopy");
            s_available = s_fiChunksToCopy != null;

            if (s_available)
                Log.Out("[FPSOptimizations] ChunkDirectionalPriority initialized");
            else
                Log.Warning("[FPSOptimizations] ChunkDirectionalPriority: m_ChunksToCopy field not found — disabled");
        }
        catch (Exception ex)
        {
            Log.Warning("[FPSOptimizations] ChunkDirectionalPriority init failed: " + ex.Message);
        }
    }

    [HarmonyPostfix]
    public static void Postfix(ChunkManager __instance)
    {
        if (!OptimizationConfig.Current.EnableChunkDirectionalPriority) return;
        if (!s_initialized) Init();
        if (!s_available) return;

        try
        {
            var world = GameManager.Instance?.World;
            if (world == null) return;

            var player = world.GetPrimaryPlayer();
            if (player == null) return;

            var chunksToCopy = s_fiChunksToCopy.GetValue(__instance) as List<Chunk>;
            if (chunksToCopy == null || chunksToCopy.Count < 2) return;

            Vector3 playerPos = player.position;
            Vector3 forward = player.GetLookVector();
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) return;
            forward.Normalize();

            chunksToCopy.Sort((a, b) =>
            {
                float scoreA = ChunkScore(a, playerPos, forward);
                float scoreB = ChunkScore(b, playerPos, forward);
                return scoreB.CompareTo(scoreA);
            });

            ProfilerCounterBridge.Increment("ChunkDir.Sorted");
        }
        catch { }
    }

    private static float ChunkScore(Chunk chunk, Vector3 playerPos, Vector3 forward)
    {
        Vector3 chunkWorldCenter = chunk.GetWorldPos().ToVector3();
        chunkWorldCenter.x += 8f;
        chunkWorldCenter.z += 8f;
        chunkWorldCenter.y = playerPos.y;

        Vector3 toChunk = chunkWorldCenter - playerPos;
        toChunk.y = 0f;
        float distSq = toChunk.sqrMagnitude;
        if (distSq < 1f) return float.MaxValue;

        float invDist = 1f / Mathf.Sqrt(distSq);
        float dot = (toChunk.x * forward.x + toChunk.z * forward.z) * invDist;

        float score = dot * 2f - distSq * 0.001f;
        if (dot < 0f) score *= 0.5f;
        return score;
    }

    // Reset initialization so the patch re-probes private fields after reload
    public static void ClearCaches()
    {
        s_initialized = false;
        s_available = false;
        s_fiChunksToCopy = null;
    }
}
