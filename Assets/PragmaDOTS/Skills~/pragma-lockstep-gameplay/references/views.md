# GameObject views reference

Namespace `Pragma.Dots.Views` (assembly `Pragma.Dots.Views`): the Views module, `PRAGMA_DOTS_VIEWS`, which needs the
Lockstep module. `EntityViewConfigAuthoring` is in the namespace and assembly `Pragma.Dots.Views.Authoring`, the actor
view previews in `Pragma.Dots.Views.Editor`. GameObject views that never write to the simulation world.

## Contents

- Setup
- Parts
- Update systems
- Catalogs
- Manager and views
- Pool
- Rules and pitfalls

## Setup

1. **Key.** `EntityViewKey { FixedString32Bytes value }` on the simulated entity, 1 to 29 bytes of UTF-8, case-sensitive.
   Give an actor a View part (`ViewPart`: the key is the actor's own unless the part names another, and the editor
   previews placed actors with the view), or set it where the entity is created (`new EntityViewKey("Knight")` or a
   `const string`, both fine in Burst). It is simulation state, part of the hash: set it the same way on every client.
   Changing it swaps the view.
2. **Prefab.** `EntityView` on the root, `TransformComponentView` to follow the interpolated `LockstepTransform`, one
   part per component type or dynamic buffer to show.
3. **Update system.** `public partial class HealthViewUpdateSystem : EntityViewUpdateSystem<Health> { }` per
   component type, `public partial class StatViewUpdateSystem : EntityBufferViewUpdateSystem<LockstepStat> { }` per
   buffer element type (`LockstepStat` is the Stats module's), in any assembly (its presentation filter keeps it out
   of simulation worlds).
4. **Catalog.** An `EntityViewConfig` asset binding keys to prefabs, baked or registered at runtime (below).

The manager shows the session of its world, found through `LockstepWorlds.TryGetClient(world)`: Netcode, offline
and replay sessions register themselves, a client of your own needs `LockstepWorlds.RegisterClient(world, client)`.
Interpolation needs `LockstepTransformPrevious` on the entity: the actor's Transform part adds it (*Interpolate*, on
by default); without it a view moves in steps.

## Parts

| Type | Behaviour |
|---|---|
| `EntityComponentView<T>` | `UpdateData(T)` whenever the chunk holding the entity's `T` was written; may repeat a value |
| `EntityComponentViewUnmanaged<T>` | `OnUpdateData(T)` only when the bytes changed; the cache is cleared on `Bind` |
| `EntityBufferView<T>` | `UpdateData(DynamicBuffer<T>)` whenever the chunk holding the entity's buffer was written; read-only, valid during the call, may repeat the contents |
| `TransformComponentView` | Applies a `LocalTransform`: world position and rotation, and the uniform scale as the local scale (keep views unparented, or under an unscaled parent) |

All parts derive from `EntityViewPart`. Virtual members: `Bind()` (the view got an entity), `BindBreak()` (it is about
to lose it), `SetViewEnable(bool)` (default: `SetActive`). `View` is the root: `View.Entity`, `View.Client` (`LocalSlot` for "is this mine"). Several
parts may show one type; a part under a nested `EntityView` belongs to that one. Parts are MonoBehaviours, so they
must live in runtime (non-Editor) assemblies.

## Update systems

- `EntityBufferViewUpdateSystem<T>` (`T : unmanaged, IBufferElementData`) pushes buffers the same way as the
  component system below, to `EntityBufferView<T>` parts.
- `EntityViewUpdateSystem<T>` (`T : unmanaged, IComponentData`) runs in `EntityViewUpdateSystemGroup`
  (`PresentationSystemGroup`, after `EntityViewManagerSystem`). It pushes the chunks whose `T` changed since its last
  push (change versions of the simulation world, so writes made outside systems such as `LockstepTime` count too) and
  every value to views spawned, attached or forced since then. Disabled components count as absent, and toggling an
  enableable component of the query counts as a change. Override
  `protected override EntityQueryBuilder ConfigureQuery(EntityQueryBuilder builder) => builder.WithAll<X>();` to
  narrow the query (simulation components only).
- `TransformViewUpdateSystem` runs every frame: `LockstepTransform` and `LockstepTransformPrevious` blended with
  `InterpolationAlpha` into a `LocalTransform`, for views with a `TransformComponentView` (or another
  `LocalTransform` part). A view snaps to the current transform until one tick after it was bound. Without views,
  `LockstepTransformExtensions.Interpolate` and `simulationManager.TryGetInterpolated(entity, lastTick, alpha,
  shownSinceTick, out transform)` apply the same rule.

## Catalogs

- `EntityViewConfig` (*Create > Pragma > DOTS > Entity View Config*): `EntityViewBinder { Key, View }` list;
  `SetBinders(...)` for configs built in code.
- Baked: `EntityViewConfigAuthoring` in a subscene loaded by the client or local world bakes an `EntityViewRegistry`
  entity with `EntityViewPrefabElement { key, UnityObjectRef<GameObject> prefab }`. With Netcode for Entities installed,
  dedicated server builds bake nothing; without it the catalog always bakes.
- Runtime: `EntityViewConfigProvider` on a scene object, or `EntityViewConfigs.Add(config)` / `Remove(config)`. Needed
  when presentation worlds are created after the scene loaded (Netcode worlds made on demand): a subscene loads only
  into the worlds that exist when it is enabled.
