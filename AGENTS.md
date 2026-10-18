# AGENTS.md

Guidance for AI agents (and humans) working in this repository.

## What this is

The development project of the Unity package `com.pragma.dots` (Pragma DOTS): building blocks for multiplayer games on
Entities. The core (fixed-point math, utilities) is always compiled; deterministic lockstep (with a Netcode for Entities
transport), navigation, vision, stats and GameObject views are opt-in modules. The package is `Assets/PragmaDOTS` and is
consumed through a git URL with `?path=Assets/PragmaDOTS`; everything else in the project (tests, the sample, research
notes) is not shipped. Editor version: `ProjectSettings/ProjectVersion.txt` (Unity 6000.6, Entities 6.6, Netcode for Entities 6.6).

The package is unreleased: backward compatibility is not required yet, prefer clean renames over compatibility shims.

## Layout

| Path | Contents |
|------|----------|
| `Assets/PragmaDOTS/Core/Mathematics` | `Pragma.Dots.Mathematics` (`Runtime`): `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`, `FixedMath`, `FixedRandom`, `FixedGrid`. No Entities dependency. `Pragma.Dots.Mathematics.Editor` (`Editor`): fixed-point drawers |
| `Assets/PragmaDOTS/Core/Utils` | `Pragma.Dots.Utils` (`Runtime`): `FixedDictionary4096Bytes`, `OptionalValue`. `Pragma.Dots.Utils.Editor` (`Editor`): fixed list and fixed string drawers. `Pragma.Dots.Utils.Physics` (`Physics`, only with `com.unity.physics`): `CollisionMatrixToFilter` |
| `Assets/PragmaDOTS/Core/Editor` | `Pragma.Dots.Editor`: *Project Settings > Pragma > DOTS* (module defines) |
| `Assets/PragmaDOTS/Modules/Lockstep/Runtime` | `PRAGMA_DOTS_LOCKSTEP`. `Pragma.Dots.Lockstep`, the lockstep assembly. `Protocol`: transport-agnostic `LockstepServer`, `LockstepClient`, settings, framing, frame history, replays, loopback network. `Simulation`: `LockstepSimulation` (the isolated world), the system group, command buffer systems, frame application, checksums, entity ids; components and singletons in `Simulation/Components`. `Transforms`: `LockstepTransform`, history and interpolation. `Client`: local input, `LockstepWorlds`, offline mode, `LockstepViewSystem`. `Actors`: `ActorDefinition`, `ActorCatalog`, `ActorPart` and the core parts (`Parts`), `ActorAuthoring`, the builders, `LockstepActors`, `LockstepActorUtility` (registry building and copying), components in `Actors/Components`. `Spatial`: `LockstepSpatialIndex` |
| `Assets/PragmaDOTS/Modules/Lockstep/Authoring` | `Pragma.Dots.Lockstep.Authoring`: `ActorCatalogAuthoring` and the actor bakers (catalog into registry, `ActorAuthoring` into scene entity) |
| `Assets/PragmaDOTS/Modules/Lockstep/Editor` | `Pragma.Dots.Lockstep.Editor`: the actor inspectors (`Actors`), *Window > Pragma > Lockstep Sessions* |
| `Assets/PragmaDOTS/Modules/Lockstep/Netcode` | `Pragma.Dots.Lockstep.Netcode`, only with `com.unity.netcode`: the RPC and the Netcode server and client systems; config, status and start/end request components in `Components` |
| `Assets/PragmaDOTS/Modules/Navigation` | `PRAGMA_DOTS_NAVIGATION`, needs Lockstep. `Pragma.Dots.Navigation` (`Runtime`): grid, obstacles, agents, `LockstepPathfinder` (A* and string pulling in integer math), the navigation systems and the actor parts (`Parts`) |
| `Assets/PragmaDOTS/Modules/Vision` | `PRAGMA_DOTS_VISION`, needs Lockstep. `Pragma.Dots.Vision` (`Runtime`): the vision grid (one plane per player slot), vision sources, `LockstepVisionSystem`, the `LockstepVision` queries and the actor parts (`Parts`) |
| `Assets/PragmaDOTS/Modules/Stats` | `PRAGMA_DOTS_STATS`, needs Lockstep. `Pragma.Dots.Stats` (`Runtime`): attributes and resources (`LockstepStat`), modifiers, one-tick changes and grants (`LockstepStatModifier`, `LockstepStatChange`, `LockstepStatGrant`, `LockstepStatGrantor`, `LockstepStatTarget`), `LockstepStatSystem`, `LockstepStats` helpers |
| `Assets/PragmaDOTS/Modules/Views` | `PRAGMA_DOTS_VIEWS`, needs Lockstep. `Pragma.Dots.Views` (`Runtime`): GameObject views ported from ECV (DawnOfWar): `EntityView`, view parts, `EntityViewKey`, `EntityViewManagerSystem`, update systems, catalogs, `IEntityViewPool`, `ViewPart`. `Pragma.Dots.Views.Authoring` (`Authoring`): `EntityViewConfigAuthoring`. `Pragma.Dots.Views.Editor` (`Editor`): the actor view previews |
| `Assets/PragmaDOTS/Skills~` | Claude Code skills shipped with the package (gameplay, sessions, desync); Unity skips the folder. Read them before writing simulation code |
| `Assets/Tests/Core/Editor` | `Pragma.Dots.Tests.Editor`: the EditMode suite of the core (not shipped): `Mathematics`, `Utils` |
| `Assets/Tests/Modules/<Module>/Editor` | `Pragma.Dots.<Module>.Tests.Editor`: the suite of a module, compiled only with its define; the lockstep suite (`Pragma.Dots.Lockstep.Tests.Editor`) keeps the session and frame helpers and test components in `Support` |
| `Assets/Tests/Modules/Views/Runtime` | `Pragma.Dots.Views.Tests.Runtime`: MonoBehaviours the view tests add to GameObjects; Unity refuses components from Editor assemblies |
| `Assets/Examples` | *Lockstep Arena* sample (not shipped); its systems run in every simulation world of this project |
| `documentation.md`, `mebecs_research.md`, `photon_research.md` | Design notes and the research they are based on |

