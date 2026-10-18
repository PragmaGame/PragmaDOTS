# Simulation-side API reference

Namespace `Pragma.Dots.Lockstep` (assembly `Pragma.Dots.Lockstep`, the Lockstep module: `PRAGMA_DOTS_LOCKSTEP`) unless
noted. Fields of components are public and camelCase; settings and options are PascalCase properties.

## Contents

- System group and order
- Singletons of the simulation world
- Player entities
- Input and commands
- Transforms
- Entity ids
- Actors in the simulation
- Navigation
- Vision
- Stats
- State machines
- Spatial index
- Bytes helpers
- Core utilities
- Presentation-side access
- Actor parts

## System group and order

| Type | Notes |
|---|---|
| `LockstepSimulationSystemGroup` | Root group of the simulation world, updated once per confirmed tick. Members inherit `LockstepWorldFilter.SIMULATION` |
| `LockstepWorldFilter.SIMULATION` | Custom `WorldSystemFilterFlags` bit (1 << 28) for systems that cannot live inside the group |
| `LockstepFrameApplySystem` | `OrderFirst`. Destroys players that left last tick, moves input to previous, clears command buffers, disables the joined flag, applies the frame |
| `LockstepTransformHistorySystem` | `OrderFirst`, after frame apply. Copies `LockstepTransform` into `LockstepTransformPrevious` |
| `LockstepBeginSimulationEntityCommandBufferSystem` | `OrderFirst`, after the transform history. Singleton: `LockstepBeginSimulationEntityCommandBufferSystem.Singleton` |
| gameplay systems | Default order slot inside the group |
| `LockstepSignalCleanupSystem` | `OrderLast`, before the end buffer. Destroys every signal (`SignalTag`): signals published through the end buffer live through the next tick |
| `LockstepEndSimulationEntityCommandBufferSystem` | `OrderLast`. Singleton: `LockstepEndSimulationEntityCommandBufferSystem.Singleton` |
| `LockstepEntityIdSystem` | `OrderLast`, after the end buffer. Assigns ids, rebuilds `LockstepEntityIdMap` |

Command buffer from a system:

```csharp
var buffer = SystemAPI.GetSingleton<LockstepEndSimulationEntityCommandBufferSystem.Singleton>()
    .CreateCommandBuffer(state.WorldUnmanaged);
// In a parallel IJobEntity: buffer.AsParallelWriter(), and use [ChunkIndexInQuery] int sortKey in Execute.
```

`LockstepSimulationOptions` (when creating `LockstepSimulation`, `LockstepClient` or `LockstepReplayPlayer`
yourself): `WorldName`, `AutoDiscoverSystems` (default true), `AdditionalSystems` (`IReadOnlyList<Type>`),
`Initialize` (`Action<World>`, runs once before tick 0), `CanCreate` (`Func<bool>`, gate before creation).

## Singletons of the simulation world

| Component | Members |
|---|---|
| `LockstepTime` | `int tick` (the tick being simulated, from 0), `int tickRate`, `FixedPoint deltaTime` (1 / tickRate, truncated), `FixedPoint elapsedTime` (tick / tickRate) |
| `LockstepSessionInfo` | `uint seed`, `int tickRate`, `int maxPlayers`, `int inputSize`, `FixedList128Bytes<byte> startData`, `T GetStartData<T>()` |
| `LockstepRandom` | `FixedRandom value` - take it by `ref` |
| `LockstepPlayerSlot` (buffer) | `Entity player` per slot, `Entity.Null` when free |
| `LockstepEntityIdCounter` | `uint last` |
| `LockstepEntityIdMap` | `NativeHashMap<uint, Entity> map`, `TryGetEntity(uint, out Entity)`, `TryGetEntity(LockstepEntityId, out Entity)`; ignored by the checksum |
| `LockstepFrameData` (buffer) | `byte value`: the raw frame of the current tick (rarely needed) |
| `ActorRegistry` + `ActorType`, `ActorTagName` (buffers; `Pragma.Dots.Actors`, the core) | The actor registry, copied from the presentation world when the simulation is created; empty when it had none yet. Read it with `ActorLookup` |

