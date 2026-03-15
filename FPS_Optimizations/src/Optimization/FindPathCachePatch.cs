// FindPathCachePatch.cs
//
// Blocks exact-duplicate path requests for distant idle zombies.
// Combat-engaged or close (<20 m) entities always get fresh A* paths.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), "FindPath")]
static class FindPathCachePatch
{
    private static readonly Dictionary<int, PathRequestInfo> s_lastRequest =
        new Dictionary<int, PathRequestInfo>(256);

    private struct PathRequestInfo
    {
        public Vector3 TargetPos;
        public float Speed;
        public float Time;
        public Vector3 EntityPos;
    }

    private const float CLOSE_DIST_SQ = 400f;
    private const float DUPLICATE_DIST_SQ = 1f;

    private static Vector3 s_cachedPlayerPos;
    private static int s_cachedFrame = -1;

    public static bool Prefix(EntityAlive __instance, Vector3 targetPos, float moveSpeed,
                              bool canBreak, EAIBase behavior)
    {
        if (!OptimizationConfig.Current.EnablePathCache) return true;
        if (__instance == null) return true;
        if (__instance is EntityPlayer) return true;

        FrameCache.EnsureUpdated();
        if (FrameCache.ShouldBypassThrottling) return true;

        try
        {
            UpdatePlayerPos();
            float distSq = (__instance.position - s_cachedPlayerPos).sqrMagnitude;
            if (distSq < CLOSE_DIST_SQ) return true;

            int entityId = __instance.entityId;
            float now = UnityEngine.Time.realtimeSinceStartup;
            float ttl = OptimizationConfig.Current.PathCacheTTLSeconds;

            if (s_lastRequest.TryGetValue(entityId, out var lastReq))
            {
                if (now - lastReq.Time < ttl)
                {
                    float targetMovedSq = (targetPos - lastReq.TargetPos).sqrMagnitude;
                    float entityMovedSq = (__instance.position - lastReq.EntityPos).sqrMagnitude;
                    if (targetMovedSq < DUPLICATE_DIST_SQ
                        && entityMovedSq < DUPLICATE_DIST_SQ
                        && Mathf.Approximately(moveSpeed, lastReq.Speed))
                    {
                        ProfilerCounterBridge.Increment("FindPath.CacheHit");
                        return false;
                    }
                }
            }

            s_lastRequest[entityId] = new PathRequestInfo
            {
                TargetPos = targetPos,
                Speed = moveSpeed,
                Time = now,
                EntityPos = __instance.position
            };
        }
        catch { }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdatePlayerPos()
    {
        int frame = UnityEngine.Time.frameCount;
        if (s_cachedFrame == frame) return;
        s_cachedFrame = frame;
        var player = GameManager.Instance?.World?.GetPrimaryPlayer();
        if (player != null) s_cachedPlayerPos = player.position;
    }

    public static void OnEntityRemoved(int entityId) => s_lastRequest.Remove(entityId);
    public static void Clear() => s_lastRequest.Clear();
}
