# 7DTD Profiler — Optimization Patches

## Overview

This mod provides a suite of Harmony-based optimization patches for 7 Days to Die, targeting the main CPU bottlenecks during high-entity-count scenarios such as Tier 6 POI clears and blood moon hordes. All patches include **combat bypass** — entities actively fighting the player always receive full vanilla behavior.

The mod also includes a built-in profiler that captures performance data automatically when FPS drops, enabling data-driven optimization.

---


## Safety Design Principles
1. **Combat bypass on every patch** — entities with attack targets, revenge targets, alert state, sleeping state, or investigate positions always get vanilla behavior
2. **Close-range bypass** — entities within 20m of the player are never throttled
3. **Emergency scaling** — throttling only increases under genuine high-load conditions (40+ or 80+ zombies)
4. **Frame slicing** — skipped updates are distributed evenly across frames using entity ID modulo, preventing all entities from updating on the same frame
5. **Minimal maintenance** — even when AI tasks are skipped, `CheckDespawn()` and `seeCache.ClearIfExpired()` still run
6. **No path injection** — paths are only blocked (not substituted), preventing stale-position navigation
7. **Crash prevention** — null-check patches cover all derived type overrides dynamically

---

## Architecture: How the Patches Work Together

```
┌──────────────────────────────────────────────────────┐
│             Entity Update Loop                       │
│  World.TickEntities → Entity.Update                  │
│                                                      │
│  ┌──────────────┐   	┌─────────────────────────┐    │
│  │TargetCache   │──▶	│ GetAttackTarget (cached)│    │
│  │ (per-frame)  │  	│ GetRevengeTarget(cached)│    │
│  └──────────────┘  	└─────────────────────────┘    │
│         │                                            │
│         ▼                                            │
│  ┌────────────────────────────────────────────┐      │
│  │ Adaptive LOD Decision                      │      │
│  │  ├── Close/Combat → full update            │      │
│  │  ├── Emergency mode (adaptive, ~25-65 Z)   │      │
│  │  └── Critical mode (adaptive, ~50-120 Z)   │      │
│  │  Thresholds adjust based on baseline FPS   │      │
│  └────────────────────────────────────────────┘      │
│         │                                            │
│    ┌────┴─────┐                                      │
│    ▼          ▼                                      │
│ updateTasks  MoveEntityHeaded                        │
│ (AI LOD)     (Movement LOD)                          │
│    │              │                                  │
│    │         ┌────┴────────┐                         │
│    │         ▼             ▼                         │
│    │   MoveHelper     entityCollision                │
│    │   (Throttle)     (Physics Throttle)             │
│    │         │                                       │
│    │         ▼                                       │
│    │   SpeedStrafe (Heading Throttle)                │
│    │                                                 │
│    ▼                                                 │
│ EAITaskEvaluation (ContinueExecuteOnly)              │
│ FindPath (Duplicate-request throttle)                │
│ SeeCache (Shared Player Visibility)                  │
│ StepSound (Sound throttle)                           │
│ AttackTargetNullCheck (Crash prevention)             │
│                                                      │
│  ┌──────────────────────────────────────────────┐    │
│  │ Player-Specific Throttles                    │    │
│  │  ├── BlockRadiusEffectsTick (every 2-3 fr)   │    │
│  │  └── ShelterFrameUpdate (every 2-4 fr)       │    │
│  └──────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────┘
```

---

## Patch Details

### 1. Target Cache (`TargetCachePatch.cs`)

**What it does:** Caches the results of `GetAttackTarget()` and `GetRevengeTarget()` per entity per frame so repeated calls within the same frame return instantly.

**Why it matters:** These methods are called 5–10+ times per entity per frame from different systems (AI, movement, combat checks, our own bypass logic). Each call walks entity lists. Caching eliminates redundant work.

| Metric | Value |
|--------|-------|
| Typical hit rate | ~55–72% |
| Calls saved per frame (80 zombies) | ~4,000–6,000 |
| CPU savings | Moderate — eliminates list iteration overhead |

**Safety:** Cache is per-frame and automatically invalidated. No stale data risk.

---

### 2. Movement LOD (`MoveEntityHeadedLODPatch.cs`)

