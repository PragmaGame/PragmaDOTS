---
name: pragma-lockstep-gameplay
description: Write deterministic gameplay with the Lockstep module of Pragma DOTS (com.pragma.dots) and the core fixed-point math - simulation systems in LockstepSimulationSystemGroup, input structs and commands, players joining and leaving, spawning actors by key (ActorLookup), LockstepEntityId references, randomness, signals, the spatial index, FixedPoint, FixedMath and FixedRandom, the determinism rules, and presenting the simulation with interpolation. Use it whenever you add or change anything that runs inside a lockstep simulation world or reads it for rendering - units, movement, combat, abilities, economy, AI, timers, randomness, input, UI - even if the user never says "deterministic" or "lockstep"; together with the skill of the module the code uses (pragma-dots-navigation, pragma-dots-vision, pragma-dots-stats, pragma-dots-views, pragma-dots-state-machines).
---

# Gameplay with Pragma DOTS lockstep

Every client runs the same simulation in its own Entities world and steps it with the same confirmed frames (one per
tick). The machines stay identical only if the simulation code is deterministic: a single `float` operation, a sort by
`Entity` or a `static` counter can make one client compute something different, and from then on the match is
desynced for good. Treat the rules below as correctness requirements, not style.

Deeper reference:

- `references/simulation-api.md` - every simulation-side type, singleton and helper of the lockstep module and the
  core utilities.
- `references/fixed-point.md` - `FixedPoint` semantics, `FixedMath`, vectors, quaternions, `FixedRandom`, `FixedGrid`.
- The package README: `Assets/PragmaDOTS/README.md` in the package repository, or
  `Library/PackageCache/com.pragma.dots@*/README.md` in a project that installed it.
- The `pragma-dots-actors` skill for content: definitions, parts, the catalog, placed actors.
- The skills of the modules on top: `pragma-dots-navigation`, `pragma-dots-vision`, `pragma-dots-stats`,
  `pragma-dots-views` (GameObject views), `pragma-dots-state-machines` (see *The modules on top*).
- The `pragma-lockstep-desync` skill when something already diverged; `pragma-lockstep-sessions` for networking and
  for running a session offline.

## Modules

The core is always compiled and knows nothing of lockstep: `Pragma.Dots.Mathematics` (fixed-point math),
`Pragma.Dots.Actors` (+ `.Authoring`), `Pragma.Dots.Utils` and `Pragma.Dots.Utils.Entities` (signals, buffer
dictionaries). Everything else is a module that compiles only with its
scripting define, all off after installation:

| Module | Define | Assembly to reference | Needs | Skill |
|---|---|---|---|---|
| Lockstep | `PRAGMA_DOTS_LOCKSTEP` | `Pragma.Dots.Lockstep`; `Pragma.Dots.Lockstep.Netcode` only with `com.unity.netcode` installed | | this one, `pragma-lockstep-sessions`, `pragma-lockstep-desync` |
| Navigation | `PRAGMA_DOTS_NAVIGATION` | `Pragma.Dots.Navigation` | Lockstep | `pragma-dots-navigation` |
| Vision | `PRAGMA_DOTS_VISION` | `Pragma.Dots.Vision` | Lockstep | `pragma-dots-vision` |
| Stats | `PRAGMA_DOTS_STATS` | `Pragma.Dots.Stats` | Lockstep | `pragma-dots-stats` |
| Views | `PRAGMA_DOTS_VIEWS` | `Pragma.Dots.Views` (+ `.Authoring`) | Lockstep | `pragma-dots-views` |
| State machines | `PRAGMA_DOTS_STATE_MACHINES` | `Pragma.Dots.StateMachines` | | `pragma-dots-state-machines` |

When a package type "could not be found", either its module is off (turn it on in *Project Settings > Pragma > DOTS*,
which writes the define to every build target and turns Lockstep on with the modules that need it) or the game's
assembly definition lacks the reference. Never set a define on one target only: a build without Lockstep, Navigation,
Vision or Stats has other simulation systems and the server refuses it; a build without Views is accepted but desyncs,
because the View part's `EntityViewKey` is simulation state. A module's actor parts are in its runtime assembly.

## Actors

