# Actors API reference

Namespace and assembly `Pragma.Dots.Actors` (the core, always compiled) unless noted; the bakers are in
`Pragma.Dots.Actors.Authoring`, the parts of a module in the module's namespace.

## Contents

- Assets
- Parts and the builder
- Components
- Run-time access
- Authoring components
- Parts of the modules

## Assets

| Type | Members |
|---|---|
| `ActorDefinition` (ScriptableObject, *Create > Pragma > DOTS > Actor Definition*) | `Key` (1 to 29 bytes of UTF-8, unique in a catalog), `Parent`, `OwnTags`, `OwnParts`, `SetTags`, `SetParts`; `GetTags(list)` and `GetParts(list)` with the inherited ones (an own part replaces the parent's part of its type), `TryGetPart<T>(out part)` (over the merged parts), `GetLineage(list)` (root first; false on a cycle), `TryGetKey(out FixedString32Bytes)`, `static TryCreateName(string, out FixedString32Bytes)` |
| `ActorCatalog` (ScriptableObject, *Create > Pragma > DOTS > Actor Catalog*) | `Actors` by type index (append only once shipped), `SetActors`, `IndexOf(key)`, `GetTags(list)` (the tag table, ordinal), `Validate(errors)` (keys, duplicates, cycles, tags, empty parts), `static ValidateOwn(definition, label, errors)` |

## Parts and the builder

| Type | Members |
|---|---|
| `ActorPart` | `[Serializable]` abstract base: `Build(IActorBuilder)`, `virtual DrawGizmos(in ActorGizmoContext)`. Serialized by reference: renaming needs `[MovedFrom]` |
| `[ActorPartMenu("Group/Name")]` | Where the part menu lists a part class |
| `[DefaultActorPart]` | Puts the part into every new definition; `Order` sorts them (then the full type name), `Replaces` names a default part this one stands in for when both are compiled |
| `[ActorComponent("Group/Name")]` | On an unmanaged `[Serializable]` `IComponentData` or `IBufferElementData`: the menu offers it as `ActorComponentPart<T>` / `ActorBufferPart<T>`, whose inspector edits the value |
| `IActorBuilder` | `FixedString32Bytes Key`, `bool IsPlaced`, `FixedPose Placement` (identity on a type's prefab), `FixedVector3 PlacementScale` (along the local axes), `HasPart<T>()`, `AddComponent<T>(in T)`, `DynamicBuffer<T> AddBuffer<T>()` (fill it before adding anything else), `SetComponentEnabled<T>(bool)`, `DependsOn(UnityEngine.Object)` (bake again when it changes; nothing at run time) |
| `BakerActorBuilder` (`Pragma.Dots.Actors.Authoring`), `EntityManagerActorBuilder` | The builder while a subscene bakes, and at run time |
| `IMergeableActorPart` | `ActorPart MergeOnto(ActorPart inherited)` (a new part: its entries over the inherited ones by key, its other settings), `ActorPart CreateOverride()` (its settings, no entries). An override of such a part merges instead of replacing |
| `ActorParts` | `Override(parts, overrides)` (the merge rule, for placement data of your own; merges mergeable parts), `IndexOf(parts, type)`, `TryGet<T>(parts, out part)` |
| `ActorGizmoContext` | `Transform`, `IsSelected`, `Parts`, `HasPart<T>()` |
| `ActorGizmos` | `DrawCircle(center, radius, color)`, `DrawRectangle(corner, size, color)` |

## Components

| Component | Members |
|---|---|
| `Actor` | `FixedString32Bytes key`, `int type` (index in the registry; -1 for a key no type has). On every actor's root entity |
| `PlacedActor` | `ulong order`: the place of a placed actor among the placed actors, baked from the object's identity in its scene (or the `order` of `CreatePlaced`) |
| `ActorRegistry` | Tag of the registry entity, which holds the buffers below |
| `ActorType` (buffer) | `key`, `Entity prefab`, `ActorTagSet tags`, per type index |
| `ActorTagName` (buffer) | `FixedString32Bytes name`, per tag index |
| `ActorTag` | `int index`, `IsValid`; a tag handle from `ActorLookup.TryGetTag` |
| `ActorTagSet` | 128 bits (`CAPACITY`): `low`, `high`, `IsEmpty`, `Contains(tag)`, `Overlaps(set)`, `ContainsAll(set)`, `Add(tag)` |

## Run-time access

| API | Notes |
|---|---|
| `ActorRegistryQuery.Create(ref SystemState)` | In `OnCreate`: the query of the registry of the system's world, required for its update; keep it |
| `ActorRegistryQuery.Get(ref SystemState, EntityQuery)` | In `OnUpdate`: the `ActorLookup` through the query from `Create` |
| `ActorLookup.Get(ref SystemState)` | The registry of the system's world, building the query where it is called (in `OnUpdate` Entities warns: use `ActorRegistryQuery`); throws without one |
| `ActorLookup.TryGet(EntityManager, out ActorLookup)` | False without a registry |
| `ActorLookup` members | `TypeCount`, `TagCount`, `TryGetType(key, out type)`, `GetKey(type)`, `GetPrefab(type)`, `GetTags(type)`, `TryGetTag(name, out ActorTag)`, `GetTagName(tag)`, `HasTag(type or in Actor, tag)`, `Instantiate(EntityManager, type)`, `Instantiate(EntityCommandBuffer, type)`, `Instantiate(EntityCommandBuffer.ParallelWriter, sortKey, type)`. Reads the buffers in place and is Burst-compatible: get it again after a structural change |
| `ActorUtility.CreateRegistry(EntityManager, ActorCatalog)` | Builds the registry entity at run time, the same as the baked one |
| `ActorUtility.HasRegistry(EntityManager)` | Whether the world has one |
| `ActorUtility.CreatePlaced(EntityManager, ActorDefinition, in FixedPose placement, FixedVector3 placementScale, IReadOnlyList<ActorPart> overrides, ulong order)` | A placed actor at run time (no subscene); `type` stays -1 until resolved |
| `ActorUtility.CreatePlaced(EntityManager, ActorAuthoring, ulong order)` | The same from an `ActorAuthoring` of a scene loaded without baking |
| `ActorUtility.Build(IActorBuilder, IReadOnlyList<ActorPart>, UnityEngine.Object context)` | Runs the parts on a builder of your own |
| `ActorUtility.ResolveTypes(EntityManager)` / `ResolveTypes(EntityManager, NativeArray<Entity>)` | Gives placed actors the type of their key (with a warning for unknown keys). The lockstep module does it while copying them into the simulation |
| `LockstepActorUtility` (`Pragma.Dots.Lockstep`) | `CopyRegistry(source, destination)`, `CopyPlacedActors(source, destination)`: what the built-in lockstep systems do before tick 0 |

## Authoring components

| Component | Notes |
|---|---|
| `ActorAuthoring` (*Pragma/DOTS/Actor*) | `Definition`, `Overrides`, `SetOverrides`, `GetParts(list)` (definition merged with the overrides), `GetPlacement()` (`FixedPose` of the transform, rotation normalized), `GetPlacementScale()` (lossy scale); static variants for any `Transform`. Bakes into a placed actor |
| `ActorCatalogAuthoring` (*Pragma/DOTS/Actor Catalog*, `Pragma.Dots.Actors.Authoring`) | Bakes an `ActorCatalog` into the registry: a prefab entity per definition and the tag table |
| `EntityViewConfigAuthoring` (*Pragma/DOTS/Entity View Config*, `Pragma.Dots.Views.Authoring`, Views module) | Bakes the GameObject view catalog into the presentation world; with Netcode, not for servers |

## Parts of the modules

By menu path, module in brackets:

- *Local Transform* (`LocalTransformPart`, core) - Unity's `LocalTransform` at the placement (identity on a type's
  prefab) and `LocalToWorld`; a scale that differs between the axes goes into a `PostTransformMatrix`.
  `[DefaultActorPart]`, replaced by the lockstep Transform.
- *Transform* (`TransformPart`, Lockstep) - `LockstepTransform` from the placement and the X of its scale (identity on
  a type's prefab); *Interpolate* (on by default) adds `LockstepTransformPrevious`. `[DefaultActorPart]`, replacing Local Transform.
- *Entity Id* (`EntityIdPart`, Lockstep) - `LockstepEntityId`. `[DefaultActorPart]`.
- *Navigation/Grid* (`NavGridPart`, Navigation) - the navigation grid of a map (size centred on the placement, cell
  size, agent radius) and its ground: a `TerrainData` covering the grid exactly, its world Y and the steepest walkable
  slope in degrees, sampled in fixed point. Selecting the placed actor previews the cells obstacles and steep ground
  block.
- *Navigation/Obstacle* (`NavObstaclePart`) - a box whose X and Z block the grid; moves with the actor when it has a
  Transform part, otherwise keeps the pose of the placement (non-uniform scale included).
- *Navigation/Agent* (`NavAgentPart`) - `LockstepNavAgent` (speed, angular speed and turn-in-place angle in degrees,
  stopping distance, radius), its waypoint buffer and `LockstepNavVelocity`; needs a Transform part.
- *Navigation/Avoidance* (`NavAvoidancePart`) - the `LockstepNavAvoidance` singleton of a map; leave it out to turn
  avoidance off.
- *Vision/Grid* (`VisionGridPart`, Vision) - the vision grid of a map (size centred on the placement, cell size: how
  fine the fog edge is).
- *Vision/Source* (`VisionSourcePart`) - `LockstepVisionSource` with a radius and slot -1 (set it to the owner's slot in
  gameplay); needs a Transform part.
- *View* (`ViewPart`, Views) - `EntityViewKey`: the actor's key unless the part names another (`Key`,
  `GetKey(actorKey)`); the editor previews the placed actor with that view.
- `StateMachinePart<TMachine, TState>` (State Machines) - abstract: a game's subclass per machine
  (`[Serializable, ActorPartMenu("Unit Brain")] public sealed class UnitBrainPart : StateMachinePart<UnitBrain, UnitState> { }`)
  gives the machine component, entering `Initial` on its first update.
- `StatsPart<TStat, TTarget>` (Stats) - abstract, closed by the game: attributes (stat, base value), resources (stat,
  cap attribute, cap policy, amount when uncapped), targets, *Receives Grants*; adds `LockstepStat` and the buffers the
  actor takes part in. Mergeable by stat; targets add up.
- `StatGrantsPart<TStat, TTarget>` (Stats) - abstract, closed by the game: a source and permanent grants (stat,
  modifier type, value, stacking, target). Mergeable by stat, modifier type and target.
