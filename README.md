# PragmaDOTSLokstep

Development project of **Pragma DOTS** (`com.pragma.dots`): building blocks for multiplayer games on Unity DOTS
(Entities). Fixed-point math, actors and the utilities are always there; deterministic lockstep, navigation, vision,
stats and GameObject views are modules a game turns on.

With the lockstep module every client runs the same simulation in its own isolated Entities world. Only player input
travels over the network: a server orders it into confirmed frames, and every client steps its simulation with exactly
the same frames. The server never simulates, and the bandwidth depends on the number of players, not on the number of
entities.

**Documentation: [Assets/PragmaDOTS/README.md](Assets/PragmaDOTS/README.md).**

## Highlights

- An isolated, deterministic simulation world; gameplay systems opt in through `LockstepSimulationSystemGroup`.
- Fixed-point math (`FixedPoint`, `FixedVector2`, `FixedVector3`, `FixedQuaternion`, `FixedMath`, `FixedRandom`), bit-identical in Mono, IL2CPP and Burst.
- Per-tick input structs, never-dropped commands with data of any length, players as entities.
- A server that orders input and never stalls, adaptive input timing, a jitter-aware playout buffer.
- Desync detection by majority vote with a per-component breakdown, replays (watched through ordinary clients, at any
  speed, from any player's view), late join, offline mode.
- Netcode for Entities integration (host, dedicated server, thin clients), compiled when Netcode is installed, over a
  transport-agnostic protocol.
- Deterministic navigation: a walkability grid with ground heights baked from a terrain (steep slopes blocked), obstacles,
  A* with string pulling in integer math, agents that walk on the ground and re-plan when obstacles change.
- Vision: what every player slot sees, stamped on a grid from vision sources on every tick, the same on every client,
  for the fog of war and for rules that need a visible target.
- Stats: attributes with flat, additive and multiplicative modifiers (timed, non-stacking, removed with their source),
  resources such as health with one-tick damage and healing and an attribute as their cap, bonuses a player or squad
  grants to all of its units; recalculated only where something changed.
- GameObject views adapted from ECV: pooled view prefabs per entity key, per-component parts fed when their component
  changes, interpolated transforms, a pool each project can replace.
- Actors: units, buildings and map objects described in assets (a key, tags, a parent, parts), spawned by key from a
  catalog, placed on maps with overrides, previewed in the editor with their views, baked or built at run time, in any
  Entities world and in the lockstep simulation.
- Modules: lockstep, navigation, vision, stats and views compile only when their scripting define is on
  (*Project Settings > Pragma > DOTS*); navigation, vision, stats and views build on lockstep. Fixed-point math, actors
  and the utilities (`FixedDictionary4096Bytes`, `OptionalValue`, fixed list and string inspectors) are always there.

## Installation

```
https://github.com/PragmaGame/PragmaDOTSLokstep.git?path=Assets/PragmaDOTS
```

Requires Unity 6000.6+ and Entities 6.6; playing lockstep over a network needs Netcode for Entities 6.6. See the
[package documentation](Assets/PragmaDOTS/README.md#installation).

## Repository

Only `Assets/PragmaDOTS` ships; everything else is the development project.

| Path | Contents |
|---|---|
| `Assets/PragmaDOTS` | The package: `Core` (math, actors, utilities), `Modules` (lockstep, navigation, vision, stats, views), Claude Code skills (`Skills~`) |
| `Assets/Examples` | *Lockstep Arena*, a playable sample: offline, host and join ([README](Assets/Examples/README.md)) |
| `Assets/Tests/Core/Editor` | EditMode tests of the core: math, utilities and actors |
| `Assets/Tests/Modules` | EditMode tests of each module, compiled with the module: `Lockstep` (serialization, checksums, simulation, actors in the simulation, spatial index, sessions, replays, presentation, Netcode integration), `Navigation`, `Vision`, `Stats`, `Views`; `Views/Runtime` holds the MonoBehaviours the view tests put on GameObjects (Unity cannot add components from Editor assemblies) |
| [`documentation.md`](documentation.md) | Design notes: architecture, decisions, what was taken from ME.BECS, Photon Quantum and ECV, and why |
| [`mebecs_research.md`](mebecs_research.md) | How ME.BECS implements determinism and networking |
| [`photon_research.md`](photon_research.md) | How Photon Quantum implements determinism and prediction/rollback |
| [`AGENTS.md`](AGENTS.md) | Conventions and workflow for contributors and AI agents |

## Sample

Open `Assets/Examples/Scenes/LockstepArena.unity` and press Play, then choose *Play offline*, *Host* or *Join*.
Move with WASD or the left stick, shoot with Space, dash with Shift, change color with C.

## Tests

Run the EditMode tests in *Window > General > Test Runner > EditMode*, or with the `unity` CLI of
`com.unity.pipeline` while the editor is open:

```bash
unity command run_tests --mode editor
```

This project has every module on; the module suites compile only with their module.

## License

MIT, see [LICENSE.md](LICENSE.md).
