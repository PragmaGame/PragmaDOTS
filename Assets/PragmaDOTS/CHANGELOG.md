# Changelog

All notable changes to this package are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `FixedMinAttribute` and `FixedRangeAttribute` (`Pragma.Dots.Mathematics`): the limits of a `FixedPoint` field in the
  inspector, which keeps an edit within them; `[FixedRange]` draws a slider with a number field. Unity's `[Min]` and
  `[Range]` cannot do it: their drawers run before the type's drawer and break a fixed-point field. The limits are
  doubles rounded to the nearest step, apply to every element of an array, and `Clamp` applies them in code.
- `ActorKeyAttribute` (`Pragma.Dots.Actors`): a `FixedString32Bytes` field that names an actor type shows in the
  inspector as a reference to the definition with that key; a pick stores the key, a key no definition has gets a
  warning icon. A serialized component or config names another type without a part that converts a reference.
- `FixedMath.Yaw` of a rotation, a direction or its X and Z: the heading on the ground in radians from +Z toward +X,
  the angle `FixedQuaternion.RotateY` takes; and `FixedMath.WrapAngle` into `[-π, π)`. The bearing of a point from a unit
  is `DeltaAngle(Yaw(rotation), Yaw(offset.Xz))`. Existing results do not change.
- `EntityViewManager.TryGetPrefab(key, out prefab)` (Views): the view prefab an entity with the key gets, picked as the
  manager picks it (baked registries first, then `EntityViewConfigs`, with the same checks), for what shows an entity
  that does not exist yet, such as the ghost of a building being placed. It works without a session and reads a
  catalog changed since the last update first.
- Navigation for gameplay: `LockstepNavGridView`, the grid with its cells and heights in one value (`CreateQuery`,
  `Get`; `IsWalkable`, `IsPassable`, `GetClearance`, `ToGround`, `ToPassable`), open ground without a grid or before its
  cells are built; the geometry of `LockstepNavObstacleFootprint` (`ClosestPoint`, `DistanceSquared`, the walk around it:
  `Perimeter`, `GetPerimeterOffset`, `GetPerimeterPoint`, `GetPerimeterDistance`); places around a footprint that
  bodies take without overlapping (`LockstepNavGridView.TryFindPerimeterPlace`, `IsPerimeterPlaceOpen`,
  `LockstepNavPerimeterPlace`) at `LockstepNavigation.GetOpenDistance`, the distance at which a footprint blocks no
  cell by the module's own rule; and `LockstepNavReach`, whether a path reaches a point completely.
- Stats: resource regeneration. `LockstepStat.regen` (`WithRegen`, *Regen* of a resource in `StatsPart`) names the
  attribute the amount grows by per second; `LockstepStatSystem` applies it while the resource is below its cap, before
  the tick's changes, and touches the chunk on every tick until it is full. `TryGetPendingValue` leaves it out.
- `LockstepStat.IsPending` (a resource that starts full and has not been filled yet, as on the tick an entity appears
  after `LockstepStatSystem`) and `IsDepleted` (a resource that is not pending and holds zero): the check of a unit's
  death or a squad's morale without testing `max`.
- `LockstepVisionFog` (Vision): the fog of war of one slot as an R8 texture with a texel per cell, smoothed over the
  neighbouring cells and fading in time, computed in Burst jobs and uploaded only when it changes, with the same
  visibility as numbers for a minimap. It needs no render pipeline: the game shades `Texture` over `Area`.

### Changed

- `LockstepStat` has a `regen` field and `LockstepStatSystem` reads `LockstepTime.deltaTime`: the stat buffers and the
  state hash change.
- `EntityViewManager` is created by its system only (its constructor is internal).
- Requires Unity 7000.0 (Unity 7) and Entities 6.7; the Netcode integration requires Netcode for Entities 7.0 or
  later: `Pragma.Dots.Lockstep.Netcode` and the server check of `EntityViewConfigAuthoring` use the `Unity.Netcode`
  namespace, and `PRAGMA_DOTS_UNITY_NETCODE` is defined from `com.unity.netcode` 7.0.0. Netcode 7 worlds listen and
  connect on `ClientServerBootstrap.AutoConnectPort` (7979 by default) when they are created, so the session examples
  set it to 0 before `LockstepNetcode.Listen` / `Connect`.

## [0.3.1] - 2026-10-21

### Added

- Claude Code skills for the modules: `pragma-dots-navigation`, `pragma-dots-vision`, `pragma-dots-stats`,
  `pragma-dots-views` and `pragma-dots-state-machines`, each with the setup, the order inside the tick, examples,
  troubleshooting and (navigation, stats, views) an API reference.