## Architecture invariants

- **The server never simulates.** `LockstepServer` orders input: it closes tick `T` when its clock reaches `T + 1`,
  repeats a late player's previous input, moves late commands to the next tick (never drops them) and puts joins and
  leaves into the frames. Anything that needs simulation state on the server is a design change.
- **The simulation world is isolated.** Only the lockstep systems write to it. Systems get in through
  `LockstepSimulationSystemGroup`, whose custom filter bit `LockstepWorldFilter.SIMULATION` (`1 << 28`) they inherit.
  System creation order is sorted (by full name, then `TypeManager.SortSystemTypesInCreationOrder`) and must never
  depend on discovery order.
- **Entity ids are process-global.** Entities 6.x allocates `Entity` index/version from one store shared by every world
  of the process, so the same simulated entity has different ids on different machines (and in two worlds of one
  process). Never hash, sort, serialize or send `Entity` values. The checksum identifies entities by traversal
  position and replaces `Entity` fields with that position; `LockstepEntityId` is the cross-client identity.
- **The checksum** (`LockstepChecksum`) hashes field bytes only (padding, pointers and blob references skipped),
  visits archetypes in creation order and component types by stable type hash, masks enableable bits, and skips
  managed, shared and chunk components, `[LockstepChecksumIgnore]` and the editor-only tags of Entities, in the values
  and in the archetype signature alike (the editor puts shared components on baked entities and hides its world time
  entity, a player build does neither). Any change to it changes every hash and invalidates the checksums stored in
  replays.
- **Math is part of the protocol.** `FixedPoint` and `FixedMath` are integer-only and bit-identical in Mono, IL2CPP and Burst.
  Changing a result (rounding, series, constants) changes `FixedPointTests.GOLDEN_HASH`, breaks old replays and
  desyncs mixed builds; do it only on purpose and record it in the CHANGELOG.
- **Wire format.** Any change to a message bumps `LockstepProtocol.VERSION`; the replay format has its own
  `FORMAT_VERSION`. `LockstepSimulation.DefaultSimulationHash` covers the protocol version and the simulation system
  list, which is what `ValidateSimulationHash` compares.
- **Netcode integration** is one hand-serialized RPC (`LockstepPacketRpc`, 1024 bytes) on the reliable channel; the
  framer fragments larger messages. Connections are tracked by entity, not by `NetworkId`.
