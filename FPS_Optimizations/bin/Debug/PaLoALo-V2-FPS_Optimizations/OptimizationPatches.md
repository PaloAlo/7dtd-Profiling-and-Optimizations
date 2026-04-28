# 7DTD FPS Optimizations — Patch Documentation

## Overview

A suite of Harmony-based optimization patches for 7 Days to Die, targeting the main CPU bottlenecks during high-entity-count scenarios such as Tier 6 POI clears and blood moon hordes.

**Core principle:** entities in active combat or close to the player always receive full vanilla behavior. Only distant, non-threatening entities get throttled — and even then, smooth movement is preserved via lite maintenance on throttled frames.

The mod also includes a built-in profiler that captures performance data automatically when FPS drops, enabling data-driven optimization.

---

## Safety Design Principles

1. **Active combat bypass** — entities with attack targets, revenge targets, or recently attacked always get full updates regardless of distance
2. **Close-range bypass** — entities within 20m of the player are never throttled (Critical tier)
3. **Aware-only demotion** — alert/investigating entities without an actual target are classified purely by distance, allowing mild throttling while maintaining responsiveness
4. **Grace period** — newly-spawned entities get 10 frames of Critical tier so AI tasks fully initialize before any throttling (prevents sleepers appearing unresponsive)
5. **Surge detection** — mass sleeper awakenings are detected and staggered across frames to prevent FPS spikes
6. **Adaptive thresholds** — emergency/critical zombie thresholds scale dynamically with baseline FPS using sqrt curves
7. **Frame slicing** — skipped updates are distributed evenly across frames using entity ID modulo, preventing all entities from updating on the same frame
8. **Lite maintenance** — even when AI task evaluation is skipped, navigation, movement, look tracking, path pickup, and lifecycle checks still run every frame
9. **Task termination on throttled frames** — executing tasks call `Continue()` on throttled frames and are properly `Reset()` if they should stop
10. **No path injection** — paths are only blocked (not substituted), preventing stale-position navigation
11. **Crash prevention** — null-check patches cover derived type overrides dynamically

---

## Architecture: EntityBudgetSystem

The central classification system that runs **once per frame** and assigns every zombie to a priority tier. All throttle patches query this system for O(1) tier lookups instead of independently computing distance and combat state.

### Tier Definitions

| Tier | Distance | Combat | Throttling |
|------|----------|--------|------------|
| **Critical** | < 20m | OR active combat (attack/revenge target, recently attacked) | Full update every frame |
| **High** | 20–50m | Non-active-combat | Full update; mild throttle only at z100+ |
| **Medium** | 50–80m | Non-active-combat | Throttled under load |
| **Low** | > 80m | Non-active-combat | Aggressively throttled |

- **Active combat** = has attack target, has revenge target, or recently attacked (`hasBeenAttackedTime > 0`)
- **Aware-only** = alert or investigating but no actual target. Classified purely by distance, tracked as `AwareDemoted` diagnostic counter
- **Grace period** = first 10 frames after entity is first seen. Always Critical tier
- **Surge** = 15+ new entities appear in a single frame (mass sleeper awakening). Proximity-only Critical entities are demoted to High to stagger processing

### Recommended Intervals by Tier

The system scales intervals based on zombie count thresholds:

| Tier | Normal | Emergency (20–48 Z) | Critical (40–80 Z) | Siege (100+ Z) |
|------|--------|---------------------|---------------------|----------------|
| Critical | 1 | 1 | 1 | 1 |
| High | 1 | 1 | 1 | 2 |
| Medium | 1 | 2 | 3 | 4 |
| Low | 3 | 4 | 6 | 8 |

### Adaptive Thresholds

Emergency and Critical zombie count thresholds adjust dynamically based on FPS headroom:
- **Baseline FPS** established after 120s of gameplay with < 10 zombies
- **sqrt scaling** prevents high-FPS systems from pushing thresholds absurdly high
- **Stress factor** reduces thresholds further when FPS drops below 50
- Emergency range: 20–48 zombies
- Critical range: 40–80 zombies

---

## Architecture Diagram

