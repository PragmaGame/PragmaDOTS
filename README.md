# PragmaDOTSLokstep

Development project of **Pragma DOTS** (`com.pragma.dots`): building blocks for games on Unity DOTS (Entities), made
of a small core that every game gets and modules that a game turns on. A module that is off is not compiled, so a game
carries and runs only what it uses.

**Documentation: [Assets/PragmaDOTS/README.md](Assets/PragmaDOTS/README.md).**

## What's inside

```
 Navigation    Vision    Stats    Views     modules on top of Lockstep: opt-in, none needs another
 ──────────────────────────────────────
 Lockstep (+ Netcode)    State machines     modules: opt-in, need only the core
 ══════════════════════════════════════
 Mathematics   Actors    Utils              core: always compiled, knows nothing of the modules
```

| Part | Kind | What it gives |
|---|---|---|
| Mathematics | core | Fixed-point math (`FixedPoint`, vectors, quaternion, pose, `FixedMath`, `FixedRandom`), integer only, bit-identical in Mono, IL2CPP and Burst |
| Actors | core | Units, buildings and map objects described in assets of parts (a key, tags, a parent), spawned by key from a catalog, placed on maps with overrides, baked or built at run time, previewed in the editor |
| Utils | core | `FixedDictionary4096Bytes`, `OptionalValue`, signals (one-off events as entities), buffers used as dictionaries, `EntityManager` helpers, fixed list and string inspectors, `CollisionMatrixToFilter` |
| Lockstep | module, `PRAGMA_DOTS_LOCKSTEP` | Deterministic lockstep: every client steps an isolated simulation world with the same confirmed input frames. A server that orders input and never simulates, never-dropped commands, desync detection, replays, late join, offline mode, Netcode for Entities integration |
| Navigation | module, `PRAGMA_DOTS_NAVIGATION` | A walkability grid with terrain heights, obstacles, A* with string pulling in integer math, agents with avoidance |
| Vision | module, `PRAGMA_DOTS_VISION` | What every player slot sees, on a grid stamped every tick, for the fog of war and for the rules |
| Stats | module, `PRAGMA_DOTS_STATS` | Attributes with timed and non-stacking modifiers, resources such as health, bonuses granted to groups |
| Views | module, `PRAGMA_DOTS_VIEWS` | Pooled GameObject views of simulation entities, fed per component when it changes, with interpolated transforms |
| State machines | module, `PRAGMA_DOTS_STATE_MACHINES` | Per-entity state machines whose states tick in parallel Burst jobs, with or without lockstep |

Turn modules on in *Project Settings > Pragma > DOTS*.

## Installation

```
https://github.com/PragmaGame/PragmaDOTS.git?path=Assets/PragmaDOTS
```

Requires Unity 7000.0+ and Entities 6.7; playing lockstep over a network needs Netcode for Entities 7.0+. See the
[package documentation](Assets/PragmaDOTS/README.md#installation).

## Repository

Only `Assets/PragmaDOTS` ships; everything else is the development project.

| Path | Contents |
|---|---|
| `Assets/PragmaDOTS` | The package: `Core` (math, actors, utilities), `Modules` (lockstep, navigation, vision, stats, views, state machines), Claude Code skills (`Skills~`) |
| `Assets/Tests/Core/Editor` | EditMode tests of the core: math, utilities and actors |
| `Assets/Tests/Modules` | EditMode tests of each module, compiled with the module: `Lockstep` (serialization, checksums, simulation, actors in the simulation, spatial index, sessions, replays, presentation, Netcode integration), `Navigation`, `Vision`, `Stats`, `Views`, `StateMachines`; `Views/Runtime` holds the MonoBehaviours the view tests put on GameObjects (Unity cannot add components from Editor assemblies) |
| [`documentation.md`](documentation.md) | Design notes: architecture, decisions, what was taken from ME.BECS, Photon Quantum and ECV, and why |
| [`mebecs_research.md`](mebecs_research.md) | How ME.BECS implements determinism and networking |
| [`photon_research.md`](photon_research.md) | How Photon Quantum implements determinism and prediction/rollback |
| [`AGENTS.md`](AGENTS.md) | Conventions and workflow for contributors and AI agents |

## Tests

Run the EditMode tests in *Window > General > Test Runner > EditMode*, or with the `unity` CLI of
`com.unity.pipeline` while the editor is open:

```bash
unity command run_tests --mode editor
```

This project has every module on; the module suites compile only with their module.

## License

MIT, see [LICENSE.md](LICENSE.md).