### Changed

- `pragma-lockstep-gameplay` keeps the lockstep module and the core; its sections on the modules moved into their
  skills, and its views reference became that of `pragma-dots-views`.

### Fixed

- The README's vision example read the cells with `SystemAPI.GetBuffer(entity, true)`, an overload that does not
  exist: it uses `SystemAPI.GetSingletonBuffer<LockstepVisionCell>(true)`.

## [0.3.0] - 2026-10-18

### Added

- `ActorRegistryQuery`: the actor registry for a system that reads it on its updates. `Create` builds the registry
  query in `OnCreate` and requires it for the update, `Get` returns the `ActorLookup` through it in `OnUpdate`;
  `ActorLookup.Get(ref state)` builds the query where it is called, which in `OnUpdate` costs a lookup per update and
  an Entities warning.

## [0.2.0] - 2026-10-18

### Added

- Actors (`Pragma.Dots.Actors`, in the core): game objects described in assets. An `ActorDefinition` has a key
  (`FixedString32Bytes`), a set of tags, a parent it inherits tags and parts from, and parts (`ActorPart`, serialized by
  reference) that add the components when the actor is built; an own part replaces the parent's part of its type. An
  `ActorCatalog` lists the types, baked by `ActorCatalogAuthoring` into the actor registry (`ActorRegistry` with
  `ActorType` and `ActorTagName` buffers: a prefab entity per type and the tag table, up to 128 tags as `ActorTagSet`
  bits), which the lockstep module copies into every simulation world. `ActorLookup` finds types by key, instantiates
  them and checks tags; `Actor { key, type }` is on every actor. `ActorAuthoring` places an actor on a map with
  overriding parts: it bakes into a `PlacedActor` (placed at a `FixedPose` with a scale) whose type is found by key when
  it is copied in, or by `ActorUtility.ResolveTypes`. `ActorUtility.CreateRegistry` and `CreatePlaced` build the same
  content at run time, without subscenes, through the same parts. Parts: `ActorComponentPart<T>` and
  `ActorBufferPart<T>` for any type with `[ActorComponent]`, and in the modules `TransformPart` and `EntityIdPart`
  (Lockstep, the default parts of new definitions through `[DefaultActorPart]`), `NavAgentPart`, `NavObstaclePart`,
  `NavAvoidancePart`, `NavGridPart`, `VisionSourcePart`, `VisionGridPart`, `ViewPart`. Inspectors show inherited parts
  with *Override*, a part menu and the project's tags; the catalog inspector lists what would break the registry. With
  the Views module, placed actors are previewed with their views in the scene and in prefab mode
  (*Tools > Pragma > DOTS > Actor View Previews*).

- Modules: lockstep, navigation, vision, stats, GameObject views and state machines compile only with their scripting
  define symbol (`PRAGMA_DOTS_LOCKSTEP`, `PRAGMA_DOTS_NAVIGATION`, `PRAGMA_DOTS_VISION`, `PRAGMA_DOTS_STATS`,
  `PRAGMA_DOTS_VIEWS`, `PRAGMA_DOTS_STATE_MACHINES`), all off after installation; navigation, vision, stats and views
  require lockstep, state machines only the core. *Project Settings > Pragma > DOTS*
  turns them on and off on every build target at once, with the modules they require or that require them, and warns
  when a module is on for some targets only. A module that is off is not compiled: its systems no longer join every
  simulation world and the simulation hash.

- Utilities (`Pragma.Dots.Utils`), taken from PragmaCommonDOTS: `FixedDictionary4096Bytes<TKey, TValue>`, a hash map
  stored in the struct (grows up to its 4 KB capacity, reuses removed slots, Burst-compatible), and `OptionalValue<T>`.
  `Pragma.Dots.Utils.Editor` draws fixed strings as text fields and fixed lists as arrays whose elements use their
  own drawers. `CollisionMatrixToFilter` (`Pragma.Dots.Utils.Physics`, compiled only with `com.unity.physics`) builds
  Unity Physics collision filters from the layer matrix, now keeping the group index of each call.

- `LockstepSpatialIndex`: the points of a tick sorted into square cells (a counting sort over their bounding box, the
  cells growing so that there are never more than a given number) for "what lies near here" queries, which visit the
  cells a square around a point touches in the same order on every client.

- Turning on the spot (`Pragma.Dots.Navigation`): `LockstepNavAgent.turnInPlaceAngle` (*Turn In Place Angle* of
  `NavAgentPart`, degrees). An agent that faces further than that from where its path leads stands and turns
  at its angular speed, and walks on once it faces within it, as a tracked vehicle does; with avoidance it picks no
  velocity while it turns. `LockstepNavAgent.MustTurnInPlace`.

