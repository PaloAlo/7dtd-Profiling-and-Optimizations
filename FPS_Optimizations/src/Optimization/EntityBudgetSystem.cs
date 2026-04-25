// EntityBudgetSystem.cs
//
// Unified per-frame entity classification that replaces the duplicate
// distance + combat checks scattered across 6+ individual throttle patches.
//
// Runs ONCE per frame (in FrameCache.Refresh) and classifies every zombie
// into one of four priority tiers based on DISTANCE + ACTIVE COMBAT:
//
//   CRITICAL  — Close range (< 20m) OR in active combat (has attack/revenge
//               target, or recently attacked).  Full update every frame.
//   HIGH      — Mid range (20–50m), not in active combat.  Full update every
//               frame at moderate counts; mild throttle under heavy load.
//   MEDIUM    — Far range (50–80m), not in active combat.  Throttled update.
//   LOW       — Very far (> 80m), not in active combat.  Aggressive throttle.
//
// Aware-only entities (just alert/investigating, no actual target) are
// classified purely by distance.  This ensures distance-based optimizations
// actually fire — previously, isAlert spread to ~90% of zombies during
// combat, forcing them all to High tier and making Budget.Low = 0.
//
// Each patch queries EntityBudgetSystem.GetTier(entityId) for O(1) lookup
// instead of independently computing distance + combat + thresholds.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public static class EntityBudgetSystem
{
    public enum Tier : byte
    {
        Critical = 0,  // Always full update
        High = 1,      // Full update unless extreme load
        Medium = 2,    // Throttled under load
        Low = 3        // Aggressively throttled
    }

    // Per-entity classification result
    public struct EntityInfo
    {
        public Tier Tier;
        public float DistanceSq;
        public bool InCombat;           // Any combat flag (attack/revenge/recently attacked/alert/investigating)
        public bool InActiveCombat;     // Has actual target or was recently attacked (attack/revenge/recentlyAttacked)
        public int FrameSlot;           // Pre-computed round-robin slot for this entity
    }

    // Lookup table: entityId -> classification
    private static readonly Dictionary<int, EntityInfo> s_entityInfo = new Dictionary<int, EntityInfo>(256);

    // Per-tier counts for diagnostics
    public static int CriticalCount { get; private set; }
    public static int HighCount { get; private set; }
    public static int MediumCount { get; private set; }
    public static int LowCount { get; private set; }

    // Distance thresholds (squared) — public for speed curve alignment
    public const float CLOSE_DIST_SQ = 400f;        // 20m — always critical
    public const float MID_DIST_SQ = 2500f;          // 50m
    public const float FAR_DIST_SQ = 6400f;           // 80m

    // Surge detection — stagger mass sleeper awakenings across frames
    private static int s_previousClassifiedCount;
    private static int s_surgeEndFrame = -1;
    private const int SURGE_THRESHOLD = 15;
    private const int SURGE_DURATION_FRAMES = 30;

    // Grace period — newly-spawned entities get Critical tier for N frames
    // so their AI task system fully initializes before any throttling
    private static readonly Dictionary<int, int> s_firstSeenFrame = new Dictionary<int, int>(128);
    private const int GRACE_PERIOD_FRAMES = 10;

    /// <summary>
    /// True when a large number of entities appeared recently (mass sleeper awakening).
    /// Proximity-only Critical entities are reclassified to High tier during surge.
    /// </summary>
    public static bool IsSurge => Time.frameCount < s_surgeEndFrame;

    /// <summary>
    /// Classify all entities. Called once per frame from FrameCache.Refresh().
    /// </summary>
    public static void ClassifyEntities(World world, Vector3 playerPos, int zombieCount)
    {
        s_entityInfo.Clear();
        CriticalCount = 0;
        HighCount = 0;
        MediumCount = 0;
        LowCount = 0;

        if (world == null) return;

        int frameCount = Time.frameCount;
        var entities = world.Entities.list;
        int graceCount = 0;

        // Combat reason breakdown counters
        int combatTotal = 0;
        int combatAttackTarget = 0;
        int combatRevengeTarget = 0;
        int combatRecentlyAttacked = 0;
        int combatAlert = 0;
        int combatInvestigating = 0;
        // Critical tier sub-reason counters
        int criticalClose = 0;
        int criticalCombat = 0;
        int awareDemotedCount = 0;

        var cfg = OptimizationConfig.Current;

        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i] as EntityAlive;
            if (entity == null) continue;
            if (entity is EntityPlayer) continue;

            int entityId = entity.entityId;
            float distSq = (entity.position - playerPos).sqrMagnitude;

            // Grace period — newly-spawned entities always get Critical
            // so AI tasks fully initialize before any throttling kicks in.
            // This prevents freshly-woken sleepers from appearing unresponsive.
            bool inGracePeriod = false;
            if (!s_firstSeenFrame.TryGetValue(entityId, out int firstFrame))
            {
                s_firstSeenFrame[entityId] = frameCount;
                inGracePeriod = true;
                graceCount++;
            }
            else if (frameCount - firstFrame < GRACE_PERIOD_FRAMES)
            {
                inGracePeriod = true;
                graceCount++;
            }

            // Combat check — done ONCE here instead of in every patch.
            // Read backing fields directly to bypass Harmony/Profiler interception
            // on GetAttackTarget()/GetRevengeTarget().
            // NOTE: Do NOT seed the target cache here.  Seeding before AI runs
            // causes stale reads for the rest of the frame after AI sets a new
            // target.  The per-frame cache in TargetCachePatch handles itself.
            bool hasAttackTarget = entity.attackTarget != null;
            bool hasRevengeTarget = entity.revengeEntity != null;
            bool recentlyAttacked = entity.hasBeenAttackedTime > 0;
            bool isAlert = entity.isAlert;
            bool isInvestigating = entity.HasInvestigatePosition;

            bool inCombat = hasAttackTarget
                         || hasRevengeTarget
                         || recentlyAttacked
                         || isAlert
                         || isInvestigating;

            // Active combat = has an actual target or was recently attacked.
            // Aware-only = alert/investigating but no target yet.
            bool inActiveCombat = hasAttackTarget || hasRevengeTarget || recentlyAttacked;

            // When sub-classification is enabled, only active combat triggers
            // Critical tier from combat.  Aware-only entities (just alert/
            // investigating) get demoted to High for mild throttling under load.
            bool combatForTier = cfg.EnableCombatSubClassification
                ? inActiveCombat
                : inCombat;

            // Accumulate per-flag combat reason counts
            if (inCombat)
            {
                combatTotal++;
                if (hasAttackTarget) combatAttackTarget++;
                if (hasRevengeTarget) combatRevengeTarget++;
                if (recentlyAttacked) combatRecentlyAttacked++;
                if (isAlert) combatAlert++;
                if (isInvestigating) combatInvestigating++;
            }

            Tier tier;
            if (inGracePeriod || distSq < CLOSE_DIST_SQ || combatForTier)
            {
                if (IsSurge && !combatForTier)
                {
                    // Surge: proximity-only entities demoted to High to stagger
                    // mass sleeper awakening processing across frames
                    tier = Tier.High;
                    HighCount++;
                }
                else
                {
                    tier = Tier.Critical;
                    CriticalCount++;

                    // Track why this entity is Critical
                    if (inActiveCombat) criticalCombat++;
                    else criticalClose++;
                }
            }
            else if (distSq < MID_DIST_SQ)
            {
                tier = Tier.High;
                HighCount++;
                if (inCombat && !inActiveCombat) awareDemotedCount++;
            }
            else if (distSq < FAR_DIST_SQ)
            {
                tier = Tier.Medium;
                MediumCount++;
                if (inCombat && !inActiveCombat) awareDemotedCount++;
            }
            else
            {
                tier = Tier.Low;
                LowCount++;
                if (inCombat && !inActiveCombat) awareDemotedCount++;
            }

            // Pre-compute round-robin slot for consistent frame assignment
            // Different patches use different intervals, but the slot base is the same
            int frameSlot = (entityId & 0x7FFFFFFF);

            s_entityInfo[entityId] = new EntityInfo
            {
                Tier = tier,
                DistanceSq = distSq,
                InCombat = inCombat,
                InActiveCombat = inActiveCombat,
                FrameSlot = frameSlot
            };
        }

        // Detect surge: mass sleeper awakening stagger
        int totalClassified = CriticalCount + HighCount + MediumCount + LowCount;
        if (totalClassified - s_previousClassifiedCount >= SURGE_THRESHOLD)
        {
            s_surgeEndFrame = frameCount + SURGE_DURATION_FRAMES;
            ProfilerCounterBridge.Increment("Budget.SurgeDetected");
        }
        s_previousClassifiedCount = totalClassified;

        // Report tier distribution to profiler
        ProfilerCounterBridge.Increment("Budget.Critical", CriticalCount);
        ProfilerCounterBridge.Increment("Budget.High", HighCount);
        ProfilerCounterBridge.Increment("Budget.Medium", MediumCount);
        ProfilerCounterBridge.Increment("Budget.Low", LowCount);
        // Report how many entities are in the initial grace period this frame
        if (graceCount > 0)
        {
            ProfilerCounterBridge.Increment("Budget.Grace", graceCount);
        }

        // Report combat reason breakdown (flags can overlap per entity)
        if (combatTotal > 0)
        {
            ProfilerCounterBridge.Increment("Combat.Total", combatTotal);
            if (combatAttackTarget > 0) ProfilerCounterBridge.Increment("Combat.AttackTarget", combatAttackTarget);
            if (combatRevengeTarget > 0) ProfilerCounterBridge.Increment("Combat.RevengeTarget", combatRevengeTarget);
            if (combatRecentlyAttacked > 0) ProfilerCounterBridge.Increment("Combat.RecentlyAttacked", combatRecentlyAttacked);
            if (combatAlert > 0) ProfilerCounterBridge.Increment("Combat.Alert", combatAlert);
            if (combatInvestigating > 0) ProfilerCounterBridge.Increment("Combat.Investigating", combatInvestigating);
        }

        // Report Critical tier sub-reasons
        if (criticalClose > 0) ProfilerCounterBridge.Increment("Critical.Close", criticalClose);
        if (criticalCombat > 0) ProfilerCounterBridge.Increment("Critical.Combat", criticalCombat);

        // Report aware-only entities demoted from Critical to High
        if (awareDemotedCount > 0) ProfilerCounterBridge.Increment("Budget.AwareDemoted", awareDemotedCount);
    }

    /// <summary>
    /// Get the pre-computed classification for an entity.
    /// Returns true if found. If not found, the entity should get full updates.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetInfo(int entityId, out EntityInfo info)
    {
        return s_entityInfo.TryGetValue(entityId, out info);
    }

    /// <summary>
    /// Quick tier check. Returns Critical (always full update) if entity not found.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Tier GetTier(int entityId)
    {
        if (s_entityInfo.TryGetValue(entityId, out var info))
            return info.Tier;
        return Tier.Critical; // Unknown entities always get full updates
    }

    /// <summary>
    /// Check if this entity should run on this frame given the specified skip interval.
    /// Uses the pre-computed frame slot for even distribution.
    /// Critical-tier entities ALWAYS return true.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ShouldRunThisFrame(int entityId, int skipInterval)
    {
        if (skipInterval <= 1) return true;

        if (!s_entityInfo.TryGetValue(entityId, out var info))
            return true; // Unknown = always run

        if (info.Tier == Tier.Critical)
            return true; // Critical = always run

        return (Time.frameCount % skipInterval) == (info.FrameSlot % skipInterval);
    }

    /// <summary>
    /// Get the recommended skip interval for the given tier at the current load level.
    /// Returns 1 = every frame, 2+ = throttled.
    /// </summary>
    public static int GetRecommendedInterval(Tier tier, int zombieCount)
    {
        bool emergency = zombieCount >= AdaptiveThresholds.EmergencyZombieThreshold;
        bool critical = zombieCount >= AdaptiveThresholds.CriticalZombieThreshold;
        bool siege = zombieCount >= 100;
        bool surge = IsSurge;

        switch (tier)
        {
            case Tier.Critical:
                return 1; // ALWAYS every frame

            case Tier.High:
                // During surge, more aggressive to stagger mass awakening
                if (surge && siege) return 3;
                if (siege) return 2;
                if (surge) return 2;
                return 1;

            case Tier.Medium:
                if (surge && siege) return 6;
                if (siege) return 4;
                if (surge && critical) return 4;
                if (critical) return 3;
                if (surge) return 3;
                if (emergency) return 2;
                return 1;

            case Tier.Low:
                if (surge && siege) return 12;
                if (siege) return 8;
                if (surge) return 8;
                if (critical) return 6;
                if (emergency) return 4;
                return 3;

            default:
                return 1;
        }
    }

    /// <summary>
    /// Convenience: should this entity get a full update this frame?
    /// Combines tier lookup + recommended interval + frame slot check.
    /// Returns true if the entity should get a full update.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ShouldFullUpdate(int entityId, int zombieCount)
    {
        if (!s_entityInfo.TryGetValue(entityId, out var info))
            return true;

        int interval = GetRecommendedInterval(info.Tier, zombieCount);
        if (interval <= 1) return true;

        return (Time.frameCount % interval) == (info.FrameSlot % interval);
    }

    /// <summary>
    /// Clean up when an entity is removed.
    /// </summary>
    public static void OnEntityRemoved(int entityId)
    {
        s_entityInfo.Remove(entityId);
        s_firstSeenFrame.Remove(entityId);
    }

    /// <summary>
    /// Clear all cached classifications.
    /// </summary>
    public static void Clear()
    {
        s_entityInfo.Clear();
        s_firstSeenFrame.Clear();
        CriticalCount = 0;
        HighCount = 0;
        MediumCount = 0;
        LowCount = 0;
        s_previousClassifiedCount = 0;
        s_surgeEndFrame = -1;
    }
}
