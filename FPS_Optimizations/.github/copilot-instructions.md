# Copilot Instructions

## General Guidelines
- NEVER ADD PREFIXES TO LOGGING
- Avoid copying other modders' code; when using their ideas, implement independently.
- Provide concise progress updates; avoid stalling mid-sentence. Continue tasks, report build results, and resume work until requested to stop.
- Allow autonomous progress; proceed without interrupting the user until scans or implementations are complete.
- Prefer concrete internal types over reflection for performance; use try/catch around task calls to avoid crashes.

## Project Guidelines
- Optimization mod (FPS Optimizations) reports runtime counters into the Profiler via reflection into `ProfilingUtils.PerFrameCounters`; keep `PerFrameCounters` API stable and preserve reflection bridge compatibility.
- Move physics instrumentation to the Profiler project rather than the FPS_Optimizations mod.
- Old profiling snapshots are stored on the Desktop in two folders:
  - Profiling log files with optimizations
  - Profiling log files just vanilla

## General Info
In this project, I have been looking for ways to improve the core of the game with respect to improving FPS through various strategies such as caching, LOD, throttling, pooling, and other methods. Other complimentary projects by other authors include:
- "E:\GitHub\7dtd\my_mods\7dtd-Visual-Studio-CSharp-mods\Laydor-Playground\Playground"
- "E:\GitHub\7dtd\my_mods\7dtd-Visual-Studio-CSharp-mods\Zehmatt-VoxelHleper\src"

I run both of those mods in the game alongside mine since running all three together has shown good improvements. In this project folder, I have two sub-folders with test results, lots of them. Folders are:
- "E:\GitHub\7dtd\my_mods\7dtd-Visual-Studio-CSharp-mods\7dtd-Profiling-and-Optimizations\Profiling log files with optimizations" 
- "E:\GitHub\7dtd\my_mods\7dtd-Visual-Studio-CSharp-mods\7dtd-Profiling-and-Optimizations\Profiling log files just vanilla" 

Please only read one or two files, or you will get lost in there. So now that you have the background, what I want to do is look at other ideas that might help reduce FPS.

1. Update() dominates - consistently 50%+ of frame time. This is unexpected; player updates shouldn't scale with zombie count.
2. EntityPlayerLocal.Update dominates - This is the real surprise. The player's Update is doing something that maybe scales with zombie count (likely iterating through entities for visibility, targeting, or buff calculations).

==================================================================================

The key expensive call is m_characterController.Move(hitMove) on line 1760, which is Unity's CharacterController.Move() - this performs physics collision detection EVERY FRAME for EVERY entity.
1. entityCollision(motion) in DefaultMoveEntity (line 3835) calls CharacterController.Move() - Unity's full physics collision detection - EVERY FRAME for EVERY entity
2. UpdateMoveHelper() runs CheckWorldBlocked() and CheckEntityBlocked() every 4 ticks (line 619), doing multiple Voxel.Raycast() calls even when zombies are moving freely

FPS-based triggers from zombie count: these numbers might have changed since this writing.
- LOW_FPS fires when FPS < 30 regardless of zombie count
- FPS_DROP fires when FPS < 50% of baseline regardless of zombie count
- HIGH_ZOMBIE_COUNT fires when zombies ≥ 50 regardless of FPS
- CheckRecoveryPattern() — isCurrentlyHighLoad now also triggers at 60% of baseline FPS

==================================================================================

This is what vanilla updateTasks() does:
1. Check DebugStopEnemiesMoving → SetMoveForward(0) and RETURN EARLY
2. CheckDespawn()
3. ClearIfExpired()
4. aiActiveDelay/aiManager.Update() (AI decisions)
5. PathFinderThread.GetPath() + navigator.SetPath()
6. navigator.UpdateNavigation()      ← CRITICAL: makes entity follow path
7. moveHelper.UpdateMoveHelper()     ← CRITICAL: makes entity physically move
8. lookHelper.onUpdateLook()

The full call chain is: EntityAlive.Update() → base.Update() (Entity.Update) → OnUpdateEntity() → OnUpdateLive() → updateTasks() → UAIBase.Update() → GetAttackTarget(). Let me verify Entity.Update():
Entity.Update() does NOT call OnUpdateEntity(). It just does transforms and audio. 
World.TickEntities() → e.OnUpdateEntity() → EntityAlive.OnUpdateLive() → updateTasks() → UAIBase.Update() → GetAttackTarget()
EntityAlive → EntityEnemy → EntityHuman → EntityZombie → EntityZombieCop

EntityHuman overrides OnUpdateLive(), and EntityZombie inherits from EntityHuman. So when a zombie's OnUpdateLive() is called, virtual dispatch goes to EntityHuman.OnUpdateLive(), NOT EntityAlive.OnUpdateLive(). Our patch on EntityAlive.OnUpdateLive never fires for zombies!

MAJOR DISCOVERY! Entity.Update() does NOT call OnUpdateEntity(). They're completely separate paths:
1.	Unity path: Entity.Update() → EntityAlive.Update() → animations/transforms only
2.	World tick path: World.TickEntities() → entity.OnUpdateEntity() → OnUpdateLive() → updateTasks() → UAIBase.Update() → GetAttackTarget()
ALL zombie AI runs through path 2. And looking at the game log, only 14 of 28+ caller-site patches succeeded — critically, OnUpdateLive is NOT among the logged patches! Let me check what's happening:

