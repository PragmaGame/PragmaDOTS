---
name: pragma-dots-actors
description: Describe game content with the actors of Pragma DOTS (package com.pragma.dots, core assembly Pragma.Dots.Actors) - ActorDefinition assets with a key, tags, a parent and parts; [ActorComponent] components and ActorPart classes built through IActorBuilder; the ActorCatalog and its registry (ActorCatalogAuthoring, ActorUtility.CreateRegistry); spawning by key and checking tags with ActorLookup; actors placed on maps (ActorAuthoring, PlacedActor, ActorUtility.CreatePlaced, ResolveTypes); the parts the modules bring (Transform, Entity Id, navigation, vision, view) and the editor (inheritance, overrides, view previews). Use it whenever you add a unit, building, projectile or map object, give entities authoring data, would write a baker or an authoring MonoBehaviour, spawn entities by type, or touch the catalog or the objects placed on a map - in any Entities game, with or without the lockstep module - even if the user only says "prefab", "unit type", "config" or "spawn".
---

# Actors in Pragma DOTS

An actor is a game object described in an asset instead of code: a unit, a building, a projectile, a capture point,
the map itself. A designer gives it a key, tags and *parts*; each part adds components when the actor is built. A
catalog lists the types a world spawns by key, and maps place actors with values of their own.

Actors are in the core of the package: namespace and assembly `Pragma.Dots.Actors` (bakers in
`Pragma.Dots.Actors.Authoring`, inspectors in `Pragma.Dots.Actors.Editor`), always compiled, usable in any Entities
world. They know nothing of the modules; the modules bring parts of their own. With the lockstep module the registry and
the placed actors are copied into every simulation world (the `pragma-lockstep-gameplay` skill covers simulation code).

Prefer actors to hand-built archetypes and to authoring MonoBehaviours with bakers: new authoring data is a part.

Deeper reference:

- `references/api.md` - every actor type and member, the parts of the modules by menu path.
- The package README, section *Actors*: `Assets/PragmaDOTS/README.md` in the package repository, or
  `Library/PackageCache/com.pragma.dots@*/README.md` in a project that installed it.

## The pieces

| Piece | What it is |
|---|---|
| `ActorDefinition` | Asset (*Create > Pragma > DOTS > Actor Definition*): `Key` (unique, 1 to 29 bytes of UTF-8: "rifleman"), `Parent`, tags ("unit", "player"), parts |
| `ActorPart` | A piece of an actor that adds its components; one part of each type per actor |
| `ActorCatalog` | Asset (*Create > Pragma > DOTS > Actor Catalog*): the types a world spawns, in order; the index is the type |
| `ActorCatalogAuthoring` | Component (*Pragma/DOTS/Actor Catalog*): bakes a catalog into the world's registry, a prefab entity per definition |
| `ActorAuthoring` | Component (*Pragma/DOTS/Actor*): an actor placed on a map, a definition plus overriding parts at the GameObject's pose |
| `Actor { key, type }` | On the root entity of every actor; `type` is the index in the registry, -1 for a key no type has |
| `ActorLookup` | The registry of a world: types by key, prefabs, tags, `Instantiate` |
| `ActorRegistryQuery` | The registry query of a system: `Create` in `OnCreate` (requires the registry for the update), `Get` in `OnUpdate` |
| `PlacedActor { order }` | On actors placed on a map: their place among the placed actors, the same on every machine |
| `ActorUtility` | The registry and placed actors at run time (`CreateRegistry`, `CreatePlaced`), `ResolveTypes` |

## Inheritance

A definition inherits its parent's tags and parts. Its own part replaces the parent's part of the same type (an
*override*), and its own tags are added to the parent's. A *mergeable* part (`IMergeableActorPart`: stats, grants)
merges with the parent's instead: the override lists only the entries it adds or changes by key, and later changes
of the parent's other entries still reach it. The inspector shows the inherited parts greyed with *Override* (a
mergeable one merged over the whole lineage, also above its override, and its override starts empty); *Add Part*
lists every part type, *Add Tag* the tags of the project. The catalog reports a cycle of parents; the
lineage then stops where it repeats.

## Parts

**A component or buffer element of the game** becomes a part without code:

```csharp
[Serializable, ActorComponent("Combat/Health")]
public struct Health : IComponentData
{
    public FixedPoint current;
    public FixedPoint max;
}
```

The part menu then offers *Combat/Health* (`ActorComponentPart<Health>`, or `ActorBufferPart<T>` for an
`IBufferElementData`), and the inspector edits its public fields; `FixedPoint` fields show as numbers. `[Serializable]`
is required, or Unity keeps no values.

**A part with logic** (several components, values worked out from others, references to other actors) derives from
`ActorPart`:

```csharp
[Serializable, ActorPartMenu("Combat/Weapon")]
public sealed class WeaponPart : ActorPart
{
    [SerializeField] private FixedPoint _range = 10;
    [SerializeField] private ActorDefinition _projectile;

    public override void Build(IActorBuilder builder)
    {
        var projectile = _projectile != null ? _projectile.Key : "";
        builder.AddComponent(new Weapon { range = _range, projectile = new FixedString32Bytes(projectile) });
    }
}
```