Units, buildings, projectiles and map objects are actors (`ActorDefinition` assets with a key, tags, a parent and
parts; core assembly `Pragma.Dots.Actors`). The `pragma-dots-actors` skill covers writing them; inside the simulation:

- The registry and the placed actors of the presentation world are copied into every simulation world before tick 0
  (`LockstepClientWorldUtility.CreateSimulationOptions`). The registry is empty when a baking subscene had not loaded
  when the simulation was created: start the session with `waitForActorRegistry`.
- **Spawning**: the registry query from `OnCreate` (`_registry = ActorRegistryQuery.Create(ref state)`), then
  `var actors = ActorRegistryQuery.Get(ref state, _registry); actors.TryGetType("rifleman", out var type);
  actors.Instantiate(state.EntityManager, type)` (or a command buffer), then set its `LockstepTransform`
  (`LockstepTransform.FromPosition`, `FromPose`): a prefab stands at the identity. Read `ActorLookup` again after a
  structural change. Look keys and tags up once and keep the indices.
- **Tags**: `actors.TryGetTag("unit", out var unit)`, then `actors.HasTag(actorComponent, unit)` (a bit test) for
  rules such as "only units capture points". `Actor { key, type }` is on every actor's root entity.
- **Map content** is placed with `ActorAuthoring`: a placed actor (`PlacedActor`) the simulation starts with, in the
  same order and archetypes on every client. References between placed actors survive the copy; references to
  anything else become `Entity.Null`, so name other types by key.
- The lockstep module brings the *Transform* (`LockstepTransform`; *Interpolate*, on by default, adds
  `LockstepTransformPrevious`) and *Entity Id* (`LockstepEntityId`) parts, both in every new definition.
- Game data is a part: `[Serializable, ActorComponent("Combat/Health")]` on a component with `FixedPoint` fields, or
  an `ActorPart` whose `Build` is deterministic from its own fields.

## Where code goes

| Code | World | How |
|---|---|---|
| Gameplay rules, state changes | simulation world | `[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]` |
| Reading keyboard, gamepad, UI | client or local world | `[UpdateInGroup(typeof(LockstepInputSystemGroup))]`, write `LockstepLocalInput` |
| Rendering, UI, audio, camera | presentation world | read `client.Simulation.World`, never write to it |

Systems in `LockstepSimulationSystemGroup` are created only in simulation worlds (the group carries a custom
`WorldSystemFilter` flag that its members inherit). A gameplay system outside the group does not run in the
simulation at all; a presentation system inside the group would run in the simulation.

## Workflow for a new mechanic

1. **State.** Put it in unmanaged `IComponentData` / `IBufferElementData` with `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`,
   integer and `bool` fields. Durations are tick counts (`int`), positions are `LockstepTransform`.
2. **Who drives it.** Continuous player control (movement, aiming, held buttons) belongs in the input struct;
   discrete actions that must never be lost (build, buy, cast, chat) are commands; everything else is driven by
   simulation state and `LockstepTime`.
3. **System.** Add an `ISystem` (Burst-compiled where possible) to `LockstepSimulationSystemGroup`, ordered against
   the other gameplay systems with `[UpdateBefore]` / `[UpdateAfter]`.
4. **Structural changes.** Never create or destroy entities while iterating a `SystemAPI.Query`: record into a
   lockstep command buffer or collect into a `NativeList` and apply after the loop.
5. **Presentation.** Read the new state from the presentation side (views, UI, effects).
6. **Verify.** Compile, run the session offline (`LockstepOfflineConfig.Create<TInput>(tickRate)`, see
   `pragma-lockstep-sessions`), and run the determinism tests (see `pragma-lockstep-desync`): at least two clients in
   one process with checksums on.

## The simulation API you need most

