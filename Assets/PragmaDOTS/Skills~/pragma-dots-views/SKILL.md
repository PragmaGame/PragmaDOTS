---
name: pragma-dots-views
description: Show lockstep simulation entities with pooled GameObjects through the Views module of Pragma DOTS (package com.pragma.dots, define PRAGMA_DOTS_VIEWS, assemblies Pragma.Dots.Views and Pragma.Dots.Views.Authoring) - EntityViewKey and the View actor part, view prefabs with EntityView, TransformComponentView and parts per component or buffer (EntityComponentView, EntityComponentViewUnmanaged, EntityBufferView), EntityViewUpdateSystem and EntityBufferViewUpdateSystem, catalogs (EntityViewConfig, baked or provided at run time), the view manager (attach, force updates, several presentation worlds), view pools (IEntityViewPool, PoolFactory) and the editor previews of placed actors. Use it whenever units, buildings or projectiles need models, animators, VFX, health bars, selection rings or world-space UI, when picking a unit under the cursor, or when views do not appear, flicker, keep stale state or should play a death animation - even if the user only says "show the units", "prefab for the unit" or "animate it".
---

# GameObject views in Pragma DOTS

The simulation world holds data; GameObjects show it. A view prefab has an `EntityView` on its root and one
MonoBehaviour *part* per component type it shows; a manager spawns a pooled view for every simulation entity with an
`EntityViewKey`, and update systems push the components to the parts when they change. Use views for what Entities
Graphics does not draw well: animators, VFX, health bars, selection rings, world-space UI. For plain meshes the
lockstep module's `LockstepViewSystem` copies each actor's prefab into the presentation world instead.

The Views module is opt-in: define `PRAGMA_DOTS_VIEWS` (turn on *Views* in *Project Settings > Pragma > DOTS*, which
also turns on Lockstep, which it needs), namespace and assembly `Pragma.Dots.Views`; `EntityViewConfigAuthoring` is in
`Pragma.Dots.Views.Authoring`, the actor view previews in `Pragma.Dots.Views.Editor`. Turn it on for every build
target: the View part's key is simulation state, and a build without the module desyncs.

**The one rule:** nothing in a view, a part or a view system writes to the simulation world, not even a tag or a
cleanup component. A structural change moves the entity to another archetype, which changes the chunk order, the state
hash and the order `LockstepEntityId`s are assigned in: a desync on this client only.

Deeper reference:

- `references/api.md` - parts, update systems, catalogs, the manager, `EntityView`, pools, pitfalls.
- The package README, section *Views* (`Assets/PragmaDOTS/README.md` in the package repository, or
  `Library/PackageCache/com.pragma.dots@*/README.md` in a project that installed it).
- `pragma-lockstep-gameplay` (*Presentation*) for interpolation without views, `pragma-dots-actors` for the View part,
  `pragma-dots-stats` and `pragma-dots-vision` for health bars and the fog of war.

## Setting it up

1. **Key.** Give the actor a *View* part (`ViewPart`): its key is the actor's own unless the part names another, and the
   editor previews placed actors with that view. Entities built in code get `new EntityViewKey("Knight")` (1 to 29
   bytes of UTF-8, case-sensitive) where they are created, on every client alike. Changing the key swaps the view.
2. **Prefab.** `EntityView` on the root, `TransformComponentView` to follow the interpolated `LockstepTransform`, and one
   part per component type to show:

```csharp
public sealed class HealthBarView : EntityComponentViewUnmanaged<Health>
{
    [SerializeField] private Transform _fill;

    protected override void OnUpdateData(Health data)
    {
        _fill.localScale = new Vector3((float)data.current / (float)data.max, 1f, 1f);
    }
}
```

3. **Update system** per component type, and per buffer element type for buffer parts, in any assembly (a presentation
   filter keeps them out of simulation worlds):

```csharp
public partial class HealthViewUpdateSystem : EntityViewUpdateSystem<Health>
{
}

public partial class StatViewUpdateSystem : EntityBufferViewUpdateSystem<LockstepStat>
{
}
```

4. **Catalog.** An `EntityViewConfig` (*Create > Pragma > DOTS > Entity View Config*) binds keys to prefabs. Bake it
   with `EntityViewConfigAuthoring` in a subscene the client (or local) world loads; with Netcode for Entities, dedicated
   servers bake nothing. When presentation worlds are created after the scene loaded (Netcode worlds made on demand),
   register it at run time instead: `EntityViewConfigProvider` on a scene object, or `EntityViewConfigs.Add(config)`.
   The first binder of a key wins.

Interpolation needs `LockstepTransformPrevious` on the entity: the Transform part adds it (*Interpolate*, on by
default); without it a view moves in steps. A new view shows the current transform until one tick after it was bound,
so it never slides in from where its entity was created.

## Writing parts