## Player entities

Created by `LockstepFrameApplySystem` on the join tick.

| Component | Members |
|---|---|
| `LockstepPlayer` | `int slot`, `int joinTick`, `FixedList64Bytes<byte> joinData`, `T GetJoinData<T>()` |
| `LockstepPlayerInput` | `int size`, `T Get<T>()`, `T GetPrevious<T>()`, `bool HasChanged()`; `CAPACITY` = 128 |
| `LockstepCommand` (buffer) | Commands confirmed for this tick, cleared at the start of the next |
| `LockstepPlayerJoined` (enableable) | Enabled only on the join tick |
| `LockstepPlayerLeft` (enableable) | Enabled only on the leave tick; the entity is destroyed at the start of the next tick |

Queries over enableable components filter on the enabled state by default:
`SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerJoined>()` returns only players that joined this tick.

## Input and commands

Client (presentation world):

| API | Notes |
|---|---|
| `LockstepInputSystemGroup` | Client/local world group for input systems, before the session update |
| `LockstepLocalInput` (singleton) | `Set<T>(in T)`, `Get<T>()`, `int size`; same entity holds the `LockstepCommand` and `LockstepCommandData` buffers for outgoing commands |
| `LockstepClient.SetInput<T>(in T)`, `AddCommand<T>(in T)`, `AddCommand<T, TData>(in T, NativeArray<TData>)` | The same from outside ECS |

`LockstepCommand`:

| Member | Notes |
|---|---|
| `static LockstepCommand Create<T>(in T payload)` | Payload: unmanaged struct, at most `MAX_PAYLOAD_SIZE` (122) bytes |
| `static LockstepCommand Create<T, TData>(in T payload, NativeArray<TData> data, DynamicBuffer<LockstepCommandData> dataBuffer)` | Plus data of any length, appended to the `LockstepCommandData` buffer of the entity the command is added to |
| `bool Is<T>()`, `bool TryGet<T>(out T)`, `T Get<T>()` | Decode the payload in the simulation |
| `NativeArray<T> GetData<T>(DynamicBuffer<LockstepCommandData>)` | The data as `T` elements: a view valid until the buffer changes; trailing bytes short of a `T` are ignored |
| `int DataLength` | Data size in bytes, 0 without data |
| `static int TypeHashOf<T>()` | Stable 32-bit hash of the assembly-qualified type name: renaming the struct, its namespace or its assembly changes it |
| `int typeHash`, `byte size` | Raw fields |

`LockstepCommandData` (buffer, `byte value`): the data of the commands of the same entity, back to back, each
starting on an 8-byte boundary; on player entities and the local input entity, cleared with the commands.

`LockstepProtocol` limits: `MAX_PLAYERS` 64, `MAX_INPUT_SIZE` 128, `MAX_COMMANDS_PER_TICK` 32 (per player; the rest
moves to later ticks), `MAX_JOIN_DATA_SIZE` 62, `MAX_START_DATA_SIZE` 126.

## Transforms

| Type | Members |
|---|---|
| `LockstepTransform` | `FixedVector3 position`, `FixedQuaternion rotation`, `FixedPoint scale`; `Identity`, `FromPosition`, `FromPositionRotation`, `FromPose(in FixedPose, FixedPoint scale)`, `Forward`, `Up`, `Right`, `TransformPoint`, `InverseTransformPoint`, `TransformDirection`, `ToLocalTransform()` (render only) |
| `LockstepTransformPrevious` | `position`, `rotation`, `scale`, `capturedTickPlusOne`, `IsCapturedAt(int tick)`; written at the start of every tick |
| `LockstepTransformExtensions.Interpolate(in LockstepTransform, in LockstepTransformPrevious, int lastSimulatedTick, float alpha)` | `LocalTransform` for rendering; current value when the capture is not from `lastSimulatedTick` |
| `simulationManager.TryGetInterpolated(Entity, int lastSimulatedTick, float alpha, int shownSinceTick, out LocalTransform)` | Reads both components and interpolates; the current value until the tick after `shownSinceTick` (when the view appeared); false without `LockstepTransform` |