```csharp
// Clock and session.
var time = SystemAPI.GetSingleton<LockstepTime>();          // tick, tickRate, deltaTime (FixedPoint), elapsedTime (FixedPoint)
var info = SystemAPI.GetSingleton<LockstepSessionInfo>();   // seed, tickRate, maxPlayers, inputSize, GetStartData<T>()

// Randomness: take a ref, or the drawn numbers do not advance the stored generator.
ref var random = ref SystemAPI.GetSingletonRW<LockstepRandom>().ValueRW.value;
var roll = random.NextInt(1, 7);

// Command buffers played back inside the tick.
var buffer = SystemAPI.GetSingleton<LockstepEndSimulationEntityCommandBufferSystem.Singleton>()
    .CreateCommandBuffer(state.WorldUnmanaged);

// Actors of the catalog, by key (the registry is copied into every simulation world; empty if a baking subscene had
// not loaded when the simulation was created, so start with waitForActorRegistry). The query of the registry comes
// from OnCreate: _registry = ActorRegistryQuery.Create(ref state).
var actors = ActorRegistryQuery.Get(ref state, _registry);
if (actors.TryGetType("rifleman", out var rifleman))
{
    var unit = actors.Instantiate(state.EntityManager, rifleman);
}

// Actors placed on the map (ActorAuthoring) are already in the simulation world at tick 0: query them like any
// entity (they keep PlacedActor).

// Cross-client identities.
var ids = SystemAPI.GetSingleton<LockstepEntityIdMap>();
ids.TryGetEntity(targetId, out var target);
```

Order inside the group: `LockstepFrameApplySystem` -> `LockstepTransformHistorySystem` ->
`LockstepBeginSimulationEntityCommandBufferSystem` -> your systems, `LockstepNavSystemGroup` and `LockstepStatSystem`
(default slot: order yours against them) -> `LockstepVisionSystem` and `LockstepSignalCleanupSystem` (`OrderLast`) ->
`LockstepEndSimulationEntityCommandBufferSystem` -> `LockstepEntityIdSystem`.

## Players

The frame apply system creates one entity per player on the join tick, with `LockstepPlayer` (`slot`, `joinTick`,
`joinData`, `GetJoinData<T>()`), `LockstepPlayerInput`, the `LockstepCommand` and `LockstepCommandData` buffers and
two enableable flags:
`LockstepPlayerJoined` (enabled only on the join tick) and `LockstepPlayerLeft` (enabled only on the leave tick; the
entity is destroyed at the start of the next tick).

```csharp
// Join tick: spawn. Leave tick: clean up everything the player owned.
var buffer = SystemAPI.GetSingleton<LockstepEndSimulationEntityCommandBufferSystem.Singleton>()
    .CreateCommandBuffer(state.WorldUnmanaged);
foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerJoined>())
{
    var avatar = buffer.CreateEntity();
    buffer.AddComponent(avatar, new Avatar { slot = player.ValueRO.slot });
    buffer.AddComponent(avatar, LockstepTransform.FromPosition(new FixedVector3(player.ValueRO.slot * 2, 0, 0)));
    buffer.AddComponent<LockstepTransformPrevious>(avatar);
}

var leftSlots = new NativeList<int>(Allocator.Temp);
foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerLeft>())
{
    leftSlots.Add(player.ValueRO.slot);
}
foreach (var (avatar, entity) in SystemAPI.Query<RefRO<Avatar>>().WithEntityAccess())
{
    if (leftSlots.Contains(avatar.ValueRO.slot))
    {
        buffer.DestroyEntity(entity);
    }
}
```

A freed slot can be reused by a later joiner, so per-player data keyed by `slot` must be removed on the leave tick.

## Input and commands

The input is one unmanaged struct (at most 128 bytes) per player per tick; its size is the session's `InputSize`.

```csharp
public struct GameInput
{
    public const byte FIRE = 1;

    public sbyte moveX;     // quantized on the client: -100..100
    public sbyte moveY;
    public byte buttons;
    public byte reserved;   // explicit padding, so no undefined bytes travel
}
```

Client side, in the presentation world (floats are fine here: they are quantized before they leave the machine):

```csharp
[UpdateInGroup(typeof(LockstepInputSystemGroup))]
public partial class GameInputSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<LockstepLocalInput>();
    }

    protected override void OnUpdate()
    {
        var stick = Vector2.ClampMagnitude(ReadStick(), 1f);
        var localInput = SystemAPI.GetSingleton<LockstepLocalInput>();
        localInput.Set(new GameInput
        {
            moveX = (sbyte)Mathf.RoundToInt(stick.x * 100f),
            moveY = (sbyte)Mathf.RoundToInt(stick.y * 100f),
            buttons = FireHeld() ? GameInput.FIRE : (byte)0,
        });
        SystemAPI.SetSingleton(localInput);

        if (BuildClicked(out var cell))
        {
            SystemAPI.GetSingletonBuffer<LockstepCommand>().Add(LockstepCommand.Create(new BuildCommand { cellX = cell.x, cellY = cell.y }));
        }
    }
}
```