- Baked catalogs come first, then runtime ones; the first binder of a key wins, a conflicting one logs a warning.

## Manager and views

`EntityViewManager.TryGet(presentationWorld, out manager)`:

| Member | Notes |
|---|---|
| `Views`, `TryGetView(entity, out view)` | Spawned views by simulation entity |
| `Attach(entity, view)` | A view the caller owns (HUD panel, scene object): same data, never pooled; false when no session, no such entity or the view is bound |
| `Detach(view)` | Ends an attachment; also automatic when the entity or the session goes |
| `ForceUpdate(entity)` | Pushes every value of the entity at the next update |
| `Client` | The session shown |

`EntityView`: `Entity`, `Client`, `IsBound`, `IsAttached`, `IsAutoUpdateEnabled` (pauses the pushes, transform
included; turning it on catches up), `Transform`, `GetComponentView<T>()`, `SetComponentViewEnable<T>(bool)`,
`UpdateData<T>(T)`, `UpdateData(IComponentData)`, `HasHandler(Type)`, `HasHandlers(params Type[])`,
`RefreshComponentViews()`, `TryGetData<T>(out T)` and `TryGetBuffer<T>(out DynamicBuffer<T>)` (read any component or
buffer of the entity from the simulation world; false while unbound or when the entity lacks it), `UpdateBuffer<T>`.

`EntityViewManagerSystem` spawns and returns views when a tick created, destroyed or re-keyed keyed entities (and
right away when a view was destroyed from outside): views of entities that went away or changed their key are returned
first, then new entities get theirs. A new session returns every view.

## Pool

- `IEntityViewPool`: `EntityView Spawn(EntityView prefab)`, `void Release(EntityView view)`. The manager binds after
  `Spawn` and unbinds before `Release`; the pool only keeps instances.
- Per project: `EntityViewManagerSystem.PoolFactory = world => ...` from the game's bootstrap (cleared when play mode
  starts; a factory that returns null falls back to the default pool). The manager owns the result: views go back to it when the world is destroyed, then it is disposed if it is
  `IDisposable`. To share one pool, return a thin wrapper per world.
- Per world: `world.GetExistingSystemManaged<EntityViewManagerSystem>().Pool = pool`; the spawned views go back to the
  previous pool first and the caller keeps ownership.
- Several presentation worlds in one process (local players, a spectator): `managerSystem.IsShown = false` returns
  that world's views to the pool at once and spawns none until it is shown again, with the views of the current state.
- Default `EntityViewPool(name, parent)`: inactive instances under one root object (kept across scene loads in play
  mode), `Prewarm(prefab, count)`, `InactiveCount`, `Dispose()` destroys every instance.
- com.pragma.pool keeps `PrefabPoolObject`s: give each view prefab one next to its `EntityView` and adapt (the
  `Spawn` and `Release` extensions need `using Pragma.Pool;`):

```csharp
public sealed class PragmaPoolEntityViewPool : IEntityViewPool
{
    private readonly IPrefabPoolService _pools;

    public PragmaPoolEntityViewPool(IPrefabPoolService pools)
    {
        _pools = pools;
    }

    public EntityView Spawn(EntityView prefab)
    {
        return _pools.Spawn(prefab.GetComponent<PrefabPoolObject>()).GetComponent<EntityView>();
    }

    public void Release(EntityView view)
    {
        _pools.Release(view.GetComponent<PrefabPoolObject>());
    }
}

// Bootstrap:
EntityViewManagerSystem.PoolFactory = world => new PragmaPoolEntityViewPool(poolService);
```

## Rules and pitfalls

- Nothing in a view, a part or a view system may write to the simulation world, not even a tag: the entity would move
  to another archetype, which changes the chunk order, the state hash and the order `LockstepEntityId`s are assigned in.
- Local state (selection, hover, "is mine", which slot's fog to draw) belongs to the presentation: compare
  `data.slot` with `View.Client.LocalSlot`, keep selection on the view or in UI code, send changes as commands. What a
  slot sees is simulation state of the Vision module (`LockstepVision.IsVisible`); hiding the views is the
  presentation's job.
- `view.Entity` is valid only in this process and session. Commands refer to `LockstepEntityId`: read it with
  `view.TryGetData<LockstepEntityId>(out var id)`.
- Parts run on the main thread for every push: keep `UpdateData` cheap and prefer `EntityComponentViewUnmanaged<T>`.
- A view that misses a type has no `EntityViewUpdateSystem<T>` (or `EntityBufferViewUpdateSystem<T>`).
- Turn the actor previews of the editor off with *Tools > Pragma > DOTS > Actor View Previews*.
- Pooled instances keep what their parts changed: restore it in `BindBreak` or `Bind`. `IsAutoUpdateEnabled` comes back
  on by itself when a view is unbound.
- Parts learn values, not absence: when `T` is removed or disabled the entity leaves the query and the part gets no
  call. A part for a tag can only ever show "on"; use a `bool` field for presence.
- A view is returned in the frame its entity is destroyed. Death animations need the entity to stay in a dying state
  for as many ticks (a deterministic countdown), or a part that leaves an effect behind in `BindBreak`. To remove the
  view of an entity that stays (a unit inside a transport), set its key to `default`: an unbound key gets no view.