`IActorBuilder` is the actor being built: `Key`, `IsPlaced`, `Placement` (a `FixedPose`: identity on the prefab of a
type), `PlacementScale` (along its local axes; X where a uniform scale is needed), `HasPart<T>()`, `AddComponent`,
`AddBuffer` (fill the buffer right away: adding anything else invalidates it), `SetComponentEnabled`, `DependsOn(asset)`
(bake again when that asset changes). `DrawGizmos(in ActorGizmoContext)` draws the part of a placed actor in the scene
view (`ActorGizmos.DrawCircle`, `DrawRectangle`). `[DefaultActorPart]` (with an `Order`) puts a part into every new
definition; `Replaces = typeof(OtherPart)` stands in for another default part.

Rules for `Build`:

- The same `Build` runs while a subscene bakes (`BakerActorBuilder`) and when actors are built at run time
  (`EntityManagerActorBuilder`). It must give the same result from the same data on every machine: `FixedPoint` fields
  rather than floats, nothing read but the part's own fields and the builder (no scene lookups, no `Random`, no time).
  A terrain or another asset is sampled in fixed point, and named with `DependsOn`.
- A part of entries by key implements `IMergeableActorPart` when variants should change single entries:
  `MergeOnto(inherited)` returns a new part (its entries replace the inherited ones with the same key, the rest are
  added; its other settings win), `CreateOverride()` the empty override the inspector starts from. Never change
  either input: they are the parts of assets.
- Reference other actors by an `ActorDefinition` field and store their key (`FixedString32Bytes`); the entity of
  another type is spawned by key later. Never store an `Entity` of another actor in a part.
- Parts are serialized by reference (`[SerializeReference]`): renaming or moving a part class, its namespace or its
  assembly loses its data in every asset unless the class gets
  `[UnityEngine.Scripting.APIUpdating.MovedFrom(...)]` with the old names.
- Keep part classes in runtime assemblies, not Editor ones: they build at run time too.

## The registry

The registry is one entity per world (`ActorRegistry` with the `ActorType` and `ActorTagName` buffers): a prefab entity
per definition of the catalog, under the catalog's indices, and the tag table.

- **Baked**: an `ActorCatalogAuthoring` in a subscene that the world loads (with lockstep: the client or local world).
- **At run time**: `ActorUtility.CreateRegistry(entityManager, catalog)` builds the same registry from the same assets,
  before anything spawns. `ActorUtility.HasRegistry(entityManager)` tells whether a world has one.
- The catalog's order is the type index: once a game has shipped (replays, saves, mixed versions), only append to it.
- All definitions of a catalog share at most 128 tags (`ActorTagSet.CAPACITY`). The catalog inspector shows the tag
  table and what would break the registry: keys that are empty, too long or taken twice, too many tags, empty parts
  (`ActorCatalog.Validate(errors)` in code).

## Spawning and tags

```csharp
public partial struct ReinforcementSystem : ISystem
{
    private EntityQuery _registry;

    public void OnCreate(ref SystemState state)
    {
        // The query of the registry, required for the update: reading it without a registry would throw.
        _registry = ActorRegistryQuery.Create(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        var actors = ActorRegistryQuery.Get(ref state, _registry);
        if (!actors.TryGetType("rifleman", out var rifleman) || !actors.TryGetTag("unit", out var unit))
        {
            return;
        }

        var spawned = actors.Instantiate(state.EntityManager, rifleman);
        // Place it: a prefab stands at the identity pose. With lockstep, set its LockstepTransform.

        // A structural change invalidates the buffers ActorLookup reads in place: get it again.
        actors = ActorRegistryQuery.Get(ref state, _registry);
        foreach (var actor in SystemAPI.Query<RefRO<Actor>>())
        {
            if (actors.HasTag(actor.ValueRO, unit))
            {
                // ...
            }
        }
    }
}
```

- Look keys and tags up once and keep the type indices and `ActorTag` handles: a check is then an array read and a bit
  test. `actors.GetTags(type)` gives an `ActorTagSet` (`Contains`, `Overlaps`, `ContainsAll`).
- `ActorLookup` is a Burst-compatible struct: pass it to jobs. Spawn through command buffers with
  `Instantiate(commandBuffer, type)` or `Instantiate(parallelWriter, sortKey, type)`.
- Create the registry query in `OnCreate` (`ActorRegistryQuery.Create`): `ActorLookup.Get(ref state)` builds it where it
  is called, and in `OnUpdate` Entities looks it up on every update and warns once per system that it creates a query
  during OnUpdate.
- Outside systems: `ActorLookup.TryGet(entityManager, out var actors)`.

## Placed actors

What a map starts with (buildings, resource nodes, spawn markers, the map's own settings) is placed with
`ActorAuthoring` in the map's subscene: choose a definition and override what differs on this map (the owner slot of a
main building, the radius of a capture point). It bakes into a placed actor, built from its parts at the pose and scale
of the GameObject, with `PlacedActor { order }` (a hash of the object's identity in its scene, the same on every
machine).