```
┌──────────────────────────────────────────────────────────┐
│              Entity Update Loop                          │
│  FrameCache.Refresh() → EntityBudgetSystem.Classify()    │
│                                                          │
│  ┌────────────────────────────────────────────────┐      │
│  │ EntityBudgetSystem (once per frame)            │      │
│  │  ├── Critical (<20m or active combat) → full   │      │
│  │  ├── High (20-50m) → full or mild throttle     │      │
│  │  ├── Medium (50-80m) → throttled under load    │      │
│  │  └── Low (>80m) → aggressively throttled       │      │
│  │  Grace period: new entities → Critical (10 fr) │      │
│  │  Surge: mass spawn → stagger to High           │      │
│  └────────────────────────────────────────────────┘      │
│         │                                                │
│    ┌────┴──────────┐                                     │
│    ▼               ▼                                     │
│ updateTasks       MoveEntityHeaded                       │
│ (AI Task LOD)     (Movement LOD, Low only)               │
│    │                    │                                │
│    │              ┌─────┴──────────┐                     │
│    │              ▼                ▼                     │
│    │        MoveHelper        entityCollision            │
│    │        (Low only)        (Low only)                 │
│    │              │                                      │
│    │              ▼                                      │
│    │        SpeedStrafe (Low only)                       │
│    │                                                     │
│    ▼                                                     │
│ Lite Maintenance (throttled frames):                     │
│  ├── CheckDespawn + seeCache.ClearIfExpired              │
│  ├── ContinueExecutingOnly (Continue/Reset tasks)        │
│  ├── PathFinderThread.GetPath + navigator.SetPath        │
│  ├── navigator.UpdateNavigation                          │
│  ├── moveHelper.UpdateMoveHelper                         │
│  └── lookHelper.onUpdateLook                             │
│                                                          │
│ Other Throttles:                                         │
│  ├── ZombieFrameSkip (Update() skip, Low only)           │
│  ├── BlockPosUpdate (Medium+, emergency only)            │
│  ├── UAIDecisionThrottle (chooseAction frequency)        │
│  ├── FindPath (duplicate request block)                  │
│  ├── StepSound (20 Hz rate limit)                        │
│  ├── ThreatLevel (music scan throttle)                   │
│  ├── ParticleEffect (horde particle reduction)           │
│  ├── VoxelFastReject (PhysX → voxel for path LOS)        │
│  └── AttackTargetNullCheck (crash prevention)            │
│                                                          │
│ Player-Specific:                                         │
│  ├── ShelterFrameUpdate (every 2-4 frames)               │
│  └── BlockRadiusEffectsTick (every 2-3 frames)           │
│                                                          │
│ World-Level:                                             │
│  ├── ChunkCopyTimeBudget (deltaTime × 0.5 cap)           │
│  ├── ChunkDirectionalPriority (player-facing first)      │
│  ├── ThreadPoolConsolidation (.NET pool for game threads)│
│  ├── SleeperVolumeThrottle (every 4th tick)              │
│  ├── VehicleRigidbodySleep (idle vehicle physics off)    │
│  └── JiggleBoneDistancePatch (>50m jiggle off)           │
└──────────────────────────────────────────────────────────┘
```

---

## Patch Details

### 1. Movement LOD — `MoveEntityHeadedLODPatch.cs`

**What it does:** Throttles `EntityAlive.MoveEntityHeaded()` for **Low-tier entities only** (> 80m, non-active-combat). On skip frames, the entity freezes in place — at 80m+ this is invisible to the player. Also applies a speed curve to reduce movement speed for distant Low-tier entities.

**Why it matters:** `MoveEntityHeaded` is consistently the **#1 CPU consumer** at 25–30% of total entity processing time. It handles heading calculations, rotation, and feeds into the physics collision system including `CharacterController.Move()`.

**V6 changes:** Removed LiteMotion extrapolation entirely. The previous LiteMotion applied gravity and friction on skip frames, but friction decay (`0.546^N`) caused "superglue feet" where entities slowed to near-zero speed. Freezing in place at 80m+ is imperceptible.

| Tier | Throttling |
|------|-----------|
| Critical (< 20m / active combat) | Full speed, full update every frame |
| High (20–50m) | Full speed, full update every frame |
| Medium (50–80m) | Full speed, full update every frame |
| Low (> 80m, non-combat) | Speed curve + freeze on skip frames |

**Speed curve:** When zombie count ≥ 30, Low-tier entities get a speed multiplier interpolated from 1.0 (at 20m) to 0.35 (at 80m+).

| Metric | Value |
|--------|-------|
| Typical skip count (52 zombies) | ~5,065 |
| CPU share addressed | 25–30% of total entity time |
| Frame-slicing | Entity ID modulo distributes updates evenly |