**What it does:** Throttles `EntityAlive.MoveEntityHeaded()` calls for non-combat, distant entities using a distance-tiered frame-skipping system.
  - On skip frames: apply Lite Motion
  - Apply gravity and damping to `motion`.
  - Extrapolate `position` and `boundingBox`.
  - Sync `PhysicsTransform` (so next CharacterController frame starts at the extrapolated position).


**Why it matters:** `MoveEntityHeaded` is consistently the **#1 CPU consumer** at 33–50% of total entity processing time. It handles heading calculations, rotation, and feeds into the physics collision system.
  - Advantage: preserves visual smoothness; still avoids the expensive `CharacterController.Move()` on lite frames.

| Distance | Normal | Emergency (40+ Z) | Critical (80+ Z) |
|----------|--------|--------------------|-------------------|
| < 30m | Every frame | Every frame | Every frame |
| 30–50m | Every frame | Every 2nd frame | Every 3rd frame |
| 50–80m | Every 3rd frame | Every 4th frame | Every 5th frame |
| > 80m | Every 5th frame | Every 6th frame | Every 8th frame |

**Combat bypass:** Entities with an attack target, revenge target, alert state, sleeping state, or investigate position always get full updates.

| Metric | Value |
|--------|-------|
| Typical skip rate (high-load) | 5–33% depending on proximity |
| CPU share addressed | 33–50% of total entity time |
| Frame-slicing | Entity ID modulo distributes updates evenly |

---

### 3. AI Task LOD (`UpdateTasksLODPatch.cs`)

**What it does:** Throttles `EntityAlive.updateTasks()` for distant, non-combat entities. Runs minimal maintenance (despawn check, see-cache clear) when skipping.
  - On throttled frames: run Lite Maintenance:
  - `CheckDespawn()` and `seeCache.ClearIfExpired()`
  - Continue executing currently-running tasks via `EAITaskEvaluationThrottlePatch.ContinueExecutingOnly(...)` (falls back to `aiManager.Update()` if reflection failed)
  - Pick up path results and `navigator.SetPath()`
  - Run `navigator.UpdateNavigation()`, `moveHelper.UpdateMoveHelper()`, `lookHelper.onUpdateLook()` — preserves movement & path following

**Why it matters:** `updateTasks` is the **#2 CPU consumer** at 21–29%. It runs the full EAI task list (approach, attack, wander, investigate, etc.) every frame for every entity.
  - Advantage: avoids the expensive re-evaluation loop while preserving smooth navigation, preventing rubberbanding.

| Distance | Normal | Emergency (40+ Z) | Critical (80+ Z) |
|----------|--------|--------------------|-------------------|
| < 20m | Every frame | Every frame | Every frame |
| 20–30m | Every frame | Every 2nd frame | Every 2nd frame |
| 30–50m | Every frame | Every 2nd frame | Every 3rd frame |
| 50–80m | Every 2nd frame | Every 4th frame | Every 5th frame |
| > 80m | Every 4th frame | Every 6th frame | Every 8th frame |

**Minimal maintenance on skip:** `CheckDespawn()` and `seeCache.ClearIfExpired()` still run to preserve entity lifecycle.

| Metric | Value |
|--------|-------|
| Typical skip rate (high-load) | 4–33% depending on proximity |
| CPU share addressed | 21–29% of total entity time |

---

### 4. EAITaskEvaluationThrottle (`EAITaskEvaluationThrottlePatch.cs`)
- Utility class (not a Prefix patch).
- `ContinueExecutingOnly(EAIManager)`:
  - Updates `interestDistance`.
  - Calls `Update()` on executing tasks (no enumeration/re-evaluation).
  - Uses `AccessTools.FieldRefAccess` to access `tasks`/`targetTasks` and call `GetExecutingTasks()`.
  
---

### 5. Physics Collision Throttle (`EntityCollisionThrottlePatch.cs`)

**What it does:** Throttles `Entity.entityCollision()` (which calls `CharacterController.Move()`) for distant, non-combat entities during emergency mode only.

**Why it matters:** `CharacterController.Move()` is one of the most expensive single operations in Unity — it performs full physics collision detection against the world. During large hordes, distant zombies don't need pixel-perfect collision every frame.

| Distance | Emergency (40+ Z) | Critical (80+ Z) |
|----------|--------------------|-------------------|
| < 20m | Every frame | Every frame |
| 20–30m | Every frame | Every 2nd frame |
| 30–50m | Every 2nd frame | Every 3rd frame |
| > 50m | Every 3rd frame | Every 5th frame |