Simulation side:

```csharp
foreach (var (player, input, commands) in SystemAPI.Query<RefRO<LockstepPlayer>, RefRO<LockstepPlayerInput>, DynamicBuffer<LockstepCommand>>())
{
    var current = input.ValueRO.Get<GameInput>();
    var previous = input.ValueRO.GetPrevious<GameInput>();
    var firePressed = (current.buttons & GameInput.FIRE) != 0 && (previous.buttons & GameInput.FIRE) == 0;
    var move = new FixedVector2(FixedPoint.FromFraction(current.moveX, 100), FixedPoint.FromFraction(current.moveY, 100));

    for (var i = 0; i < commands.Length; i++)
    {
        if (commands[i].TryGet<BuildCommand>(out var build))
        {
            // Validate here (money, free cell): every client runs the same validation.
        }
    }
}
```

Lists of any length (the unit ids of an order, waypoints) are the command's data, never a `FixedList` capped by the
payload size or a command split in chunks:

```csharp
// Client: the data goes into the LockstepCommandData buffer of the entity the command is added to.
var commands = SystemAPI.GetSingletonBuffer<LockstepCommand>();
var commandData = SystemAPI.GetSingletonBuffer<LockstepCommandData>();
commands.Add(LockstepCommand.Create(new MoveCommand { target = target }, unitIds.AsArray(), commandData));

// Simulation: query the data buffer next to the commands.
foreach (var (player, commands, commandData) in
         SystemAPI.Query<RefRO<LockstepPlayer>, DynamicBuffer<LockstepCommand>, DynamicBuffer<LockstepCommandData>>())
{
    for (var i = 0; i < commands.Length; i++)
    {
        if (commands[i].TryGet<MoveCommand>(out var move))
        {
            var unitIds = commands[i].GetData<uint>(commandData); // view; check each unit's owner before using it
        }
    }
}
```

- The client samples input once per tick. A tap shorter than a tick can be missed: send must-not-miss actions as
  commands or latch them on the client until the next tick.
- Command payload structs are at most 122 bytes; their data has no limit (large commands are fragmented on the wire
  and still arrive in one tick). A player sends at most 32 commands per tick and the rest move to later ticks.
- Command types are identified by a hash of the assembly-qualified type name: renaming the struct, its namespace or
  its assembly changes the id, so all clients need the same build.

## Determinism rules

1. **No `float`, `double`, `Mathf` or `Unity.Mathematics` float math in anything that changes state.** Floating point
   results differ between CPUs, compilers and Burst modes. Use `FixedPoint` and `FixedMath`; build constants from integers and
   fractions (`FixedPoint.FromFraction(7, 2)`), not from float literals computed at runtime.
2. **Time and randomness come from the simulation.** `LockstepTime` (or tick counters), `LockstepRandom` or a
   `FixedRandom` stored in a component. `SystemAPI.Time` is float; `UnityEngine.Time`, `UnityEngine.Random` and
   `System.Random` differ per machine.
3. **All state lives in simulation components.** The checksum only sees components, so a divergence in a system
   field goes unnoticed until it corrupts something else; statics are shared by every world of the process (a second
   client in a test, a replay running next to the game); managed objects and components on system entities are not
   hashed at all. Systems must be stateless between ticks (caching an `EntityArchetype` or a query is fine); put
   singleton state on an ordinary entity, not on `state.SystemHandle`. A cache that is rebuilt from the state every
   tick may carry `[LockstepChecksumIgnore]` (on the type or a field) to stay out of the hash.
