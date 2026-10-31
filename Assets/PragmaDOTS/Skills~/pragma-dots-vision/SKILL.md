---
name: pragma-dots-vision
description: Keep what every player sees inside a lockstep simulation with the Vision module of Pragma DOTS (package com.pragma.dots, define PRAGMA_DOTS_VISION, assembly Pragma.Dots.Vision) - the vision grid of a map (LockstepVisionGrid, the Vision/Grid part) with one plane per player slot, vision sources (LockstepVisionSource, the Vision/Source part) whose slot and radius gameplay keeps current, LockstepVisionSystem, the LockstepVision queries (IsVisible for points and bodies, IsCellVisible, GetPlane), revealing the map, shared vision of allies, checking commands and AI against vision, and drawing the fog of war (LockstepVisionFog) and hiding views in the presentation. Use it whenever the task involves fog of war, line of sight between players and units, scouting, stealth or hidden units, "attack only what you see", a minimap that hides enemies, or reveal-map options - even if the user only says "fog", "visibility" or "the enemy should not see my base".
---

# Vision in Pragma DOTS

What each player slot sees is simulation state, like positions: on every tick a grid of cells on the XZ plane is filled
again, one plane per slot, from the entities that see. Every client computes the same planes, so rules may depend on
them (orders only on visible targets, the AI, detection) and the presentation draws the fog of war from them. Fog that
is only drawn, with rules that ignore it, lets a modified client act on what its player cannot see; fog that is
computed only on the local machine desyncs as soon as a rule reads it.

The Vision module is opt-in: define `PRAGMA_DOTS_VISION` (turn on *Vision* in *Project Settings > Pragma > DOTS*, which
also turns on Lockstep, which it needs), namespace and assembly `Pragma.Dots.Vision`, actor parts under *Vision*.
Vision code in the simulation follows the rules of the `pragma-lockstep-gameplay` skill.

Deeper reference: the package README, section *Vision* (`Assets/PragmaDOTS/README.md` in the package repository, or
`Library/PackageCache/com.pragma.dots@*/README.md` in a project that installed it); `pragma-dots-actors` for parts,
`pragma-dots-stats` for a sight stat, `pragma-dots-views` for the views the fog hides.

## The pieces