---

### 2. AI Task LOD — `UpdateTasksLODPatch.cs`

**What it does:** Throttles the full `updateTasks()` re-evaluation for distant, non-combat entities. On throttled frames, runs **Lite Maintenance** that preserves smooth movement while skipping the expensive AI decision loop.

**What Lite Maintenance runs every frame:**
- `CheckDespawn()` and `seeCache.ClearIfExpired()` (lifecycle)
- `ContinueExecutingOnly()` — calls `Continue()` on executing tasks, `Reset()` if task should stop (approach keeps walking, dead targets are dropped)
- `PathFinderThread.GetPath()` + `navigator.SetPath()` (path pickup)
- `navigator.UpdateNavigation()` (smooth path following)
- `moveHelper.UpdateMoveHelper()` (smooth obstacle avoidance)
- `lookHelper.onUpdateLook()` (smooth head tracking)
- Distraction cleanup (dead/unloaded distractions cleared)

**What gets throttled:** Only the full `aiManager.Update()` re-evaluation — the expensive `CanExecute / Continue / isBestTask` loop across all EAI tasks.

**Why it matters:** `updateTasks` is the **#2 CPU consumer** at 20–25%. The lite maintenance approach avoids the expensive re-evaluation loop while preserving smooth navigation, preventing rubberbanding and path loss.

| Tier | Throttling |
|------|-----------|
| Critical | Full updateTasks every frame |
| High (z < 100) | Full updateTasks every frame |
| High (z ≥ 100) | Interval 2 |
| Medium | Interval 2–4 depending on load |
| Low | Interval 3–8 depending on load |

| Metric | Value |
|--------|-------|
| Typical LiteRun count (52 zombies) | ~5,333 |
| CPU share addressed | 20–25% of total entity time |

---

### 3. EAI Task Evaluation Throttle — `EAITaskEvaluationThrottlePatch.cs`

Utility class used by `UpdateTasksLODPatch.RunLiteMaintenance`. Not a Harmony patch itself.

**`ContinueExecutingOnly(EAIManager)`:**
- Fades `interestDistance` (cheap, keeps state consistent)
- For each executing task: calls `action.Continue()` — if true, calls `action.Update()`; if false, calls `action.Reset()` so the next full evaluation picks a new task
- Reverse iteration protects against list modification during Reset()
- Uses `AccessTools.FieldRefAccess` for zero-allocation access to private `tasks`/`targetTasks` fields

---

### 4. Physics Collision Throttle — `EntityCollisionThrottlePatch.cs`

**What it does:** Throttles `Entity.entityCollision()` (which calls `CharacterController.Move()`) for **Low-tier entities only** during emergency mode (zombie count ≥ emergency threshold).

**Why it matters:** `CharacterController.Move()` is one of the most expensive single operations in Unity — full physics collision detection against the world every call. At 80m+ distance, per-frame collision is unnecessary.

| Tier | Throttling |
|------|-----------|
| Critical / High / Medium | Full collision every frame |
| Low (emergency mode only) | Interval 3–8 depending on load |

**Safety:** Only activates during emergency mode. All non-Low entities get full collision every frame.

---

### 5. Move Helper Throttle — `MoveHelperThrottlePatch.cs`

**What it does:** Throttles `EntityMoveHelper.UpdateMoveHelper()` obstacle checking for **Low-tier entities only**. Uses stuck-detection: entities moving freely get infrequent checks, stuck entities get frequent checks.

**Why it matters:** `UpdateMoveHelper` runs constant raycasting for `CheckWorldBlocked` and `CheckEntityBlocked` even when entities are moving freely. The stuck-detection logic ensures only entities needing course corrections get frequent checks.

| State | Check Interval |
|-------|---------------|
| Moving freely (Low tier) | Every 20th frame (40th at critical load) |
| Stuck (Low tier) | Every 4th frame (vanilla rate) |
| All other tiers | Every frame |

| Metric | Value |
|--------|-------|
| Typical throttle count (52 zombies) | ~2,920 |
| Stuck threshold | < 0.1m movement over check window |

---

### 6. Speed/Strafe Throttle — `SpeedStrafeThrottlePatch.cs`

**What it does:** Throttles `EntityAlive.updateSpeedForwardAndStrafe` for **Low-tier entities only** during emergency mode.

