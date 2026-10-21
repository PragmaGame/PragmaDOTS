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
- Modules on top
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
| `LockstepEntityId` | `uint value`, `bool IsAssigned`. Add with value 0; assigned at the end of the creation tick, from 1, never reused. A copy made with `Instantiate` gets its own (the id stays with the entity that had it); disabled entities keep theirs |
| `LockstepEntityIdMap` | Singleton (on a system entity, so use `SystemAPI.GetSingleton`, or a query with `EntityQueryOptions.IncludeSystems`). Rebuilt at the end of every tick that created, destroyed or moved an entity with an id: an entity created on tick T resolves from tick T + 1; disabled entities are in it |
| `EntityIdPart` | Gives an actor a `LockstepEntityId` |

## Actors in the simulation

Actors are core types (`Pragma.Dots.Actors`; the `pragma-dots-actors` skill and its `references/api.md` list them all).
What the lockstep module adds:

| Type | Notes |
|---|---|
| `ActorRegistryQuery` | `Create(ref state)` in `OnCreate` (the registry query, required for the update), `Get(ref state, query)` in `OnUpdate`: the `ActorLookup` of the simulation world. `ActorLookup.Get(ref state)` builds the query where it is called (in `OnUpdate`, an Entities warning) |
| `ActorLookup` | The registry: `TryGetType(key, out type)`, `TryGetTag(name, out tag)`, `HasTag(type or Actor, tag)`, `Instantiate(EntityManager, type)`, `Instantiate(EntityCommandBuffer, type)`, `Instantiate(ParallelWriter, sortKey, type)`. Get it again after a structural change |
| `LockstepActorUtility` | `CopyRegistry(source, destination)`, `CopyPlacedActors(source, destination)`: what `LockstepClientWorldUtility.CreateSimulationOptions` does before tick 0 |
| `PlacedActor { ulong order }` | Placed actors are copied into every simulation world before tick 0 in this order, typed by key; references to anything outside the placed actors (actor prefabs included) become `Entity.Null` |
| `TransformPart`, `EntityIdPart` | The lockstep parts: `LockstepTransform` from the placement (*Interpolate*, on by default, adds `LockstepTransformPrevious`), `LockstepEntityId` |

Spawning: `var actors = ActorRegistryQuery.Get(ref state, _registry); if (actors.TryGetType("rifleman", out var type))
{ var e = actors.Instantiate(state.EntityManager, type); }` with `_registry` from `ActorRegistryQuery.Create` in
`OnCreate`, then set its `LockstepTransform` (a prefab stands at the identity).
The registry always exists in simulation worlds made by the built-in systems; it is empty when a baking subscene had
not loaded, so start the session with `waitForActorRegistry`.

## Modules on top

The types of the navigation, vision, stats, views and state machines modules are listed in the `references/api.md`
of their skills (`pragma-dots-navigation`, `pragma-dots-stats`, `pragma-dots-views`) or in the skill itself
(`pragma-dots-vision`, `pragma-dots-state-machines`).

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
| `LockstepViewSystem` (presentation worlds, `SimulationSystemGroup` before `TransformSystemGroup`) | Mirrors actors as copies of their type's prefab (Entities Graphics); `LockstepView { Entity simulationEntity }`, `TryGetView`, `ViewCount` |
| `EntityViewManager.TryGet(World, out manager)` (`Pragma.Dots.Views`) | GameObject views of the entities with an `EntityViewKey`: `TryGetView`, `Attach`, `Detach`, `ForceUpdate`; see the `pragma-dots-views` skill |

## Actor parts

The parts of every module, by menu path, and the authoring components are listed in the `pragma-dots-actors` skill
(`references/api.md`). For data of your own, write a part rather than a baker: the same part serves subscenes and
run-time content, and the inspector edits it in the definition.