| Piece | What it is |
|---|---|
| `LockstepVisionGrid` | Singleton: `origin`, `cellSize`, `width`, `height` (a `FixedGrid`, `Layout`), `slotCount` (planes: the session's `maxPlayers`, set by the system), `isRevealed` (every query answers true) |
| `LockstepVisionCell` | Buffer on the grid entity: `bool isVisible`, plane by plane in slot order, each row by row along X (slot `s`, cell `i` at `s * CellCount + i`) |
| `LockstepVisionSource` | On whatever sees: `FixedPoint radius` (on XZ, up to `LockstepVision.MAX_RADIUS` = 30 000; negative sees nothing), `int slot` (outside the session's slots, -1 included, sees for nobody). Needs `LockstepTransform` |
| `LockstepVisionSystem` | `OrderLast` in `LockstepSimulationSystemGroup`, before the end-of-tick command buffer: sizes the buffer, clears it, stamps every source |
| `LockstepVision` | Burst-compatible queries: `IsVisible(grid, cells, slot, xz[, radius])`, `IsCellVisible(grid, cells, slot, cell)`, `GetPlane(grid, cells, slot)`, `HasCells(grid, cells)`, `Stamp(grid, cells, slot, xz, radius)` |
| `LockstepVisionFog` | Presentation: the fog of a slot as an R8 texture, a texel per cell, smoothed and fading; `TryUpdate(grid, cells, slot, tick, deltaTime)`, `Texture`, `Area`, `Visibility`, `Version`, `IsSettled`, `FadeSeconds`, `Restart()`, `Dispose()` |
| *Vision/Grid* (`VisionGridPart`) | `Size` (100 x 100, centred on the placement), `CellSize` (2, at least 0.05): the grid and an empty cell buffer |
| *Vision/Source* (`VisionSourcePart`) | `Radius` (20): a source with slot -1. Needs a Transform part |

## Setting it up

1. Give the map's placed actor a *Vision/Grid* part, usually over the area of the navigation grid. The cell size is how
   fine the edge of the fog is: a few world units are plenty, and smaller cells cost more on every tick. A grid can also
   be created in code before tick 0. Without a grid there is no fog.
2. Give units and buildings a *Vision/Source* part. It starts with slot -1, seeing for nobody: set `slot` where the actor
   gets its owner, and keep it current when it changes hands.

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

3. Sight that research or abilities change is a stat: copy it into `radius` with a job filtered on `LockstepStat`
   changes, read with `in`, as the `pragma-dots-stats` skill shows for speed.

## Asking what a slot sees

```csharp
var grid = SystemAPI.GetSingleton<LockstepVisionGrid>();
var cells = SystemAPI.GetSingletonBuffer<LockstepVisionCell>(true).AsNativeArray();
var canAttack = LockstepVision.IsVisible(grid, cells, player.slot, target.position.Xz, targetRadius);
```

- A cell is visible when a circle of sight reaches into it, not only over its centre: whatever lies within a source's
  radius lies in a visible cell, so a unit that fights what it sees never fights what its player cannot see. Pass the
  body radius: a building is seen as soon as its edge is.
- Nothing outside the grid is visible unless the grid is revealed.
- **Timing.** The system runs last in the tick, so the planes show the state the tick ended with. Every system of the
  next tick, commands included, reads those planes: validate a command against them ("attack only what you see"). A
  source created through the end-of-tick command buffer sees from the next tick. On the first tick, before the system
  ran once, the buffer is empty and every query answers false.
- **Allies.** Each source sees for one slot. Players that share vision are asked together:

```csharp
var isSeen = false;
for (var slot = 0; slot < grid.slotCount && !isSeen; slot++)
{
    isSeen = IsAlly(player.slot, slot) && LockstepVision.IsVisible(grid, cells, slot, position.Xz, radius);
}
```

- **Revealing.** `isRevealed` makes every query answer true for every slot (a match option, a debug switch, the end of a
  match); the planes are still filled, so turning it off shows the fog at once. Set it in the simulation, for example on
  tick 0 from the start data:

```csharp
var time = SystemAPI.GetSingleton<LockstepTime>();
if (time.tick == 0)
{
    var options = SystemAPI.GetSingleton<LockstepSessionInfo>().GetStartData<MatchOptions>();
    SystemAPI.GetSingletonRW<LockstepVisionGrid>().ValueRW.isRevealed = options.revealMap != 0;
}
```

- **Explored areas** ("seen before") are not part of the module. If rules need them, keep a per-slot buffer of your own
  in the simulation and OR the planes into it early in each tick; if only the picture needs them, keep them in the
  presentation.
- Rules built on vision (stealth, detection, AI that only knows what its slot sees) read the planes like any other
  simulation state. Never branch on the local slot inside the simulation: every slot's plane is computed on every
  client.

## Fog of war in the presentation

Draw the fog from the local slot's plane and hide the views of what it does not see; never write any of it back to the
simulation world. `LockstepVisionFog` turns a slot's plane into a texture, with soft edges (each cell the share of the
visible cells around it) and a fade in time (`FadeSeconds`), and uploads only when something changed; how it is shaded
(a full-screen pass, a projector, a terrain layer) is the game's, so the module needs no render pipeline:

```csharp
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class FogOfWarSystem : SystemBase
{
    private readonly LockstepVisionFog _fog = new LockstepVisionFog { FadeSeconds = 0.35f };
    private World _simulationWorld;
    private EntityQuery _gridQuery;

    protected override void OnDestroy()
    {
        _fog.Dispose();
    }

    protected override void OnUpdate()
    {
        if (!LockstepWorlds.TryGetClient(World, out var client) || client.Simulation == null || !client.Simulation.World.IsCreated)
        {
            return;
        }
        var simulation = client.Simulation.World;
        if (simulation != _simulationWorld)
        {
            _simulationWorld = simulation;
            _gridQuery = simulation.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LockstepVisionGrid>());
            _fog.Restart();
        }
        if (!_gridQuery.TryGetSingletonEntity<LockstepVisionGrid>(out var gridEntity))
        {
            return;
        }

        var manager = simulation.EntityManager;
        var grid = manager.GetComponentData<LockstepVisionGrid>(gridEntity);
        var cells = manager.GetBuffer<LockstepVisionCell>(gridEntity, true).AsNativeArray();
        if (_fog.TryUpdate(grid, cells, client.LocalSlot, client.Simulation.Tick, UnityEngine.Time.unscaledDeltaTime))
        {
            Shader.SetGlobalTexture(FogTextureId, _fog.Texture);   // the game's shader maps it onto _fog.Area
        }
    }
}
```

- The texture has a texel per cell, row `y` at world Z `origin.y + y * cellSize`: map it onto `fog.Area` (X and Z of the
  corner, width along X, height along Z). `fog.Visibility` and `fog.Version` give a minimap the same picture.
- `Restart()` after showing another slot, or after frames without fog, so the next update shows the plane at once
  instead of fading from the old one. A revealed grid shows everything.
- A client without a slot (`LocalSlot` -1, before it joined) gets no plane: decide what it shows (everything, for a
  spectator).
- Hide the views of entities the local slot does not see (`EntityViewManager.TryGet(World, out var views)`, then for each
  view `view.TryGetData<LockstepTransform>(out var transform)` and `IsVisible` at its position): toggle a child of the
  view that holds its renderers, not the view's own GameObject, which the pool activates and deactivates. Keep the
  hidden state on the view.
- What a slot sees changes on every tick while views get data only when components change: hide views from a
  per-frame system like the one above, not from an `EntityComponentView` part.

## Cost

Each tick clears `slotCount` planes and stamps every source over the square around its circle: about
(2 x radius / cell size)^2 cells per source. A 256 x 256 map with 2-unit cells and two slots holds 32 768 cells.
Large radii on small cells are what costs; raise the cell size before limiting sight.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Nothing is hidden | No grid in the world (no *Vision/Grid* part on the map's actor, or the map's subscene did not load before the simulation started), or `isRevealed` is set |
| Everything is hidden, even own units | Sources keep slot -1: nothing sets `slot` from the owner |
| A query is false on tick 0 | The system has not run yet: the planes appear at the end of the first tick |
| A unit fights an enemy the player cannot see | The rule checks distance only: validate against `IsVisible` with the target's radius |
| Clients disagree after adding fog | The simulation branched on the local slot, or the presentation wrote to the simulation world |
| The fog lags a tick behind units | Expected: the planes are the state the tick ended with; interpolate the drawing, not the rule |
| Sight upgrades do nothing | `radius` is never updated from the stat |

## Before you finish

- [ ] Every source gets the owner's `slot`, and its `radius` follows sight changes.
- [ ] Commands and AI that must respect the fog check `IsVisible`, with the target's radius.
- [ ] The simulation never reads the local slot; the presentation never writes the fog or hidden state back.
- [ ] A session test with latency and jitter shows no desync with vision on (`pragma-lockstep-desync`).