## Entity ids

| Type | Notes |
|---|---|
| `LockstepEntityId` | `uint value`, `bool IsAssigned`. Add with value 0; assigned at the end of the creation tick, from 1, never reused |
| `LockstepEntityIdMap` | Singleton (on a system entity, so use `SystemAPI.GetSingleton`, or a query with `EntityQueryOptions.IncludeSystems`). Rebuilt at the end of every tick: an entity created on tick T resolves from tick T + 1 |
| `EntityIdPart` | Gives an actor a `LockstepEntityId` |

## Actors in the simulation

Actors are core types (`Pragma.Dots.Actors`; the `pragma-dots-actors` skill and its `references/api.md` list them all).
What the lockstep module adds:

| Type | Notes |
|---|---|
| `ActorLookup.Get(ref state)` | The registry of the simulation world: `TryGetType(key, out type)`, `TryGetTag(name, out tag)`, `HasTag(type or Actor, tag)`, `Instantiate(EntityManager, type)`, `Instantiate(EntityCommandBuffer, type)`, `Instantiate(ParallelWriter, sortKey, type)`. Get it again after a structural change |
| `LockstepActorUtility` | `CopyRegistry(source, destination)`, `CopyPlacedActors(source, destination)`: what `LockstepClientWorldUtility.CreateSimulationOptions` does before tick 0 |
| `PlacedActor { ulong order }` | Placed actors are copied into every simulation world before tick 0 in this order, typed by key; references to anything outside the placed actors (actor prefabs included) become `Entity.Null` |
| `TransformPart`, `EntityIdPart` | The lockstep parts: `LockstepTransform` from the placement (*Interpolate*, on by default, adds `LockstepTransformPrevious`), `LockstepEntityId` |

Spawning: `var actors = ActorLookup.Get(ref state); if (actors.TryGetType("rifleman", out var type)) { var e =
actors.Instantiate(state.EntityManager, type); }`, then set its `LockstepTransform` (a prefab stands at the identity).
The registry always exists in simulation worlds made by the built-in systems; it is empty when a baking subscene had
not loaded, so start the session with `waitForActorRegistry`.

## Navigation

Namespace and assembly `Pragma.Dots.Navigation`: the Navigation module (`PRAGMA_DOTS_NAVIGATION`, needs Lockstep).