- Watching replays through clients: `LockstepReplayHost` plays a `LockstepReplay` to ordinary `LockstepClient`s in the
  server's place — each connection joins as the slot it watches (`Watch`), recorded frames are released on a playback
  clock (`Speed`, `IsPaused`, `Position`), the session ends after the last frame, client input is ignored and client
  checksums are compared with the recorded ones (`FirstMismatchTick`, `MismatchEvent`, a desync on the client).
  `LockstepReplaySession` runs a host and one client per watching world over a loopback network and registers the
  clients in `LockstepWorlds`, so the presentation shows a replay as it shows a match.
- `LockstepClient.PlaybackSpeed`: the playout and the arrival of frames are measured in real time scaled by it, so a
  client follows a replay at any speed and holds its picture at 0.
- `LockstepReplay.Players` (`LockstepReplaySlot`: slot, join and leave ticks, join data) and
  `LockstepReplay.DurationSeconds`, read from the frames.
- `ILockstepServerEndpoint`: the server side as a transport sees it, implemented by `LockstepServer` and
  `LockstepReplayHost`; `LockstepLoopbackNetwork.AttachServer` takes it.

- Bodies larger than the grid's agent radius (`Pragma.Dots.Navigation`): `LockstepNavCell.clearance`, the rings of
  free cells around a cell, kept by `LockstepNavObstacleSystem` (`LockstepNavigation.UpdateClearance`), and
  `LockstepNavigation.GetClearance`, how much of it a body of a radius needs. Paths, line of sight, steps, pushes and
  stepping out of obstacles keep an agent to the cells with room for its body (`IsPassable`, `TryFindNearestPassable`,
  `HasLineOfSight` and `TryStep` with a clearance); `LockstepNavPathMap` is the grid as one agent plans on it.
- One search for a group of agents sent together: `LockstepNavAgent.SetDestination(destination, groupGoal)` and
  `groupGoal`. `LockstepNavPathSystem` groups the paths of a tick by goal cell and clearance, searches once per group
  back from its goal to all of its agents (`LockstepPathfinder.SearchGroup`, `GetChain`) and gives each agent its own
  path along its chain to its own destination (`FollowChain`); groups and paths are planned in parallel jobs, a path
  per thread. An order moving 1200 agents in eight armies plans in about 8 ms instead of 25 ms.
- Crowds cost paths: with avoidance, a standing agent makes the cells its body covers cost
  `LockstepNavAvoidance.crowdCost` cells more, so paths go around idle crowds when the way around is shorter.
- `LockstepNavAgent.pace`: an agent walks no faster than the pace of the group it walks with; `WalkSpeed` is the speed
  it walks at. `LockstepNavAgent.Replan` plans the path again keeping the destination and the group.
- `NavigationPerformanceTests`, run by name: navigation of 1200 agents on a 256 by 256 grid.
- Local avoidance (`Pragma.Dots.Navigation`): while the world has a `LockstepNavAvoidance` (time horizon, neighbour
  distance, how many neighbours; `NavAvoidancePart` adds it, `Default` suits units walking a few units
  per second), `LockstepNavAvoidanceSystem` picks the velocity of every walking agent with a `LockstepNavVelocity` and
  a radius in a parallel Burst job: sampled around the velocity that walks its path and scored by how soon it would
  touch its nearest neighbours (found in a spatial hash; walking ones take half of the avoiding), capped so agents
  never wait or turn back and push through crowds they cannot go around. `LockstepNavMoveSystem` walks that velocity,
  slides along walls and plans the path again when a wall hides the next waypoint. Without the singleton agents walk
  exactly as before. `LockstepNavVelocity.value` records what an agent walked on its last step; `NavAgentPart` adds
  the component.
- `LockstepNavigation.TryStep`: where a step may end without entering a blocked cell (whole, along X, or along Z);
  pushes of `LockstepNavSeparationSystem` use it.