| Tier | Throttling |
|------|-----------|
| All except Low | Every frame |
| Low (emergency mode only) | Interval 3–8 depending on load |

---

### 7. Zombie Frame Skip — `ZombieFrameSkipPatch.cs`

**What it does:** Skips `EntityAlive.Update()` entirely for **Low-tier entities** during emergency mode. `Update()` itself is cheap (~0.008 ms) but at 50+ zombies the aggregate adds up.

**Safety:** Never modifies entity state. Sleeping, combat, close, and non-Low entities always run.

| Metric | Value |
|--------|-------|
| Typical skip count (52 zombies) | ~12,032 |
| Skip interval | 3 (emergency) or 4 (critical) |

---

### 8. Block Position Update Throttle — `BlockPosUpdateThrottlePatch.cs`

**What it does:** Throttles `EntityAlive.updateCurrentBlockPosAndValue` for non-Critical entities during emergency mode. Uses EntityBudgetSystem tier intervals.

**Why it matters:** This method updates which block the entity occupies (used for fire/drowning damage, navigation hints). For distant entities, stale-by-a-few-frames data is harmless.

| Metric | Value |
|--------|-------|
| Typical throttle count (52 zombies) | ~6,372 |

---

### 9. UAI Decision Throttle — `UAIDecisionThrottlePatch.cs`

**What it does:** Reduces the frequency of `UAIBase.chooseAction()` for distant entities. Vanilla runs this 5×/sec per zombie. Each call triggers spatial queries, consideration scoring, and multiple `GetAttackTarget` calls.

| Tier | Frequency |
|------|-----------|
| Close / combat | 5/sec (unchanged) |
| High (20–50m) | 2.5/sec |
| Medium (50–80m) | ~1.7/sec |
| Low (> 80m) | 1/sec |

Current tasks (movement, attacks) continue every frame — only the expensive "should I switch to a better action?" re-evaluation is throttled.

---

### 10. FindPath Duplicate Throttle — `FindPathCachePatch.cs`

**What it does:** Blocks duplicate A* pathfinding requests when target position, entity position, and speed haven't changed within a 1-second TTL window.

**Safety:** Combat-engaged and close (< 20m) entities always get fresh paths.

| Metric | Value |
|--------|-------|
| Default TTL | 1 second |
| Close bypass | 20m |

---

### 11. Step Sound Throttle — `ThrottleStepSoundPatch.cs`

**What it does:** Rate-limits `EntityAlive.updateStepSound()` to 20 Hz per entity (one call per 50ms).

**Why it matters:** Step sounds are purely cosmetic. Vanilla fires them every frame, generating audio events and raycasts for ground material detection far more often than audibly necessary.

---

### 12. Threat Level Music Throttle — `ThreatLevelThrottlePatch.cs`

**What it does:** Caches the result of `DynamicMusic.ThreatLevelUtility.GetThreatLevelOn` for 30 frames (~0.5s) when zombie count ≥ 15.

**Why it matters:** This method calls `GetEntitiesInBounds()` plus iterates two entity lists plus scans sleeper volumes **every single frame** just for dynamic music. The result already feeds a rolling average queue, so skipping frames is invisible to the music system.

---

### 13. Particle Effect Throttle — `ParticleEffectThrottlePatch.cs`

**What it does:** During horde combat (20+ zombies):
1. Skips redundant particle respawns when entity already has active particles of the same type (avoids `GetComponentsInChildren` allocations + restart)
2. Scales down emission rate and max particles on new spawns (reduces GPU overdraw)

**Why it matters:** Effects like `p_bleeding` are full-screen particles that get respawned every 4 buff ticks. Each respawn does `GetComponentsInChildren<ParticleSystem>()` (GC allocation) plus `Clear()/Play()` restart.

---

### 14. Voxel Fast-Reject — `VoxelFastRejectPatches.cs`

**What it does:** Replaces expensive PhysX raycasts with cheap voxel grid raycasts in two hot paths:
1. `ASPPathFinder.IsLineClear` — path line-of-sight uses `Voxel.GetNextBlockHit` as fast-reject before PhysX
2. `EAIApproachAndAttackTarget.GetMoveToLocation` — target position ground check uses `FindSupportingBlockPos` instead of `Physics.Raycast`

**Why it matters:** 7DTD is voxel-based. `Voxel.GetNextBlockHit` is an O(distance) grid walk — trivially cheap compared to PhysX broadphase→narrowphase→contact generation. If the voxel says "blocked", skip physics entirely.

