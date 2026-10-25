# Pragma DOTS

Building blocks for games on Unity DOTS (Entities): a small core that every game gets, and modules that a game turns
on. The core is fixed-point math, [actors](#actors) (units, buildings and map objects described in assets of parts)
and a few utilities. The modules are deterministic [lockstep](#lockstep) multiplayer, played over Netcode for Entities
or offline, and on top of it [navigation](#navigation), [vision](#vision), [stats](#stats) and GameObject
[views](#views); [state machines](#state-machines) serve any game. A module that is off is not compiled at all, so a
game carries and runs only what it uses.

```
 Navigation    Vision    Stats    Views     modules on top of Lockstep: opt-in, none needs another
 ──────────────────────────────────────
 Lockstep (+ Netcode)    State machines     modules: opt-in, need only the core
 ══════════════════════════════════════
 Mathematics   Actors    Utils              core: always compiled, knows nothing of the modules
```

## Contents

- [What's inside](#whats-inside)
- [Requirements](#requirements)
- [Installation](#installation): [assemblies](#assemblies), [turning modules on](#turning-modules-on)
- Core: [Fixed-point math](#fixed-point-math), [Actors](#actors), [Utilities](#utilities)
- Modules:
  - [Lockstep](#lockstep): [when to use it](#when-to-use-it), [how it works](#how-it-works),
    [quick start](#quick-start), [writing the simulation](#writing-the-simulation),
    [determinism rules](#determinism-rules), [sessions](#sessions), [presentation](#presentation),
    [timing and latency](#timing-and-latency), [configuration](#configuration-reference),
    [desyncs](#desync-detection-and-debugging), [replays](#replays), [testing](#testing-a-simulation),
    [custom transports](#custom-transports), [protocol](#protocol), [limits](#limits)
  - [Navigation](#navigation)
  - [Vision](#vision)
  - [Stats](#stats)
  - [Views](#views)
  - [State machines](#state-machines)
- [Package layout](#package-layout)
- [Claude Code skills](#claude-code-skills)
- [Troubleshooting](#troubleshooting)
- [Not included](#not-included)

## What's inside

### The core

Always compiled. It works in any Entities world, so a game without lockstep (single player, or multiplayer on Netcode
ghosts) uses it alone.

- **[Fixed-point math](#fixed-point-math)** (`Pragma.Dots.Mathematics`). `FixedPoint` (Q47.16), `FixedVector2`,
  `FixedVector3`, `FixedQuaternion`, `FixedPose` and `FixedMath`, shaped after `Mathf`: `Sqrt`, trigonometry, `Exp`,
  `Log` and `Pow` in integer math, bit-identical in Mono, IL2CPP and Burst. `FixedRandom`, a deterministic PCG32
  generator, and `FixedGrid`. The inspector edits fixed-point fields as numbers, within the limits of `[FixedMin]` and `[FixedRange]`. No
  Entities dependency.
- **[Actors](#actors)** (`Pragma.Dots.Actors`). Units, buildings and map objects described in assets, without code:
  an `ActorDefinition` with a key, tags and parts (any component of the game, a path-finding agent, a view),
  inheriting from a parent. A catalog makes them the types a world spawns by key, maps place them with overrides, and
  the editor shows what a definition inherits and previews placed actors with their views. The same parts bake into
  subscenes or build at run time; the modules bring parts of their own.
- **[Utilities](#utilities)** (`Pragma.Dots.Utils`, `Pragma.Dots.Utils.Entities`). `FixedDictionary4096Bytes` (a hash
  map that fits in a component), `OptionalValue`, inspectors for fixed lists and fixed strings and
  `EnumId` (enum values as ints, in Burst too) without an Entities dependency, `CollisionMatrixToFilter` for projects
  with Unity Physics; signals (one-off events as entities), buffers used as dictionaries of any size and
  `EntityManager` helpers on top of Entities.

### The modules

Each is off after installation; [turn on](#turning-modules-on) the ones you use.

| Module | Define | Needs | In short |
|---|---|---|---|
| [Lockstep](#lockstep) | `PRAGMA_DOTS_LOCKSTEP` | | A deterministic simulation world that every client steps with the same input |
| [Navigation](#navigation) | `PRAGMA_DOTS_NAVIGATION` | Lockstep | Grid path finding, obstacles and walking agents in integer math |
| [Vision](#vision) | `PRAGMA_DOTS_VISION` | Lockstep | What every player sees, for the fog of war and for the rules |
| [Stats](#stats) | `PRAGMA_DOTS_STATS` | Lockstep | Attributes with modifiers, resources such as health, bonuses granted to groups |
| [Views](#views) | `PRAGMA_DOTS_VIEWS` | Lockstep | Pooled GameObject views of simulation entities |
| [State machines](#state-machines) | `PRAGMA_DOTS_STATE_MACHINES` | | Per-entity state machines whose states tick in parallel Burst jobs |

- **Lockstep** (`Pragma.Dots.Lockstep`). Every client runs the same simulation in its own isolated Entities world, and
  only input travels: a server orders it into confirmed frames, one per tick, and never simulates, so the bandwidth
  depends on the number of players, not on the number of entities.
  - Gameplay systems opt in by living in `LockstepSimulationSystemGroup`; they never run in the default, client or
    server worlds.
  - The per-tick input is a struct of your own, with the previous one for exact button edges. Commands (build, buy,
    use) are never dropped and carry data of any length, such as the unit ids of an order. Players are entities that
    join and leave on the same tick everywhere.
  - A server that never stalls (a late input repeats the previous one), adaptive input timing and a jitter-aware
    playout buffer.
  - Desync detection by majority vote with a per-component breakdown, replays watched through ordinary clients at any
    speed and from any player's view, late join, offline mode over the same protocol.
  - Netcode for Entities integration (host, dedicated server, thin clients) in `Pragma.Dots.Lockstep.Netcode`,
    compiled when the project has Netcode, over a transport-agnostic protocol.
  - Interpolated transforms, a spatial index for proximity queries, the Transform and Entity Id actor parts, and the
    registry and placed actors copied into every simulation.
- **Navigation** (`Pragma.Dots.Navigation`). A walkability grid with ground heights baked from a terrain (steep slopes
  blocked) and obstacles that block it while their entity exists; A* with string pulling in integer math; agents that
  walk the paths on the ground and plan again when obstacles change. Large bodies keep to cells with room for them, an
  order that moves an army costs one search, and with avoidance agents steer around each other, go around idle crowds
  and pass each other in narrow passages.
- **Vision** (`Pragma.Dots.Vision`). What every player slot sees, stamped on a grid from vision sources on every tick.
  It is simulation state, the same on every client, so rules may depend on it (orders only on visible targets, the
  AI), and the presentation draws the fog of war from it.
- **Stats** (`Pragma.Dots.Stats`). Attributes with flat, additive and multiplicative modifiers (timed or lasting until
  their source removes them, stacking or not), resources capped by an attribute and changed by one-tick changes such
  as damage, and modifiers a player or a squad grants to all of its units, the later ones included. In `FixedPoint`,
  recalculated only where something changed.
- **Views** (`Pragma.Dots.Views`). GameObject views adapted from ECV (Entity Component View): a pooled prefab per
  entity key, one view part per component or buffer type, fed when it changes, transforms interpolated between ticks,
  and a pool each project can replace (com.pragma.pool, Addressables...). For what Entities Graphics does not draw
  well: animators, VFX, health bars, world-space UI. In the editor, placed actors show their views.
- **State machines** (`Pragma.Dots.StateMachines`). A machine component per machine (a unit's brain, a spell's
  cooldown) with the current, previous and requested state; its states are systems that tick their entities in
  parallel Burst jobs, with enter and exit logic on the update of a transition. Deterministic, so it serves lockstep
  simulations and any other world alike; an actor part gives the initial state.

Tooling: the *Project Settings > Pragma > DOTS* page, the *Window > Pragma > Lockstep Sessions* debug window, and
[Claude Code skills](#claude-code-skills).

## Requirements

- Unity 6000.6 (Unity 6.6) or newer.
- Entities 6.6 (installed as a dependency).
- Netcode for Entities 6.6 to play lockstep over a network: install `com.unity.netcode` yourself, and the Netcode
  integration of the lockstep module compiles. Offline mode, replays and custom transports do without it.
- Burst is recommended: the per-tick systems of the package (frame application, transform history, entity ids,
  navigation, vision, stats) are Burst-compiled, and `FixedPoint` math gives the same bits with or without Burst.

## Installation

In the Package Manager choose **+ > Install package from git URL** and enter:

```
https://github.com/PragmaGame/PragmaDOTSLokstep.git?path=Assets/PragmaDOTS
```

Or add it to `Packages/manifest.json`:

```json
"com.pragma.dots": "https://github.com/PragmaGame/PragmaDOTSLokstep.git?path=Assets/PragmaDOTS"
```

Append `#<tag or commit>` to the URL to pin a revision.

### Assemblies

Reference the assemblies you use from your assembly definitions. The editor assemblies (`*.Editor`) need no
reference.

| Assembly | Part | Contents | Reference it from |
|---|---|---|---|
| `Pragma.Dots.Mathematics` | core | `FixedPoint`, vectors, quaternion, pose, `FixedMath`, `FixedRandom`, `FixedGrid` | simulation and presentation code |
| `Pragma.Dots.Utils` | core | `FixedDictionary4096Bytes`, `OptionalValue` | any code |
| `Pragma.Dots.Utils.Physics` | core, with `com.unity.physics` | `CollisionMatrixToFilter` | presentation code |
| `Pragma.Dots.Utils.Entities` | core | `Signals`, `SignalCleanupSystem`, `BufferDictionary`, `EntityManagerExtensions` | simulation and presentation code |
| `Pragma.Dots.Actors` | core | `ActorDefinition`, `ActorCatalog`, `ActorPart` and the generic parts, `ActorAuthoring`, `ActorLookup`, `ActorRegistryQuery`, `ActorUtility` and the actor components | simulation and presentation code |
| `Pragma.Dots.Actors.Authoring` | core | `ActorCatalogAuthoring` and the bakers of the actor catalog and of placed actors | authoring code, if any |
| `Pragma.Dots.Lockstep` | Lockstep | The protocol, the simulation world, its components, checksums, replays, offline mode, presentation helpers, the spatial index, the Transform and Entity Id actor parts and the copy of actors into the simulation | simulation and presentation code |
| `Pragma.Dots.Lockstep.Netcode` | Lockstep, with `com.unity.netcode` | The Netcode for Entities RPC, the server and client systems, `LockstepNetcode` | bootstrap code |
| `Pragma.Dots.Navigation` | Navigation | The walkability grid, obstacles, agents, `LockstepPathfinder`, the navigation systems and their actor parts | simulation code |
| `Pragma.Dots.Vision` | Vision | The vision grid, vision sources, `LockstepVisionSystem`, the `LockstepVision` queries and their actor parts | simulation code, and presentation code that draws the fog of war |
| `Pragma.Dots.Stats` | Stats | Attributes, resources, modifiers, changes and grants, `LockstepStatSystem` and the `LockstepStats` helpers | simulation code, and presentation code that shows stats |
| `Pragma.Dots.Views` | Views | `EntityView`, view parts, `EntityViewKey`, the view manager, catalogs, the pool abstraction, the View actor part | presentation code, and simulation code that sets an `EntityViewKey` |
| `Pragma.Dots.Views.Authoring` | Views | `EntityViewConfigAuthoring` | authoring code, if any |
| `Pragma.Dots.StateMachines` | State machines | `StateMachine`, `IStateMachine`, `StateMachineSystemGroup<TMachine>`, `StateMachinePart<TMachine, TState>` | simulation code |

### Turning modules on

A module compiles only when its scripting define symbol is set, and all of them are off after installation. Turn on
the ones you use in **Project Settings > Pragma > DOTS**: the page writes the define to every build target at once.
Navigation, vision, stats and views run on the lockstep simulation, so they need the Lockstep module: the page turns it
on with them and turns them off with it. They do not depend on each other. State machines need only the core. The
actor parts of the modules are in their runtime assemblies, so they build at run time as well as in subscenes.

Why defines rather than references alone: Unity compiles every assembly of a package, and a compiled module's systems
join every simulation world (they carry the simulation filter) and enter `LockstepSimulation.DefaultSimulationHash`.
Turned off, a module is not compiled at all: no idle systems, no code in the build.

All builds that play together need the same modules. A build without one of the modules that bring simulation systems
(Lockstep, Navigation, Vision, Stats) has a different simulation hash, and the server refuses clients whose hash
differs from its own (`ValidateSimulationHash`). Views has no simulation system, so a build without it passes that
check, but its actors lack the `EntityViewKey` of their View part and it desyncs. Keep the defines the same on every
target: the page warns when a module is on for some targets only. Build Profiles can add scripting defines of their own; keep
the module defines in Player Settings, where the page writes them.

## Fixed-point math

Part of the core: namespace and assembly `Pragma.Dots.Mathematics`, no Entities dependency. The lockstep simulation
does all of its math with it; anything else may use it where it wants exact, portable numbers.

| Type | Role | Float counterpart |
|---|---|---|
| `FixedPoint` | Scalar | `float` |
| `FixedVector2`, `FixedVector3` | Vectors | `float2` / `Vector2`, `float3` / `Vector3` |
| `FixedQuaternion` | Rotation | `quaternion` / `Quaternion` |
| `FixedPose` | Position and rotation | `Pose` |
| `FixedMath` | Functions and constants | `Mathf`, `math` |
| `FixedRandom` | Random numbers | `Unity.Mathematics.Random` |

### `FixedPoint`

- 64-bit signed raw value with 16 fractional bits (Q47.16): `FixedPoint.FromRaw(65536)` is 1.0.
- Step `1/65536` (about 0.000015), range about ±1.4·10¹⁴.
- Multiplication keeps a 64-bit intermediate: keep the magnitude of a product below about 2·10⁹.
  `FixedMath.MulShiftRound` multiplies with a 128-bit intermediate, and so does `FixedMath.MulDiv(a, b, c)`
  (`a * b / c`, for scaling a large amount); division falls back to exact long division when the shifted dividend
  would overflow.

| Creating values | |
|---|---|
| `FixedPoint x = 5;` | Implicit from `int` |
| `FixedPoint.FromFraction(3, 10)` | Exact fraction, truncated toward zero (0.3) |
| `FixedPoint.Parse("0.3")`, `FixedPoint.TryParse` | Deterministic decimal parser (integer arithmetic, culture independent) for config text |
| `FixedPoint.FromRaw(raw)` | From a raw value |
| `(FixedPoint)1.5f`, `(FixedPoint)1.5` | Explicit, rounded to the nearest step. Fine at bake time or for literals; never for values computed with floats during a tick |
| `FixedPoint.Zero`, `One`, `Half`, `Two`, `MinusOne`, `Epsilon`, `MaxValue`, `MinValue` | Constants |

| Operator | Semantics |
|---|---|
| `+`, `-` | Exact (wrap on overflow, like `long`) |
| `a * b` | Rounded to the nearest step (half up) |
| `a * 3`, `3 * a` | Exact product with an integer |
| `a / b`, `a / 3` | Truncated toward zero; division by zero saturates instead of throwing |
| `%` | Remainder with the sign of the dividend, like C#; zero divisor returns zero |
| `(int)x`, `(long)x` | Truncate toward zero, like a float cast |
| `(float)x`, `(double)x` | For presentation only |

Define tuning constants as properties built from integers and fractions, so they are exact:

```csharp
public static class Rules
{
    public static FixedPoint MoveSpeed => FixedPoint.FromFraction(7, 2);
    public static FixedPoint Friction => FixedPoint.FromFraction(9, 10);
}
```

### `FixedMath`

| Group | Functions |
|---|---|
| Basic | `Abs`, `Sign`, `Min`, `Max`, `Clamp`, `Clamp01`, `Lerp`, `InverseLerp` (zero for an empty range), `Remap`, `MulDiv`, `Select`, `Step`, `SmoothStep`, `MoveTowards` |
| Rounding | `Floor`, `Ceil`, `Round`, `Truncate`, `Frac`, `FloorToInt`, `CeilToInt`, `RoundToInt` |
| Modulo | `Remainder` (sign of the dividend, like `%`), `Mod` (always in `[0, Abs(y))`), `Wrap(x, min, max)` |
| Powers | `Sqrt`, `Rsqrt`, `Exp`, `Exp2`, `Log`, `Log2`, `Log10`, `Pow(FixedPoint, FixedPoint)`, `Pow(FixedPoint, int)` |
| Trigonometry | `Sin`, `Cos`, `SinCos`, `Tan`, `Asin`, `Acos`, `Atan`, `Atan2` |
| Angles | `ToRadians`, `ToDegrees`, `DeltaAngle` |
| Constants | `Pi`, `TwoPi`, `HalfPi`, `E`, `Ln2`, `Ln10`, `Sqrt2`, `Rad2Deg`, `Deg2Rad`, `Epsilon` |
| `FixedVector2` | `Dot`, `Cross` (z of the 3D cross), `Length`, `LengthSquared`, `Distance`, `DistanceSquared`, `Normalize`, `NormalizeSafe`, `ClampLength`, `MoveTowards`, `Rotate`, `Perpendicular`, `Reflect`, `Direction(angle)`, `Angle(v)`, `Abs`, `Min`, `Max`, `Clamp`, `Lerp`, `Floor`, `Round` |
| `FixedVector3` | `Dot`, `Cross`, `Length`, `LengthSquared`, `Distance`, `DistanceSquared`, `Normalize`, `NormalizeSafe`, `ClampLength`, `MoveTowards`, `Reflect`, `Project`, `ProjectOnPlane`, `Angle(a, b)`, `Abs`, `Min`, `Max`, `Clamp`, `Lerp`, `Floor`, `Round` |
| `FixedQuaternion` | `Mul`, `Rotate`, `Conjugate`, `Inverse`, `Dot`, `Length`, `Normalize`, `NormalizeSafe`, `Nlerp`, `Slerp`, `Angle`, `RotateTowards`, `Forward`, `Up`, `Right` |
| Raw helpers | `MulShiftRound`, `DivideRaw`, `SqrtRounded` |

Accuracy, checked against `double` by the tests: `Sqrt` within 1 step below about 2·10⁹ (above, it drops the 8 lowest
bits: within 128 steps, a relative error below 10⁻⁷); `Sin`, `Cos`, `Atan`, `Atan2` within 2 steps;
`Asin`, `Acos`, `Log`, `Log2`, `Log10` within 3 steps; `Exp`, `Exp2` and `Tan` within 0.01–0.02 % (or 3 steps).
Everything is computed with integer series and range reduction, without lookup tables, and gives the same bits
everywhere. Out-of-domain inputs saturate instead of producing NaN: `Sqrt` of a negative is 0, `Log` of 0 is
`FixedPoint.MinValue`.

`LengthSquared` overflows for components above about 46 000; `Length` and `Distance` scale internally and do not.

### Vectors and quaternions

- `FixedVector2`, `FixedVector3`: fields `x`, `y`, `z`; component-wise `+ - * /`, scaling by `FixedPoint` and `int`,
  swizzles `Xy`, `Xz`, `Yz`, `Yx`, constants `Zero`, `One`, `Up`, `Down`, `Left`, `Right`, `Forward`, `Back`.
- `FixedQuaternion`: fields `x`, `y`, `z`, `w` (laid out like `Unity.Mathematics.quaternion`), `Identity`, `Xyz`,
  `AxisAngle`, `RotateX/Y/Z`, `Euler` (radians, Unity's order: z, then x, then y), `LookRotation`, `FromBasis`;
  `q * v` rotates a vector, `a * b` applies `b` first.
- `FixedPose`: fields `position`, `rotation`, `Identity`, `Forward`, `Up`, `Right`; the placement of actors.
- Conversions to and from `float2`, `float3`, `quaternion`, `Vector2`, `Vector3` and `Quaternion` are explicit.
  `int2` and `int3` convert implicitly into the vectors (not back: use `FloorToInt` per component or
  `FixedGrid.WorldToCell`).

### `FixedRandom`

PCG32 (XSH-RR) with 128 bits of state, seeded through SplitMix64.

| Method | Result |
|---|---|
| `new FixedRandom(seed)`, `FixedRandom.CreateFromIndex(seed, index)` | A generator; different indices give independent streams |
| `NextUInt()`, `NextUInt(max)`, `NextUInt(min, max)` | Unbiased integers |
| `NextInt(max)`, `NextInt(min, max)`, `NextBool()` | |
| `NextChance(probability)` | `true` with the given `FixedPoint` probability |
| `NextFixedPoint()`, `NextFixedPoint(min, max)` | Uniform `FixedPoint` in `[0, 1)` or `[min, max)` |
| `NextVector2(min, max)`, `NextVector3(min, max)` | Uniform vectors |
| `NextAngle()`, `NextDirection2()`, `NextDirection3()`, `NextInsideUnitCircle()`, `NextRotation()` | Geometry helpers |

### Inspector

`FixedPoint` fields are edited as decimal numbers, `FixedVector2`/`FixedVector3` as vectors and `FixedQuaternion` as Euler angles in degrees.
The editor converts once and stores raw values.

Unity's `[Min]` and `[Range]` do not work on fixed-point fields: their drawers come before the drawer of the field's
type and handle only float and int (and, for `[Min]`, vector) fields. `[FixedMin(min)]` and `[FixedRange(min, max)]`
limit a `FixedPoint` field: an edit outside the limits becomes the nearest one, and `[FixedRange]` draws a slider with a
number field. The limits are doubles rounded to the nearest step (`Min`, `Max`); on an array they apply to every
element, and `Clamp` applies them in code.

```csharp
[SerializeField, FixedMin(0)] private FixedPoint _speed = 2;
[SerializeField, FixedRange(0, 1)] private FixedPoint _chance = FixedPoint.Half;
```

## Actors

An actor is a game object: a unit, a building, a capture point, the map itself. A game designer describes it in an
asset, without code: a key, tags, and parts that give it its components. The types a game spawns are listed in a
catalog, and maps place actors with values of their own.

Actors are part of the core (namespace and assembly `Pragma.Dots.Actors`, bakers in `Pragma.Dots.Actors.Authoring`)
and work in any Entities world. With the lockstep module, the registry and the placed actors of the presentation world
are copied into every simulation world, and the module adds the Transform and Entity Id parts.

| Type | What it is |
|---|---|
| `ActorDefinition` | Asset (*Create > Pragma > DOTS > Actor Definition*): `Key` (unique, 1 to 29 bytes of UTF-8: "rifleman"), `Parent`, tags ("unit", "player") and parts. A derived definition inherits its parent's tags and parts; its own part replaces the parent's part of the same type, or merges with it (stats, grants: `IMergeableActorPart`). A new definition starts with the parts marked `[DefaultActorPart]`: with the lockstep module, a Transform and an Entity Id part |
| `ActorPart` | A piece of an actor that adds its components when the actor is built. An actor has one part of each type |
| `ActorCatalog` | Asset (*Create > Pragma > DOTS > Actor Catalog*): the actor types; the index is the type. Only add at the end once a game has shipped. All its definitions share at most 128 tags |
| `ActorCatalogAuthoring` | Bakes a catalog into the actor registry of the presentation world: a prefab entity per definition and the tag table. Put it in a subscene of the client (or local) world |
| `ActorAuthoring` | An actor placed on a map: a definition at the pose of the GameObject, with parts of its own that override the definition's |
| `Actor` | On the root entity of every actor: `key` and `type` (the index in the registry; -1 for a key no type has) |
| `ActorLookup` | The registry of a world: `TryGetType(key)`, `GetKey`, `GetPrefab`, `Instantiate`, `TryGetTag(name)`, `HasTag`, `GetTags` |
| `ActorRegistryQuery` | The registry for a system that reads it on its updates: `Create` builds the query in `OnCreate` and requires it for the update, `Get` returns the `ActorLookup` through it in `OnUpdate` |
| `PlacedActor` | On an actor placed on a map: `order`, its place among the placed actors, the same on every machine |
| `ActorUtility` | Builds the registry and placed actors at run time (`CreateRegistry`, `CreatePlaced`); `ResolveTypes` gives placed actors the type of their key |
| `LockstepActorUtility` | Lockstep: copies the registry and the placed actors into a simulation world (`CopyRegistry`, `CopyPlacedActors`), which the built-in systems do before tick 0 |

### Parts

| Part | Module | Adds |
|---|---|---|
| Local Transform (`LocalTransformPart`) | core | `LocalTransform` at the placement (identity on the prefab of a type) and `LocalToWorld`; a scale that differs between the axes goes into a `PostTransformMatrix`, as baking does. In a subscene it takes over the baked entity's transform (`TransformUsageFlags.ManualOverride`) |
| Transform (`TransformPart`) | Lockstep | `LockstepTransform`: the pose of a placed actor and the X of its scale, identity on the prefab of a type (spawning code places it); with *Interpolate*, `LockstepTransformPrevious` |
| Entity Id (`EntityIdPart`) | Lockstep | `LockstepEntityId` |
| Navigation/Agent, Obstacle, Avoidance, Grid | Navigation | See [Navigation](#navigation) |
| Vision/Source, Grid | Vision | See [Vision](#vision) |
| View (`ViewPart`) | Views | `EntityViewKey`: the actor's key, unless the part names another; see [Views](#views) |
| Stats, Stat Grants (subclasses of `StatsPart`, `StatGrantsPart`) | Stats | The stats of an actor, the grants it gives; see [Stats](#stats) |
| State machines (subclasses of `StateMachinePart`) | State Machines | The machine component and its initial state; see [State machines](#state-machines) |

New definitions start with Unity's transform (Local Transform); with the lockstep module they start with its Transform
and Entity Id parts instead, since the lockstep Transform replaces the Unity one (`[DefaultActorPart(Replaces = ...)]`).

A component or buffer element of the game becomes a part without code: give the type `[ActorComponent]` (and
`[Serializable]`, so Unity keeps its values). The part menu lists it, and the inspector edits its fields; `FixedPoint`
fields show as numbers.

```csharp
[Serializable, ActorComponent("Combat/Health")]
public struct Health : IComponentData
{
    public FixedPoint current;
    public FixedPoint max;
}
```

A part with logic (several components, values worked out from others, references to other actors) derives from
`ActorPart`. `IActorBuilder` is the actor being built: its `Key`, whether it `IsPlaced`, its `Placement` (a
`FixedPose`) and `PlacementScale` (along its local axes; X where a uniform scale is needed), `HasPart<T>()`,
`AddComponent`, `AddBuffer` (fill the buffer before adding anything else), `SetComponentEnabled`, `DependsOn` (bake
again when an asset changes). `DrawGizmos` draws the part of a placed actor in the scene view. `[DefaultActorPart]`
puts a part into every new definition (`Order` sorts them, `Replaces` names a default part it stands in for).

```csharp
[Serializable, ActorPartMenu("Combat/Weapon")]
public sealed class WeaponPart : ActorPart
{
    [SerializeField] private FixedPoint _range = 10;
    [SerializeField] private ActorDefinition _projectile;

    public override void Build(IActorBuilder builder)
    {
        builder.AddComponent(new Weapon { range = _range, projectile = new FixedString32Bytes(_projectile != null ? _projectile.Key : "") });
    }
}
```

A part of entries by key (stats by stat, weapons by slot) may implement `IMergeableActorPart`: its override then merges
with the inherited part instead of replacing it, so a derived definition or a placement lists only the entries it
adds or changes, and later changes of the parent's other entries still reach it. `MergeOnto(inherited)` returns the
merge as a new part (the override's entries replace those with the same key, its other settings win),
`CreateOverride()` the empty override the inspector starts from.

The same `Build` runs when a subscene bakes and when actors are built at run time, so it must give the same result from
the same data on every machine: keep `FixedPoint` fields rather than floats, and read only the part's data and the
builder. Parts are serialized by reference: renaming or moving a part class loses its data unless the class gets
`[UnityEngine.Scripting.APIUpdating.MovedFrom]`.

### Editing

The inspector of a definition shows the tags and parts it inherits, greyed, with *Override* to copy a part and change
it; its own parts have *Remove* (or *Revert* for an override). *Add Part* lists every part type (an inherited type is
added as an override), *Add Tag* lists the tags of the project's definitions. A mergeable part shows what the whole
lineage merged into it, also above its override, and *Override* starts from no entries. The catalog inspector shows the tag
table and what would break the registry: keys that are empty, too long or taken twice, too many tags, empty parts.

### Spawning and tags

In a lockstep simulation, for instance:

```csharp
[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
public partial struct ReinforcementSystem : ISystem
{
    private EntityQuery _registry;

    public void OnCreate(ref SystemState state)
    {
        // The system updates once the world has a registry.
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
        state.EntityManager.SetComponentData(spawned, LockstepTransform.FromPosition(new FixedVector3(10, 0, 0)));

        // Structural changes invalidate the registry read before them: read it again.
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

`ActorLookup` reads the registry in place and is Burst-compatible; pass it to jobs. Look keys and tags up once and keep
the type indices and `ActorTag` handles: a check is then an array read and a bit test (the example looks them up on
every update for brevity, and its string keys keep `OnUpdate` from compiling with Burst). Both are positions in the
registry: the tag table is sorted by name, so a new tag can renumber the others. Keep them for the session, and store
keys and tag names in saves, commands and replays. Instantiate with an
`EntityCommandBuffer` too (`Instantiate(commandBuffer, type)`).

The query of the registry is created in `OnCreate`: `ActorLookup.Get(ref state)` builds it where it is called, which in
`OnUpdate` makes Entities look the query up on every update and warn once per system that it creates a query during
OnUpdate. Outside systems, `ActorLookup.TryGet(entityManager, out var actors)` reads the registry of any world.

### Placing actors on a map

Put a GameObject with an `ActorAuthoring` into the subscene of the map, choose its definition and override what differs
on this map: the owner slot of a main building, the radius of a capture point. It bakes into a placed actor
(`PlacedActor`) built from its parts at the pose of the GameObject, which every simulation world starts with; its type
comes from the registry by key when it is copied in (a key no type has leaves it with type -1 and no tags, with a
warning). Without the lockstep module, call `ActorUtility.ResolveTypes(entityManager)` once the catalog and the map
have loaded. With it, keep renderers off the GameObject: the baked entity is copied whole into the simulation, and the
view shows it.

A special entity can also be a definition of its own, derived from the common one and listed in the catalog ("main
building of map 1"), and a prefab with an `ActorAuthoring` is a preset to drop into maps: Unity's prefab overrides work
on its parts.

### View previews

With the Views module, a placed actor with a View part shows its view in the scene and in prefab mode: the view bound to
its key in the `EntityViewConfig` assets of the project. The actor is built from its parts in a scratch world and its
components go to the view's parts, so the preview runs the game's view code (a team colour, a level, a size). The
preview is never saved; in an open subscene it lives outside the subscene so that it is not baked, and picking it
selects the actor. *Tools > Pragma > DOTS > Actor View Previews* turns previews off. A view part written for bound
views (one that reads `View.Client`) may not run in edit mode: the preview logs a warning once and skips it.

### Without subscenes

The registry and placed actors can be built at run time, from the same assets, before the session starts:

```csharp
var entityManager = clientWorld.EntityManager;
ActorUtility.CreateRegistry(entityManager, catalog);
for (var i = 0; i < level.Actors.Count; i++)
{
    var entry = level.Actors[i];
    ActorUtility.CreatePlaced(entityManager, entry.Definition, entry.Placement, new FixedVector3(1, 1, 1), entry.Overrides, (ulong)i);
}
// Or the ActorAuthoring objects of a scene loaded without baking: CreatePlaced(entityManager, authoring, order).
```

Every client must build the same content: the same catalog and the same placements in the same order (the order
argument). The parts are deterministic for that: terrains, for instance, are sampled in fixed point.

## Utilities

`Pragma.Dots.Utils` holds small types that DOTS games keep needing. It depends neither on the rest of the package nor
on Entities; `Pragma.Dots.Utils.Entities` (same namespace) adds the helpers that need Entities.

| Type | Use |
|---|---|
| `FixedDictionary4096Bytes<TKey, TValue>` | A hash map of unmanaged keys and values stored in the struct itself (4 KB), so it fits in a component and gets copied, hashed and serialized with it. Open addressing with linear probing; the table starts at 16 slots and doubles up to `MaxCapacity` (about 340 entries of `int` to `int`). `Add` and the indexer throw when it is full, `TryAdd` returns false. Burst-compatible |
| `OptionalValue<T>` | An unmanaged value that may be absent (`IsSet`, `Value`, `ToNullable`, `FromNullable`): a `Nullable<T>` for components |
| `CollisionMatrixToFilter` | Unity Physics `CollisionFilter`s built from the layer collision matrix of *Project Settings > Physics*, cached per layer. In `Pragma.Dots.Utils.Physics`, compiled only when the project has `com.unity.physics`. For the presentation side: Unity Physics is not part of a lockstep simulation |

The dictionary enumerates its slots, so the order follows the keys' `GetHashCode` and the history of adds and removes.
In simulation state use keys whose hash is the same on every machine: integers, fixed strings, structs that override
`GetHashCode` over such fields. Not `Entity` (its index differs between clients) and not structs left with the default
`ValueType.GetHashCode`.

`Pragma.Dots.Utils.Editor` draws `FixedString32Bytes` to `FixedString4096Bytes` fields as text fields (text past the
capacity is cut) and `FixedList32Bytes<T>` to `FixedList4096Bytes<T>` fields as arrays whose elements use their own
property drawers (a `FixedPoint` element shows as a number), capped at the list's capacity.

### Signals

One-off events as entities: a signal is an entity with a component of the game (the event and its data) and a
`SignalTag`. Consumers read it with an ordinary query, and every signal is destroyed at the end of the update, just
before the end-of-update command buffer plays back: by `SignalCleanupSystem` in `SimulationSystemGroup`, and by
`LockstepSignalCleanupSystem` at the end of a lockstep tick.

```csharp
public struct UnitDied : IComponentData
{
    public uint unitId;
    public int killerSlot;
}

// Publish, also from a job (Publish(sortKey, signal) on a parallel writer):
var commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
commandBuffer.Publish(new UnitDied { unitId = id, killerSlot = slot });

// Consume, in any system of the next update:
foreach (var died in SystemAPI.Query<RefRO<UnitDied>>().WithAll<SignalTag>())
{
    // ...
}
```

- **Lifetime.** Published through the end-of-update command buffer, the usual way, a signal is created after the
  cleanup: it lives through the whole next update, and every system of that update sees it once. Published at once
  (`entityManager.Publish(signal)`) or through a buffer played back earlier, it is seen by the systems that follow, up
  to the end of the same update.
- **In a lockstep simulation** signals are state like any other: published through the end-of-tick buffer, they are
  hashed between the two ticks and seen on the next one by every client. The presentation may skip ticks, so effects
  and sounds read a tick-stamped buffer instead ([Effects and sounds](#effects-and-sounds)).
- A signal without data is a tag of its own (`Publish(new Spawned())`). A world without either group destroys its
  signals itself: `entityManager.DestroyEntity` of a query over `SignalTag`.

### Buffer dictionaries

A dynamic buffer used as a dictionary of any size, where `FixedDictionary4096Bytes` would not fit: the game's element
type names its key (`IDictionaryElement<TKey>`), and `BufferDictionary` keeps the elements sorted by it.

```csharp
[InternalBufferCapacity(0)]
public struct Cooldown : IBufferElementData, IDictionaryElement<int>
{
    public int ability;
    public int ticksLeft;

    public int Key { get => ability; set => ability = value; }
}

cooldowns.GetOrAdd(ability).ticksLeft = 30;   // a reference to the entry, added when missing
if (cooldowns.TryGet(ability, out var cooldown)) { }
cooldowns.Remove(ability);
```

- `IndexOf` (the index, or the complement of where the key would go), `ContainsKey`, `TryGet`, `GetOrAdd` (also with
  `out bool added`), `Remove`; Burst-compatible.
- Iterating the buffer visits the entries in key order, whatever order they were added in: deterministic for keys
  with a deterministic `CompareTo` (integers, fixed strings; not `Entity`). A lookup costs O(log n), an add or a remove
  O(n) for the elements it moves.
- Change the entries only through these methods (or keep the buffer sorted yourself), and never change a key in place.

### EntityManager extensions

`TryGetComponent(entity, out component)` and `TryGetBuffer(entity, isReadOnly, out buffer)`: false for an entity that
does not exist or lacks it. Entities covers the rest: `AddComponent` and `RemoveComponent` do nothing (and return
false) when the component is already there or already gone, and `AddComponentData` also sets an existing component.

## Lockstep

Deterministic lockstep, played over Netcode for Entities or offline. Every client runs the same simulation in its own
isolated Entities world. Only player input travels over the network. A server collects the input of every player,
orders it into *confirmed frames* (one per tick) and streams them to all clients. Each client steps its simulation
with exactly the same frames, so every machine computes exactly the same state. The server never simulates, and the
bandwidth depends on the number of players, not on the number of simulated entities.

A [module](#turning-modules-on): turn it on with `PRAGMA_DOTS_LOCKSTEP` and reference `Pragma.Dots.Lockstep` (and
`Pragma.Dots.Lockstep.Netcode` to play over Netcode for Entities).

### When to use it

Lockstep fits games where many things are simulated but few players send input:

- RTS, tower defense, colony and auto-battler games with hundreds or thousands of units.
- Co-op and competitive games that need exact replays, spectating from input logs, or a provably shared simulation.
- Slow or medium-paced real-time games where about one round trip of input delay is acceptable.

It is a poor fit for:

- Shooters, fighting and racing games where input delay must be hidden. This package is strict lockstep: there is
  no client-side prediction or rollback (yet). Use Netcode for Entities ghosts with prediction for those games.
- Simulations that depend on float-based engine systems (Unity Physics, NavMesh, Animator) for gameplay results.
  Unit movement around obstacles is covered: the package has its own deterministic [navigation](#navigation).

### How it works

```
 client A                         server                          client B
 ────────                         ──────                          ────────
 input for tick T  ──────────►  collects inputs
                                closes tick T on its clock  ◄────  input for tick T
                                (late input: previous one repeats,
                                 commands move to the next tick)
 frame T  ◄──────────────────── frame T: inputs, commands, ───────►  frame T
                                joins, leaves of every player
 simulate T in the                                                  simulate T in the
 simulation world                                                   simulation world
 hash every N ticks ─────────►  majority vote on hashes  ◄────────  hash every N ticks
```

**Worlds.** A client process has the usual worlds (a Netcode client world, or a local world when offline) plus one
*simulation world* per session:

```
 client or local world (presentation)                 simulation world (deterministic)
 ────────────────────────────────────                 ─────────────────────────────────
 LockstepInputSystemGroup                             LockstepSimulationSystemGroup, once per tick:
   your input systems -> LockstepLocalInput             LockstepFrameApplySystem
 LockstepOfflineSystem or                                LockstepTransformHistorySystem
 LockstepNetcodeClientSystem                             LockstepBeginSimulationEntityCommandBufferSystem
   LockstepClient: sends input, receives frames,         your gameplay systems
   steps the simulation world  ───────────────────►     LockstepEndSimulationEntityCommandBufferSystem
 LockstepViewSystem, EntityViewManagerSystem,            LockstepEntityIdSystem
 GameObject views, the rest of your presentation
   read the simulation world  ◄── read only ───
```

The simulation world is created by `LockstepSimulation` and is never added to the player loop. `LockstepClient` steps
it once for every confirmed frame that is due. Systems reach it through `LockstepSimulationSystemGroup`: the group
carries a custom world filter flag (`LockstepWorldFilter.SIMULATION`), and systems inside it inherit that flag.
System creation order is sorted, so it is the same on every machine.

**Server.** `LockstepServer` closes tick `T` when its clock reaches `T + 1`. The frame of a tick holds the input of
every player whose input changed (otherwise the previous input repeats), the commands, the joins and the leaves.
Frames are tiny and travel over a reliable, ordered channel.

**Client.** `LockstepClient` runs two clocks:

- The *input clock* runs ahead of the server. The server reports how early each input arrived, and the client
  speeds up or slows down so that its input lands `InputMarginTicks` before the deadline.
- The *playout clock* runs behind the newest confirmed frame, by `PlayoutDelayTicks` plus twice the measured jitter,
  and simulates frames as they become due. It catches up within a per-update tick and time budget.

**Transport.** The protocol core (`LockstepServer`, `LockstepClient`) only sees bytes through `ILockstepTransport`.
The Netcode systems move those bytes as RPCs; offline mode and tests use the in-process `LockstepLoopbackNetwork`.

### Quick start

Turn the Lockstep module on and reference `Pragma.Dots.Lockstep` and `Pragma.Dots.Mathematics` from the game's assembly
(and `Pragma.Dots.Lockstep.Netcode` for the host and join of step 4).

#### 1. Define the input

The input is a plain unmanaged struct, at most 128 bytes. Quantize analog values on the client: the bytes of this
struct are what every client simulates, so the float stick value never reaches the simulation.

```csharp
public struct GameInput
{
    public const byte FIRE = 1;

    // -100..100
    public sbyte moveX;
    public sbyte moveY;
    public byte buttons;
    // Explicit, so no undefined padding byte travels with the input.
    public byte reserved;
}
```

#### 2. Write the simulation

Systems in `LockstepSimulationSystemGroup` run once per tick, only in simulation worlds. Keep all state in
components and use `FixedPoint` math.

```csharp
public struct Avatar : IComponentData
{
    public int slot;
}

[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
[BurstCompile]
public partial struct AvatarSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<LockstepTime>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var time = SystemAPI.GetSingleton<LockstepTime>();
        ref var random = ref SystemAPI.GetSingletonRW<LockstepRandom>().ValueRW.value;

        // A player joined on this tick: give them an avatar. Structural changes are not allowed while a query is
        // iterated, so they go through the command buffer played back at the end of the tick.
        var buffer = SystemAPI.GetSingleton<LockstepEndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerJoined>())
        {
            var avatar = buffer.CreateEntity();
            buffer.AddComponent(avatar, new Avatar { slot = player.ValueRO.slot });
            buffer.AddComponent(avatar, LockstepTransform.FromPosition(new FixedVector3(random.NextFixedPoint(-5, 5), 0, 0)));
            buffer.AddComponent<LockstepTransformPrevious>(avatar);
        }

        // Inputs by slot. Query order is identical on every client.
        var maxPlayers = SystemAPI.GetSingleton<LockstepSessionInfo>().maxPlayers;
        var inputs = new NativeArray<GameInput>(maxPlayers, Allocator.Temp);
        foreach (var (player, input) in SystemAPI.Query<RefRO<LockstepPlayer>, RefRO<LockstepPlayerInput>>())
        {
            inputs[player.ValueRO.slot] = input.ValueRO.Get<GameInput>();
        }

        var speed = FixedPoint.FromFraction(7, 2);
        foreach (var (avatar, transform) in SystemAPI.Query<RefRO<Avatar>, RefRW<LockstepTransform>>())
        {
            var input = inputs[avatar.ValueRO.slot];
            var move = new FixedVector3(FixedPoint.FromFraction(input.moveX, 100), 0, FixedPoint.FromFraction(input.moveY, 100));
            transform.ValueRW.position += move * speed * time.deltaTime;
        }
    }
}
```

A complete game also removes the avatar when the player leaves (`LockstepPlayerLeft`); see
[Players](#players).

#### 3. Gather the local input

On the client, write `LockstepLocalInput` every frame from a system in `LockstepInputSystemGroup`. The group exists
only in client and local worlds.

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
        var stick = ReadStick(); // a Vector2 from the Input System, clamped to length 1
        var input = new GameInput
        {
            moveX = (sbyte)Mathf.RoundToInt(stick.x * 100f),
            moveY = (sbyte)Mathf.RoundToInt(stick.y * 100f),
        };

        var localInput = SystemAPI.GetSingleton<LockstepLocalInput>();
        localInput.Set(input);
        SystemAPI.SetSingleton(localInput);
    }
}
```

Outside ECS, `LockstepWorlds.TryGetClient(world, out var client)` followed by `client.SetInput(input)` does the same.

#### 4. Start a session

```csharp
// Offline: an in-process server and client inside a client or local world.
world.EntityManager.CreateSingleton(LockstepOfflineConfig.Create<GameInput>(tickRate: 30));

// Host: a Netcode server world and a client world in this process.
var settings = LockstepServerSettings.Default;
settings.TickRate = 30;
settings.MaxPlayers = 4;
settings.InputSize = UnsafeUtility.SizeOf<GameInput>();
settings.MinPlayersToStart = 2;

var server = ClientServerBootstrap.CreateServerWorld("Server");
var client = ClientServerBootstrap.CreateClientWorld("Client");
LockstepNetcode.HostSession(server, settings);
LockstepNetcode.JoinSession(client, LockstepClientSettings.Default);
LockstepNetcode.Listen(server, NetworkEndpoint.AnyIpv4.WithPort(7979));
LockstepNetcode.Connect(client, NetworkEndpoint.LoopbackIpv4.WithPort(7979));

// Join: only a client world.
var remote = ClientServerBootstrap.CreateClientWorld("Client");
LockstepNetcode.JoinSession(remote, LockstepClientSettings.Default);
LockstepNetcode.Connect(remote, NetworkEndpoint.Parse("192.168.1.20", 7979));
```

See [Sessions](#sessions) for bootstraps, dedicated servers, thin clients and the match flow.

#### 5. Present it

```csharp
if (LockstepWorlds.TryGetClient(presentationWorld, out var client) && client.Simulation != null)
{
    var simulation = client.Simulation;
    var entityManager = simulation.World.EntityManager; // read only
    var lastTick = simulation.Tick - 1;
    var alpha = client.InterpolationAlpha;
    // For each entity: LockstepTransformExtensions.Interpolate(transform, previous, lastTick, alpha) -> LocalTransform
}
```

Or let the package spawn and move views for you: GameObject prefabs bound to an `EntityViewKey`
([Views](#views)), or Entities Graphics copies of the actor prefabs (`LockstepViewSystem`).

### Writing the simulation

#### System order

`LockstepSimulationSystemGroup` updates once per confirmed tick:

| Order | System | What it does |
|---|---|---|
| first | `LockstepFrameApplySystem` | Destroys players that left last tick, shifts input to previous, clears commands, disables the joined flag, then applies the frame: new players, inputs, commands, leaves |
| | `LockstepTransformHistorySystem` | Copies `LockstepTransform` into `LockstepTransformPrevious` for interpolation |
| | `LockstepBeginSimulationEntityCommandBufferSystem` | Plays back buffers recorded for the start of the tick |
| | your systems | Ordered with `[UpdateBefore]` / `[UpdateAfter]`, or in your own subgroups |
| last | `LockstepSignalCleanupSystem` | Destroys the signals of the tick ([Signals](#signals)) |
| last | `LockstepEndSimulationEntityCommandBufferSystem` | Plays back buffers recorded during the tick |
| last | `LockstepEntityIdSystem` | Assigns `LockstepEntityId`s to new entities and rebuilds `LockstepEntityIdMap` |

Systems in the group are created only in simulation worlds. To put a system into a simulation world without the
group, give it `[WorldSystemFilter(LockstepWorldFilter.SIMULATION)]`, or pass it in
`LockstepSimulationOptions.AdditionalSystems`.

#### Singletons of the simulation world

| Singleton | Contents |
|---|---|
| `LockstepTime` | `tick` being simulated (from 0), `tickRate`, `deltaTime` (`FixedPoint`, 1 / tick rate), `elapsedTime` (`tick / tickRate`) |
| `LockstepSessionInfo` | `seed`, `tickRate`, `maxPlayers`, `inputSize`, `startData`; `GetStartData<T>()` |
| `LockstepRandom` | `value`, an `FixedRandom` seeded from the session seed |
| `LockstepPlayerSlot` (buffer) | Player entity of every slot, `Entity.Null` when free |
| `ActorRegistry` | The actor types (`ActorType` buffer: key, prefab, tags) and the tag table (`ActorTagName` buffer), copied from the presentation world; read them with `ActorLookup` |
| `LockstepEntityIdMap` | Id to entity lookup, rebuilt at the end of every tick that created, destroyed or moved an entity with an id |
| `LockstepEntityIdCounter` | Last id handed out |

#### Players

A player is an entity created by `LockstepFrameApplySystem` on the tick the player joins:

| Component | Contents |
|---|---|
| `LockstepPlayer` | `slot` in `[0, maxPlayers)`, `joinTick`, `joinData`; `GetJoinData<T>()` |
| `LockstepPlayerInput` | The confirmed input of this tick: `Get<T>()`, `GetPrevious<T>()`, `HasChanged()` |
| `LockstepCommand` (buffer) | Commands confirmed for this tick, cleared every tick |
| `LockstepCommandData` (buffer) | The data of those commands, back to back; read it with `LockstepCommand.GetData<T>` |
| `LockstepPlayerJoined` (enableable) | Enabled only during the join tick |
| `LockstepPlayerLeft` (enableable) | Enabled only during the leave tick; the entity is destroyed at the start of the next tick |

Typical lifecycle code:

```csharp
// Spawn on join (through a command buffer, or collect the slots and create after the loop).
foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerJoined>())
{
    var character = player.ValueRO.GetJoinData<CharacterChoice>();
    // record the avatar of player.ValueRO.slot
}

// Despawn on leave: collect the slots, then destroy their avatars.
var leftSlots = new NativeList<int>(Allocator.Temp);
foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>().WithAll<LockstepPlayerLeft>())
{
    leftSlots.Add(player.ValueRO.slot);
}
```

You can also add your own components to the player entity itself (on the join tick), as long as nothing else must
survive the leave: the entity is destroyed on the tick after `LockstepPlayerLeft`.

A slot becomes free once the leave has been announced in a frame, and a later joiner may get it. Key per-player data
by `slot` only while the player is present.

#### Input

`LockstepPlayerInput.Get<T>()` returns the input confirmed for this tick, `GetPrevious<T>()` the one of the previous
tick (zero before the first input), and `HasChanged()` compares the two.

```csharp
var current = input.ValueRO.Get<GameInput>();
var previous = input.ValueRO.GetPrevious<GameInput>();
var firePressed = (current.buttons & GameInput.FIRE) != 0 && (previous.buttons & GameInput.FIRE) == 0;
```

- The input size is part of the session (`InputSize`, `UnsafeUtility.SizeOf<T>()`); every client must send exactly
  that struct.
- The client sends input once per tick. A button pressed and released between two ticks is lost: for presses that
  must never be missed, use a command or latch the button on the client until the next tick.
- When the server repeats a late player's previous input, the simulation sees exactly that, on every client.

#### Commands

Commands are discrete actions with a payload struct of up to 122 bytes and, optionally, data of any length. They
travel reliably and are never dropped: when they reach the server late, they move to the next tick.

```csharp
public struct BuildCommand
{
    public int blueprint;
    public int cellX;
    public int cellY;
}

// Client, in LockstepInputSystemGroup:
SystemAPI.GetSingletonBuffer<LockstepCommand>().Add(LockstepCommand.Create(new BuildCommand { blueprint = 2, cellX = 10, cellY = 4 }));
// or, outside ECS: client.AddCommand(new BuildCommand { ... });

// Simulation:
foreach (var (player, commands) in SystemAPI.Query<RefRO<LockstepPlayer>, DynamicBuffer<LockstepCommand>>())
{
    for (var i = 0; i < commands.Length; i++)
    {
        if (commands[i].TryGet<BuildCommand>(out var build))
        {
            // validate and apply for player.ValueRO.slot
        }
    }
}
```

Lists go into the command's data: an array of any unmanaged element type and any length, kept in the
`LockstepCommandData` buffer next to the commands. An order for a whole selection is one command, however many units
it names:

```csharp
public struct MoveCommand
{
    public FixedVector3 target;
}

// Client: the data goes into the LockstepCommandData buffer of the entity the command is added to.
var commands = SystemAPI.GetSingletonBuffer<LockstepCommand>();
var commandData = SystemAPI.GetSingletonBuffer<LockstepCommandData>();
commands.Add(LockstepCommand.Create(new MoveCommand { target = target }, unitIds.AsArray(), commandData));
// or, outside ECS: client.AddCommand(new MoveCommand { target = target }, unitIds.AsArray());

// Simulation:
foreach (var (player, commands, commandData) in
         SystemAPI.Query<RefRO<LockstepPlayer>, DynamicBuffer<LockstepCommand>, DynamicBuffer<LockstepCommandData>>())
{
    for (var i = 0; i < commands.Length; i++)
    {
        if (commands[i].TryGet<MoveCommand>(out var move))
        {
            var unitIds = commands[i].GetData<uint>(commandData);   // a view, valid until the buffer changes
            // validate that each unit belongs to player.ValueRO.slot, then order it
        }
    }
}
```

- A command type is identified by a stable hash of its assembly-qualified name (`LockstepCommand.TypeHashOf<T>()`):
  renaming the struct, its namespace or its assembly changes the id, so all clients need the same build.
- Data has no size limit: a large command is fragmented on the wire and still reaches every client in one tick. It
  takes longer to arrive (Netcode sends what does not fit its reliable window on the following ticks), and on the
  ordered channel the inputs behind it wait for it: fine for an order now and then, not for kilobytes every tick.
- `GetData<T>` ignores trailing bytes that do not fill a whole `T`, the same way on every client; `DataLength` is the
  size in bytes. Data starts at 8-byte boundaries, so any element type reads aligned.
- Each player contributes at most 32 commands to one tick; the rest move to the following ticks, in order.
- Validate commands in the simulation (enough money, valid cell, own units...): every client runs the same validation,
  so a cheating client only desyncs itself.

#### Join data and start data

- **Join data** (up to 62 bytes) is sent by a client when joining: `LockstepNetcode.JoinSession(world, settings,
  LockstepBytes.ToFixedList64(new CharacterChoice { id = 3 }))`. The simulation reads it with
  `LockstepPlayer.GetJoinData<T>()`.
- **Start data** (up to 126 bytes) is set on the server (`LockstepServerSettings.StartData`, or
  `LockstepOfflineConfig.startData`) and reaches every client before tick 0. Read it with
  `LockstepSessionInfo.GetStartData<T>()` (map id, game mode, rules).

#### Creating and destroying entities

- Main-thread `EntityManager` calls are deterministic: the structural changes happen in system order.
- Command buffers: `SystemAPI.GetSingleton<LockstepEndSimulationEntityCommandBufferSystem.Singleton>()
  .CreateCommandBuffer(state.WorldUnmanaged)` (or the `Begin` variant for the start of the next tick).
- In parallel jobs, record with a parallel writer and use `[ChunkIndexInQuery]` as the sort key, so playback order
  does not depend on thread timing.
- Actors: spawn the types of the actor catalog by key (`ActorLookup`, see [Actors](#actors)). The registry always
  exists in the simulation world, but it is empty when the presentation world had none when the simulation was
  created: start the session with `waitForActorRegistry` when the catalog is baked into a subscene.
- Placed actors: what a map starts with (buildings, resource nodes, spawn markers) is placed as actors
  (`ActorAuthoring`). The built-in systems copy every `PlacedActor` of the presentation world (with its linked
  entities) into the simulation before tick 0, sorted by an order baked from the object's identity in its scene, so
  every client starts from the same entities, in archetypes created in the same order, whatever order its subscenes
  loaded in. References between placed actors are kept; references to anything else, actor prefabs included, become
  `Entity.Null`, so a placed actor names an actor type by key. The presentation world keeps its own inert copy of the
  baked entity.

#### Referring to entities

`Entity` values differ between clients: Entities hands out entity ids from one store shared by every world of the
process, so the same simulated entity has a different index and version on every machine. Inside one tick on one
machine an `Entity` is fine as a handle (in a lookup, in a component that points at another entity), but it must never
be *data* that affects the result: never sort by it, hash it, seed randomness with it, or send it in a command.

For identities that must match across clients, use `LockstepEntityId`:

```csharp
// Add it when creating the entity (or give the actor an Entity Id part).
state.EntityManager.AddComponent<LockstepEntityId>(unit);

// Ids are assigned at the end of the creation tick, starting at 1 and never reused. A copy made with Instantiate
// gets an id of its own: the id stays with the entity that had it. Disabled entities keep theirs.
// A command carries the id; the simulation resolves it:
var map = SystemAPI.GetSingleton<LockstepEntityIdMap>();
if (map.TryGetEntity(attack.targetId, out var target))
{
    // ...
}
```

#### Randomness

```csharp
ref var random = ref SystemAPI.GetSingletonRW<LockstepRandom>().ValueRW.value;
var damage = random.NextInt(10, 20);
var direction = random.NextDirection2();
```

Use `ref`: the generator is a struct, and a copy would not advance the stored state. Draw numbers in a deterministic
order (main thread, query order). For per-entity streams, store a `FixedRandom` in a component, created with
`FixedRandom.CreateFromIndex(sessionSeed, lockstepEntityId.value)` once the id is assigned (`IsAssigned`, from the
tick after creation): on the creation tick every id is still 0, so all streams would start out identical.

#### Time

`LockstepTime.deltaTime` is `1 / tickRate` as `FixedPoint` (truncated to the fixed-point step). For durations, prefer
integer tick counters (`cooldownTicks = tickRate / 2`), which are exact.

#### Jobs and Burst

- Burst-compiled `ISystem`s and jobs are welcome: `FixedPoint` math is integer math, so Burst produces the same results as
  Mono and IL2CPP.
- `IJobEntity.ScheduleParallel` is deterministic when every entity writes only its own components.
- Avoid anything whose result depends on thread timing: shared accumulators, `Interlocked`, appending to a
  `NativeList.ParallelWriter` and then using the order, parallel hash map writes followed by iteration.
- Every scheduled job is completed at the end of the tick, before the state is hashed or presented.

#### Spatial queries

`LockstepSpatialIndex` answers "what lies near here" without visiting every entity: build it from the positions of a
tick (an array of `FixedVector2`, the index of a point is its place in the array) and query it as often as needed.

```csharp
using (var index = new LockstepSpatialIndex(positions, cellSize: 8, maxCells: 1 << 14, Allocator.Temp))
{
    foreach (var i in index.Query(from.Xz, reach + largestRadius))
    {
        // Every point within the radius comes up, and some further ones: check the distance, pick the nearest and
        // break ties by i, so that the answer does not depend on the cell size.
    }
}
```

- The cells cover the bounding box of the points; when the points are far apart, the cells grow so that there are never
  more than `maxCells`. Building is a counting sort, O(points + cells).
- A query walks the cells a square around the point touches, row by row along X, and the points of a cell by index:
  the same order on every client. Bodies with a size are indexed by a point: widen the query by the largest distance
  from a point to its edge.
- Burst-compatible; dispose it or allocate it with `Allocator.Temp` or the world update allocator.

#### What the simulation must not touch

- `SystemAPI.Time`, `UnityEngine.Time`, `DateTime`, `Stopwatch`.
- `UnityEngine.Random`, `System.Random`.
- `float`, `double`, `Mathf`, `Unity.Mathematics` float types for anything that affects the state.
- Static fields, system fields that carry state between ticks, managed components and managed objects.
- Unity Physics, NavMesh and Animator results: write collisions in `FixedPoint` and move units with the package's
  [navigation](#navigation).
- Anything local: the local player, the camera, devices, the frame rate, the platform. The simulation is the same
  everywhere; local effects belong to the presentation.
- Other worlds. And nothing outside the lockstep systems may write to the simulation world.

### Determinism rules

The package makes the plumbing deterministic; the gameplay code has to keep it that way.

1. **No `float` or `double` in simulation logic.** Use `FixedPoint` and `FixedMath`. Convert authoring values once, at bake time
   (actor parts keep `FixedPoint` fields; a placed actor's transform is converted once), and quantize input on the
   client. Unity Physics and `Unity.Mathematics` float math
   are not deterministic across platforms and compilers.
2. **Time and randomness come from the simulation.** `LockstepTime`, `LockstepRandom` or an `FixedRandom` stored in a
   component.
3. **All state lives in components** of the simulation world. Never in system fields, statics or managed objects, and
   never in anything that depends on the local machine.
4. **Never use `Entity` values as data.** Use `LockstepEntityId` for cross-client identity. Query iteration order is
   deterministic (it follows chunk order, which only depends on the simulation's own structural changes); hash map
   iteration order and anything keyed by `Entity` is not.
5. **Order-independent or explicitly ordered parallel work only.** Sort keys for command buffers in jobs; no atomics
   or shared accumulators.
6. **The same build everywhere.** Clients send `LockstepSimulation.DefaultSimulationHash` (protocol version plus the
   list of simulation systems) when joining, and the server refuses a different build while
   `ValidateSimulationHash` is on. Changing `FixedPoint` math or system order also invalidates old replays.

| Instead of | Use |
|---|---|
| `float`, `double`, `Mathf`, `math.sin` | `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`, `FixedMath.Sin` |
| `SystemAPI.Time.DeltaTime` | `LockstepTime.deltaTime`, or tick counters |
| `UnityEngine.Random`, `System.Random` | `LockstepRandom`, an `FixedRandom` in a component |
| `Entity` in commands, sorting, seeds | `LockstepEntityId` |
| `LocalTransform` in the simulation | `LockstepTransform` |
| Unity Physics queries for gameplay | Your own `FixedPoint` collision code; `LockstepSpatialIndex` finds what is near |
| `Dictionary` or `NativeHashMap` iteration | Iterate a query or a sorted buffer |
| `if (slot == localSlot)` in a system | Nothing: decide locally in the presentation |

### Sessions

#### Offline

```csharp
var config = LockstepOfflineConfig.Create<GameInput>(tickRate: 30);
config.seed = 42;                   // zero picks a random seed
config.checksumInterval = 0;        // nothing to compare against offline
config.waitForActorRegistry = true; // when a subscene bakes the actor catalog
world.EntityManager.CreateSingleton(config);
```

`LockstepOfflineSystem` runs in client and local worlds. It creates an in-process `LockstepServer` and
`LockstepClient` connected through `LockstepLoopbackNetwork`, so offline play runs the whole protocol. Destroy the
singleton to end the session. `LockstepWorlds.TryGetClient(world, ...)` and `TryGetServer(world, ...)` return both
sides.

#### Netcode for Entities

`LockstepNetcodeServerSystem` runs in server worlds while a `LockstepServerConfig` singleton exists, and
`LockstepNetcodeClientSystem` runs in client and thin client worlds while a `LockstepClientConfig` singleton exists.
`LockstepNetcode` creates those singletons and wraps the Netcode calls:

| Call | Effect |
|---|---|
| `HostSession(serverWorld, settings)` | Hosts a session (replaces the config if one exists) |
| `JoinSession(clientWorld, settings, joinData, waitForActorRegistry)` | Joins as soon as the world is connected |
| `Listen(serverWorld, endpoint)`, `Connect(clientWorld, endpoint)` | `NetworkStreamDriver.Listen` / `Connect` |
| `RequestStart(serverWorld)` | Starts the match on the next server update with whoever joined |
| `RequestEnd(serverWorld, reason)` | Ends the match |
| `TryGetServer(serverWorld, out server)`, `TryGetClient(clientWorld, out client)` | The running session objects |

The same can be done with components: create `LockstepServerConfig { settings }` or
`LockstepClientConfig { settings, joinData, waitForActorRegistry }`, create an entity with
`LockstepStartGameRequest` or `LockstepEndGameRequest { reason }`, and read the `LockstepServerStatus` singleton
(`state`, `closedTicks`, `playerCount`) in the server world.

**Bootstrap.** Netcode's automatic bootstrap creates client and server worlds when the game starts (in the editor,
according to the PlayMode Tools). Either use those worlds, or disable the automatic bootstrap with an
`OverrideAutomaticNetcodeBootstrap` component in the scene (or your own `ClientServerBootstrap`) and create worlds on
demand with `ClientServerBootstrap.CreateServerWorld` / `CreateClientWorld`.

**Dedicated server.** Create only a server world (a server build, or `ClientServerBootstrap.CreateServerWorld`),
`HostSession` with `MinPlayersToStart` set to the number of expected players (or call `RequestStart` from your own
lobby logic) and `Listen`. The server never simulates and needs no content, but it compares the clients'
`DefaultSimulationHash` with its own, so build it from the same code as the clients (or turn `ValidateSimulationHash`
off).

**Thin clients.** `LockstepNetcodeClientSystem` also runs in thin client worlds and forces `Simulate` off there: give
them a `LockstepClientConfig` (or call `JoinSession`) and they join without simulating or reporting checksums.
`LockstepInputSystemGroup` does not run in thin client worlds, so they send an empty input unless a system of theirs
(`WorldSystemFilterFlags.ThinClientSimulation`) calls `client.SetInput` or `AddCommand`. Handy for load tests: Netcode's
PlayMode Tools create thin clients (*Num Thin Clients*), and so does `ClientServerBootstrap.CreateThinClientWorld`.

**Background.** Netcode drops connections of a player that stops updating; set `Application.runInBackground = true`
for hosts and clients.

#### Match flow

```
Lobby ──(MinPlayersToStart reached, RequestStart or StartGame)──► Running ──(RequestEnd, EndGame)──► Ended
          countdown of StartDelaySeconds, then tick 0
```

- **Lobby.** Clients that joined wait in `LockstepClientState.Lobby`. Each client creates its simulation world when the
  start message arrives, during the countdown.
- **Running.** The server closes ticks on its clock. `client.StartedEvent` fires when the client enters `Running`.
- **Late join.** With `AllowLateJoin` (default), a client joining a running match receives every frame from tick 0
  and re-simulates them within its catch-up budget (`MaxTicksPerUpdate`, `MaxSimulationMillisecondsPerUpdate`). Its
  player enters the simulation at `client.JoinTick`. `client.SimulatedTicks` against `client.ServerTicks` (how many
  ticks the server has closed, from its start and pong messages) gives the progress for a loading screen. The cost
  grows with the match length.
- **Leaving.** `client.Leave()`, removing the `LockstepClientConfig`, a dropped connection, or a disposed client
  world: the server announces the leave in a frame and the slot becomes free. A new config joins again over the same
  connection.
- **End.** `RequestEnd` / `server.EndGame(reason)` stops closing ticks and tells everybody;
  `client.EndedEvent` fires, `client.EndReason` says why (`Finished`, or `ServerShutdown` when the server config goes
  away) and the client finishes simulating the frames it already has. Ending in the lobby tells the players waiting
  there the same way (they never simulate), and later joins are rejected with `GameEnded`. A client whose connection
  drops ends with `ConnectionLost` (`client.OnDisconnected()`, which `LockstepNetcodeClientSystem` calls) and also
  simulates the frames it has, such as the last ones of a match the server ended before it closed the connections; the
  Netcode session stays registered until its config is removed or a new connection joins again.
- **Rejection.** `client.RejectedEvent` reports `LockstepJoinRejectReason`: `InvalidRequest`, `ProtocolMismatch`,
  `SimulationMismatch` (a different build), `SessionFull`, `GameInProgress` (late join is off), `GameEnded`.

#### Session objects

| `LockstepClient` | |
|---|---|
| `State` | `Idle`, `Joining`, `Lobby`, `Running`, `Ended`, `Rejected` |
| `LocalSlot`, `JoinTick` | The local player's slot (or -1) and the tick it enters the simulation |
| `Config` | The `LockstepSessionConfig` sent by the server |
| `Simulation` | The `LockstepSimulation` (null before the start and on clients that do not simulate) |
| `ConfirmedTicks`, `SimulatedTicks`, `BufferedTicks`, `ServerTicks` | Frame progress; `ServerTicks` is how far the server is, for a loading screen |
| `InterpolationAlpha` | Blend factor for rendering, in `[0, 1]` |
| `PlaybackSpeed` | How fast the playout runs: 1 is real time, 0 holds the picture (replays) |
| `RoundTripTime`, `JitterTicks` | Network measurements |
| `IsDesynced`, `DesyncTick`, `LocalChecksums` | Desync state |
| `EndReason` | Why the match ended, once `State` is `Ended` |
| `StartedEvent`, `DesyncedEvent`, `EndedEvent`, `RejectedEvent` | Events |
| `SetInput`, `AddCommand`, `Join`, `Leave`, `ExportReplay` | Actions |

| `LockstepServer` | |
|---|---|
| `State` | `Lobby`, `Running`, `Ended` |
| `Config`, `Settings` | The session configuration and the server settings |
| `ClosedTicks`, `PlayerCount`, `History` | Progress, players present, every closed frame |
| `VerifiedChecksumCount`, `LateInputCount` | Diagnostics |
| `DesyncDetectedEvent`, `ProtocolViolationEvent` | Events; a violation (malformed data, or a message or commands over the limits) carries the connection id and the reason, and the data is dropped |
| `StartGame`, `EndGame`, `ExportReplay`, `TryGetPlayerSlot`, `IsSlotInUse` | Actions and queries |

| `LockstepReplayHost` | |
|---|---|
| `Replay`, `FrameCount` | The recording it plays |
| `Position`, `ReleasedTicks`, `IsFinished` | The playback clock in ticks, frames released to the clients, whether all are out |
| `Speed`, `IsPaused` | Pace of the clock |
| `FirstMismatchTick`, `MismatchEvent` | A client's state differed from the recording |
| `Watch`, `Update` | Assign a connection the slot it watches; advance the clock and stream frames |

Both servers implement `ILockstepServerEndpoint` — the connection events and packets a transport feeds.

### Presentation

The simulation world is read-only for everything outside the lockstep systems. Presentation code finds it through
`LockstepWorlds.TryGetClient(world, out var client)` (or `TryGetSimulation`) and reads it after the client update.

#### Interpolation

The simulation advances in ticks while the screen renders frames. `client.InterpolationAlpha` is how far the
playout clock is between the previous and the latest simulated tick. Entities with `LockstepTransformPrevious` keep their
start-of-tick transform, and `LockstepTransformExtensions.Interpolate(current, previous, simulation.Tick - 1, alpha)`
returns the blended `LocalTransform`. It returns the current value when the previous transform was not captured on the
latest tick (a newly created entity), so new entities never slide in from the origin.
`simulationManager.TryGetInterpolated(entity, lastTick, alpha, shownSinceTick, out var transform)` reads both
components and applies the rule of the package's views: the current value until the tick after `shownSinceTick` (the
last simulated tick when your view of the entity appeared), because an entity copied from a live one during a tick
carries that entity's capture.

#### Entities Graphics copies: LockstepViewSystem

`LockstepViewSystem` runs in presentation worlds and mirrors the simulation into rendered entities:

For every simulation actor (`Actor`), the view system instantiates the prefab of its type from the actor
registry of the presentation world, adds `LockstepView { simulationEntity }`, writes the interpolated `LocalTransform`
every frame and destroys the view when the actor goes away. It runs in `SimulationSystemGroup`, after the session
system and before `TransformSystemGroup`, so a view and its children are drawn where they are on this frame, a new one
too. `TryGetView(simulationEntity, out view)` finds a view. The
prefab shows only what its parts gave it: a part that adds rendering components (and a Transform part with
*Interpolate*) makes actors drawn this way; most games use GameObject views instead.

#### Reading the simulation directly

Anything else (UI, audio, a minimap) can read components straight from the simulation world. Cache the queries
per simulation world: the world changes when a new session starts.

#### Effects and sounds

Presentation may skip ticks (several ticks can be simulated in one frame) and must never feed anything back. To
trigger one-shot effects, let the simulation record them in its own state with the tick, for example a buffer of
`{ tick, kind, position }` elements trimmed after a second, and let the presentation play the entries newer than the
last tick it handled.

### Timing and latency

What a player sees after pressing a button is roughly:

```
input delay ≈ round trip time
            + (1 + InputMarginTicks + PlayoutDelayTicks + 2 × jitter) / tick rate
            + one rendered frame
```

The `1` is the tick itself: the server closes tick `T` when its clock reaches `T + 1`. `InputMarginTicks` grows
automatically after late inputs, and the playout part is capped by `MaxPlayoutDelayTicks`. At 30 ticks per second and
a 60 ms round trip this is roughly 200 ms, which suits strategy games. Tune it this way:

- **Tick rate.** 15–30 ticks per second is typical for lockstep; 10–20 for RTS. A higher rate shortens the
  quantization delay but costs bandwidth and simulation time. Keep it at or below Netcode's `SimulationTickRate`
  (the server closes ticks inside its update).
- **`InputMarginTicks`** (default 1.5) trades latency for fewer late inputs. Late inputs raise the margin
  automatically and it decays back.
- **`PlayoutDelayTicks`** (default 1) and `MaxPlayoutDelayTicks` (10) trade latency for smoothness on jittery
  connections.
- **`MaxInputWaitTicks`** (default 0) on the server: 0 never waits, so a lagging player's previous input repeats and
  only that player feels the lag. A positive value holds a frame up to that many ticks for missing input: classic
  lockstep, where everybody waits for the slowest player. A player is waited for once one of its inputs arrived in
  time, so a late joiner that is still finding its timing holds nobody up.

Bandwidth: a frame carries a player's record only when something about that player changed, so idle players cost
nothing and an active player costs roughly the input size per tick downstream to every client. Upstream, each client
sends its input once per tick (repeats are a single flag).

### Configuration reference

#### `LockstepServerSettings`

Start from `LockstepServerSettings.Default`. The settings are serializable, so they can be edited in an inspector.

| Setting | Default | Meaning |
|---|---|---|
| `TickRate` | 30 | Simulation ticks per second, 1–1000 |
| `MaxPlayers` | 8 | Up to 64 |
| `InputSize` | 0 | `UnsafeUtility.SizeOf<YourInput>()`, at most 128 bytes |
| `Seed` | 0 | Session seed; 0 picks a random one at start |
| `MinPlayersToStart` | 1 | Start automatically when this many players joined; 0 waits for `StartGame` / `RequestStart` |
| `StartDelaySeconds` | 0.5 | Countdown between the start message and tick 0 |
| `AllowLateJoin` | true | Accept players after the start; they re-simulate the match |
| `ChecksumInterval` | 60 | Ticks between state hashes; 0 disables desync detection |
| `MaxInputWaitTicks` | 0 | How long a frame may wait for a missing input; 0 never waits |
| `MaxInputLeadTicks` | 0 (4 s) | Inputs further ahead are ignored (a broken client clock) |
| `FeedbackIntervalTicks` | 4 | Ticks between timing feedback messages to each client |
| `MaxSendBytesPerUpdate` | 32 KB | Frame bytes per connection per update; limits late-join bursts (0 takes the default) |
| `MaxPacketSize` | 1024 | Largest packet handed to the transport; larger messages are fragmented |
| `MaxMessageSize` | 4 MB | Largest message reassembled from a client; a larger one is dropped as a protocol violation |
| `MaxCommandDataSize` | 1 MB | Largest data of one command; a larger one is a protocol violation |
| `MaxCommandBytesPerTick` | 64 KB | Command bytes of one player per frame; the rest waits for the next ticks, a single larger command goes alone |
| `MaxBufferedCommandBytes` | 4 MB | Command bytes of one player the server holds (sent ahead or waiting); beyond it they are dropped as a protocol violation |
| `ValidateSimulationHash` | true | Refuse clients whose `DefaultSimulationHash` differs |
| `StartData` | empty | Up to 126 bytes, readable in the simulation through `LockstepSessionInfo` |

#### `LockstepClientSettings`

Start from `LockstepClientSettings.Default`.

| Setting | Default | Meaning |
|---|---|---|
| `Simulate` | true | Off for bots and thin clients that only send input |
| `InputMarginTicks` | 1.5 | How early input should reach the server |
| `PlayoutDelayTicks` | 1 | Confirmed frames held in reserve; twice the measured jitter is added |
| `MaxPlayoutDelayTicks` | 10 | Upper bound of the playout delay |
| `MaxTicksPerUpdate` | 60 | Catch-up limit per update |
| `MaxSimulationMillisecondsPerUpdate` | 16 | Catch-up time budget per update; at least one tick always runs |
| `PingIntervalSeconds` | 0.5 | Round-trip measurement interval |
| `RecordReplay` | true | Keep every frame so the match can be exported; a thin client without it keeps no frames at all |
| `StopOnDesync` | false | Stop simulating after a reported desync |
| `MaxPacketSize` | 1024 | Largest packet handed to the transport |

#### `LockstepOfflineConfig`

`Create<TInput>(tickRate)` fills `tickRate`, `maxPlayers = 1` and `inputSize`. The other fields are `seed`,
`checksumInterval`, `startData`, `joinData` and `waitForActorRegistry`.

#### `LockstepSimulationOptions`

Used when you create a `LockstepClient`, `LockstepSimulation` or `LockstepReplayPlayer` yourself.

| Option | Default | Meaning |
|---|---|---|
| `WorldName` | "Lockstep Simulation" | Name of the simulation world |
| `AutoDiscoverSystems` | true | Create every system of `LockstepSimulationSystemGroup` |
| `AdditionalSystems` | none | Extra system types, for example `[DisableAutoCreation]` test systems |
| `Initialize` | none | Runs once before tick 0 (for example to copy content in); must do the same on every client |
| `CanCreate` | none | Gate checked before the world is created; frames keep buffering meanwhile |

`LockstepClientWorldUtility.CreateSimulationOptions(presentationWorld, waitForActorRegistry)` builds the options the
built-in systems use: they copy the actor registry and the placed actors of the presentation world
into the simulation world.

### Desync detection and debugging

Every `ChecksumInterval` ticks each simulating client hashes its simulation world (`LockstepChecksum`) and sends the
hash to the server. The server compares the hashes of a tick by majority and broadcasts the result:

- The server raises `DesyncDetectedEvent` with a `LockstepDesyncReport` (`tick`, `slotMask` of the clients that
  disagree; with no majority, for example two players, every reporter is flagged). A diverged client disagrees on every
  later round too: the server reports a round only when it flags a slot no earlier report did (a new player in the
  slot counts as new). Hashes are taken only for ticks on the `ChecksumInterval`.
- Each client raises `DesyncedEvent(tick, isLocalClientAffected)` and logs an error. With `StopOnDesync` it stops
  simulating.

The hash covers every entity and every unmanaged component of the simulation world. It is independent of padding
bytes, type indices and entity ids (entities are identified by traversal position, and `Entity` fields by the
position of their target). Not hashed: pointers, blob asset references, managed, shared and chunk components,
components on system entities, and anything marked `[LockstepChecksumIgnore]` (a component type or a single field).
Ignored types do not count in the archetype signature either, and neither do the types Entities adds only in the
editor (the `HideInHierarchy` tag, and `EntityGuid`, which the entities of a subscene open for editing keep), so an
editor and a player build hash the same state alike; other tag components do count.
Keep simulation singletons on ordinary entities so they are covered.

**Finding the cause.**

1. Note the tick of the report. *Window > Pragma > Lockstep Sessions* lists the running servers and clients with
   their ticks, round trip, jitter and desync state, and exports replays.
2. Find the component that diverged. All clients received the same frames, so re-simulate the same replay to the
   reported tick on each machine and compare `LockstepChecksum.ComputePerType` (a hash per component type):

   ```csharp
   using (var replay = LockstepReplay.Read(bytes))
   using (var simulation = new LockstepSimulation(replay.Config))
   {
       for (var tick = replay.Frames.FirstTick; tick <= desyncTick; tick++)
       {
           replay.Frames.TryGet(tick, out var frame, out var length); // unsafe: frame is a byte*
           simulation.Step(frame, length);
       }
       foreach (var pair in LockstepChecksum.ComputePerType(simulation.World.EntityManager))
       {
           Debug.Log($"{pair.Key}: {pair.Value:X16}");
       }
   }
   ```

   *Log state hashes* in the debug window prints the same list for a running client. When the simulation spawns
   actors, pass the options the game uses
   (`LockstepClientWorldUtility.CreateSimulationOptions(presentationWorld, false)`) so the registry is copied in.
3. Look for the usual causes in the systems that write that component: float math, `Entity` values used as data,
   hash map iteration, parallel writes, state outside components, local-only branches.
4. Reproduce it in a test: two clients in one process already diverge when the bug depends on `Entity` values or on
   static state (see [Testing a simulation](#testing-a-simulation)). `LockstepReplayPlayer.SimulateToEnd()` returns
   the first tick that no longer matches the recorded checksums.

### Replays

A replay is the session configuration, every confirmed frame and the checksums recorded while playing. Clients record
by default (`RecordReplay`); the server always keeps the frames.

```csharp
File.WriteAllBytes(path, client.ExportReplay()); // or server.ExportReplay(), or the debug window

using (var replay = LockstepReplay.Read(File.ReadAllBytes(path)))
using (var player = new LockstepReplayPlayer(replay))
{
    // Whole match at once, verifying the recorded checksums: -1 when everything matched.
    var firstMismatch = player.SimulateToEnd();
}
```

`replay.Players` lists who played — slot, the ticks the player entered and left, its join data — and
`replay.DurationSeconds` how long the match lasted; both come from the frames, so a replay list can show them without
simulating anything.

#### Watching a replay

To show a replay the way the game shows a match, play it to ordinary clients: `LockstepReplayHost` takes the server's
place, accepts each connection as the player it watches and releases the recorded frames on a playback clock. The
presentation then finds the replay through `LockstepWorlds.TryGetClient` like any session — views, interpolation,
whatever the game reads from `LocalSlot`. `LockstepReplaySession` wires it up for client worlds:

```csharp
var session = new LockstepReplaySession(LockstepReplay.Read(bytes)); // owns the replay
foreach (var player in session.Replay.Players)
{
    var world = ClientServerBootstrap.CreateClientWorld($"Replay {player.slot}");
    session.Watch(world, player.slot, clientSettings, LockstepClientWorldUtility.CreateSimulationOptions(world, true));
}

session.Speed = 2f;          // the host and every watching client
session.IsPaused = true;
session.Update(Time.unscaledTimeAsDouble); // every frame
session.Dispose();           // unregisters the clients
```

- Several worlds can watch different slots of one replay at one pace — switch between them to see the match through
  each player's eyes.
- The clock runs at `Speed` ticks per tick of real time and stands while `IsPaused`. The clients follow at the same
  speed (`LockstepClient.PlaybackSpeed`), so playout stays smooth at any speed; a paused client holds its picture.
- Once every frame is released the host ends the session (`EndedEvent`); the clients finish what they buffer.
- Input and commands of the watching clients are ignored. Their checksums are compared with the recorded ones: a state
  that differs — a replay of another build — raises `LockstepReplayHost.MismatchEvent` and reaches the client as a
  desync (`IsDesynced`), once per connection.
- Without the session, give the host a transport (`LockstepLoopbackNetwork` accepts any `ILockstepServerEndpoint`),
  assign every connection its slot with `Watch(connectionId, slot)` before it joins, and call `Update` every frame. A
  connection without a slot is rejected with `InvalidRequest`.

For checks without a presentation, `LockstepReplayPlayer` simulates the frames directly: call `player.Update(deltaTime)`
every frame (`Speed` scales playback) and present `player.Simulation.World` with `player.InterpolationAlpha`, or
`SimulateToEnd()`. When the simulation spawns actors, create the player with the options the game uses:
`new LockstepReplayPlayer(replay, LockstepClientWorldUtility.CreateSimulationOptions(presentationWorld, false))`.

A replay plays back only with the same build of the simulation: the same systems in the same order, and the same
math.

### Testing a simulation

Determinism bugs are cheapest to find in EditMode tests that run whole sessions in one process. A server and several
clients talk through `LockstepLoopbackNetwork`, which can add latency and jitter, and the test drives the time:

```csharp
[Test]
public void TwoClients_StayInSync()
{
    var settings = LockstepServerSettings.Default;
    settings.InputSize = UnsafeUtility.SizeOf<GameInput>();
    settings.MinPlayersToStart = 2;
    settings.ChecksumInterval = 10;

    var network = new LockstepLoopbackNetwork { Latency = 0.05, Jitter = 0.03 };
    using (var server = new LockstepServer(settings, network.ServerTransport))
    using (var a = new LockstepClient(LockstepClientSettings.Default, network.CreateClientTransport(1)))
    using (var b = new LockstepClient(LockstepClientSettings.Default, network.CreateClientTransport(2)))
    {
        network.AttachServer(server);
        network.AttachClient(1, a);
        network.AttachClient(2, b);
        var desyncs = 0;
        server.DesyncDetectedEvent += _ => desyncs++;
        a.Join();
        b.Join();

        for (var time = 0.0; time < 10.0; time += 0.016)
        {
            a.SetInput(new GameInput { moveX = 100 });
            b.SetInput(new GameInput { moveY = -50 });
            network.Deliver(time);
            a.Update(time);
            b.Update(time);
            network.Deliver(time);
            server.Update(time);
            network.Deliver(time);
        }

        Assert.AreEqual(0, desyncs);
        Assert.Greater(server.VerifiedChecksumCount, 0);
    }
}
```

- Pass `new LockstepSimulationOptions { AutoDiscoverSystems = false, AdditionalSystems = new[] { typeof(MySystem) } }`
  to the clients to test a few systems in isolation. A simulation that spawns actors needs a registry in tests too:
  build one from a catalog with `ActorUtility.CreateRegistry` in a presentation world and pass
  `LockstepClientWorldUtility.CreateSimulationOptions(presentationWorld, false)`.
- Compare `a.LocalChecksums` with `b.LocalChecksums` for a per-tick assertion.
- Keep a replay of a real match as a test asset and assert that `SimulateToEnd()` returns -1: it fails as soon as a
  change alters the simulation, which also tells you that old replays stopped working.

### Custom transports

`LockstepServer` and `LockstepClient` work over any reliable, ordered transport (Steam networking, a relay, WebSockets).
Implement `ILockstepTransport.Send(connectionId, data, length)` and feed the received bytes back:

```csharp
// Server side
var server = new LockstepServer(settings, myServerTransport);
server.OnConnected(connectionId);                     // a peer connected
server.OnPacket(connectionId, data, length, now);    // bytes from that peer
server.OnDisconnected(connectionId);                  // the peer left
server.Update(now);                                   // every frame

// Client side
var client = new LockstepClient(clientSettings, myClientTransport, simulationOptions);
client.Join(joinData);                                // once connected
client.OnPacket(data, length, now);                  // bytes from the server
client.Update(now);                                   // every frame
```

- `now` is a monotonic time in seconds; every call of one side must use the same clock.
- Packets never exceed `MaxPacketSize`; larger messages are fragmented and reassembled by the package.
- The transport must deliver every packet of a connection exactly once and in order (a reliable, ordered channel).
- Register the client with `LockstepWorlds.RegisterClient(presentationWorld, client)` so `LockstepViewSystem` and the
  debug window find it, and unregister it when done.

### Protocol

All messages are little-endian byte strings, versioned by `LockstepProtocol.VERSION`.

| Message | Direction | Contents |
|---|---|---|
| `JoinRequest` | client → server | Protocol version, simulation hash, flags (simulates), join data |
| `JoinAccepted` / `JoinRejected` | server → client | Slot / reason |
| `Start` | server → client | Session configuration, the client's slot and join tick, the server clock position and its closed ticks |
| `Input` | client → server | Inputs of consecutive ticks; a repeat flag instead of unchanged bytes; commands |
| `Frames` | server → client | Consecutive confirmed frames |
| `InputFeedback` | server → client | How early the client's inputs arrive (1/256 tick), late drops |
| `Checksum` | client → server | Tick and state hash |
| `Desync` | server → client | Tick and slot mask |
| `Ping` / `Pong` | client → server / server → client | Round-trip measurement; the pong also carries the server clock position and its closed ticks |
| `Leave` | client → server | The player leaves |
| `End` | server → client | End of the match and its reason |

A frame is a record count followed by one record per player with news: the slot, flags (`Joined`, `Left`, `Input`,
`Commands`) and the matching payloads. A player without a record keeps the previous input. A command is written the
same way in `Input` and in frames: type hash, payload size, payload, data length (var uint), data. The server checks
that form and relays the bytes without decoding them.

### Limits

| Limit | Value |
|---|---|
| Players | 64 |
| Input size | 128 bytes per tick |
| Command payload | 122 bytes, plus data of any length |
| Commands per player per tick | 32; extra ones move to the next ticks |
| Join data / start data | 62 / 126 bytes |
| View key (`EntityViewKey`) | 29 bytes of UTF-8 |
| `FixedPoint` | step 1/65536, range ±1.4·10¹⁴, products below about 2·10⁹ |

## Navigation

`Pragma.Dots.Navigation` walks units around obstacles inside the simulation: a walkability grid on the XZ plane,
with optional ground heights, A* in integer math, string pulling, agents that follow the paths in `FixedPoint`, bodies
larger than the grid's agent radius kept out of gaps they do not fit through, one search for a group of agents sent
together, and optional avoidance that steers walking agents around each other and paths around crowds. NavMesh cannot
drive gameplay in lockstep: it works in floats, is built per platform and answers differently on different machines.

A [module](#turning-modules-on) on top of [Lockstep](#lockstep): turn it on with `PRAGMA_DOTS_NAVIGATION`. Its [actor parts](#parts) are under *Navigation*
in the part menu.

| Type | What it is |
|---|---|
| `LockstepNavGrid` | Singleton: origin, cell size, width, height, agent radius, the steepest walkable slope (`maxSlope`, rise over run) and a version that changes with the cells. The cells are the `LockstepNavCell` buffer of the same entity: the number of obstacles over each cell and its clearance, how many rings of free cells surround it |
| `LockstepNavHeight` | Optional buffer on the grid entity: world Y of the ground at every cell corner (`CornerCount`, row by row along X). Agents walk on it, and cells steeper than `maxSlope` are blocked for good |
| `LockstepNavObstacle` | A rectangle (center, size) in the space of the entity's `LockstepTransform` (yaw and uniform scale apply), or in world space without one. It blocks every cell whose centre is within the agent radius of it; a rectangle thinner than a cell with that radius counts as that thick, so a thin wall still blocks a line of cells |
| `LockstepNavAgent` | Speed, pace, angular speed, the angle beyond which it turns on the spot, stopping distance and body radius of a unit that walks, its destination, the goal of the group it was sent with (`groupGoal`) and status (`Idle`, `Requested`, `Moving`, `Arrived`). The path is its `LockstepNavWaypoint` buffer |
| `LockstepNavVelocity` | What an agent walked on its last step (`value`) and what avoidance picked for this one (`desired`), in units per second on X and Z. An agent with it and a radius takes part in avoidance |
| `LockstepNavAvoidance` | Singleton that turns avoidance on: how many seconds ahead agents look (`timeHorizon`), how far (`neighbourDistance`) and how many neighbours they avoid (`maxNeighbours`), what a standing agent costs a path (`crowdCost`); `Default`. Without it agents plan and walk their paths as if they were alone |
| `LockstepNavSystemGroup` | Inside `LockstepSimulationSystemGroup`: `LockstepNavObstacleSystem` stamps obstacles and keeps the clearance of the cells, `LockstepNavPathSystem` plans and checks paths in parallel, `LockstepNavAvoidanceSystem` picks the velocities of avoiding agents in parallel, `LockstepNavMoveSystem` walks agents, `LockstepNavSeparationSystem` pushes overlapping agents apart |
| `LockstepPathfinder`, `LockstepNavPathMap` | The search: one agent's path (`FindPath`), the search of a group (`SearchGroup`, `GetChain`, `FollowChain`), and the grid as one agent plans on it (its clearance, the crowds and what they cost) |
| `LockstepNavigation` | The grid queries (`IsWalkable`, `IsPassable`, `GetClearance`, `UpdateClearance`, `HasLineOfSight`, `TryFindNearestWalkable`, `TryFindNearestPassable`, `TryStep`, `IsClear`, `Covers`, `GetCoverage`, `Stamp`) and the ground queries (`HasHeights`, `GetHeight`, `ToGround`, `IsSteep`) for systems of your own |

### Setting it up

1. Place an actor for the map (`ActorAuthoring`) with a *Navigation/Grid* part: the size in world units, centred on
   the actor, the cell size and the agent radius. For ground that is not flat, give it the `TerrainData` of the map,
   the world Y of the terrain and the steepest walkable slope in degrees: the terrain is sampled in fixed point at every
   cell corner when the actor is built, and editing it bakes the grid again. Place the terrain so that it covers the
   grid exactly (its corner at the grid's corner, its size the grid's size); Entities Graphics does not draw terrains,
   so the terrain object itself stays in the main scene. Selecting the actor previews the cells the obstacles of the
   open scenes block (red) and the steep ground blocks (yellow).
2. Give obstacles a *Navigation/Obstacle* part. On an actor with a Transform part the rectangle moves and turns with
   it (buildings spawned from the catalog). On an actor without one (a wall placed on the map) the pose of the
   placement is kept, non-uniform scale included.
3. Give units a *Navigation/Agent* part (with a Transform part), with the radius of their body if they should not stand
   inside each other. It adds a `LockstepNavVelocity` too, so they can avoid each other. Set the grid's agent radius to
   that of the common units; larger bodies (vehicles) keep to cells with room for them.
4. For units that steer around each other, add a *Navigation/Avoidance* part to the map's actor, or add the
   `LockstepNavAvoidance` singleton from settings of your own (a match option) before tick 0. Leave it out to turn
   avoidance off: agents then walk exactly as without it.
5. Set destinations from a system that updates before `LockstepNavSystemGroup`; the agent plans its path and takes its
   first step on the same tick. Send the agents of one order with `SetDestination(destination, groupGoal)`, each to its
   own place around the point of the order, so that the whole order costs one search:

```csharp
[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
[UpdateBefore(typeof(LockstepNavSystemGroup))]
public partial struct MoveOrderSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (agent, order) in SystemAPI.Query<RefRW<LockstepNavAgent>, RefRO<MoveOrder>>())
        {
            agent.ValueRW.SetDestination(order.ValueRO.target);
        }
    }
}
```

`LockstepNavAgent.Stop()` stops an agent where it stands. `LockstepNavAgent.pace` slows an agent down to the pace of a
group it walks with, such as a squad that keeps together at the speed of its slowest member; zero walks at its own speed.

To check where a building may go, build its footprint with `LockstepNavObstacleFootprint.Create(obstacle, transform)`
and ask `LockstepNavigation.IsClear(grid, cells, footprint)`: true when every cell it would block is inside the grid and
walkable. Footprints that are not in the grid yet (two buildings placed on one tick) are compared cell by cell with
`GetCoverage` and `Covers`.

### Behaviour

- **Paths.** A* over the eight neighbours of a cell with integer costs (10 straight, 14 diagonal) and an octile
  heuristic. A diagonal step needs both cells beside it free, so paths never cut a blocked corner. String pulling keeps
  a corner only where the straight line is blocked; from each corner it finds the farthest cell it sees by doubling
  the reach and halving the gap. Line of sight is traced exactly through the cell borders, and a segment through a cell
  corner counts both cells beside it.
- **Bodies.** Obstacles block the cells within the grid's agent radius, which fits the common units. A larger body
  needs clearance: `LockstepNavigation.GetClearance` gives one more ring of free cells around the cell for every cell
  size the radius exceeds the agent radius, rounded to the nearest. The cells keep their clearance (the distance in
  rings to the nearest blocked cell or the edge of the grid, `UpdateClearance`), and paths, line of sight, steps,
  pushes and stepping out of obstacles all keep a large body to the cells with room for it: a vehicle goes round
  through a wide gap where infantry squeeze through a narrow one. A large agent that ends up on a cell too narrow for it
  only has to keep to walkable cells on its way out. A weapon that strikes from the centre of a large body needs a
  reach longer than the body and the clearance around it.
- **Groups.** The paths planned on one tick are grouped by the cell of their agent's `groupGoal` and the clearance of
  its body. A group of two or more searches once, back from its goal to all of its agents at once (A* with the octile
  distance to the box around them as the heuristic, so every chain of cells is a shortest one), and each agent follows
  its own chain of cells, then goes on to its own destination; corners its destination does not need are left out. An
  agent the chain does not bring to its destination (a wall between the goal and it) plans alone. Agents sent alone
  (`SetDestination(destination)`) group only with agents sent to the same cell. Paths planned again keep their group.
- **Blocked and unreachable destinations.** A destination in a blocked cell moves to the nearest free cell. When the
  destination cannot be reached at all, the path ends at the reachable cell closest to it and `isPathPartial` is set.
  An agent standing in an obstacle first walks to the nearest free cell. An agent standing still on a cell that becomes
  blocked (a building placed on top of it, a unit spawned or stopped inside one) walks to the nearest free cell by
  itself and arrives there.
- **Changing obstacles.** Obstacles block cells while their entity exists. Spawning, moving, resizing or destroying one
  updates the cells on the next navigation update and changes `LockstepNavGrid.version`; walking agents then check the
  rest of their path and plan again when it is blocked. Partial paths are planned again on every change, since the
  destination may have opened up.
- **Walking.** Agents walk on the XZ plane at `speed`, or at their `pace` when that is slower, keep it through
  corners, turn to face where they walk (`angularSpeed` radians per second; zero turns at once), stop
  `stoppingDistance` before the end of the path and become `Arrived`. An agent with a `turnInPlaceAngle` (a tracked
  vehicle) that faces further than that from where its path leads stands and turns on the spot, and walks on once it
  faces within it: sent behind itself, it turns round first; avoidance picks no velocity for it while it turns. With ground heights, the Y of a walking or pushed
  agent is the ground under it (bilinear inside a cell); without them it stays as it is: the Y of the destination is
  never walked to. Without a grid agents walk straight to the destination.
- **Ground.** `LockstepNavigation.GetHeight` is the height of the ground at any X and Z (the edge of the grid beyond
  it), `ToGround` puts a position on it: use them for whatever you place on the map (spawned units, buildings). A cell
  two neighbouring corners of which differ by more than `maxSlope` times the cell size is steep: the grid blocks it with
  one permanent blocker when it builds its cells, so paths, `IsClear` and stepping out of obstacles all treat steep
  ground as an obstacle. Heights are static: they are read when the cells are built.
- **Avoiding.** With a `LockstepNavAvoidance` in the world, every walking agent with a `LockstepNavVelocity` and a
  radius picks the velocity of its step before it walks: it tries the velocity that walks its path, the one it walked
  last and the path velocity turned up to a right angle either way at full and half speed, and walks the cheapest. A
  velocity costs its distance from the path velocity, an eighth of its distance from the last one (so agents do not
  waver), and the threat of its nearest neighbours: none for a neighbour it would not touch within `timeHorizon`, more
  the sooner it would. A walking neighbour is expected to keep its last velocity and to take half of the avoiding
  (reciprocal velocity obstacles); a standing one is walked around. The threat is capped: walkers make way for each
  other as far as a right angle, a standing agent is passed with an early small turn, and where every way is crowded
  the agent walks on and shoulders its way through, so agents never turn back and never wait. Ties go to the earlier
  velocity, right turns before left ones: two agents walking into each other both turn right and pass. Collisions
  after the end of the path do not count, and agents standing at the end are shouldered aside. An avoiding agent never
  steps onto a cell its body does not fit in, nor through the corner between two blocked cells (it slides along the
  wall, `LockstepNavigation.TryStep`), passes a corner
  of its path when its body covers it, lands exactly on the end of its path, and plans its path again when it has
  turned so far aside that a wall hides its next waypoint. Without the singleton, or without a radius, agents walk their
  paths straight; one pushed off its path whose way to the next waypoint runs into a wall slides along it and plans
  its path again. `LockstepNavVelocity.value` is what the agent walked on its last step, avoidance or not.
- **Crowds.** With avoidance, a path goes around agents standing in its way: every cell a standing agent's body, grown
  by the grid's agent radius, covers costs `crowdCost` cells more to enter, and straight lines are drawn through
  crowded cells only where the path itself crosses them. A walker goes around an idle squad when the way around is
  shorter, and through it, shouldering it aside, when it is not. Walking agents cost nothing: they move on.
- **Keeping apart.** After they walked, agents closer than the sum of their `radius` are pushed apart by half of the
  overlap per tick (an agent at most by its radius), so a crowd that met on one point spreads out over a few ticks
  instead of jumping. Two walking or two standing agents share the push equally; a walking agent takes three quarters
  of it from a standing one, so it slides past units that hold their place and still makes its way to a destination
  one of them stands on. Two agents walking opposite ways (by their last steps) are only pushed sideways, each to its
  own side: they step aside and pass, and in a passage too narrow to step aside in they squeeze past each other
  instead of blocking it for good. A push never ends on a cell the body does not fit in: it is cut to its X or Z part,
  or dropped. Agents with a zero radius take no part. A pushed agent walks on to its next waypoint from where it
  stands, and agents sent to one point crowd around it.
- **Determinism.** Integer and `FixedPoint` math only, a heap with a strict total order, and paths that depend only on
  the grid, the standing agents, their own agent and the agents of their group, so planning in parallel gives the same
  result on any number of threads; requests and groups are taken in the order of the query, which every client shares.
  Cells count obstacles, so the order obstacles are stamped in does not matter. Burst and Mono find bit-identical paths.
  An agent's push is a sum of pair pushes in exact integer math, so the order agents are visited in does not matter
  either; agents on the very same point split along directions picked by their order in the query, which every client
  shares. Avoiding agents read the positions and last velocities of all agents from a copy taken before any of them
  picked, try velocities in a fixed order and find their neighbours in a spatial hash sorted by cell and query index, so
  they pick in parallel with the same result on any number of threads.

### Cost

A search visits each cell at most once and stops at the destination. Its cost grows with the area between start and
destination; an unreachable destination makes it visit the whole reachable area. The searches of a tick run in
parallel jobs, a group per thread and then a path per thread, each with the scratch memory of its job thread, and
walking agents only re-check their remaining segments when the grid changes. The clearance of the cells is computed
again, in two passes over the grid, whenever an obstacle changes. Keeping apart sorts the agents with a radius into a
spatial hash once per tick and compares each with the agents of the nine hash cells around it. Avoidance does the same
with cells of the neighbour distance, then, in a parallel Burst job, scores about twenty velocities against at most
`maxNeighbours` neighbours per walking agent: a larger neighbour distance finds more neighbours in a crowd and costs more.

Measured on a 256 by 256 grid of 1 m cells with buildings and a wall with gaps, 1200 agents in eight armies of 150
(`NavigationPerformanceTests`, run by name): a path across the map costs about 0.25 ms alone, the search of a whole
army about 2 ms and a soldier's path along it under 0.01 ms; the tick in which all eight armies get their orders takes
about 8 ms (about 25 ms with every soldier sent alone), and a tick of 1200 agents walking with avoidance about 2 ms.
Send the agents of an order as a group. Pick the cell size from the narrowest gap units must pass: about the agent
radius is a good start.

## Vision

`Pragma.Dots.Vision` keeps what every player slot sees inside the simulation: a grid of cells on the XZ plane, one
plane per slot, filled again on every tick from the entities that see. Vision is simulation state like positions: every
client computes the same planes, so rules may depend on it (orders only on targets the player sees, units that hide
until detected, the AI) and the presentation draws the fog of war from it.

A [module](#turning-modules-on) on top of [Lockstep](#lockstep): turn it on with `PRAGMA_DOTS_VISION`. Its [actor parts](#parts) are under *Vision* in the
part menu.

| Type | What it is |
|---|---|
| `LockstepVisionGrid` | Singleton: origin, cell size, width, height (laid out as a `FixedGrid`, like the navigation grid), the number of planes (`slotCount`, the session's `maxPlayers`) and `isRevealed`, which shows everything to every slot. The cells are the `LockstepVisionCell` buffer of the same entity: plane by plane in slot order, `isVisible` per cell |
| `LockstepVisionSource` | An entity that lets a slot see: the radius of its sight on the XZ plane and the slot it sees for (-1, or any slot outside the session, sees for nobody), around its `LockstepTransform` |
| `LockstepVisionSystem` | Last in `LockstepSimulationSystemGroup`, before the end-of-tick command buffer: sizes the buffer, clears it and stamps every source |
| `LockstepVision` | The queries: `IsVisible` (a point, or a body of some radius, seen by its edge), `IsCellVisible`, `GetPlane` (the cells of one slot, to draw its fog), `HasCells`, `Stamp` |

### Setting it up

1. Give the map's actor a *Vision/Grid* part: the size in world units, centred on the actor, usually the area of the
   navigation grid, and the cell size, which is how fine the edge of the fog is (a few world units).
2. Give whatever sees a *Vision/Source* part (with a Transform part) with its radius. The slot it starts with is -1: set
   `slot` where the actor gets its owner, and keep `radius` current from gameplay if sight can change (research,
   abilities):

```csharp
[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
public partial struct VisionOwnerSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (source, owner) in SystemAPI.Query<RefRW<LockstepVisionSource>, RefRO<Owner>>().WithChangeFilter<Owner>())
        {
            source.ValueRW.slot = owner.ValueRO.slot;
        }
    }
}
```

3. Ask what a slot sees with the grid and its cells:

```csharp
var grid = SystemAPI.GetSingleton<LockstepVisionGrid>();
var cells = SystemAPI.GetSingletonBuffer<LockstepVisionCell>(true).AsNativeArray();
var canAttack = LockstepVision.IsVisible(grid, cells, slot, target.position.Xz, targetRadius);
```

### Behaviour

- **Cells.** A cell is visible to a slot when the circle of one of its sources reaches into the cell, not only over its
  centre: whatever lies within a source's radius lies in a visible cell, so a unit that fights what it sees never
  fights something its player cannot see. A body is seen as soon as its edge is (`IsVisible` with a radius). Nothing
  outside the grid is visible.
- **Timing.** The planes show the state the tick ended with: the presentation draws them after the tick, and the
  commands of the next tick, which the players issued looking at them, are checked against them.
- **Revealing.** `isRevealed` makes every query answer true for every slot (a match option, a debug switch, the end of
  the match); the planes are still filled, so turning it off again shows the fog at once.
- **Allies.** Each source sees for one slot; players that share vision are asked together (visible to any of their
  slots).
- **Determinism.** Distances are compared on raw fixed-point values. Stamping only marks cells, so the order sources are
  visited in does not matter. The planes are hashed with the rest of the state.

### Cost

Each tick clears `slotCount` planes and stamps every source over the square around its circle: about
(2 × radius / cell size)² cells per source. A 256 × 256 map with 2-unit cells and two slots holds 32 768 cells.

## Stats

`Pragma.Dots.Stats` keeps the numbers of entities in `FixedPoint`: *attributes* that upgrades, research, abilities
and auras change (max health, speed, damage, range) and *resources* that gameplay spends and refills (health, morale,
energy, a player's money). One system applies the modifiers, the grants an entity inherits and the one-tick changes, and
recalculates only what changed.

A [module](#turning-modules-on) on top of [Lockstep](#lockstep): turn it on with `PRAGMA_DOTS_STATS`.

| Type | What it is |
|---|---|
| `LockstepStat` | Buffer element: one stat of the entity. Its `type` (an id the game defines, usually an enum value; 0 is `NONE`), `kind` (`Attribute` or `Resource`) and `value`; an attribute's `baseValue`; a resource's `cap` (the attribute that caps it), `capPolicy` and `max` (the cap it was fitted to). Made with `LockstepStat.Attribute` and `LockstepStat.Resource` |
| `LockstepStatModifier` | Buffer element: a change to an attribute. Its `stat`, `type` (`Flat`, `AdditivePercent`, `MultiplicativePercent`), `value`, `stacking` (`Stack` or `Strongest`), `source` and `endTick` (`PERMANENT` by default) |
| `LockstepStatChange` | Buffer element: a one-tick change of a resource (`stat`, `amount`, `source`): damage, healing, income, a payment |
| `LockstepStatGrant` | Buffer element of an entity that gives modifiers to the entities inheriting from it: the `modifier` and the `target` a receiver needs (`ANY` for every receiver) |
| `LockstepStatGrantor` | Buffer element: an entity whose grants this one receives (its player, its squad, the unit carrying it) |
| `LockstepStatTarget` | Buffer element: a group the entity belongs to for grants (its type, its category) |
| `LockstepStatSource` | What applied a modifier, a grant or a change: two numbers the game picks (`kind`, `id`) |
| `LockstepStatSystem` | In `LockstepSimulationSystemGroup`: removes expired modifiers and grants, recalculates the attributes whose base values, modifiers or grants changed, fits resources to their caps and applies the tick's changes |
| `LockstepStats` | Extensions on the stat buffer, also taking the game's enum: `TryGet`, `TryGetValue`, `TryGetPendingValue`, `TryGetBase`, `TrySetBase`; `RemoveModifiers`, `RemoveGrants`, `Calculate` (the formula, for tooltips and previews), `Id` |
| `StatsPart<TStat, TTarget>` | Base of the game's actor part with the stats of an actor: attributes (stat, base value), resources (stat, the attribute that caps it, its policy, the amount of an uncapped one), the targets grants reach it as, whether it receives grants. Mergeable |
| `StatGrantsPart<TStat, TTarget>` | Base of the game's actor part with the grants an actor gives: a source and grants (stat, modifier type, value, stacking, target). Mergeable |

### Setting it up

Name the stats with an enum of your own (and the groups grants target with another, or use `NoStatTarget`), and
declare the two [actor parts](#parts) closed over them:

```csharp
public enum StatType { None = 0, MaxHealth = 1, Speed = 2, Health = 3 }
public enum StatTarget { Any = 0, Infantry = 1, Vehicle = 2 }

[Serializable, ActorPartMenu("Stats")]
public sealed class UnitStatsPart : StatsPart<StatType, StatTarget>
{
}

[Serializable, ActorPartMenu("Stat Grants")]
public sealed class UnitGrantsPart : StatGrantsPart<StatType, StatTarget>
{
}
```

- **Stats.** Give units and buildings a *Stats* part: attributes (*Max Health* 100, *Speed* 5), resources (*Health*
  capped by *Max Health*, keeping its ratio; it starts full, or with its *Amount* when it has one, such as a building
  under construction; an uncapped one such as money starts with its amount),
  the targets grants reach it as (*Infantry*) and whether it receives grants. Besides `LockstepStat` the part adds
  the buffers the actor takes part in: `LockstepStatModifier` with any attribute, `LockstepStatChange` with any
  resource, `LockstepStatTarget` with any target, `LockstepStatGrantor` when it receives grants (gameplay names the
  grantors, its player or squad, where the unit is created). It warns about a stat listed twice and a resource
  capped by a stat that is not an attribute of the actor.
- **Grants.** Give the entities that grant (a squad, a commander, a banner) a *Stat Grants* part: a source and the
  grants, each a modifier (stat, type, value, stacking) and the target it reaches (zero for every receiver). They
  are permanent; gameplay ends them with `RemoveGrants(source)`.
- **Variants.** Both parts merge: the *Stats* of a veteran derived from the rifleman lists only *Max Health* 150, and
  a placed building on one map only its own *Health* cap; everything else, and every later change of the parent,
  comes from the parent. A stat listed as a resource where the parent has an attribute becomes a resource, targets
  add up, and the other settings are the override's.
- **In code.** An entity built by hand gets the same buffers: `LockstepStat.Attribute`, `LockstepStat.Resource`, then
  the empty buffers; gameplay fills them without structural changes.

Change stats in systems that update before `LockstepStatSystem` and read them in systems after it, and both happen on
the same tick:

```csharp
[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
[UpdateBefore(typeof(LockstepStatSystem))]
public partial struct SprintSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var time = SystemAPI.GetSingleton<LockstepTime>();
        var source = new LockstepStatSource((uint)EffectKind.Sprint, 0);
        foreach (var modifiers in SystemAPI.Query<DynamicBuffer<LockstepStatModifier>>().WithAll<SprintRequest>())
        {
            // Applied again, the effect is refreshed instead of stacking.
            modifiers.RemoveModifiers(source);
            modifiers.Add(LockstepStatModifier.AdditivePercent(StatType.Speed, FixedPoint.Half, source, time.tick + 5 * time.tickRate));
        }
    }
}

// Damage, from a system before LockstepStatSystem: a one-tick change of the target's health.
changes.Add(LockstepStatChange.Create(StatType.Health, -damage, new LockstepStatSource((uint)EffectKind.Shot, attackerId)));

// Research: one grant on the player's entity reaches all of its infantry, also the units made later...
grants.Add(LockstepStatGrant.Create(LockstepStatModifier.AdditivePercent(StatType.MaxHealth, FixedPoint.Half, research), StatTarget.Infantry));
// ...because every unit of the player names that entity where the unit is created.
grantors.Add(new LockstepStatGrantor(playerEntity));

[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
[UpdateAfter(typeof(LockstepStatSystem))]
[UpdateBefore(typeof(LockstepNavSystemGroup))]
public partial struct SpeedStatSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        new ApplyJob().ScheduleParallel();
    }

    [WithChangeFilter(typeof(LockstepStat))]
    private partial struct ApplyJob : IJobEntity
    {
        // Read with `in`: writing the stats would mark them changed and make LockstepStatSystem recalculate them every tick.
        private void Execute(ref LockstepNavAgent agent, in DynamicBuffer<LockstepStat> stats)
        {
            if (stats.TryGetValue(StatType.Speed, out var speed))
            {
                agent.speed = speed;
            }
        }
    }
}
```

### Behaviour

- **Attributes.** Over the entity's own modifiers and the grants it receives,
  `value = (base + sum of Flat) * max(0, 1 + sum of AdditivePercent) * product of max(0, 1 + MultiplicativePercent)`.
  `AdditivePercent` values are shares that add up (0.25 and 0.15 make +40 %); `MultiplicativePercent` ones stack on each
  other (0.5 and 0.5 make 2.25 times, -0.5 halves). The percentage factors stop at zero: -100 % or less makes the
  attribute zero, and two factors below zero never cancel out into a buff. The flat part may go below zero (negative
  armor or regeneration). A modifier of a resource, or of a stat the entity does not have, changes nothing.
- **Non-stacking modifiers.** Of the `Strongest` modifiers of an attribute with the same type and source `kind`, only
  the one with the largest absolute value applies (the first one on a tie), own modifiers and grants alike: two leaders
  with the same aura give its bonus once.
- **Resources.** The changes of a resource on one tick are summed, then the amount is kept between zero and its cap, so
  the result does not depend on the order the changes were added in. A change of an attribute changes nothing. Until
  `LockstepStatSystem` runs, the buffer lists the tick's changes with their sources, for whatever needs to know who
  dealt the damage.
- **Payments.** `value` is the amount as of the last update, so it does not see the payments already added on this
  tick. Check a price with `stats.TryGetPendingValue(changes, type, out amount)`: the amount the update will leave after
  the changes in the buffer, under the cap as of the last update. Two purchases on one tick then cannot spend the same
  money.
- **Caps.** A resource follows the attribute named in `cap` as that attribute is on the same tick. `KeepRatio` keeps
  the share (80 of 100 becomes 120 of 150, and 80 of 100 again), so a bonus to the maximum neither heals nor wounds;
  `Clamp` keeps the amount and cuts it down to a lower cap; shares of large amounts are computed without overflow. A
  capped resource starts full, under the modifiers of its cap, unless it is created with an amount
  (`LockstepStat.Resource(type, cap, policy, amount)`), which it keeps under the cap. One whose cap went to zero has no
  share left to keep and stays empty when the cap comes back. `max` is the cap it was fitted to, the maximum a bar
  shows (negative before the first update of a resource that starts full). Without its cap attribute a resource is
  uncapped.
- **Grants.** A grant reaches every entity whose `LockstepStatGrantor` buffer names the granting entity and whose
  `LockstepStatTarget`s include the grant's `target` (any entity, for `ANY`). Adding, removing or expiring a grant
  updates every receiver on the same tick, and an entity that starts inheriting later gets the grants in place. A
  grantor that no longer exists is dropped from the buffer, and with it what it gave; one that loses its
  `LockstepStatGrant` buffer stops giving on the same tick. `LockstepStatGrantor` holds an
  `Entity`: a reference inside the simulation world, which the checksum hashes by its target; never sort by it, hash it
  or send it.
- **Timed modifiers and grants.** `endTick` is the tick they are removed on: added on tick T for D ticks, they end on
  T + D and apply from T to T + D - 1. Durations are ticks: `seconds * time.tickRate`.
- **Sources.** `RemoveModifiers` and `RemoveGrants` remove everything of a source and keep the order of the rest.
  Removing a source before adding its modifiers again refreshes an effect instead of stacking it.
- **Base values.** `TrySetBase` changes an attribute's base value (a level up); the value follows in the next stat
  update.
- **Enums.** The factories and the read and write helpers take the game's enum and store its value
  (`LockstepStats.Id`), in Burst too.
- **When values change.** `LockstepStatSystem` decides per chunk, by change versions, whether to recalculate: its stat,
  modifier, grantor and target buffers, the grants in the chunks of its grantors, pending changes and expiring
  modifiers. It writes the stats only then, so systems that follow a stat can use a change filter on `LockstepStat`. A
  recalculation gives the same values however often it runs, so the state never depends on the versions themselves.
- **Determinism.** Integer and `FixedPoint` math only; chunks are processed in parallel and each writes only its own
  entities.

## Views

`Pragma.Dots.Views` adapts ECV (Entity Component View) to lockstep: GameObject prefabs show simulation entities,
with one MonoBehaviour *part* per component type that receives the component when it changes. Use it for what Entities
Graphics does not draw well: animators, VFX, health bars, selection rings, world-space UI.

A [module](#turning-modules-on) on top of [Lockstep](#lockstep): turn it on with `PRAGMA_DOTS_VIEWS`.
`EntityViewConfigAuthoring` is in `Pragma.Dots.Views.Authoring`, the actor view previews in `Pragma.Dots.Views.Editor`.

### Setting it up

1. **Give entities a key.** `EntityViewKey` (a `FixedString32Bytes`, 1 to 29 bytes of UTF-8) names the view that shows
   an entity. It is simulation data like any other: give an actor a View part (the key is the actor's own unless the
   part names another, and the editor previews the actor with that view), or set it where the entity is created.
   Changing it swaps the view.

   ```csharp
   var knight = state.EntityManager.CreateEntity(_knightArchetype); // the archetype includes EntityViewKey
   state.EntityManager.SetComponentData(knight, new EntityViewKey("Knight"));
   ```

2. **Make the view prefab.** Put `EntityView` on the root. Add `TransformComponentView` where the view should follow
   the interpolated `LockstepTransform`, and one part per component type to show:

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

   `EntityComponentView<T>.UpdateData` runs whenever the chunk holding the entity's `T` was written, so it may repeat
   a value; `EntityComponentViewUnmanaged<T>` compares the bytes, calls `OnUpdateData` only on a change and forgets the
   cached value when a pooled view is bound to another entity. Several parts may show the same type. A part reaches its
   root through `View`: `View.Entity`, `View.Client.LocalSlot`.

   A dynamic buffer (the stats, a production queue) is shown by an `EntityBufferView<T>` part. Its
   `UpdateData(DynamicBuffer<T>)` gets the buffer itself, read-only and valid only during the call; it runs whenever the
   chunk's buffer was written, so compare what the part shows before redrawing:

   ```csharp
   public sealed class HealthBarView : EntityBufferView<LockstepStat>
   {
       [SerializeField] private Transform _fill;

       public override void UpdateData(DynamicBuffer<LockstepStat> stats)
       {
           if (stats.TryGet(StatType.Health, out var health) && health.max > FixedPoint.Zero)
           {
               _fill.localScale = new Vector3((float)health.value / (float)health.max, 1f, 1f);
           }
       }
   }
   ```

3. **Push the component type.** One empty system per type, in any assembly (its presentation filter keeps it out of
   simulation worlds), and one per buffer element type for buffer parts:

   ```csharp
   public partial class HealthViewUpdateSystem : EntityViewUpdateSystem<Health>
   {
   }

   public partial class StatViewUpdateSystem : EntityBufferViewUpdateSystem<LockstepStat>
   {
   }
   ```

   Override `ConfigureQuery` to narrow the simulation query. Transforms need no system of yours:
   `TransformViewUpdateSystem` interpolates them every frame, and a view shows the current transform until one tick
   after it was bound, so it never slides in from where its entity was created.

4. **List the views in a catalog.** Create an `EntityViewConfig` (*Create > Pragma > DOTS > Entity View Config*)
   and bind each key to a prefab. Bake it with `EntityViewConfigAuthoring` in a subscene loaded by the client (or local)
   world; with Netcode for Entities, dedicated server builds bake nothing. Or register it at runtime with `EntityViewConfigProvider` on a scene
   object (`EntityViewConfigs.Add` in code): a subscene loads only into the worlds that exist when it is enabled, a
   runtime catalog reaches presentation worlds created later. Baked catalogs come first; the first binder of a key wins.

### The view manager

`EntityViewManagerSystem` runs early in `PresentationSystemGroup` (`OrderFirst`), before the update systems, and shows
the session of its world (`LockstepWorlds`). When a tick created, destroyed or re-keyed keyed entities, it compares them
with its views: new entities get a view from the pool, entities that went away or changed their key give theirs back.
Views are returned first, so a released instance serves a new entity in the same frame. Then the systems of
`EntityViewUpdateSystemGroup` push the data: only the chunks whose component (or an enableable component of the query)
changed since the previous push, judged by the change versions of the simulation world, and every value to the views
spawned, attached or forced since then, so a view always starts from the current state. Nothing is written to the
simulation world, not even a cleanup component, so views cannot cause desyncs.

| API | Use |
|---|---|
| `EntityViewManager.TryGet(world, out manager)` | The manager of a presentation world |
| `manager.TryGetView(entity, out view)`, `manager.Views` | Spawned views by simulation entity |
| `manager.Attach(entity, view)`, `manager.Detach(view)` | Show an entity on a view you own, such as a HUD panel; it gets the same data and is never pooled |
| `manager.ForceUpdate(entity)` | Push every value of the entity again at the next update |
| `view.Entity`, `view.Client`, `view.IsBound`, `view.IsAttached` | What a view shows |
| `view.IsAutoUpdateEnabled` | Pause the pushes, transform included; turning it back on catches up |
| `view.TryGetData<T>(out data)`, `view.TryGetBuffer<T>(out buffer)` | Read any component or buffer of the entity from the simulation world, such as its `LockstepEntityId` |
| `view.GetComponentView<T>()`, `view.SetComponentViewEnable<T>(bool)`, `view.UpdateData(data)`, `view.UpdateBuffer(buffer)` | Reach or drive the parts by hand |
| `view.RefreshComponentViews()` | Collect the parts again after adding some at runtime |

`Entity` values work as keys only in this process and session. To send a command about the unit under the cursor,
read its id with `view.TryGetData<LockstepEntityId>(out var id)`. State that is local to one player (selection, hover,
whether a view is hidden by the fog of war) belongs to the presentation: keep it on the views, never in the simulation.
What a player sees is simulation state (see [Vision](#vision)); hiding the views of what it does not see is the
presentation's job.

A pooled instance keeps whatever its parts changed on it: restore that in `BindBreak` (or `Bind`).
`EntityComponentViewUnmanaged<T>` forgets its cached value on `Bind`, and unbinding turns `IsAutoUpdateEnabled` back on.
Parts learn values, not absence: when `T` is removed or disabled the entity leaves the query and the part gets no call,
so show presence with a field (a `bool`) rather than with a tag.

A view goes back to the pool in the frame its entity is destroyed. For a death animation, keep the entity in a dying
state for as many ticks as the animation lasts (a countdown in the simulation, the same on every client), or let a part
leave an effect behind in `BindBreak`. To take the view away from an entity that stays (a unit inside a transport), set
its key to `default`: a key no catalog binds gets no view.

Several presentation worlds can run in one process (the local players of a hot-seat test, a spectator next to a
player), and each one shows its own session. To show one of them, hide the others: `managerSystem.IsShown = false`
returns the views of that world to the pool at once, and a hidden world spawns none; shown again, it spawns the views
of the current state.

### The view pool

Views come from an `IEntityViewPool` with two methods, `Spawn(prefab)` and `Release(view)`; the manager binds and
unbinds the views, the pool only keeps instances. The default `EntityViewPool` keeps released instances inactive under
one root object per world that survives scene loads (`Prewarm` creates instances ahead). Each project can plug in its
own pool:

- `EntityViewManagerSystem.PoolFactory = world => ...` creates the pool of every presentation world. Set it in the
  game's bootstrap; it is cleared when play mode starts. The manager owns what the factory returns: the views go back
  to it when the world is destroyed, and then it is disposed if it is `IDisposable`.
- `managerSystem.Pool = pool` replaces the pool of one world. The spawned views go back to the previous pool first;
  the caller keeps ownership of the new one.

An adapter for com.pragma.pool, whose pools hold `PrefabPoolObject`s (give each view prefab one next to its
`EntityView`):

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

// In the bootstrap, before the presentation worlds spawn views:
EntityViewManagerSystem.PoolFactory = world => new PragmaPoolEntityViewPool(poolService);
```

## State machines

`Pragma.Dots.StateMachines` gives entities state machines whose states are systems: every state ticks the entities in
it with a parallel Burst job and runs its enter and exit logic on the update of a transition. It works in any Entities
world: in a lockstep simulation the machines are deterministic state like any other, outside one they serve any game.

A [module](#turning-modules-on) that needs only the core: turn it on with `PRAGMA_DOTS_STATE_MACHINES`.

| Type | What it is |
|---|---|
| `StateMachine` | One machine: `current`, `previous`, `next` (the requested state) and `ticksInState`. States are numbers the game picks, usually an enum's values (`NONE` = 0). `Create(initial)`, `Is`, `Entered`, `Exited`, `Request`, `HasRequest`, `Id(enum)`; every method also takes the enum |
| `IStateMachine` | A machine component of the game: a struct whose only field is a `StateMachine`. One type per machine, so an entity may run several |
| `StateMachineSystemGroup<TMachine>` | Updates the machines of one component: applies the requested transitions in a parallel Burst job, then runs its systems, the states. The game declares one empty subclass per machine |
| `StateMachinePart<TMachine, TState>` | Base of an actor part that gives the machine, entering its initial state (picked in the inspector) on its first update |

### Setting it up

1. Name the states with an enum, and declare the machine component and its group where the machine should update:

```csharp
public enum SpellState : byte { None = 0, Unavailable = 1, Available = 2, Active = 3 }

public struct SpellMachine : IStateMachine
{
    public StateMachine machine;
}

[UpdateInGroup(typeof(LockstepSimulationSystemGroup))] // SimulationSystemGroup in a game without lockstep
public partial class SpellMachineGroup : StateMachineSystemGroup<SpellMachine>
{
}
```

2. Give entities the machine: an actor part of the game,
   `[Serializable, ActorPartMenu("Spell Machine")] public sealed class SpellMachinePart : StateMachinePart<SpellMachine, SpellState> { }`,
   or `new SpellMachine { machine = StateMachine.Create(SpellState.Unavailable) }` where the entity is created. Keep the
   data of the states (timers, targets) in components of their own: the machine component holds the machine only.
3. Write every state as a system in the group, with a parallel job over the machine's entities:

```csharp
[UpdateInGroup(typeof(SpellMachineGroup))]
[BurstCompile]
public partial struct SpellActiveState : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new Job().ScheduleParallel();
    }

    [BurstCompile]
    private partial struct Job : IJobEntity
    {
        private void Execute(ref SpellMachine spell, ref SpellEffect effect)
        {
            ref var machine = ref spell.machine;
            if (machine.Exited(SpellState.Active))
            {
                effect.isOn = false;
            }
            if (!machine.Is(SpellState.Active))
            {
                return;
            }
            if (machine.Entered(SpellState.Active))
            {
                effect.isOn = true;
            }
            if (machine.ticksInState >= effect.durationTicks)
            {
                machine.Request(SpellState.Unavailable);
            }
        }
    }
}
```

### Behaviour

- **Updates.** Every update of the group applies the requested transitions first, then runs the states in system order.
  A request made by a state is applied on the next update of the machine: the requesting state finishes its own update,
  and the new one is entered and ticked on the next. Requests from systems that update before the group are applied in
  the same update. In a lockstep simulation the group updates once per tick.
- **Transitions.** The last request of an update wins: `Request` replaces an earlier one, and a request of the current
  state (or of `NONE`) cancels a pending one; the current state is never entered again. On the update of a transition `ticksInState` is 0, `Entered(new)` and `Exited(old)`
  are true and `previous` is the old state. A new machine enters its initial state on its first update.
- **Enter and exit.** Both run on the update of the transition, in the order of the state systems: when the exit of
  one state and the enter of the next touch the same data, order the systems (`[UpdateBefore]`), or handle both states
  in one job with a `switch` on `current`.
- **Threads and determinism.** The transitions and the usual state jobs write only the entity's own machine, so any
  number of threads gives the same result, in Burst and in Mono alike. A system that changes the machines of other
  entities (a stun that hits everything in a blast) runs single-threaded, or leaves a component on the target for the
  target's own state to read.
- **Several machines.** An entity has one machine per machine component (a unit's movement and its weapon), each
  updated by its own group.
- **Cost.** The transitions are one parallel pass over the machine's entities per update, finished before the states
  start, since the states are systems inside the group's update. Every state's job visits all of the machine's entities
  and skips those in other states: for a machine with many states over many entities, one job with a `switch` on
  `current` visits them once.

## Package layout

| Path | Contents |
|---|---|
| `Core/Mathematics/Runtime` | `Pragma.Dots.Mathematics`: `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`, `FixedPose`, `FixedMath`, `FixedRandom`, `FixedGrid` |
| `Core/Mathematics/Editor` | `Pragma.Dots.Mathematics.Editor`: fixed-point drawers |
| `Core/Actors/Runtime` | `Pragma.Dots.Actors`: `ActorDefinition`, `ActorCatalog`, `ActorPart` and the generic parts, `ActorAuthoring`, `IActorBuilder` and the run-time builder, `ActorLookup`, `ActorRegistryQuery`, `ActorUtility`; the actor components in `Components` |
| `Core/Actors/Authoring` | `Pragma.Dots.Actors.Authoring`: `ActorCatalogAuthoring`, the actor bakers and the baking builder |
| `Core/Actors/Editor` | `Pragma.Dots.Actors.Editor`: the actor inspectors |
| `Core/Utils/Runtime` | `Pragma.Dots.Utils`: `FixedDictionary4096Bytes`, `OptionalValue`, `EnumId` |
| `Core/Utils/Editor` | `Pragma.Dots.Utils.Editor`: drawers of fixed lists and fixed strings |
| `Core/Utils/Physics` | `Pragma.Dots.Utils.Physics`: `CollisionMatrixToFilter` |
| `Core/Utils/Entities` | `Pragma.Dots.Utils.Entities`: signals and their cleanup, `BufferDictionary`, `EntityManagerExtensions`; `SignalTag` in `Components` |
| `Core/Editor` | `Pragma.Dots.Editor`: the modules page |
| `Modules/Lockstep/Runtime/Protocol` | `LockstepServer`, `LockstepClient`, settings, framing, frame history, replays and `LockstepReplayHost`, loopback network |
| `Modules/Lockstep/Runtime/Simulation` | `LockstepSimulation`, the system group, command buffer systems, frame application, checksums, entity ids |
| `Modules/Lockstep/Runtime/Simulation/Components` | The simulation components and singletons (`LockstepTime`, `LockstepPlayer`, `LockstepCommand`...) |
| `Modules/Lockstep/Runtime/Spatial` | `LockstepSpatialIndex`: points sorted into cells for proximity queries |
| `Modules/Lockstep/Runtime/Transforms` | `LockstepTransform`, `LockstepTransformPrevious`, transform history |
| `Modules/Lockstep/Runtime/Client` | Local input, `LockstepWorlds`, offline mode, `LockstepViewSystem`, `LockstepActorUtility` (actors into the simulation) |
| `Modules/Lockstep/Runtime/Parts` | The Transform and Entity Id actor parts |
| `Modules/Lockstep/Editor` | `Pragma.Dots.Lockstep.Editor`: the debug window |
| `Modules/Lockstep/Netcode` | `Pragma.Dots.Lockstep.Netcode`: the RPC, server and client systems, `LockstepNetcode`; config, status and request components in `Components` |
| `Modules/Navigation` | `Runtime`: `Pragma.Dots.Navigation`, its parts in `Parts` |
| `Modules/Vision` | `Runtime`: `Pragma.Dots.Vision`, its parts in `Parts` |
| `Modules/Stats` | `Runtime`: `Pragma.Dots.Stats`, its parts in `Parts` |
| `Modules/Views` | `Runtime`: `Pragma.Dots.Views`, the `ViewPart`; `Authoring`: `Pragma.Dots.Views.Authoring`; `Editor`: `Pragma.Dots.Views.Editor`, the view previews |
| `Modules/StateMachines` | `Runtime`: `Pragma.Dots.StateMachines`, the actor part base in `Parts` |
| `Skills~` | Claude Code skills (Unity skips folders whose name ends with `~`) |

## Claude Code skills

The package ships [Claude Code](https://docs.claude.com/en/docs/claude-code/overview) skills that teach an agent
how to work with it:

| Skill | Use it for |
|---|---|
| `pragma-dots-actors` | Content as actors in any Entities game: definitions, parts, the catalog and its registry, spawning by key, tags, placed actors, the parts of the modules |
| `pragma-lockstep-gameplay` | Writing simulation code: systems, input, commands, players, `FixedPoint` math, spawning, determinism rules, interpolation |
| `pragma-lockstep-sessions` | Offline, host, join, dedicated servers, settings, match flow, custom transports, replays |
| `pragma-lockstep-desync` | Finding and preventing desyncs, determinism tests |
| `pragma-dots-navigation` | The navigation grid, terrain, obstacles and building placement, agents and group orders, large bodies, avoidance, path queries |
| `pragma-dots-vision` | The vision grid, sources and their owners, visibility checks in rules, revealing, drawing the fog of war |
| `pragma-dots-stats` | Attributes and modifiers, resources and caps, damage and payments, grants, the stat parts, the order around the stat system |
| `pragma-dots-views` | GameObject views: keys, prefabs and parts, update systems, catalogs, picking, lifetimes, pools, editor previews |
| `pragma-dots-state-machines` | Machine components, their groups and state systems, requests and timing, in lockstep and in any world |

The skills live in the package's `Skills~` folder. Claude Code loads project skills from `.claude/skills` in the
project root, so copy them there once after installing (and again after updating the package). A package installed
from git sits in `Library/PackageCache`; for a local `file:` package, copy from its folder instead:

```bash
# bash, from the project root
mkdir -p .claude/skills
cp -r Library/PackageCache/com.pragma.dots@*/Skills~/* .claude/skills/
```

```powershell
# PowerShell, from the project root
New-Item -ItemType Directory -Force .claude/skills | Out-Null
Copy-Item -Recurse -Force (Resolve-Path "Library/PackageCache/com.pragma.dots@*/Skills~/*") .claude/skills/
```

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `LockstepNetcode.TryGetClient` finds no client | The client world is not connected yet (the session starts once Netcode assigned a `NetworkId`): wrong address or port, or the server is not listening |
| The client stays in `Joining` | No `LockstepServerConfig` in the server world: the server drops the packets |
| Rejected with `SimulationMismatch` | The client and the server builds have different simulation systems |
| Rejected with `GameInProgress` | The match runs and `AllowLateJoin` is off |
| "The input set on the client is N bytes but the session input size is M bytes" | `InputSize` does not match the struct written with `LockstepLocalInput.Set` |
| The match never starts | `MinPlayersToStart` is higher than the number of players; call `RequestStart` |
| Nothing moves although the session runs | The gameplay systems are not in `LockstepSimulationSystemGroup`, or no system writes `LockstepLocalInput` |
| Entities Graphics copies do not appear | No actor registry in the presentation world, the entity is not an actor, its type's prefab has no rendering components, or the simulation started before the subscene loaded (`waitForActorRegistry`) |
| An actor type is missing or has no tags | It is not in the catalog the registry was built from, its key is empty or too long, or the catalog uses more than 128 tags (the catalog inspector lists the errors) |
| A placed actor's view does not show in the editor | The Views module is off, the actor has no View part, no `EntityViewConfig` asset binds its key, or previews are turned off (*Tools > Pragma > DOTS > Actor View Previews*) |
| GameObject views do not appear | The entity has no `EntityViewKey`, no catalog binds its key (keys are case-sensitive), or the catalog was baked into a subscene that did not load into this world: register it with `EntityViewConfigProvider` |
| A map object is missing from the simulation | It has no `ActorAuthoring` or no definition, its subscene had not loaded when the session started (`waitForActorRegistry`), or its definition has no valid key |
| A GameObject view misses a component | No `EntityViewUpdateSystem<T>` subclass for that type, or the part sits under a nested `EntityView` |
| A GameObject view does not move | No `TransformComponentView` on it, or the entity has no `LockstepTransform`; without `LockstepTransformPrevious` it moves in steps |
| Agents do not move | The agent has no `LockstepTransform` or `LockstepNavWaypoint` buffer, its speed is zero, or the destination was set after `LockstepNavSystemGroup` updated (it starts on the next tick) |
| Agents walk through an obstacle | The obstacle is not in the simulation world (a static map object must be a placed actor) or lies outside the grid |
| Agents stop short of the destination | `isPathPartial`: the destination is blocked or walled in, and the path ends at the closest reachable point |
| A stat keeps its old value | The modifier names another stat or a resource (resources take `LockstepStatChange`s), the change names an attribute, the receiver lacks the grant's `LockstepStatTarget` or a `LockstepStatGrantor` naming the granting entity, or it was added after `LockstepStatSystem` updated (it applies from the next tick) |
| A capped resource stays empty or ignores its cap | The entity has no attribute of the `cap` type, so the resource is uncapped; list the cap attribute in the same *Stats* part (it warns) |
| `LockstepStatSystem` recalculates every tick | A system writes the stats every tick, also by only taking `DynamicBuffer<LockstepStat>` in a `SystemAPI.Query`, which is read-write: read them with `in` in an `IJobEntity` or with a read-only `BufferLookup` |
| Connections drop when the window loses focus | `Application.runInBackground` is off |
| A "Burst error" in the console | Burst falls back to managed code silently in some cases; fix the reported construct (for example a managed array in a Burst method) |
| A state machine stays in `NONE` | No subclass of `StateMachineSystemGroup<T>` updates its component, or the subclass is not in a group of this world (in a lockstep simulation: `LockstepSimulationSystemGroup`) |
| "The state machine component ... must have one field" | The machine component holds data beside its `StateMachine`: move the data to a component of its own |
| A state's enter or exit logic never runs | It checks `Entered` / `Exited` after returning for entities not in the state, or its system is not in the machine's group |
| Desync reports | See [Desync detection and debugging](#desync-detection-and-debugging) |

## Not included

- **Prediction and rollback.** This is strict lockstep. The isolated world and the state hash are a base for a
  predicted world later.
- **Late join from a snapshot.** Joining re-simulates the match from tick 0, which grows with the match length.
- **Deterministic physics.** Use `FixedPoint` math for collisions. Unity Physics is not deterministic across platforms.
- **Richer navigation.** One grid per world; no cell costs of terrain kinds, no queues in front of passages, and paths
  that go around walking agents only locally.
- **Line of sight.** Vision is a radius on the XZ plane: neither the ground nor obstacles block it.

## License

MIT, see [LICENSE.md](LICENSE.md).