| Base | Called |
|---|---|
| `EntityComponentViewUnmanaged<T>` | `OnUpdateData(T)` only when the bytes changed; the cache is cleared on `Bind`. The usual choice |
| `EntityComponentView<T>` | `UpdateData(T)` whenever the chunk holding the entity's `T` was written; may repeat a value |
| `EntityBufferView<T>` | `UpdateData(DynamicBuffer<T>)` whenever the chunk's buffer was written: read-only, valid only during the call; compare before redrawing |

- Parts are MonoBehaviours: keep them in runtime assemblies, never Editor ones.
- `View` is the root: `View.Entity`, `View.Client` (`LocalSlot` for "is this mine": `data.slot == View.Client.LocalSlot`).
- Override `Bind()` (the view got an entity) and `BindBreak()` (it is about to lose it) to set up and restore. Pooled
  instances keep whatever their parts changed (colours, active children, animator state): restore it there.
- Parts learn values, not absence: when `T` is removed or disabled, the entity leaves the query and the part gets no
  call. Show presence with a `bool` field, not with a tag component.
- Narrow an update system with
  `protected override EntityQueryBuilder ConfigureQuery(EntityQueryBuilder builder) => builder.WithAll<Unit>();`
  (simulation components only).
- Parts run on the main thread for every push: keep them cheap.

## Interaction

Local state (selection, hover, which slot's fog to draw) stays in the presentation, on the views or in UI code; changes
to the game go through commands, which name entities by `LockstepEntityId`, never by `Entity`:

```csharp
var view = hit.collider.GetComponentInParent<EntityView>();
if (view != null && view.IsBound && view.TryGetData<LockstepEntityId>(out var id) && id.IsAssigned)
{
    _selection.Add(id.value);   // a move order later sends these ids as command data
}
```

`view.TryGetData<T>` and `view.TryGetBuffer<T>` read any component or buffer of the entity from the simulation world.
`EntityViewManager.TryGet(presentationWorld, out var manager)` gives `TryGetView(entity, out view)`, `Views`,
`Attach(entity, view)` (a HUD panel you own shows the same data, never pooled), `Detach(view)` and
`ForceUpdate(entity)`.

## Lifetimes

- A view goes back to the pool in the frame its entity is destroyed. For a death animation keep the entity in a dying
  state for as many ticks as the animation lasts (a countdown in the simulation, the same on every client), or let a
  part leave an effect behind in `BindBreak`.
- To take the view away from an entity that stays (a unit inside a transport), set its key to `default` in the
  simulation: a key no catalog binds gets no view.
- One-shot effects (a shot, a hit) are not views: append `{ tick, kind, position }` to a simulation buffer and play the
  entries newer than the last tick the presentation handled.

## Pools and worlds

- The default `EntityViewPool` keeps released instances inactive under one root per world (`Prewarm(prefab, count)`).
  The pool activates and deactivates view roots: hide a view (fog of war) through a child, not its root.
- A project plugs in its own pool with `EntityViewManagerSystem.PoolFactory = world => ...` in its bootstrap (cleared
  when play mode starts; the manager owns and disposes what it returns), or per world with `managerSystem.Pool`.
  `references/api.md` has an adapter for com.pragma.pool.
- Several presentation worlds in one process (hot-seat tests, a spectator next to a player):
  `managerSystem.IsShown = false` returns that world's views at once and spawns none until it is shown again.

## Editor previews

A placed actor with a View part shows its view in the scene and in prefab mode: its parts are built in a scratch world
and fed to the view's parts, so the preview runs the game's view code. Previews are never saved; *Tools > Pragma >
DOTS > Actor View Previews* turns them off.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Views do not appear | The entity has no `EntityViewKey`, no catalog binds its key (case-sensitive), or the catalog was baked into a subscene that did not load into this world: register it with `EntityViewConfigProvider` |
| A view never shows a component | No `EntityViewUpdateSystem<T>` (or `EntityBufferViewUpdateSystem<T>`) for that type, or the part is under a nested `EntityView` |
| A view moves in steps | No `LockstepTransformPrevious` (the Transform part's *Interpolate* is off) or no `TransformComponentView` |
| A pooled view shows the last unit's colour or state | Restore what parts change in `BindBreak` or `Bind` |
| A view keeps showing a removed effect | Parts are not told about absence: use a `bool` field the simulation clears |
| The view vanishes before the death animation | The entity was destroyed at once: keep it in a dying state for the animation's ticks |
| Desync right after adding a view feature | Presentation code wrote to the simulation world (a tag, a cleanup component, a value) |
| The view of a placed actor is missing in the editor | The Views module is off, no View part, no `EntityViewConfig` asset binds the key, or previews are turned off |

## Before you finish

- [ ] Nothing in views, parts or view systems writes to the simulation world.
- [ ] Every component or buffer a part shows has its update system.
- [ ] Parts restore pooled state in `Bind` / `BindBreak` and live in runtime assemblies.
- [ ] Commands from the presentation carry `LockstepEntityId`, never `Entity`.
- [ ] The Views module is on for every build target that plays the game.