- Its `type` is resolved by key against the registry: with lockstep, when the placed actors are copied into the
  simulation; without it, call `ActorUtility.ResolveTypes(entityManager)` once the registry and the map have loaded. A
  key no type has leaves `type` at -1 and no tags, with a warning.
- A special object can be a definition of its own, derived from the common one and listed in the catalog ("main
  building of map 1"). A prefab with an `ActorAuthoring` is a preset to drop into maps; Unity's prefab overrides work
  on its parts.
- With lockstep, keep renderers off the placed GameObject: the baked entity is copied whole into the simulation, and a
  view shows it.
- **Without subscenes** (levels loaded from data, procedural maps), build the same content at run time on every
  machine, in the same order:

```csharp
ActorUtility.CreateRegistry(entityManager, catalog);
for (var i = 0; i < level.Actors.Count; i++)
{
    var entry = level.Actors[i];
    ActorUtility.CreatePlaced(entityManager, entry.Definition, entry.Placement, new FixedVector3(1, 1, 1), entry.Overrides, (ulong)i);
}
// Or the ActorAuthoring objects of a scene loaded without baking: ActorUtility.CreatePlaced(entityManager, authoring, order).
```

## Parts of the modules

| Part (menu) | Module | Adds |
|---|---|---|
| *Local Transform* (`LocalTransformPart`) | core | Unity's `LocalTransform` at the placement and `LocalToWorld`; a non-uniform scale goes into a `PostTransformMatrix` |
| *Transform* (`TransformPart`) | Lockstep | `LockstepTransform` from the placement and the X of its scale; *Interpolate* (on by default) adds `LockstepTransformPrevious` |
| *Entity Id* (`EntityIdPart`) | Lockstep | `LockstepEntityId` |
| *Navigation/Grid*, *Obstacle*, *Agent*, *Avoidance* | Navigation | The grid of a map (with its terrain), obstacles, walking agents, avoidance |
| *Vision/Grid*, *Source* | Vision | The vision grid of a map, what sees |
| *View* (`ViewPart`) | Views | `EntityViewKey`: the actor's key unless the part names another |
| A subclass of `StateMachinePart<TMachine, TState>` | State machines | A machine component of the game, entering the initial state on its first update |

New definitions start with the parts marked `[DefaultActorPart]`: Local Transform in a game without lockstep; with the
lockstep module its Transform (which replaces Local Transform) and Entity Id. The Stats module's parts are generic
bases the game closes over its enums in one line each:
`[Serializable, ActorPartMenu("Stats")] public sealed class UnitStatsPart : StatsPart<StatType, StatTarget> { }` and
`UnitGrantsPart : StatGrantsPart<StatType, StatTarget>` (see the `pragma-lockstep-gameplay` skill).

## Without the lockstep module

Actors get Unity's transform from the core *Local Transform* part, which new definitions start with, and the game
calls `ActorUtility.ResolveTypes(entityManager)` after the catalog and the maps have loaded. Spawned prefabs stand at
the identity: set their `LocalTransform`. Determinism is then
up to the game; `FixedPoint` fields still keep part data exact.

## Editor

- The definition inspector shows inherited tags and parts greyed (*Override* copies a part to change it, or starts an
  empty override of a mergeable part, which then lists only what it changes; own parts have *Remove*, overrides
  *Revert*).
- With the Views module, a placed actor with a View part shows its view in the scene and in prefab mode: its parts
  are built in a scratch world and fed to the view's parts, so the preview runs the game's view code. Previews are never
  saved; *Tools > Pragma > DOTS > Actor View Previews* turns them off.
- Selecting a placed actor draws its parts' gizmos (the navigation grid previews the cells obstacles block).

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| A type is missing or has no tags | Not in the catalog the registry was built from, an empty or too long key, or more than 128 tags (the catalog inspector lists the errors) |
| `ActorLookup.Get` throws | The world has no registry yet: the catalog's subscene has not loaded (or `CreateRegistry` was not called); create the query with `ActorRegistryQuery.Create`, which requires the registry |
| "creates a query during OnUpdate" | `ActorLookup.Get(ref state)` in `OnUpdate`: create the query in `OnCreate` with `ActorRegistryQuery` |
| A placed actor has type -1 | Its key is in no catalog, or `ResolveTypes` was not called (without lockstep) |
| A part lost its values | Its class was renamed or moved without `[MovedFrom]`, or a component part's type is not `[Serializable]` |
| A component is not in the part menu | The type lacks `[ActorComponent]` or `[Serializable]`, or is not unmanaged |
| Built actors differ between machines | A part's `Build` reads floats, the scene, time or randomness |

## Before you finish

- [ ] New authoring data is a part (`[ActorComponent]` or an `ActorPart`), not a MonoBehaviour with a baker.
- [ ] Part fields that reach simulation state are `FixedPoint`, integers, `bool` or fixed strings.
- [ ] Other actors are referenced by definition and stored by key, never by `Entity`.
- [ ] Renamed part classes carry `[MovedFrom]`; the catalog was only appended to after shipping.
- [ ] A system that reads the registry creates its query in `OnCreate` (`ActorRegistryQuery`) and gets `ActorLookup`
  again after structural changes.