---

### 15. Move Speed Cache — `MoveSpeedCachePatch.cs`

**What it does:** Caches `GetSpeedModifier()` results for 20 game ticks per entity.

| Metric | Value |
|--------|-------|
| Cache hit rate | 100% |
| Cache duration | 20 game ticks |

---

### 16. Player Update Throttle — `PlayerUpdateThrottlePatch.cs`

**What it does:** Throttles two player-specific expensive methods:
- `ShelterFrameUpdate()` — raycasts for shelter detection, throttled to every 2–4 frames
- `BlockRadiusEffectsTick()` — chunk iteration for nearby block effects, throttled to every 2–3 frames

---

### 17. Target Cache — `TargetCachePatch.cs`

**Status: Disabled by default** (`EnableTargetCache = false`)

Caches `GetAttackTarget()` and `GetRevengeTarget()` per entity per frame. **Disabled** because the underlying methods just return a backing field (O(1)) — the cache adds overhead and caused stale target reads when AI set new targets mid-frame after EntityBudgetSystem classification, leading to "stop as if invisible" behavior.

---

### 18. Attack Target Null Check — `AttackTargetNullCheckPatch.cs`

**What it does:** Prevents `NullReferenceException` crashes in `GetAttackTargetHitPosition()` when the attack target is killed between the AI decision and attack execution frames.

**Why it matters:** Vanilla bug — the destroyed target's `getChestPosition()` throws NRE that propagates through the entity tick loop. Returns the entity's own chest position as a safe fallback.

---

### 19. World-Level Optimizations

| Patch | What it does |
|-------|-------------|
| `ChunkCopyTimeBudgetPatch` | Caps `ChunkManager.CopyChunksToUnity` to `deltaTime × 0.5` (2–8ms) |
| `ChunkDirectionalPriorityPatch` | Reorders chunk copy queue to prioritize player-facing direction |
| `ThreadPoolConsolidationPatch` | Redirects bursty game threads (ChunkCalc, MeshBake, Regeneration, etc.) to .NET thread pool |
| `SleeperVolumeThrottlePatch` | Throttles `World.TickSleeperVolumes` to every 4th tick |
| `VehicleRigidbodySleepPatch` | Puts idle vehicle rigidbodies to sleep (no driver, no motion) |
| `JiggleBoneDistancePatch` | Disables jiggle bone simulation for entities > 50m away |

---

### 20. XUiThrottlePatch.cs 
	— XUi frame rate cap
	Caps XUiUpdater.Update() to 45fps. 
	HUD layout recalc every frame is wasted CPU — imperceptible above ~30fps.

### 21. XUiAlwaysUpdatePatch.cs 
	— Stop forced hotbar/radial rebuild every frame
	Toolbelt + Radial AlwaysUpdate()→false stops forced dirty-rebuild every frame.

### 22. OcclusionLimitFix.cs 
	— Prevent occlusion pool exhaustion
	Prevents occlusion entry pool exhaustion during large hordes. 
	When pool empties, entities stop being occlusion-culled and render through walls — direct GPU draw-call waste.

### 23. LayerDistanceCullingPatch.cs 
	— Per-layer GPU cull distances for terrain/vegetation
	Sets per-layer camera cull distances for terrain-detail (layer 23) and vegetation (layer 28). 
	Caps grass/detail rendering at 75-150m depending on elevation, reducing GPU fill rate significantly.

---

## Configuration