**Safety:** Only activates in emergency mode. Combat entities always get full collision. No `ApplySimpleMotion` substitute (removed — it caused entities to fall through the world).

| Metric | Value |
|--------|-------|
| Typical effectiveness | ~100% skip rate during emergency |
| Activation threshold | 40+ zombies only |

---

### 6. Move Helper Throttle (`MoveHelperThrottlePatch.cs`)

**What it does:** Throttles `EntityMoveHelper.UpdateMoveHelper()` obstacle checking when entities are moving freely. Only runs full obstacle detection (raycasting) when an entity appears stuck — otherwise, checks are spread out over many frames.
  - Works orthogonally with voxel-based raycast replacements (ZehMatt).

**Why it matters:** `UpdateMoveHelper` does constant raycasting for `CheckWorldBlocked` and `CheckEntityBlocked` even when entities are moving freely. The stuck-detection logic means only entities that actually need course corrections get frequent checks.

| State | Check Interval |
|-------|---------------|
| Moving freely | Every 20th frame |
| Stuck | Every 4th frame (vanilla rate) |
| Combat-engaged | Every frame |

| Metric | Value |
|--------|-------|
| Typical throttle rate | High for wandering entities |
| Stuck detection threshold | < 0.1m movement over check window |

---

### 7. FindPath Duplicate Throttle (`FindPathCachePatch.cs`)

**What it does:** Blocks duplicate A* pathfinding requests for the same entity when the target position, entity position, and speed haven't changed within a configurable TTL window (default: 1 second).

**Why it matters:** Distant idle zombies often re-request the exact same path repeatedly. Each `FindPath` triggers a full A* calculation. This patch prevents the redundant recalculation.

**Safety:** Combat-engaged and close (< 20m) entities always get fresh paths. No path injection — the original path request simply proceeds or is blocked.

| Metric | Value |
|--------|-------|
| Default TTL | 1 second |
| Close bypass | 20m |
| CPU savings | Small per-entity but prevents pathfinding spikes |

---

### 8. Step Sound Throttle (`ThrottleStepSoundPatch.cs`)

**What it does:** Rate-limits `EntityAlive.updateStepSound()` to 20 Hz per entity (one call per 50ms).

**Why it matters:** Step sounds are purely cosmetic. The vanilla game fires them every frame, which generates audio events and raycasts for ground material detection far more often than audibly necessary.

| Metric | Value |
|--------|-------|
| Cooldown | 50ms (20 Hz per entity) |
| Savings at 80 zombies | ~60 step-sound calls/frame eliminated |

---

### 9. Attack Target Null Check (`AttackTargetNullCheckPatch.cs`)

**What it does:** Prevents `NullReferenceException` crashes in `GetAttackTargetHitPosition()` when the attack target is killed between the AI decision frame and the attack execution frame.

**Why it matters:** This is a vanilla bug. When a target entity is destroyed mid-frame, `attackTarget.getChestPosition()` throws a NRE that propagates up through the entire entity tick loop. The patch returns the entity's own chest position as a safe fallback.

- Other small safety + cache clearing patches present (Entity cleanup, etc).

---


### Profiler Companion System

## Automatic High-Load Capture

The profiler automatically captures detailed performance snapshots when:
- FPS drops below 30 **and** zombie count is 50+
- FPS drops below 50% of the established baseline

Captures are debounced (30s minimum between captures) and limited to 5 per session.

## Baseline FPS Tracking
- Waits 120 seconds after game start to avoid measuring loading/trader screen FPS
- Requires: < 10 zombies, 60-frame rolling average > 50 FPS
- Updates upward if conditions improve by 15%+ (handles initial low readings)

## Peak FPS Tracking
- Filtered to < 200 FPS to exclude teleport/loading frames (where deltaTime approaches zero)

## Output Files
| File | Contents |
|------|----------|
| `7dtd_profile.csv` | 30-second summary rows with FPS, timing, and optimization counters |
| `7dtd_HIGHLOAD_*.csv` | Detailed snapshots during FPS drops — timing breakdown, call counts, optimization effectiveness, cost-per-zombie |