| Type | Members |
|---|---|
| `LockstepNavGrid` | Singleton. `FixedVector2 origin` (corner of cell 0, 0 on X and Z), `FixedPoint cellSize`, `int width`, `int height`, `FixedPoint agentRadius`, `FixedPoint maxSlope` (rise over run; steeper cells are blocked, 0 none), `uint version` (changes with any cell); `Layout` (the `FixedGrid` of its cells), `CellCount`, `CornerCount`, `IsValid`, `Contains`, `GetIndex`, `GetCornerIndex`, `GetCell`, `WorldToCell`, `GetCellCenter`, `ClampToGrid` |
| `LockstepNavCell` | Buffer on the grid entity, row by row along X: `ushort blockers`, `byte clearance` (rings of free cells to the nearest blocked cell or the edge: 0 blocked, 1 next to one), `IsWalkable`. Sized by `LockstepNavObstacleSystem`, which starts every steep cell with one permanent blocker and computes the clearance again whenever a cell changes |
| `LockstepNavHeight` | Optional buffer on the grid entity: `FixedPoint value`, world Y of the ground at each cell corner, `CornerCount` of them row by row along X. Static: read when the cells are built. Agents walk on it |
| `LockstepNavObstacle` | `FixedVector2 center`, `FixedVector2 size`: a rectangle in the space of the entity's `LockstepTransform` (yaw, uniform scale), world space without one |
| `LockstepNavObstacleFootprint` | Cleanup component the obstacle system writes: the stamped world rectangle (`center`, `right`, `halfSize`, `Forward`); `Create(obstacle, transform)`, `Create(obstacle)` |
| `LockstepNavAgent` | `speed`, `pace` (walks no faster than this while above 0: the pace of its group), `angularSpeed` (radians per second, 0 turns at once), `turnInPlaceAngle` (radians: facing further than this from where its path leads, it stands and turns on the spot first; 0 walks while it turns), `stoppingDistance`, `radius` (body: overlapping agents are pushed apart; larger than the grid's agent radius, it keeps to cells with room for it; 0 takes no part), `destination`, `groupGoal` (the point of the group it was sent with: requests of a tick to the same goal cell share one search), `status`, `isPathPartial`, `waypointIndex`, `gridVersion`; `SetDestination(FixedVector3)` (alone), `SetDestination(destination, groupGoal)` (with a group), `Replan()` (plan again, same destination and group), `MustTurnInPlace(rotation, heading)`, `Stop()` (the agent's cell is checked again: stopped inside an obstacle, it walks out), `IsMoving`, `WalkSpeed`. An idle or arrived agent on a cell that becomes blocked, or too narrow for its body, walks to the nearest one it fits in |
| `LockstepNavStatus` | `Idle`, `Requested` (planned in the next navigation update), `Moving`, `Arrived` |
| `LockstepNavWaypoint` | Buffer: `FixedVector3 position`; the path, end included, Y of the destination (not walked to: agents walk on X and Z, their Y is the ground) |
| `LockstepNavVelocity` | `FixedVector2 value` (what the agent walked on its last step, units per second on X and Z), `FixedVector2 desired` (what avoidance picked for this step). With a radius, the agent takes part in avoidance |
| `LockstepNavAvoidance` | Singleton that turns avoidance on: `FixedPoint timeHorizon` (seconds ahead), `FixedPoint neighbourDistance`, `int maxNeighbours` (at most `MAX_NEIGHBOURS`), `FixedPoint crowdCost` (cells a standing agent adds to a path through the cells its body covers; 0 plans through crowds); `Default` (1.5 s, 5 units, 8, 4 cells). Without it agents plan and walk their paths as if alone |
| `LockstepNavSystemGroup` | In `LockstepSimulationSystemGroup`: `LockstepNavObstacleSystem` (first; keeps the clearance), `LockstepNavPathSystem` (groups requests, one search per group, paths in parallel), `LockstepNavAvoidanceSystem` (picks avoiding velocities, with a `LockstepNavAvoidance`), `LockstepNavMoveSystem`, `LockstepNavSeparationSystem` (pushes overlapping agents apart; opposite walkers only sideways). Set destinations before it |
| `LockstepNavPathMap` | The grid as one agent plans on it: `grid`, `cells`, `clearance` (`GetClearance` of its radius), `crowd` (standing agents per cell, empty for none), `crowdCost`; `new LockstepNavPathMap(grid, cells, clearance = 1)`, `IsPassable(cell)`. In a job, mark the field `[ReadOnly]` and create every array |
| `LockstepPathfinder` | `new LockstepPathfinder(cellCount, allocator)`, `FindPath(grid, cells, ...)` or `FindPath(map, start, destination, NativeList<FixedVector3> or DynamicBuffer<LockstepNavWaypoint>)` returns `LockstepPathStatus` (`Failed`, `Complete`, `Partial`); a group: `SearchGroup(map, goalCell, startCells)` (one search back from the goal to all starts), `GetChain(startCell, chain)`, `FollowChain(map, start, destination, chain, waypoints)` (`Failed` when the chain does not lead there: plan alone); `TryGetStartCell`, `TryGetEndCell`; `Dispose()`. Keeps scratch memory; one per thread |
| `LockstepNavigation` | `IsWalkable(grid, cells, cell or position)`, `GetClearance(grid, radius)` (1 up to the agent radius, one more per cell size larger, rounded), `IsPassable(grid, cells, cell or position, clearance)`, `UpdateClearance(grid, cells)` (after stamping cells yourself), `HasLineOfSight(grid, cells, from, to)` (`FixedVector3` or XZ) and `HasLineOfSight(grid, cells, fromXz, toXz, clearance)` (exact, corners count both sides), `TryFindNearestWalkable(grid, cells, cell, out nearest)`, `TryFindNearestPassable(grid, cells, cell, clearance, out nearest)`, `TryStep(grid, cells, from, step[, clearance], out position)` (the passable part of a step: whole, X or Z), `GetStepClearance`, `IsClear(grid, cells, footprint)` (every cell the footprint would block is inside the grid and walkable: where a building may go), `Covers(grid, footprint, cell)` and `GetCoverage(grid, footprint, out min, out max)` (the cells a footprint blocks, outside the grid too, for footprints not stamped yet), `Stamp(grid, cells, footprint, count)` for previews and tests; ground: `HasHeights(grid, heights)`, `GetHeight(grid, heights, xz)` (bilinear, the edge beyond the grid), `ToGround(grid, heights, position)` (unchanged without heights), `IsSteep(grid, heights, cell)` |