All patches can be toggled via `fps_optimization_config.json` loaded at startup. Supports hot-reload via file watcher.

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableMoveLOD` | `true` | Movement, collision, speed/strafe, move helper, and frame skip throttling |
| `EnableStaggeredUpdate` | `true` | AI task LOD throttling with lite maintenance |
| `EnablePathCache` | `true` | FindPath duplicate-request blocking |
| `PathCacheTTLSeconds` | `1.0` | How long to block duplicate path requests |
| `EnableTargetCache` | `false` | Per-frame target cache (disabled — see patch #17) |
| `EnableStepSoundThrottle` | `true` | Step sound 20 Hz rate limiting |
| `EnableBlockPosThrottle` | `true` | Block position update throttling |
| `EnableSpeedCurveLOD` | `true` | Distance-based speed reduction for Low tier |
| `SpeedCurveZombieThreshold` | `30` | Min zombies to activate speed curve |
| `SpeedCurveMinMult` | `0.35` | Min speed multiplier at max distance |
| `EnableSleeperVolumeThrottle` | `true` | Sleeper volume tick throttle |
| `EnableVehicleRigidbodySleep` | `true` | Idle vehicle physics sleep |
| `EnableJiggleBoneToggle` | `true` | Distance-based jiggle bone disable |
| `EnableChunkCopyTimeBudget` | `true` | Chunk copy time budget |
| `EnableChunkDirectionalPriority` | `true` | Player-facing chunk priority |
| `EnableThreadPoolConsolidation` | `true` | .NET thread pool for game threads |
| `EnableUAIDecisionThrottle` | `true` | UAI chooseAction frequency reduction |
| `EnableParticleThrottle` | `true` | Particle effect throttle during hordes |
| `ParticleThrottleZombieThreshold` | `20` | Min zombies to activate particle throttle |
| `EnableThreatLevelThrottle` | `true` | Music threat-level scan throttle |
| `ThreatLevelThrottleZombieThreshold` | `15` | Min zombies to activate |
| `ThreatLevelThrottleFrames` | `30` | Cache duration in frames |
| `EnableCombatSubClassification` | `true` | Separates active combat from aware-only for tier classification |
| `EnableXUiThrottle` | `true` | XUi throttle patch |
| `XUiThrottleFPS` | `45.0` | Target FPS for XUi throttle |
| `EnableLayerDistanceCulling` | `true` | Layer distance culling patch |
| `EnableEAIManagerThrottle` | `false` | EAI manager eval throttle (disabled — caused sleeper detection issues) |

Config version: 15

---

## Test Results (V14 — Post-Refactor, April 2026)

### Session Data (Tier 6 POI Clear, 26–57 zombies)

| Capture | Zombies | FPS | Top Bottleneck | MoveHeaded Skip | Tasks LiteRun | ZombieFrame Skip |
|---------|---------|-----|----------------|-----------------|---------------|-----------------|
| #1 (early) | 26 | 46 | updateTasks (24%) | — | 192 | 594 |
| #2 (building) | 43 | 46 | MoveEntityHeaded (24%) | 2,335 | 1,676 | 5,732 |
| #3 (peak FPS drop) | 52 | 38 | MoveEntityHeaded (25%) | 5,065 | 5,333 | 12,032 |
| #4 (spreading) | 48 | 51 | MoveEntityHeaded (25%) | 5,045 | 4,546 | 12,625 |
| #5 (peak load) | 57 | 30 | MoveEntityHeaded (30%) | 5,455 | 5,867 | 9,139 |

### Budget Tier Distribution (capture window totals)

| Capture | Zombies | Budget.Critical | Budget.High | Budget.Medium | Budget.Low | AwareDemoted |
|---------|---------|-----------------|-------------|---------------|-----------|-------------|
| z26 | 26 | 7,132 | 3,581 | 2,733 | 890 | 6,397 |
| z43 | 43 | 9,624 | 5,275 | 2,510 | 8,610 | 12,802 |
| z52 | 52 | 10,578 | 7,108 | 4,803 | 16,051 | 25,284 |
| z48 | 48 | 7,394 | 15,994 | 6,753 | 17,851 | 30,834 |
| z57 | 57 | 15,854 | 13,284 | — | 12,184 | 18,036 |

**Key insight:** Budget.Low was **0 in all pre-V14 captures** because alert/investigating entities were forced to High tier. V14 classifies aware-only entities purely by distance — Budget.Low now reaches 8,610–17,851 during combat, enabling all distance-based throttling.

### Optimization Effectiveness by Patch

| Patch | Status | Technique | Measured Effect |
|-------|--------|-----------|----------------|
| EntityBudgetSystem | ✅ Core | 4-tier classification | Budget.Low 890–17,851 (was 0) |
| MoveEntityHeadedLODPatch V6 | ✅ Good | Low-tier freeze + speed curve | 2,335–5,455 skipped per capture |
| UpdateTasksLODPatch V4 | ✅ Good | Lite maintenance on throttled frames | 1,676–5,867 lite runs per capture |
| EAITaskEvaluationThrottlePatch | ✅ Good | Continue-only mode with Reset | 1,676–5,867 eval throttled |
| ZombieFrameSkipPatch | ✅ Good | Low-tier Update() skip | 594–12,625 skipped per capture |
| EntityCollisionThrottlePatch | ✅ Good | Low-tier collision skip | Active during emergency mode |
| MoveHelperThrottlePatch | ✅ Good | Stuck-aware throttle | 1,409–2,920 throttled per capture |
| SpeedStrafeThrottlePatch | ✅ Good | Low-tier distance throttle | Active during emergency mode |
| BlockPosUpdateThrottlePatch | ✅ Good | Tier-based throttle | 243–7,331 throttled per capture |
| MoveSpeedCachePatch | ✅ Excellent | 20-tick cache | 100% cache hit rate (5,282–29,879) |
| FindPathCachePatch | ✅ Good | Duplicate request block | Prevents redundant A* pathfinding |
| ThrottleStepSoundPatch | ✅ Good | 20 Hz rate limit | Cosmetic-only throttling |
| ThreatLevelThrottlePatch | ✅ Good | 30-frame cache | 448 throttled at z26 |
| UAIDecisionThrottlePatch | ✅ Good | Distance-based eval frequency | Active for distant entities |
| VoxelFastRejectPatches | ✅ Good | PhysX → voxel fast-reject | Cheaper per-call cost |
| ParticleEffectThrottlePatch | ✅ Good | Respawn skip + emission scale | Reduces GC + GPU during hordes |
| PlayerUpdateThrottlePatch | ✅ Good | Shelter + BlockRadius throttle | Per-frame player overhead reduced |
| AttackTargetNullCheckPatch | ✅ Safety | Null guard | Prevents vanilla NRE crash |
| TargetCachePatch | ⬚ Disabled | Per-frame cache | Disabled — stale read risk outweighs O(1) field access benefit |

### Combat Reason Breakdown (z52 capture)

| Combat Flag | Count | Notes |
|-------------|-------|-------|
| Combat.Total | 32,927 | Entities with any combat flag |
| Combat.Alert | 29,055 | ~88% of combat entities are "just alert" |
| Combat.Investigating | 25,359 | Investigating a position |
| Combat.AttackTarget | 4,909 | Has actual attack target |
| Combat.RevengeTarget | 3,143 | Has revenge target |
| AwareDemoted | 25,284 | Classified by distance instead of forced to Critical |

This demonstrates why V14's aware-only demotion was critical — 88% of "combat" entities are just alert bystanders, not actively engaged.

### Key Observations

1. **MoveEntityHeaded** remains the #1 bottleneck at 25–30% of total CPU time
2. **Budget.Low now receives entities** — all distance-based patches are fully active
3. **LOD skip rates are appropriately low during close combat** (4–14%) because most close zombies are Critical tier — this is by design
4. **LOD skip rates increase with distance** — at z48 with zombies spread across rooms, MoveHeaded skipped 5,045 times
5. **Lite maintenance preserves smooth movement** — user reports only a "split second" occasional issue vs. previous circling/stuck/unresponsive behavior
6. **FPS at 48 zombies: 51 FPS** (good); **57 zombies: 30 FPS** (acceptable floor)
7. **Recovery captures** show FPS returning to 70–81 at 0–4 zombies (EntityPlayerLocal.Update dominates at ~1000ms total across 2000 frames = 0.5ms/frame — normal)

---

## Profiler Companion System

### Automatic High-Load Capture

The profiler automatically captures detailed performance snapshots when:
- FPS drops below 30 **and** zombie count is 50+
- FPS drops below 50% of the established baseline

Captures are debounced (30s minimum between captures) and limited to 5 per session.

### Output Files

| File | Contents |
|------|----------|
| `7dtd_profile_*.csv` | 30-second summary rows with FPS, timing, and Top 5 bottlenecks |
| `7dtd_HIGHLOAD_*.csv` | Detailed snapshots: timing breakdown, call counts, budget tier distribution, optimization counters, combat reason breakdown, per-zombie cost |

### Key CSV Counters

| Counter | Description |
|---------|-------------|
| `Budget.Critical/High/Medium/Low` | Entity tier distribution this capture window |
| `Budget.AwareDemoted` | Alert-only entities classified by distance (not forced to Critical) |
| `Budget.Grace` | Entities in initial spawn grace period |
| `Budget.SurgeDetected` | Mass sleeper awakening detected |
| `Combat.Total/AttackTarget/RevengeTarget/Alert/Investigating` | Combat flag breakdown |
| `Critical.Close/Critical.Combat` | Why entities are in Critical tier |
| `MoveEntityHeaded.Skipped` | Movement updates skipped by LOD |
| `updateTasks.LiteRun` | AI task evaluations replaced with lite maintenance |
| `EAIManager.EvalThrottled` | Full EAI re-evaluations skipped |
| `ZombieFrame.Skipped` | EntityAlive.Update() calls skipped |
| `MoveHelper.Throttled` | Move helper obstacle checks throttled |
| `BlockPosUpdate.Throttled` | Block position updates throttled |
| `MoveSpeed.CacheHit/Curved` | Speed cache hits and speed curve applications |
| `ThreatLevel.Throttled` | Music threat scans skipped |

---

## Appendix: Vanilla Game Design Reference

### Player Update Loop

```
EntityPlayerLocal.Update()                     ← EVERY FRAME (MonoBehaviour)
├── base.Update() → EntityPlayer.Update()
│   ├── base.Update() → EntityAlive.Update()
│   │   ├── base.Update() → Entity.Update()   ← transform + audio
│   │   ├── updateNetworkStats()
│   │   ├── Progression.Update()
│   │   └── render fade
│   ├── ChunkObserver.SetPosition()
│   ├── avatar controller angles
│   └── achievement/time tracking
├── renderManager.FrameUpdate()
├── CameraDOFFrameUpdate()                     ← EMPTY (no-op)
├── FrameUpdateCamera()                        ← Raycasts (3rd person only)
├── ShelterFrameUpdate()                       ← THROTTLED ✓
├── crosshair calculations
└── autoMove.Update()