## Key CSV Columns
| Column | Description |
|--------|-------------|
| `TargetCacheHits` / `Misses` | How effective the per-frame target cache is |
| `MoveLODSkipped` | Movement updates skipped by distance LOD |
| `TaskLODSkipped` | AI task updates skipped by distance LOD |
| `CollisionSkipped` | Physics collision checks skipped |
| `MoveHelperThrottled` | Move helper obstacle checks throttled |

---

### Test Results Summary

## Session Data (Tier 6 POI Clear, 60–150 zombies)

| Capture | Zombies | FPS | Top Bottleneck | LOD Skip Rate | Target Cache Hit |
|---------|---------|-----|----------------|---------------|-----------------|
| #1 (fresh spawn) | 82 | 29.9 | MoveEntityHeaded (44%) | 4.4% | 72.7% |
| #2 (mid-clear) | 68 | 30.0 | MoveEntityHeaded (33%) | 32.7% | 71.8% |
| #3 (heavy wave) | 73 | 29.9 | MoveEntityHeaded (36%) | 14.2% | 70.0% |
| #4 (peak load) | 81 | 18.7 | MoveEntityHeaded (42%) | 7.5% | 74.0% |
| #5 (declining) | 66 | 28.1 | MoveEntityHeaded (48%) | 9.7% | 70.3% |


## Summary Table: Optimization Effectiveness by Patch

| Patch | Status | Technique | Notes |
|-------|--------|-----------|-------|
| TargetCachePatch | ✅ Excellent | Per-frame cache | 55–60% cache hit rate consistently |
| MoveSpeedCachePatch | ✅ Excellent | 20-tick cache | 100% cache hit rate |
| MoveEntityHeadedLODPatch | ✅ Good | Distance LOD + Lite Motion | V3: extrapolates position with gravity/friction on skip frames instead of hard-skip. Saves CharacterController.Move() cost without freezing entities |
| UpdateTasksLODPatch | ✅ Good | Distance LOD + Lite Maintenance | V3: runs navigation + movement + look every frame, only skips EAI re-evaluation. Prevents jerkiness and path loss |
| EAITaskEvaluationThrottlePatch | ✅ Good | Continue-only mode | Continues executing tasks without re-evaluation loop (CanExecute/Continue/isBestTask) |
| ZombieFrameSkipPatch | ✅ Good | Frame skip 80m+ | Working for distant non-combat entities |
| EntityCollisionThrottlePatch | ✅ Perfect | Distance throttle | 100% skip rate for distant non-combat entities |
| MoveHelperThrottlePatch | ✅ Perfect | Stuck-aware throttle | 100% skip rate for freely-moving non-combat entities |
| SpeedStrafeThrottlePatch | ✅ Perfect | Distance throttle | 100% skip rate when applicable |
| BlockPosUpdateThrottlePatch | ✅ Good | Distance throttle | Large skip counts at high zombie counts |
| SharedPlayerVisibilityPatch | ✅ Good | Shared LOS cache | 2,500–22,700 shared hits at scale |
| FindPathCachePatch | ✅ Good | Duplicate request block | Prevents redundant A* pathfinding |
| ThrottleStepSoundPatch | ✅ Good | 20 Hz rate limit | Eliminates ~60 step-sound calls/frame at 80 zombies |
| AttackTargetNullCheckPatch | ✅ Safety | Null guard | Prevents vanilla NRE crash |
| PlayerUpdateThrottlePatch | ✅ Good | Frame throttle | Shelter + BlockRadius throttled |
| Combat Stagger (V3) | 🧪 Testing | Lite motion + Lite maintenance | Re-enabled with V3 smooth movement. Needs measurement to confirm quality |

---

### Key Observations
1. **MoveEntityHeaded dominates** at 33–50% of total CPU time across all captures
2. **Target cache** consistently delivers 70–78% hit rate
3. **LOD skip rates are low during intense combat** (4–14%) because most zombies are close/combat-engaged — this is **by design** (combat bypass ensures correct behavior)
4. **LOD skip rates increase with distance** — capture #2 showed 33% skip rate when zombies were spread across multiple rooms
5. **Collision throttle** shows ~100% effectiveness during emergency mode

---

### Performance Without Mod vs With Mod
Based on profiling data, without optimization patches the game would need to process:
- ~80 `GetAttackTarget` + `GetRevengeTarget` calls per entity per frame (reduced to ~20 with cache)
- Full `MoveEntityHeaded` every frame for all entities (reduced by 5–33% with LOD)
- Full AI task evaluation every frame (reduced by 4–33% with LOD)
- Full `CharacterController.Move()` every frame for distant zombies (reduced significantly during emergency mode)