- **Modules.** Lockstep, navigation, vision, stats and views compile only with their define (`defineConstraints`), all
  off in a new project; this project turns every one on in Player Settings (through *Project Settings > Pragma > DOTS*).
  Navigation, vision, stats and views require Lockstep and list both defines in their constraints; the settings page
  turns a module on with what it requires and off with what requires it. Otherwise a module never depends on another
  one, so every allowed subset compiles; its actor parts live in its runtime assembly. The core never references a
  module, and no package dependency is optional: Netcode for Entities is not one, and `Pragma.Dots.Lockstep.Netcode`
  compiles only when it is installed (`versionDefines`, like `Pragma.Dots.Utils.Physics`). `Utils` is the lowest
  layer: it references neither the rest of the core nor Entities. A compiled module's systems join every simulation
  world and `DefaultSimulationHash`, which is why modules are opt-in.
- **Actors are the authoring model.** Content is `ActorDefinition` assets with parts (`[SerializeReference]`
  `ActorPart`s); new authoring data is a part, not a MonoBehaviour with a baker. A part's `Build` runs through
  `IActorBuilder` both while a subscene bakes (`BakerActorBuilder`) and at run time (`EntityManagerActorBuilder`), so
  it must be deterministic from its data alone (`FixedPoint` fields; terrains are sampled in fixed point). The registry
  (`LockstepActorRegistry`) is built in the presentation world and copied into the simulation like the scene
  entities; placed actors get their type by key in the copy. Renaming a part class needs `[MovedFrom]`.
- **No managed components** (deprecated in Entities 6.6): running sessions are found through the static
  `LockstepWorlds` registry keyed by `World.SequenceNumber`.
- **The presentation never writes to the simulation world**, not even a tag or a cleanup component: it would move the
  entity to another archetype, change the chunk order, the hash and the `LockstepEntityId` order. GameObject views find
  lifetimes by comparing keyed entities with their views and changes by chunk change versions;
  `EntityViewTests.Views_LeaveTheSimulationUntouched` guards it. Local state (selection, hover) stays on the views.
- **Burst.** Burst-compiled code must not allocate managed arrays (archetypes use `stackalloc ComponentType[] { ... }`).
  Burst falls back to managed code with only a console error, so tests still pass: check the console for
  "Burst error" after touching Burst code. Burst 2.0 also compiled the value 2 of a byte enum nested in a generic
  struct as 0 without any error (`FixedDictionary4096Bytes` uses byte constants for that reason): code with such
  enums needs a test that compares a Burst run with a managed one.

## Code style

The maintainer uses Rider with these rules; match them in every C# file.

- 4 spaces. Braces on their own lines (Allman / BSD) and **always present**, also around single statements of `if`,
  `else`, `for`, `foreach`, `while`, `do`, `using`, `fixed` and `lock`. Stacked `using` / `fixed` headers may share
  one block.
- Explicit access modifiers: `private` on members, `internal` on top-level types that have no other modifier.
- Naming:
  - types, methods, properties: `PascalCase`;
  - private fields: `_camelCase`; private static readonly fields: `PascalCase`;
  - public and internal fields (ECS components, plain data structs): `camelCase`;
  - constants: `UPPER_SNAKE_CASE` (`LockstepProtocol.MAX_PLAYERS`);
  - events: suffix `Event` (`StartedEvent`, `DesyncDetectedEvent`);
  - Unity-serialized fields: `[SerializeField] private T _camelCase`; serializable settings structs use
    `[field: SerializeField] public T Name { get; set; }`.
- **One type per file**, named after the type (partial classes may span `Type.Part.cs` files). Only nested private
  types (and nested types an API pattern requires, like the command buffer `Singleton`s) share a file with their
  parent. ECS components go to the `Components` folders.
- The math API follows the same rules: `FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`,
  `FixedRandom`, `FixedMath` with `Mathf`-like method names (`Sqrt`, `Lerp`, `NormalizeSafe`); vector fields stay
  `x`, `y`, `z`, swizzles are `Xy`/`Xz`.
- `var` for locals; expression bodies only for trivial one-liners.
- C# 9 (Unity): no file-scoped namespaces, no records.
- Comments explain *why*, not what. Public API gets terse XML docs. Code, comments, docs, log messages: English.
- Source files: UTF-8 without BOM.
- Every asset under `Assets` needs a `.meta`: let Unity create it (refresh), never invent GUIDs. Move or rename files
  together with their `.meta`.

## Working with the Unity Editor