EntityPlayerLocal.OnUpdateLive()               ← EVERY TICK (20Hz game tick)
├── bedroll/radiation checks (5s timers)
├── pushOutOfBlocks() × 4
├── base.OnUpdateLive() → EntityAlive.OnUpdateLive()
│   ├── Stats.Tick()
│   ├── updateCurrentBlockPosAndValue()
│   ├── MoveEntityHeaded()                     ← heavy (collision, physics)
│   └── stun/damage processing
├── challengeJournal.Update()
├── QuestJournal.Update()
├── BlockRadiusEffectsTick()                   ← THROTTLED ✓
└── stamina/exhaustion processing
```

### Vanilla updateTasks() Flow

1. Check `DebugStopEnemiesMoving` → `SetMoveForward(0)` and RETURN EARLY
2. `CheckDespawn()`
3. `seeCache.ClearIfExpired()`
4. `aiActiveDelay` / `aiManager.Update()` (AI decisions)
5. `PathFinderThread.GetPath()` + `navigator.SetPath()`
6. `navigator.UpdateNavigation()` — makes entity follow path
7. `moveHelper.UpdateMoveHelper()` — makes entity physically move
8. `lookHelper.onUpdateLook()`

### Entity Call Chain

```
World.TickEntities() → entity.OnUpdateEntity() → OnUpdateLive() → updateTasks()
Entity.Update() (Unity MonoBehaviour) → transforms + audio only (SEPARATE path)
```

EntityHuman overrides OnUpdateLive(), and EntityZombie inherits from EntityHuman. Zombie OnUpdateLive() dispatches to EntityHuman.OnUpdateLive(), not EntityAlive.OnUpdateLive().

---

## Appendix: Companion Mods

### ZehMatt's VoxelDirector

Attacks a completely different bottleneck — replaces PhysX raycasts/sweeps inside EntityMoveHelper with cheap voxel grid raycasts:

| Patch | Vanilla Cost | Approach |
|-------|-------------|----------|
| CheckWorldBlocked | PhysX raycast | Voxel fast-reject; physics only if voxel says "clear" |
| CheckBlocked | PhysX raycast per height layer | Same voxel fast-reject |
| CalcObstacleSideStepArc | PhysX sweep at N angles | Voxel raycast per angle |
| CheckEntityBlocked | PhysX sphere query | GetEntitiesAround + manual math (full replacement) |

**Complementary:** Our mod reduces *how often* expensive methods run. ZehMatt's makes each call *cheaper*. Combined: fewer calls × cheaper calls = compounding savings. The mods compose naturally with no conflicts.

### Laydor's Playground

Sorts `World.TickEntities` entity list by type name and slices ticks across frames at low FPS. Improves CPU instruction-cache locality. Testing showed ~24% reduction in per-zombie CPU cost when combined with our mod. **Note:** This mod is not publicly released.