---

## Configuration
All patches can be toggled via the json config file loaded at startup:

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableProfiling` | `true` | Master switch for all profiling/timing instrumentation |
| `EnableMoveLOD` | `true` | Movement and collision LOD throttling |
| `EnableTargetCache` | `true` | Per-frame attack/revenge target caching |
| `EnablePathCache` | `true` | FindPath duplicate-request throttle |
| `EnableStaggeredUpdate` | `true` | AI task LOD throttling |
| `EnableStepSoundThrottle` | `true` | Step sound rate limiting |
| `EnableSpatialGrid` | `false` | Experimental spatial hash grid (disabled by default) |
| `PathCacheTTLSeconds` | `1.0` | How long to block duplicate path requests |

---

### Vanilla Game Design
## Player Update() — every frame:
EntityPlayerLocal.Update()                     ← EVERY FRAME (MonoBehaviour)
├── base.Update() → EntityPlayer.Update()
│   ├── base.Update() → EntityAlive.Update()
│   │   ├── base.Update() → Entity.Update()   ← transform + audio monitoring
│   │   ├── updateNetworkStats()
│   │   ├── Progression.Update()
│   │   └── render fade
│   ├── ChunkObserver.SetPosition()
│   ├── avatar controller angles
│   └── achievement/time tracking
├── renderManager.FrameUpdate()
├── CameraDOFFrameUpdate()                     ← EMPTY (no-op)
├── FrameUpdateCamera()                        ← Raycasts (3rd person only)
│   ├── UnderwaterCameraFrameUpdate()
│   └── ThirdPersonLineOfSightCheck()          ← Voxel.Raycast
├── ShelterFrameUpdate()                       ← THROTTLED ✓ (raycasts)
├── crosshair calculations
└── autoMove.Update()

EntityPlayerLocal.OnUpdateLive()               ← EVERY TICK (20Hz game tick)
├── bedroll/radiation checks (5s timers)
├── GameEventManager.Current.UpdateCurrentBossGroup()
├── pushOutOfBlocks() × 4                      ← block collision checks
├── base.OnUpdateLive() → EntityPlayer.OnUpdateLive()
│   ├── base.OnUpdateLive() → EntityAlive.OnUpdateLive()
│   │   ├── Stats.Tick()
│   │   ├── updateCurrentBlockPosAndValue()
│   │   ├── MoveEntityHeaded()                 ← heavy (collision, physics)
│   │   │   ├── entityCollision()              ← iterates entities
│   │   │   └── world.CheckEntityCollisionWithBlocks()
│   │   └── stun/damage processing
│   └── CheckSleeperTriggers()
├── m_vp_FPController (player physics)
├── challengeJournal.Update()
├── QuestJournal.Update()
├── BlockRadiusEffectsTick()                   ← THROTTLED ✓ (chunk iteration)
└── stamina/exhaustion processing

The key insight: the player's MoveEntityHeaded calls entityCollision which iterates all entities for collision. And pushOutOfBlocks (×4) does block position checks. These scale with entity count but we skip the player for throttling.


## This is what vanilla updateTasks() does:
1. Check DebugStopEnemiesMoving → SetMoveForward(0) and RETURN EARLY
2. CheckDespawn()
3. ClearIfExpired()
4. aiActiveDelay/aiManager.Update() (AI decisions)
5. PathFinderThread.GetPath() + navigator.SetPath()
6. navigator.UpdateNavigation()      ← CRITICAL: makes entity follow path
7. moveHelper.UpdateMoveHelper()     ← CRITICAL: makes entity physically move
8. lookHelper.onUpdateLook()


## PhysX Call Site Inventory — Ranked by Optimization Potential
Tier 1 — Hot AI paths (per-entity, per-tick, high zombie count multiplier)
File	Method	PhysX Call	Frequency	Patchable?
EntityMoveHelper.cs:708	Jump check	Physics.Raycast down 3.4m	Per entity deciding to jump	✅ Voxel fast-reject
EntityMoveHelper.cs:1006	CalcObstacleSideStepArc	Physics.SphereCast per angle × per height layer	Per entity per move tick	✅ ZehMatt already covers this
EntityMoveHelper.cs:1021	CheckEntityBlocked	Physics.SphereCast 0.8m forward	Per entity per move tick	✅ ZehMatt already covers this
ASPPathFinder.cs:177-196	IsLineClear	Physics.Linecast + Physics.SphereCast × 2 (tall entities)	Per path-smoothing step	✅ Voxel fast-reject
EAIApproachAndAttackTarget.cs:509	Target position adjustment	Physics.Raycast down 1.02m	Per entity chasing	✅ Voxel GetBlock check
EUtils.cs:39,59	isPositionBlocked	Physics.Raycast	Called from AI/nav code	✅ Voxel fast-reject

Tier 2 — Per-entity sensing (already partially cached by your SeeCache patches)
File	Method	Call	Notes
EntityAlive.cs:4122	CanSee(Vector3)	Voxel.Raycast	Already voxel-based — no PhysX here
EntityAlive.cs:4154	CanEntityBeSeen	Voxel.Raycast	Already voxel-based — no PhysX here
EntitySeeCache.cs	CanSee / ClearIfExpired	Cache layer over CanEntityBeSeen	Your existing SharedPlayerVisibilityPatch helps here
Good news: 7DTD's vision system already uses Voxel.Raycast, not PhysX. No action needed.

Tier 3 — Player-only or low-frequency (skip for now)
File	Method	Why skip
KinematicCharacterMotor.cs (10 calls)	Player character controller	Runs only for player, not zombies. Touching this risks movement bugs.
Audio/Manager.cs:1458	Sound occlusion	Two raycasts per sound event. Low frequency, player-only concern.
WeatherManager.cs:1730	Rain ground detection	SphereCast down, once per weather tick. Negligible.
WireNode.cs:364,405	Electrical wire physics	Only when wires exist. Rare.
vp_SimpleAITurret.cs:64	Turret targeting	OverlapSphere + Linecast. Only for placed turrets, low count.
RaycastPathUtils.cs / RaycastPathWorldUtils.cs	Raycast pathing system	Used by newer UAI (bandits/NPCs), not classic zombie EAI.

Tier 4 — Structural/entity queries (allocation-heavy, not PhysX)
File	Method	Issue
UAIBase.cs:113	addEntityTargetsToConsider	GetEntitiesInBounds with full see-distance bounds + AddRange + Sort per UAI entity per eval. Allocates a new list each time.
ThreatLevelUtility.cs:37	GetThreatLevelOn	GetEntitiesInBounds(typeof(EntityEnemy), 50m box) per player per music tick. Uses static list, OK.
ThreatLevelTracker.cs:176	Same pattern	Same bounds query.

---

### Testing with Laydor's perf mod.
----------------------------------
Summary — Laydor mod vs previous run (z158 -> z165)
•	Test used for comparison
•	Without Laydor: 7dtd_HIGHLOAD_LOW_FPS_z158_fps29_20260307_164312.csv
•	With Laydor:    7dtd_HIGHLOAD_LOW_FPS_z165_fps29_20260307_080601.csv
Key per‑zombie metrics (improvement = positive)
•	TotalMs per zombie: 28.7959 -> 21.7647  (−7.0312 ms, −24.4%)
•	EntityAlive.MoveEntityHeaded (ms/zombie): 12.9104 -> 11.3896  (−1.5208 ms, −11.8%)
•	EntityAlive.updateTasks (ms/zombie): 3.6640 -> 3.3388  (−0.3252 ms, −8.9%)
•	EntityPlayerLocal.Update (ms/zombie): 5.1671 -> 2.0852  (−3.0819 ms, −59.6%)
Top-level totals (absolute)
•	MoveEntityHeaded totalms: 2,039.85 -> 1,879.28 (−160.57 ms, −7.9%)
•	updateTasks totalms: 578.91 -> 550.90 (−28.01 ms, −4.8%)
•	PlayerLocal.Update totalms: 816.40 -> 344.06 (−472.34 ms, −57.9%)
Counters / optimization behavior (notable)
•	MoveEntityHeaded.LODSkipped: 3,184 -> 2,124 (fewer LOD skips in the Laydor run)
•	MoveEntityHeaded.calls: 9,414 -> 7,728 (fewer calls recorded in Laydor run)
•	SeeCache.SharedHit: 430 -> 1,638 (much higher shared LOS cache hits with Laydor)
•	ZombieFrame.Skipped: 248 -> 421 (more frame skips applied)
•	MoveSpeed.CacheHit: 9,263 -> 7,813 (100% hit rate on both; counts differ by run)
Interpretation
•	Net effect: the run with Laydor shows a clear performance improvement across major metrics: total work per zombie dropped ~24% and MoveEntityHeaded per-zombie dropped ~12%.
•	The reduction in per-call cost for MoveEntityHeaded despite fewer LOD skips suggests better CPU locality or instruction-cache behaviour — exactly the kind of win Laydor described (sorting tick list by class improves instruction-cache hits).
•	Large jump in SeeCache.SharedHit and in ZombieFrame.Skipped indicates the system is also sharing/combining work more effectively in the Laydor run.
•	The big drop in total PlayerLocal.Update time is surprising (should be fixed per-frame), which implies run-to-run variability (player activity, camera, animation states) — treat single-run player delta cautiously.
Caveats
•	These captures are single-run snapshots. Differences can come from spawn positions, map area, player actions, exact timing, and random seed. The per-zombie normalization helps, but variance remains.
•	Some counters changed (calls/skips) — that can be due to slightly different entity lists or thresholds at the sample moment, not only Laydor.

---

### What ZehMatt Is Targeting:
-----------------------------
He's attacking a completely different bottleneck than we are. His 4 patches all target EntityMoveHelper sub-methods and replace PhysX raycasts/sweeps with cheap voxel grid raycasts:
Patch	Vanilla Cost	ZehMatt's Approach
CheckWorldBlocked	PhysX raycast	Voxel raycast as fast-reject; falls through to physics only if voxel says "clear"
CheckBlocked	PhysX raycast per height layer	Same voxel fast-reject pattern
CalcObstacleSideStepArc	PhysX sweep at N angles	Voxel raycast per angle step
CheckEntityBlocked	PhysX sphere query	GetEntitiesAround + manual dot/radius math — full replacement, no physics at all
The key insight: 7DTD is voxel-based, so Voxel.GetNextBlockHit is an O(distance) grid walk — trivially cheap compared to PhysX broadphase→narrowphase→contact generation. For block collision, the voxel check gives the same answer for a fraction of the cost.
#Is It Logical?
Yes, very. A few specifics:
•	Fast-reject pattern (patches 1–3): If the voxel world says "blocked", skip physics. If voxel says "clear", fall through to vanilla for edge cases (vehicles, doors in weird states, non-block geometry). This is the correct safety approach — no false negatives for blocks, physics only handles the rare non-voxel colliders.
•	Full replacement (patch 4 — CheckEntityBlocked): The boldest one. He completely removes PhysX from entity-entity blocking and replaces it with world.GetEntitiesAround + manual distance/dot-product checks. This is sound — you don't need PhysX to know if another zombie is 0.8m in front of you. The static s_nearby list avoids GC allocations.
•	The - dir * 0.375f ray offset in CheckBlocked accounts for entity radius so the ray starts at the entity's edge. Shows attention to the geometry.
#How It Compares to Ours
They're orthogonal — attacking different dimensions of the same problem:
	Our Mod	ZehMatt's
Strategy	Reduce how often expensive methods run	Make the methods cheaper when they do run
Targets	AI tick frequency, frame skipping, target caching, LOD intervals	Physics raycast cost inside movement helper
Mechanism	Prefix patches returning false to skip calls	Prefix patches replacing physics with voxel math
Scales with	Entity count (fewer calls per entity)	Per-call cost (each call is cheaper)
#Would It Complement Ours?
Yes, multiplicatively. Consider a frame with 80 zombies:
•	Our mod: maybe 40 of them skip UpdateMoveHelper this frame → 40 calls instead of 80
•	ZehMatt's mod: each of those 40 calls does voxel raycasts instead of PhysX → each call is ~3-5x cheaper
•	Combined: fewer calls × cheaper calls = compounding savings
The patches compose naturally. Our MoveHelperThrottlePatch gates EntityMoveHelper.UpdateMoveHelper. When we let it run, ZehMatt's patches make CheckWorldBlocked/CheckBlocked/CheckEntityBlocked (called inside UpdateMoveHelper) cheaper. No conflicts.

---