- Vision (`Pragma.Dots.Vision`): what every player slot sees, as simulation state. `LockstepVisionGrid` (a grid of
  cells on the XZ plane, one plane of `LockstepVisionCell`s per slot of the session, `isRevealed` to show everything)
  is filled again on every tick by `LockstepVisionSystem`, last in the tick, from the `LockstepVisionSource`s (a radius
  and the slot it sees for) at their transforms; a cell is visible when a circle of sight reaches into it.
  `LockstepVision` answers `IsVisible` (a point, or a body seen by its edge), `IsCellVisible` and `GetPlane` (to draw a
  slot's fog of war). `VisionGridPart` and `VisionSourcePart` add them.
- `FixedPose` (`Pragma.Dots.Mathematics`): a position and a rotation, the placement of actors.
- `FixedGrid` (`Pragma.Dots.Mathematics`): the cell math of a rectangle of square cells on the XZ plane (which cell
  holds a position, cell corners and centres, indices), shared by the navigation and the vision grids;
  `LockstepNavGrid.Layout` and `LockstepVisionGrid.Layout` return it.
- Ground heights for navigation: `LockstepNavHeight`, a buffer of world Y at every cell corner of the grid, and
  `LockstepNavGrid.maxSlope`, the steepest walkable slope. Agents walk on the ground (`LockstepNavMoveSystem`,
  `LockstepNavSeparationSystem`), cells steeper than the limit are blocked for good, and `LockstepNavigation` answers
  `HasHeights`, `GetHeight`, `ToGround` and `IsSteep`. `NavGridPart` samples the heights in fixed point from a
  `TerrainData` (its world Y and the slope limit in degrees) and previews the steep cells. The package now depends on
  `com.unity.modules.terrain`.

- GameObject views (`Pragma.Dots.Views`), adapted from ECV (Entity Component View): `EntityView` prefabs with
  one part per component type (`EntityComponentView<T>`, `EntityComponentViewUnmanaged<T>`, `TransformComponentView`)
  or dynamic buffer (`EntityBufferView<T>`), all deriving from `EntityViewPart`, spawned and returned by
  `EntityViewManagerSystem` for the simulation entities with an `EntityViewKey`, fed by `EntityViewUpdateSystem<T>` and
  `EntityBufferViewUpdateSystem<T>` when their component or buffer changes and by `TransformViewUpdateSystem` with
  interpolated transforms. Nothing is written to the simulation world.
- View catalogs: `EntityViewConfig`, baked with `EntityViewConfigAuthoring` (skipped for dedicated servers) or
  registered at runtime with `EntityViewConfigProvider` / `EntityViewConfigs`; `ViewPart` gives actors their keys.
- Pool abstraction: `IEntityViewPool`, chosen per project with `EntityViewManagerSystem.PoolFactory` or per world with
  `EntityViewManagerSystem.Pool`; `EntityViewPool` is the default.
- `EntityViewManager`: views by entity, `Attach`/`Detach` for views the caller owns (HUD panels), `ForceUpdate`.
- `EntityViewManagerSystem.IsShown`: a hidden presentation world returns its views to the pool and spawns none until it
  is shown again, so one process can run several presentation worlds and show one of them.
- `EntityView.TryGetData<T>` and `TryGetBuffer<T>`: read a component or a buffer of the entity shown from the
  simulation world (its `LockstepEntityId` for commands).
- `LockstepTransformExtensions.TryGetInterpolated`: reads and interpolates the transform of a simulation entity with the
  rule both view systems use.
- Placed actors: the actors placed on a map (`ActorAuthoring`), which every simulation world starts with
  (buildings, resource nodes, spawn markers). `LockstepClientWorldUtility.CreateSimulationOptions` copies them, with
  their linked entities, after the actor registry and before tick 0, in an order baked from the objects' identities
  (`PlacedActor.order`), so the result does not depend on the order subscenes loaded in.
  `LockstepActorUtility.CopyPlacedActors` does the copy for custom flows.
- Navigation (`Pragma.Dots.Navigation`): a walkability grid (`LockstepNavGrid` with its `LockstepNavCell`
  buffer), obstacles (`LockstepNavObstacle`) that block cells while their entity exists, A* with string pulling in
  integer math (`LockstepPathfinder`, queries in `LockstepNavigation`) and agents (`LockstepNavAgent`,
  `LockstepNavWaypoint`) that walk their paths in `FixedPoint`, plan in parallel and plan again when an obstacle
  blocks their way. `LockstepNavSystemGroup` runs `LockstepNavObstacleSystem`, `LockstepNavPathSystem` and
  `LockstepNavMoveSystem`; `NavGridPart`, `NavObstaclePart` and `NavAgentPart` add them, and selecting a placed grid
  previews the cells the obstacles block. An agent standing still on a cell that becomes
  blocked (a building placed on top of it, a unit spawned or stopped inside one) walks to the nearest walkable cell.
  `LockstepNavigation.IsClear` tells whether a footprint could be stamped without touching a blocked cell (where a
  building may be placed); `Covers` and `GetCoverage` give the cells a footprint blocks, for footprints not stamped
  yet. Agents with a `radius` keep apart: `LockstepNavSeparationSystem` pushes overlapping agents away from each other
  after they walked, never onto a blocked cell, in a sum that does not depend on the order agents are visited in.
- Stats (`Pragma.Dots.Stats`), under the game's own stat ids or enum values, in `FixedPoint`:
  - attributes (`LockstepStat.Attribute`): a base value and `LockstepStatModifier`s, flat, additive or multiplicative
    (percentage factors stop at zero), each with a `LockstepStatSource`, an optional end tick and a stacking rule
    (`Strongest` applies only the strongest modifier of a kind);
  - resources (`LockstepStat.Resource`): an amount changed by one-tick `LockstepStatChange`s (damage, healing, income),
    never below zero, optionally capped by an attribute with a `LockstepStatCapPolicy` (`KeepRatio` or `Clamp`) and
    starting full;
  - grants: `LockstepStatGrant`s of an entity (a player, a squad) reach the entities whose `LockstepStatGrantor`
    names it and whose `LockstepStatTarget`s match, the later ones included;
  - `LockstepStatSystem` removes expired modifiers and grants and recalculates, in parallel, only the chunks where
    something changed; `LockstepStats` reads and changes stats (`TryGet`, `TryGetValue`, `TryGetBase`, `TrySetBase`,
    also with the game's enum), removes modifiers and grants by source and computes the formula;
  - `LockstepStats.TryGetPendingValue`: the amount a resource will have after the changes not yet applied on this tick,
    by the same rule as the update, to check payments against.
- Command data: besides its payload struct (still up to 122 bytes) a command carries data of any length, an array of
  any unmanaged element type, for lists such as the unit ids of an order. `LockstepCommand.Create(payload, data,
  dataBuffer)` writes it into the new `LockstepCommandData` buffer, which the local input entity and every player
  entity have next to their `LockstepCommand` buffer; `LockstepCommand.GetData<T>(dataBuffer)` reads it as a view and
  `DataLength` gives its size. Outside ECS, `LockstepClient.AddCommand(payload, data)`. Large commands are fragmented on
  the wire and reach every client in one tick.
- The `pragma-dots-actors` Claude Code skill: content as actors, in any Entities game.
- `Pragma.Dots.Utils.Entities`, taken from PragmaCommonDOTS: signals, one-off events as entities (`Publish` on an
  `EntityManager`, a command buffer or a parallel writer, `SignalTag`), destroyed once per update by
  `SignalCleanupSystem` (end of `SimulationSystemGroup`) and `LockstepSignalCleanupSystem` (end of a lockstep tick),
  both before the end-of-update command buffer; `BufferDictionary`, a dynamic buffer of the game's
  `IDictionaryElement<TKey>` kept sorted by key (`IndexOf`, `ContainsKey`, `TryGet`, `GetOrAdd`, `Remove`), Burst-
  compatible and iterated in key order; `EntityManagerExtensions.TryGetComponent` and `TryGetBuffer`.
- `LocalTransformPart` in the core: Unity's `LocalTransform` and `LocalToWorld` (with a `PostTransformMatrix` for a
  non-uniform scale) for games without lockstep; while baking, a part that adds Unity's transform takes over the
  entity's transform (`TransformUsageFlags.ManualOverride`), since baking strips it otherwise. `DefaultActorPartAttribute.Replaces`: the lockstep Transform part
  replaces it in new definitions.
- `LockstepClient.EndReason`: why the server ended the match.
- `IMergeableActorPart`: a part whose override merges with the inherited part of its type instead of replacing it, so
  derived definitions and placements list only the entries they add or change; the inspector shows the merged
  inherited part and starts overrides empty.
- Stats actor parts, generic bases the game closes over its enums: `StatsPart<TStat, TTarget>` (attributes,
  resources with their caps, targets, whether the actor receives grants; it adds the buffers the actor takes part in)
  and `StatGrantsPart<TStat, TTarget>` (permanent grants with a source). Both merge by stat (grants by stat, modifier
  type and target). `NoStatTarget` for games without targets; `LockstepStatSource` is serializable.
- State machines (`Pragma.Dots.StateMachines`, module `PRAGMA_DOTS_STATE_MACHINES`, needs only the core), taken from
  PragmaCommonDOTS and redesigned for jobs: a machine component of the game holds a `StateMachine` (current,
  previous and requested state, ticks in the state; states are the values of an enum), a
  `StateMachineSystemGroup<TMachine>` subclass applies the requested transitions in a parallel Burst job and then runs
  its systems, the states, which tick their entities in parallel jobs with enter and exit logic on the update of a
  transition (`Is`, `Entered`, `Exited`, `Request`). An entity may run several machines, one per component.
  `StateMachinePart<TMachine, TState>` gives an actor its machine and initial state. Deterministic, so it serves
  lockstep simulations and any other world.
- `FixedMath.MulDiv(a, b, c)`: `a * b / c` with a 128-bit intermediate, truncated toward zero.
- `LockstepStat.Resource(type, cap, capPolicy, amount)`: a capped resource that starts with an amount (a building under
  construction, energy that starts empty); the *Amount* of a capped resource in `StatsPart` does the same.
- `LockstepVision.MAX_RADIUS` (30 000): a larger radius of sight counts as it.
- Server limits on what a client can make it hold: `MaxMessageSize` (4 MB, the reassembly of fragments),
  `MaxCommandDataSize` (1 MB), `MaxBufferedCommandBytes` (4 MB per player, commands sent ahead or waiting) and
  `MaxCommandBytesPerTick` (64 KB per player and frame, so the history grows at a bounded rate). Going beyond one
  drops the data and raises `ProtocolViolationEvent`. `LockstepPacketFramer` takes a message size limit.
- `LockstepClient.OnDisconnected()` and `LockstepEndReason.ConnectionLost`; `LockstepClientSettings.Validate()`, which
  the client calls.
- `EnumId.Of(value)` (`Pragma.Dots.Utils`): an enum value of any size as an `int`, in Burst too; `StateMachine.Id` and
  `LockstepStats.Id` use it.
- `LockstepClient.ServerTicks`: the ticks the server has closed, from its start and pong messages, for the progress
  of a late joiner.

### Changed

- The package is Pragma DOTS: `com.pragma.dots` in `Assets/PragmaDOTS` (was `com.pragma.dotslockstep` in
  `Assets/PragmaDOTSLockstep`), building blocks for DOTS games with lockstep as one of its modules. Layout: `Core`
  (`Mathematics`, `Actors`, `Utils`, the modules page in `Editor`) and `Modules` (`Lockstep` with `Runtime`, `Editor`
  and `Netcode`; `Navigation`, `Vision`, `Stats`, `Views`). Assemblies and namespaces are `Pragma.Dots.*`:
  `Pragma.Lockstep` is `Pragma.Dots.Lockstep` (also `.Editor`, `.Netcode`), `Pragma.Lockstep.Mathematics`
  is `Pragma.Dots.Mathematics`, and the utilities and modules follow (`Pragma.Dots.Utils`, `Pragma.Dots.Navigation`...);
  `EntityViewConfigAuthoring` is in `Pragma.Dots.Views.Authoring`. Menus are under *Pragma > DOTS*.
- Netcode for Entities is no longer a dependency of the package: `Pragma.Dots.Lockstep.Netcode` compiles when the
  project has `com.unity.netcode`, and so does the server check of `EntityViewConfigAuthoring`.
- Authoring is done with actors: the authoring components of the core and of the modules became parts, and
  `Pragma.Dots.Actors.Authoring` holds `ActorCatalogAuthoring` and the actor bakers. `Pragma.Dots.Views.Authoring`
  keeps `EntityViewConfigAuthoring`.
- The prefab registry is the actor registry: `waitForPrefabRegistry` is `waitForActorRegistry` (`LockstepOfflineConfig`,
  `LockstepClientConfig`, `LockstepNetcode.JoinSession`, `LockstepClientWorldUtility.CreateSimulationOptions`),
  `LockstepPrefabUtility` is `ActorUtility` (building content) and `LockstepActorUtility` (copying it into a
  simulation), and `LockstepViewSystem` mirrors actors by `Actor.type`.
- Navigation grids sample terrains in fixed point (bilinear over the raw heightmap) and take the slope limit through
  `FixedMath.Tan`, so a grid built at run time is the same on every machine; heights differ slightly from the float
  sampling before.
- The fixed-point drawers moved to `Pragma.Dots.Mathematics.Editor`.

- Protocol version 2: a command on the wire ends with its data length and data. Clients, servers and replays of
  version 1 are refused.
- `LockstepClient` and `LockstepServer` queue commands in their wire form; the server checks the form and relays the
  bytes without decoding them.
- Agents walk on the XZ plane: their speed is over it, and their Y is the ground under them, or stays as it is on a grid
  without heights. They no longer climb or sink towards the Y of their destination.
- `LockstepNavSeparationSystem` pushes two agents walking opposite ways only sideways, each to its own side: head-on
  walkers step aside and pass, and in a passage one body wide they squeeze past each other instead of blocking it.
- String pulling finds the farthest cell a corner sees by doubling the reach and halving the gap: a few line tests per
  corner instead of one per cell.
- `LockstepNavPathSystem` checks walking paths, collects the requests, searches the groups and plans the paths in a
  chain of jobs; a path blocked by a new obstacle is planned again on the same tick, with its group.
- An actor built at run time refuses a component type it has already, as a baked one does: two parts adding one type
  gave the last part's value at run time and the first one's when baked.
- `LockstepViewSystem` runs in `SimulationSystemGroup`, after the session and before `TransformSystemGroup`, so the
  Entities Graphics copies are drawn where they are on the frame they move, and a new one is not drawn at the origin
  first.
- `LockstepEntityIdSystem` keeps the ids of disabled entities in the map and rebuilds it only when an entity with an id
  was created, destroyed or moved, without marking the chunks changed.
- An obstacle thinner than a cell together with the agent radius blocks cells as if it were that thick: a thin wall
  blocked no cell at all when it passed between cell centres.
- A `FixedRandom` left at `default` runs the default sequence from a zero state instead of returning zero forever, and
  `FixedMath.InverseLerp` of an empty range is zero (it saturated, and `Remap` overflowed).
- `LockstepNetcodeClientSystem` keeps a session whose connection dropped: it ends with `ConnectionLost`, simulates the
  frames it has and stays registered until the config is removed or a new connection joins (it disposed the session at
  once, losing the end of a match the server had ended).
- The server reports a desync once per newly diverged slot instead of on every later checksum round, and takes
  checksum reports only for ticks on the interval. A client sends at most 32 commands, and about 256 KB of them, per
  input message.
- `MaxSendBytesPerUpdate` of zero takes the default (32 KB); it sent no frames at all.
- `EntityView.IsHasHandler` and `IsHasHandlers` are `HasHandler` and `HasHandlers`. The views, navigation and
  utilities log with `[Views]`, `[Navigation]` and `[Utils]` instead of `[Lockstep]`.
- `Pragma.Dots.Mathematics` no longer references Burst nor allows unsafe code, neither of which it used.
- The version is 0.2.0, and the first snapshot is 0.1.0 (it was 1.0.0): the package is not released yet.

### Fixed

- `LockstepServer.EndGame` in the lobby told nobody: the players waiting there stayed in `Lobby`. They now get the
  end of the match like the players of a running one, and every client reads the reason, which it used to drop.
- An editor and a player build reported a desync on tick 0 with identical state: the checksum's archetype signature
  counted the types the editor alone adds — the shared `EditorRenderData` on baked entities and the `HideInHierarchy`
  tag on the world time entity. The signature now lists only the types the checksum hashes (unmanaged components,
  buffers, tags): shared, managed and chunk components, `[LockstepChecksumIgnore]` types and the editor-only tags of
  Entities count nowhere. Changes every hash and invalidates the checksums stored in replays.
- The prefab registry and the placed actors are copied into archetypes created in their order first: the copy alone
  created them in an order that followed the memory of the presentation world, so two clients could start with their
  archetypes in a different order and report a desync on tick 0. Within an archetype the copies are also moved into
  chunks in their order: actors of one archetype from two subscenes landed in an order that depended on the chunks
  of the presentation world.
- `Instantiate` of an entity with a `LockstepEntityId` gave the copy the same id, and the map resolved the id to either
  of them; the copy now gets an id of its own.
- `LockstepEntityIdSystem` cleared the map while jobs of the tick could still read it.
- The checksum hashed `EntityGuid`, which the entities of a subscene open for editing keep and which differs between
  editor processes.
- The input clock of a client started at the join tick during the start countdown, so for about the first second of
  every match its inputs were sent `StartDelaySeconds` late; it now counts down with the server's clock.
- `BufferDictionary` lookups threw on read-only buffers.
- `LockstepViewSystem` threw on every frame with two actor registries in the presentation world; it uses the first
  one, like the copy into the simulation.
- `LockstepNavigation.TryStep` let a step cut the corner between two blocked cells that touch diagonally; it now
  checks every cell the step crosses. Agents walking without avoidance walked from wherever separation pushed them
  straight through walls to their next waypoint; that leg now slides along walls and plans the path again.
- `LockstepNavObstacleSystem` stamped a grid again only when its number of cells changed: moving it, changing its cell
  size, agent radius or slope limit, or swapping its width and height left stale blockers. A cell buffer the game
  sized itself was not stamped until an obstacle changed. An agent without a `LockstepNavWaypoint` buffer made path
  planning throw; it stays idle.
- Vision queries and stamps overflowed for radii above about 32 768.
- `KeepRatio` overflowed when the amount times the new cap passed about 2·10⁹ (two amounts of about 46 000); a
  resource whose cap went through zero filled up again when the cap came back; a grantor that lost its
  `LockstepStatGrant` buffer kept giving until something else changed the receivers.
- An override of a `StatsPart` or `StatGrantsPart` replaced only the first of two inherited entries of one stat (or
  grant), and the second one won when built.
- `FixedPoint.TryParse` wrapped to a negative value for the largest integer with a fraction that rounds up, and a
  `FixedGrid` with a zero cell size divided by zero.
- A start message that reached a client after it had left started it again, at tick 0 with no frames to come.
- A thin client without `RecordReplay` kept every frame of the match.
- With `MaxInputWaitTicks`, every late join held the frames of all players for about a round trip: the server waited for
  inputs the joiner could not have sent yet. A player is now waited for once one of its inputs arrived in time.

### Removed

- `LockstepPrefabRegistry`, `LockstepPrefabElement`, `LockstepPrefabId`, `LockstepPrefabRegistryAuthoring`: actors and
  the actor catalog replace them.
- `LockstepTransformAuthoring`, `LockstepEntityIdAuthoring`, `LockstepSceneEntityAuthoring`, `EntityViewKeyAuthoring`,
  `LockstepNavGridAuthoring`, `LockstepNavObstacleAuthoring`, `LockstepNavAgentAuthoring`,
  `LockstepNavAvoidanceAuthoring`, `LockstepVisionGridAuthoring`, `LockstepVisionSourceAuthoring`, and the
  `Pragma.Dots.Navigation.Authoring` and `Pragma.Dots.Vision.Authoring` assemblies: use the parts
  (`TransformPart`, `EntityIdPart`, `ActorAuthoring`, `ViewPart`, `NavGridPart`, `NavObstaclePart`, `NavAgentPart`,
  `NavAvoidancePart`, `VisionGridPart`, `VisionSourcePart`).
- `LockstepClient.AddCommand(in LockstepCommand)`: use `AddCommand<T>(payload)`, `AddCommand<T, TData>(payload, data)`
  or the `LockstepCommand` buffer of the local input entity.

## [0.1.0] - 2026-10-03

### Added

- Deterministic math: `FixedPoint` (Q47.16 fixed point), `FixedVector2`, `FixedVector3`, `FixedQuaternion` and `FixedMath`, with integer-only
  `Sqrt`, trigonometry, `Exp`/`Log`/`Pow`, exact decimal parsing and Burst-identical results; `FixedRandom` (PCG32).
- `LockstepSimulation`: an isolated Entities world stepped one confirmed frame at a time, with
  `LockstepSimulationSystemGroup`, begin/end command buffer systems, `LockstepTime`, `LockstepSessionInfo`,
  `LockstepRandom`, player entities (`LockstepPlayer`, `LockstepPlayerInput`, `LockstepCommand`,
  joined/left flags), `LockstepTransform` with interpolation history and `LockstepEntityId`.
- `LockstepServer`: transport-agnostic session authority. Orders inputs, closes ticks on its own clock, repeats a
  late player's previous input, never drops commands, streams frames, measures input timing, compares checksums by
  majority and reports desyncs; optional wait for late inputs; late join by replaying the match; server-side replays.
- `LockstepClient`: input clock steered by server feedback, jitter-aware playout buffer with catch-up budget,
  checksum reporting, desync events, replay export.
- `LockstepChecksum`: state hash over every unmanaged component, independent of padding, type indices and entity ids,
  with a per-type breakdown for desync diagnosis.
- Netcode for Entities integration: one manually serialized RPC, server and client systems driven by
  `LockstepServerConfig` / `LockstepClientConfig`, thin client support, `LockstepNetcode` helpers.
- Offline mode (`LockstepOfflineConfig`) running the same protocol over an in-process loopback.
- `LockstepReplay` / `LockstepReplayPlayer` with checksum verification.
- Presentation: `LockstepInputSystemGroup` and `LockstepLocalInput`, `LockstepViewSystem` mirroring the simulation
  into entity views from a `LockstepPrefabRegistry`.
- Authoring: `LockstepTransformAuthoring`, `LockstepPrefabRegistryAuthoring`, `LockstepEntityIdAuthoring`.
- Editor: inspectors for fixed-point fields, *Window > Pragma > Lockstep Sessions* debug window.
- Claude Code skills in `Skills~`: `pragma-lockstep-gameplay`, `pragma-lockstep-sessions`,
  `pragma-lockstep-desync`.