4. **`Entity` values are never data.** Entities allocates entity ids from one store shared by every world of the
   process, so the same simulated entity has different `Index`/`Version` on every client. Never sort by an `Entity`,
   hash it, seed randomness with it, iterate a hash map keyed by it, or send it in a command. Use `LockstepEntityId`
   (added at creation or by the actor's Entity Id part; assigned at the end of the creation tick) and resolve
   it through `LockstepEntityIdMap`. Query iteration order itself is deterministic.
5. **Ordered work only.** No `Interlocked`, no shared accumulators in parallel jobs, no order taken from a
   `NativeList.ParallelWriter`; parallel command buffers use `[ChunkIndexInQuery]` sort keys. Iterating a
   `NativeHashMap`/`Dictionary` gives an order that depends on hashes and history: iterate queries or sorted buffers.
6. **Nothing local.** No branch on the local slot, camera, screen, device, platform or frame rate inside the
   simulation. Local-only behaviour (camera follow, "my unit" highlight) belongs to the presentation.
7. **The simulation world is written only by the lockstep systems.** Presentation code reads it and nothing else.

| Instead of | Use |
|---|---|
| `float`, `double`, `Mathf.*`, `math.sin` | `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`, `FixedMath.Sin` |
| `SystemAPI.Time.DeltaTime`, `Time.time` | `LockstepTime.deltaTime`, `LockstepTime.tick`, tick counters |
| `UnityEngine.Random`, `System.Random` | `LockstepRandom.value`, a `FixedRandom` in a component |
| `Entity` in commands, sort keys, seeds, hash keys | `LockstepEntityId.value` |
| A `FixedList` of ids in a command payload, orders split into chunks | Command data: `LockstepCommand.Create(payload, data, dataBuffer)`, `GetData<T>` |
| `LocalTransform` for gameplay | `LockstepTransform` |
| Unity Physics queries for gameplay | Own `FixedPoint` collision code, `LockstepSpatialIndex` for what is near |
| NavMesh, `NavMeshAgent` | `LockstepNavAgent` on the `LockstepNavGrid` (`Pragma.Dots.Navigation`) |
| `static` counters, system fields with state | Singleton components on ordinary entities |
| Iterating a hash map | Iterate a query or a buffer sorted by a deterministic key |

## Useful patterns

- **Per-slot lookup:** `SystemAPI.GetSingletonBuffer<LockstepPlayerSlot>()[slot].player` is the player entity of a
  slot (`Entity.Null` when free). For your own per-slot data, fill a `NativeArray` of `info.maxPlayers` from a query
  and index it by slot.
- **Cooldowns:** `int cooldownTicks`, decremented every tick; seconds become ticks with `seconds * time.tickRate`.
- **Distance checks:** compare `FixedMath.DistanceSquared(a, b) < radius * radius` for small coordinates; `LengthSquared` overflows
  above about 46 000 per component, use `FixedMath.Length`/`Distance` for large worlds.
- **Nearest of many (targets, neighbours):** build a `LockstepSpatialIndex` from the positions of the tick once and
  query it (`Query(center, radius)`: the points in the cells around, in the same order everywhere); check the distance,
  widen the radius by the largest body, and break ties by the point's index.
- **Spawning many entities from a query:** collect the spawn data (position, direction, owner) in a `NativeList` of
  a small struct and create them after the loop, or use the end-of-tick command buffer.
- **Per-entity random streams:** `FixedRandom.CreateFromIndex(info.seed, lockstepEntityId.value)` stored in a component,
  created once `LockstepEntityId.IsAssigned` (the tick after creation; on the creation tick every id is still 0).
- **Events between systems (a unit died, a building finished):** signals of the core
  (`Pragma.Dots.Utils.Entities`): `commandBuffer.Publish(new UnitDied { ... })` through the end-of-tick buffer (also
  from jobs, with a sort key), read on the next tick with `SystemAPI.Query<RefRO<UnitDied>>().WithAll<SignalTag>()`;
  `LockstepSignalCleanupSystem` destroys them at the end of that tick. Every client sees the same signals on the same
  tick; the presentation may miss them when it skips ticks.
- **A per-entity map of any size (cooldowns by ability, counts by type):** a buffer of the game's element with
  `IDictionaryElement<TKey>` used through `BufferDictionary` (`GetOrAdd(key)` returns a reference, `TryGet`, `Remove`):
  sorted by key, so iterating it is deterministic, unlike a hash map. Up to about 340 small entries,
  `FixedDictionary4096Bytes` fits in a component instead (iteration follows the keys' hashes).
- **One-shot effects for the presentation:** append `{ tick, kind, position }` to a singleton buffer in the
  simulation and trim old entries; the presentation plays entries newer than the last tick it handled (it may skip
  ticks when it catches up).

## The modules on top

Navigation, vision, stats, views and state machines have skills of their own with the setup, the API and the
pitfalls: load the module's skill together with this one before writing its code. What every simulation system has to
know about them:

| Module | Skill | Order and rules inside the tick |
|---|---|---|
| Navigation | `pragma-dots-navigation` | Set `LockstepNavAgent` destinations (`SetDestination`, a group with `groupGoal`) before `LockstepNavSystemGroup`, read arrivals after it. Never write the `LockstepTransform` of a walking agent (`Stop()` first). With ground heights, place things with `LockstepNavigation.ToGround` and compare positions on X and Z |
| Vision | `pragma-dots-vision` | `LockstepVisionSystem` runs last (`OrderLast`): every system reads the planes the previous tick ended with. Sources see for their `slot` (-1 until gameplay sets the owner's); validate commands with `LockstepVision.IsVisible` |
| Stats | `pragma-dots-stats` | Add modifiers, changes and grants before `LockstepStatSystem`, read after it. Never write a stat's value; check prices with `TryGetPendingValue`; follow stats with `in DynamicBuffer<LockstepStat>` and a change filter |
| State machines | `pragma-dots-state-machines` | One `StateMachineSystemGroup<T>` per machine component, its states are systems in it. A request from a state applies on the next update, one from a system before the group on this one |
| Views | `pragma-dots-views` | Presentation only. `EntityViewKey` (the View part) is simulation state; nothing in the views writes to the simulation world |

`LockstepNavSystemGroup` and `LockstepStatSystem` take the default slot of `LockstepSimulationSystemGroup`: order your
systems against them with `[UpdateBefore]` / `[UpdateAfter]`; without a constraint, which of them runs first is not
something to rely on.

## Burst and jobs

`FixedPoint` is integer math, so Burst, Mono and IL2CPP produce identical bits; Burst-compile simulation systems freely.
`IJobEntity.ScheduleParallel` is deterministic when each entity writes only itself. Jobs are completed at the end of
every tick. Burst rejects managed arrays: create archetypes with `stackalloc ComponentType[] { ... }`. A failed Burst
compile falls back to managed code with only a console error, so check the console for "Burst error" after changes.

## Presentation

```csharp
if (LockstepWorlds.TryGetClient(presentationWorld, out var client) && client.Simulation != null)
{
    var simulation = client.Simulation;                  // simulation.World: read only
    var lastTick = simulation.Tick - 1;
    var alpha = client.InterpolationAlpha;
    var render = LockstepTransformExtensions.Interpolate(transform, previous, lastTick, alpha); // -> LocalTransform
}
```

- Entities need `LockstepTransformPrevious` to interpolate: actors with a Transform part have it (*Interpolate*, on by
  default); add it yourself to entities built by hand.
- GameObject views (the Views module, `pragma-dots-views`) are the usual way to show units: a View part gives the
  actor its `EntityViewKey`, a prefab with `EntityView` and one part per component type shows it, an
  `EntityViewUpdateSystem<T>` per type pushes the changes.
- `LockstepViewSystem` spawns an Entities Graphics copy of the type's prefab for every simulation actor and moves it
  every frame before the transform systems (only useful when a part adds rendering components).
- Presentation code never writes to the simulation world, not even a tag. Local state (selection, hover, "is mine":
  `data.slot == View.Client.LocalSlot`) stays in the presentation; changes go through commands.
- Convert for display only: `(float)value`, `(float3)position`, `(quaternion)rotation`, `transform.ToLocalTransform()`.
  Never feed a converted value back into the simulation.

## Before you finish

- [ ] New simulation systems are in `LockstepSimulationSystemGroup`; input readers in `LockstepInputSystemGroup`.
- [ ] No float/double/Mathf/`math.*` float functions, `Time`, `Random`, statics or system-field state in simulation
  code.
- [ ] No `Entity` value used as data; cross-client references use `LockstepEntityId`.
- [ ] No structural change inside a `SystemAPI.Query` loop; parallel command buffers have sort keys.
- [ ] Input changes keep the struct size in sync with `InputSize` (`UnsafeUtility.SizeOf<T>()`).
- [ ] Presentation code (views, parts, UI) only reads the simulation world.
- [ ] The code compiles without Burst errors, and a two-client test or offline run shows no desync.