The project has `com.unity.pipeline`, which exposes the running editor to the `unity` CLI
(`%LOCALAPPDATA%\Unity\bin\unity.exe`), discovered from the project directory:

```bash
export MSYS_NO_PATHCONV=1                                   # Git Bash mangles /paths in arguments otherwise
unity --no-banner --json command editor_status              # must report "ready"
unity --no-banner --json command menu --path "Assets/Refresh"   # import changes while the editor is unfocused
unity --no-banner --json command recompile                  # then poll recompile_status
unity --no-banner --json command recompile_status           # "completed" / "up_to_date"; errors listed
unity --no-banner --json command console_status             # compile-failure flag, error and warning counts
unity --no-banner --json command console --tail 50 --level error
unity --no-banner --json command --timeout 1500 run_tests --mode editor   # every suite; --filter_type assembly --filter <name> for one
```

`recompile` may find the editor already compiled the change: compare the timestamps of
`Library/ScriptAssemblies/Pragma.Dots*.dll`. If a command hangs, `editor_status` reports a blocking modal dialog.

Without a running editor, run the tests in batch mode from a copy of the project (the open editor locks this one):
`Unity.exe -batchmode -nographics -projectPath <copy> -runTests -testPlatform EditMode -testResults <xml>`.
Copies under long temp paths hit the 260-character limit; map them to a drive letter with `subst`.

## Tests

- The EditMode suites: `Assets/Tests/Core/Editor` (math and utilities) and `Assets/Tests/Modules/<Module>/Editor`
  (each module; the vision suite also needs navigation, its session moves the sources with agents). They
  cover math accuracy and Burst equality, golden results, serialization, actors (inheritance, the catalog and its
  registry, placed actors and overrides, the module parts, a session spawning by key and tag),
  checksums, simulation rules, the spatial index (queries against a brute-force search), navigation (pathfinder, Burst equality, obstacles, agents, keeping agents apart, ground heights and steep cells, a session over a hill), vision (stamping, bodies seen by their edge, revealing, one
  plane per slot, a session), stats (formula,
  non-stacking modifiers, sources, timed modifiers, resources and caps, changes, grants, enum ids in Burst, change
  versions, a session with a late joiner), sessions
  (latency, jitter, late join, disconnects, desync attribution, replays, commands with large data), presentation,
  GameObject views (component and buffer parts), offline mode and a real Netcode server with two clients (including a command larger than the
  reliable window).
- The suites reach internals through `InternalsVisibleTo` in `Modules/Lockstep/Runtime/AssemblyInfo.cs` (and
  `Core/Utils/Editor/AssemblyInfo.cs`); the module suites reach the internal helpers of the lockstep suite through its
  own `AssemblyInfo.cs`. A new test assembly needs its entries there.
- Every test assembly that declares systems has `[assembly: DisableAutoCreation]` in its `AssemblyInfo.cs`: test
  systems enter simulations only through `LockstepSimulationOptions.AdditionalSystems`. In the lockstep `Support`,
  `SessionHarness` runs a server and clients over the loopback network and `FrameBuilder` writes frames by hand in the
  wire format; `ViewHarness` (views suite) is a presentation world with the view systems and a runtime catalog.
- MonoBehaviours a test adds to a GameObject (view parts) go to `Assets/Tests/Modules/Views/Runtime`: `AddComponent`
  refuses scripts from Editor assemblies ("Can't add script behaviour ... because it is an editor script").
- Settings and configs are structs with properties: never mutate a struct-typed property in place
  (`config.StartData.Add(x)` changes a copy and compiles silently); build a local and assign it.
- The Netcode integration test disables `LockstepInputSystemGroup` in its worlds, so the sample's input system does
  not interfere.
- Name tests `Subject_ExpectedBehaviour`. New behaviour gets a test; protocol and simulation changes get a session
  test with latency and jitter.

## Documentation

Keep in sync with code changes: the package README (API, defaults, behaviour), the root README, the CHANGELOG, the
sample README, the skills in `Skills~` (they quote API names, defaults and limits) and `documentation.md`
(design decisions).

## Git

Commit finished work yourself (tests pass, documentation updated); push only when asked. The maintainer prefers
answers in Russian; everything in the repository stays in English.

Commit subjects (the first line of the message) are at most 72 characters: what was done, briefly. Details go
to the body, after a blank line.