Read the cells with `SystemAPI.GetSingletonBuffer<LockstepNavCell>(true).AsNativeArray()` and the grid with
`SystemAPI.GetSingleton<LockstepNavGrid>()`.

`FixedGrid` (`Pragma.Dots.Mathematics`) is the cell math both grids share: `origin`, `cellSize`, `width`, `height`;
`CellCount`, `IsValid`, `Contains`, `GetIndex`, `GetCell`, `WorldToCell(xz)` (integer math on raw values, may lie
outside), `GetCellMin`, `GetCellCenter`, `ClampToGrid`.

## Vision

Namespace and assembly `Pragma.Dots.Vision`: the Vision module (`PRAGMA_DOTS_VISION`, needs Lockstep).

| Type | Members |
|---|---|
| `LockstepVisionGrid` | Singleton. `origin`, `cellSize`, `width`, `height` (a `FixedGrid`, `Layout`), `int slotCount` (planes: the session's `maxPlayers`, set by the system), `bool isRevealed` (every query answers true: fog off); `CellCount` (of one plane), `IsValid`, `Contains`, `WorldToCell` |
| `LockstepVisionCell` | Buffer on the grid entity, plane by plane in slot order, each row by row along X: `bool isVisible`. Sized, cleared and stamped on every tick |
| `LockstepVisionSource` | `FixedPoint radius` (sight on XZ, up to 30 000; negative sees nothing), `int slot` (sees for it; outside the session's slots, -1 included, for nobody). Needs `LockstepTransform`. Gameplay keeps both current (owner, a sight stat) |
| `LockstepVisionSystem` | `OrderLast` in `LockstepSimulationSystemGroup`, before `LockstepEndSimulationEntityCommandBufferSystem`: completes the tick's jobs, sizes the buffer (`slotCount` planes), clears it and stamps every source. The planes show the state the tick ended with |
| `LockstepVision` | `HasCells(grid, cells)`, `GetPlane(grid, cells, slot)` (one slot's cells, to draw its fog; default for a slot without a plane), `IsVisible(grid, cells, slot, xz)`, `IsVisible(grid, cells, slot, xz, radius)` (a body seen by its edge), `IsCellVisible(grid, cells, slot, cell)`, `Stamp(grid, cells, slot, xz, radius)`. A cell is visible when a circle of sight reaches into it; nothing outside the grid is visible unless revealed |

Read them with `SystemAPI.GetSingletonEntity<LockstepVisionGrid>()` and the buffer of that entity. A command checked
against vision ("attack only what you see") reads the planes the previous tick left, which is what the player saw.

## Stats

Namespace and assembly `Pragma.Dots.Stats`: the Stats module (`PRAGMA_DOTS_STATS`, needs Lockstep). It has no actor
part: a part of the game builds the buffers (`builder.AddBuffer<LockstepStat>()`, filled at once, then the empty
`LockstepStatModifier`, `LockstepStatChange`, `LockstepStatGrantor`, `LockstepStatTarget`).

| Type | Members |
|---|---|
| `LockstepStat` | Buffer: `int type` (the game's stat id, `NONE` = 0 is none), `LockstepStatKind kind` (`Attribute`, `Resource`), `FixedPoint value`; attributes: `FixedPoint baseValue`; resources: `int cap` (an attribute type or `NONE`), `LockstepStatCapPolicy capPolicy` (`KeepRatio`, `Clamp`), `FixedPoint max` (the cap fitted to, 0 before the first update). Factories `Attribute(type, baseValue)`, `Resource(type, amount)`, `Resource(type, cap, capPolicy)` (starts full), also with an enum type; `IsAttribute`, `IsResource`. Each stat once per entity |
| `LockstepStatModifier` | Buffer, attributes only: `int stat`, `LockstepStatModifierType type`, `LockstepStatStacking stacking`, `FixedPoint value`, `LockstepStatSource source`, `int endTick` (`PERMANENT` = 0: until removed); `Flat`, `AdditivePercent`, `MultiplicativePercent` factories `(stat, value, source = default, endTick = PERMANENT, stacking = Stack)`, also with an enum stat; `IsExpired(tick)` |
| `LockstepStatModifierType` | `Flat` (adds), `AdditivePercent` (shares summed: 0.25 adds 25 %, a sum of -1 or less gives 0), `MultiplicativePercent` (times 1 + value, stacking; -1 or less gives 0) |
| `LockstepStatStacking` | `Stack`, `Strongest` (of the `Strongest` modifiers of an attribute with one type and source `kind`, only the largest absolute value applies, the first on a tie) |
| `LockstepStatChange` | Buffer, resources only: `int stat`, `FixedPoint amount`, `LockstepStatSource source`; `Create(stat, amount, source = default)`, also with an enum stat. Applied and cleared by the next `LockstepStatSystem` update |
| `LockstepStatGrant` | Buffer of a granting entity: `int target` (`ANY` = 0: every receiver), `LockstepStatModifier modifier`; `Create(modifier, target = ANY)`, also with an enum target |
| `LockstepStatGrantor` | Buffer of a receiver: `Entity entity`, an entity whose grants it receives. Dropped by the stat system when the entity is gone |
| `LockstepStatTarget` | Buffer of a receiver: `int value`, a group grants can target; `Create(enum)` |
| `LockstepStatSource` | `uint kind`, `uint id`, equality. What applied a modifier, grant or change; one source's modifiers are removed together |
| `LockstepStatSystem` | In `LockstepSimulationSystemGroup`: removes expired modifiers and grants, recalculates the chunks whose stats, modifiers, grantors or targets changed, whose grantors' grants changed or which have pending changes, writes `LockstepStat` only then |
| `LockstepStats` | Extensions on `DynamicBuffer<LockstepStat>`, each with an `int` or enum type: `TryGet(type, out LockstepStat)`, `TryGetValue`, `TryGetPendingValue(changes, type, out value)` (a resource after the changes not yet applied, for payments), `TryGetBase`, `TrySetBase` (attributes only). `RemoveModifiers(modifiers, source)`, `RemoveGrants(grants, source)` (return the count, keep the order), `Calculate(type, baseValue, NativeArray<LockstepStatModifier>)`, `Id(enum)` |

Attributes, over own modifiers and received grants:
`value = (base + sum of Flat) * max(0, 1 + sum of AdditivePercent) * product of max(0, 1 + MultiplicativePercent)`; the
percentage factors stop at 0, the flat part may go below. Resources: the tick's changes are summed, then the amount is
kept within 0 and the cap; `KeepRatio` keeps the share when the cap changes, `Clamp` keeps the amount. A modifier or
grant ending on tick T + D was applied on ticks T to T + D - 1 when added on tick T before `LockstepStatSystem`.

## State machines

Namespace and assembly `Pragma.Dots.StateMachines`: the State Machines module (`PRAGMA_DOTS_STATE_MACHINES`, needs
only the core; works in any world).

| Type | Members |
|---|---|
| `StateMachine` | `int current`, `int previous`, `int next` (requested; `NONE` = 0 when none), `int ticksInState` (0 on the update of the transition); `Create(initial)`, `HasRequest`, `Is(state)`, `Entered(state)`, `Exited(state)`, `Request(state)`, `Id(enum)`; every method also takes the game's enum |
| `IStateMachine` | Marker of a machine component: a struct of the game whose only field is a `StateMachine` (checked when its group is created) |
| `StateMachineSystemGroup<TMachine>` | Abstract `ComponentSystemGroup`: every update applies the requested transitions of `TMachine` in a parallel Burst job, finished before its systems run, then updates its systems, the states. Declare an empty subclass per machine and place it with `[UpdateInGroup]` |
| `StateMachinePart<TMachine, TState>` | Abstract actor part: `Initial`; builds the machine with `StateMachine.Create(Initial)`. Declare a `[Serializable]` subclass per machine |

## Spatial index

| API | Notes |
|---|---|
| `new LockstepSpatialIndex(NativeArray<FixedVector2> positions, FixedPoint cellSize, int maxCells, AllocatorManager.AllocatorHandle allocator)` | Counting sort of the points into cells over their bounding box; the cells double while there would be more than `maxCells`. `IDisposable` |
| `Query(FixedVector2 center, FixedPoint radius)` | `Enumerator` (usable in `foreach`) of the point indices in the cells the square of half side `radius` touches: a superset of the points within `radius`. Cells row by row along X, points of a cell by index; a negative radius finds nothing, `FixedPoint.MaxValue` everything |
| `Layout`, `Count`, `IsCreated` | The cells (`FixedGrid`), the number of points |

## Bytes helpers

`LockstepBytes.ToFixedList64(in T)`, `ToFixedList128(in T)`, `Read<T>(in FixedList64Bytes<byte>)`,
`Read<T>(in FixedList128Bytes<byte>)` - for join data and start data structs.

## Core utilities

Namespace `Pragma.Dots.Utils`, assembly `Pragma.Dots.Utils.Entities` (the core).

| API | Notes |
|---|---|
| `Signals.Publish(this EntityCommandBuffer, in T)`, `Publish(this EntityCommandBuffer.ParallelWriter, sortKey, in T)`, `Publish(this EntityManager, in T)` | A signal entity: the game's component `T` and `SignalTag`. Through the end-of-tick buffer it lives through the next tick |
| `SignalTag` | On every signal; query `WithAll<SignalTag>()` |
| `IDictionaryElement<TKey>` | `TKey Key { get; set; }` on the game's buffer element (`TKey : unmanaged, IComparable<TKey>`) |
| `BufferDictionary` | Extensions on `DynamicBuffer<TElement>`: `IndexOf(key)` (index, or `~insertionIndex`), `ContainsKey`, `TryGet(key, out element)`, `ref GetOrAdd(key[, out added])`, `Remove(key)`. Sorted by key; Burst-compatible |
| `EntityManagerExtensions` | `TryGetComponent(entity, out T)`, `TryGetBuffer(entity, isReadOnly, out buffer)` |

## Presentation-side access

| API | Notes |
|---|---|
| `LockstepWorlds.TryGetClient(World, out LockstepClient)` | The session of a client or offline world |
| `LockstepWorlds.TryGetSimulation(World, out LockstepSimulation)` | Its simulation, once created |
| `LockstepWorlds.TryGetServer(World, out LockstepServer)` | Server of a server world, or the in-process server of an offline world |
| `LockstepWorlds.GetClients(list)`, `GetServers(list)` | All sessions of the process |
| `LockstepSimulation.World`, `.Tick` (simulated ticks = next tick), `.Config`, `.ComputeChecksum()` | Read only |
| `LockstepClient.InterpolationAlpha` | Blend factor in `[0, 1]` |
| `LockstepViewSystem` (presentation worlds) | Mirrors actors as copies of their type's prefab (Entities Graphics); `LockstepView { Entity simulationEntity }`, `TryGetView`, `ViewCount` |
| `EntityViewManager.TryGet(World, out manager)` (`Pragma.Dots.Views`) | GameObject views of the entities with an `EntityViewKey`: `TryGetView`, `Attach`, `Detach`, `ForceUpdate`; see `views.md` |

## Actor parts

The parts of every module, by menu path, and the authoring components are listed in the `pragma-dots-actors` skill
(`references/api.md`). For data of your own, write a part rather than a baker: the same part serves subscenes and
run-time content, and the inspector edits it in the definition.