UAI.UAIBase.chooseAction(). This method runs 5x/sec per zombie and triggers the full decision evaluation chain:
chooseAction → addEntityTargetsToConsider (GetRevengeTarget ×2, GetEntitiesInBounds)
→ UAIPackage.DecideAction → UAIAction.GetScore per action × target
→ UAIConsiderationPathBlocked.GetScore → GetAttackTarget (1-4 calls)

Regarding the player update loop:
- GetThreatLevelOn calls World.GetEntitiesInBounds() every single frame plus iterates the results (entities)
- DynamicMusic.ThreatLevelUtility.GetThreatLevelOn — calls World.GetEntitiesInBounds + iterates all entities every single frame just for music

World.Tick:
- PrefabLODManager.FrameUpdate() — runs every frame from the main game loop.

==================================================================================

We systematically scanned the entire assembly for per-frame entity-scanning patterns and found this:
System Checked	--> Result
GetEntitiesInBounds (11 call sites)	--> 	Only ThreatLevelUtility was per-frame + zombie-scaling
DynamicMusic.PlayerTracker	--> 			Scans NPCs/traders (small count, not zombie-scaling)
Update(float, int) (11.71%)	--> 			Distributed small ops per entity, no single hotspot
Update(float, int)	--> 					Physics interp + transform sync — core, can't skip
GameManager.gmUpdate subsystems	--> 		Most already covered or don't scale with zombies
World.EntityActivityUpdate	--> 			Cloth/jiggle toggle + sort — cheap math ops
PrefabLODManager.FrameUpdate				Scales with POIs, not zombies
Per-entity physics queries	--> 			All event-driven (explosions, damage, turrets)
updateNetworkStats	--> 					Queue processing, no-op when empty
CameraDOFFrameUpdate	--> 				Empty method (no-op)

April 08/26 Profiler Fix (CallChainInstrumentation.cs)
==================================================================================
- Added EntityHuman.OnUpdateLive — fixes the virtual dispatch miss (zombies inherit from EntityHuman, not EntityAlive directly)
- Added UAI.UAIConsiderationPathBlocked.GetScore — the direct caller of GetAttackTarget in the UAI chain
- Added UAI.UAIBase.addEntityTargetsToConsider — the direct caller of GetRevengeTarget
- Added 6 missing EAI types (TakeCover, Distraction, ConsiderCover, NearestCorpse, Target)
- Fixed DeclaredOnly method resolution — EAI .Update() patches were failing because GetMethod found the base class's empty virtual method instead of the override
- Upgraded diagnostic logging to Info level with declaring-type info — now you'll see exactly which patches succeed/fail and where virtual dispatch resolves

April 09/26 Implemented: ThreatLevelThrottlePatch
==================================================================================
Created ThreatLevelThrottlePatch.cs — a Harmony Prefix+Postfix on DynamicMusic.ThreatLevelUtility.GetThreatLevelOn:
- Prefix: When zombie count ≥ threshold (default 15), caches the float result and returns cached value for 30 frames (~0.5s at 60fps), skipping the expensive computation
- Postfix: Captures the actual result when computation runs, for use in subsequent cached frames
- Why it works: The method already smooths output via a Queue-based rolling average — skipping frames is invisible to the music system
- What it eliminates: Per-frame GetEntitiesInBounds (chunk iteration), 2 entity-list iterations (zombiesContributingThreat + EnemiesTargeting), sleeper-volume scan, environmental checks
Config fields added (v11): EnableThreatLevelThrottle, ThreatLevelThrottleZombieThreshold=15, ThreatLevelThrottleFrames=30

Analysis of the Particle Effect Problem:
The "double whammy": When you're bleeding, zombies are right on top of you. Our distance-based optimizations (speed-curve, move-helper throttle, collision throttle, etc.) don't kick in for close zombies. Meanwhile, p_bleeding is a full-screen particle effect that:
1. Has CPU particle simulation cost every frame per active particle
2. Causes high GPU fill-rate / overdraw (blood sprites near camera = large screen coverage)
3. Gets RESPAWNED every 4 buff ticks (calls SpawnParticleEffect → GetComponentsInChildren allocations + Clear()/Play() restart)
Key optimization targets in SpawnParticleEffect:
- Redundant respawns: When entity already has active particles of same type, game's reuse path still does GetComponentsInChildren<ParticleSystem>() + GetComponentsInChildren<TemporaryObject>() (GC allocations every time) and restarts all particles via Clear()/Play()
- Emission rate: Reducing emission count reduces both CPU simulation and GPU rendering without removing the effect entirely
- New spawn path: Object.Instantiate + GetComponentsInChildren<Renderer>() on every new particle
The patch strategy:
1. Prefix: Under high zombie load, if entity already has an active particle of same type → skip the spawn entirely. Existing particles keep playing. Avoids all allocations.
2. Postfix: For newly spawned particles under load → scale down emission rate and max particles. Less GPU cost.

April 10/26 Implemented: DefaultMoveEntityLODPatch
==================================================================================
Three changes:
1. New DefaultMoveEntityLODPatch.cs — physics LOD with independent intervals (the big win)
2. Wire Update(float, int) into Refresh() (bug fix)
3. Update ClearAllOptimizationCaches() for the new patch
