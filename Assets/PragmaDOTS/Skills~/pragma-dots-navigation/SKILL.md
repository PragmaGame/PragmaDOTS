---
name: pragma-dots-navigation
description: Walk units around obstacles inside a lockstep simulation with the Navigation module of Pragma DOTS (package com.pragma.dots, define PRAGMA_DOTS_NAVIGATION, assembly Pragma.Dots.Navigation) - the walkability grid of a map (LockstepNavGrid, the Navigation/Grid part) with terrain heights and steep slopes, obstacles that block cells while their entity exists (buildings, walls), agents (LockstepNavAgent, the Navigation/Agent part) sent alone or as a group with SetDestination, large bodies and clearance, keeping apart and local avoidance (LockstepNavAvoidance), building placement checks (IsClear, footprints), units standing around a building (perimeter places), whether an order can reach its target (LockstepNavReach), the grid as gameplay reads it (LockstepNavGridView), line of sight, nearest walkable cells and LockstepPathfinder for paths outside agents. Use it whenever units walk, follow move orders, get stuck, overlap or jam in passages, when buildings or walls are placed, a terrain is added, or the AI needs paths, reachability or line of sight - even if the user only says "pathfinding", "NavMesh", "move command", "formation" or "units clump together".
---

# Navigation in Pragma DOTS

Units of a lockstep game walk on a walkability grid inside the simulation: A* in integer math with string pulling,
agents that follow the paths in `FixedPoint`, obstacles that block cells while their entity exists, optional ground
heights, keeping apart and avoidance. NavMesh cannot drive lockstep gameplay: it works in floats, is built per platform
and answers differently on different machines. Every client plans the same paths here, bit for bit, in Burst and Mono.

The Navigation module is opt-in: define `PRAGMA_DOTS_NAVIGATION` (turn on *Navigation* in *Project Settings > Pragma >
DOTS*, which also turns on Lockstep, which it needs), namespace and assembly `Pragma.Dots.Navigation`, actor parts under
*Navigation* in the part menu. Navigation code is simulation code: everything in the `pragma-lockstep-gameplay` skill
(fixed-point math, no `Entity` values as data, systems in `LockstepSimulationSystemGroup`) applies.

Deeper reference:

- `references/api.md` - every type, member, part field and default of the module.
- The package README, section *Navigation* (behaviour of paths, groups, avoidance, cost): `Assets/PragmaDOTS/README.md`
  in the package repository, or `Library/PackageCache/com.pragma.dots@*/README.md` in a project that installed it.
- `pragma-dots-actors` for parts and placed actors, `pragma-dots-stats` to drive speed from a stat,
  `pragma-dots-vision` for what a player sees (navigation line of sight is about walls, not about vision).

## The pieces

| Piece | What it is |
|---|---|
| `LockstepNavGrid` | Singleton: origin, cell size, width, height, agent radius, `maxSlope`, `version`. Its cells are the `LockstepNavCell` buffer of the same entity (obstacles over each cell, clearance), its ground the optional `LockstepNavHeight` buffer |
| `LockstepNavObstacle` | A rectangle (center, size) in the space of the entity's `LockstepTransform`: blocks the cells within the grid's agent radius of it while the entity exists |
| `LockstepNavAgent` | A unit that walks: speed, pace, angular speed, turn-in-place angle, stopping distance, body radius, destination, `groupGoal`, `status`, `isPathPartial`. Its path is the `LockstepNavWaypoint` buffer |
| `LockstepNavVelocity` | What an agent walked on its last step (`value`) and what avoidance picked for this one (`desired`) |
| `LockstepNavAvoidance` | Singleton that turns avoidance on; without it agents walk their paths as if alone |
| `LockstepNavSystemGroup` | In `LockstepSimulationSystemGroup`: obstacles, paths, avoidance, walking, keeping apart |
| `LockstepNavigation` | Grid and ground queries for systems of your own |
| `LockstepNavGridView` | The grid, its cells and heights in one value for gameplay; open ground without a grid or before its cells are built |
| `LockstepNavObstacleFootprint` | The world rectangle of an obstacle: nearest point, distance, the walk around it (places at a building) |
| `LockstepNavReach` | Whether an order's target can be reached at all (the path is `Complete`) |
| `LockstepPathfinder`, `LockstepNavPathMap` | The search itself, for paths outside agents (AI, previews) |

| Part (menu) | Put it on |
|---|---|
| *Navigation/Grid* (`NavGridPart`) | The map's placed actor: size centred on it, cell size, agent radius, terrain, its world Y, steepest walkable slope |
| *Navigation/Obstacle* (`NavObstaclePart`) | Buildings, walls, rocks: a box on the actor's local X and Z |
| *Navigation/Agent* (`NavAgentPart`) | Units (with a Transform part): speed, angular speed and turn-in-place angle in degrees, stopping distance, radius |
| *Navigation/Avoidance* (`NavAvoidancePart`) | The map's actor, to turn avoidance on |

## Setting up a map

1. **Grid.** Give the map's placed actor (`ActorAuthoring` in the map's subscene) a *Navigation/Grid* part. Size in
   world units, centred on the actor. Cell size from the narrowest gap units must pass: about the agent radius is a good
   start (smaller cells follow obstacles closer and cost more to search). Agent radius: that of the common units;
   obstacles are grown by it.
2. **Ground** (hilly maps). Give the part the `TerrainData`, the world Y of the terrain object and the steepest walkable
   slope in degrees (35 by default, up to 89; 0 walks every slope). The terrain is sampled in fixed point at every cell
   corner when the actor is built; it must cover the grid exactly (its corner at the grid's corner, its size the grid's
   size), or a warning says its heights are stretched. Entities Graphics does not draw terrains: the terrain object stays
   in the main scene. Steep cells are blocked for good.
3. **Obstacles.** *Navigation/Obstacle* on buildings and walls. With a Transform part the box moves and turns with the
   actor (buildings spawned from the catalog; yaw and uniform scale apply). Without one (a wall placed on the map) the
   pose of the placement is kept, non-uniform scale included.
4. **Agents.** *Navigation/Agent* on units, with a Transform part. Give them the `radius` of their body, or they stand
   inside each other and avoid nothing. Larger bodies (vehicles) than the grid's agent radius keep to cells with room
   for them.
5. **Avoidance** (optional). *Navigation/Avoidance* on the map's actor, or add `LockstepNavAvoidance.Default` from your
   match settings before tick 0. Leave it out and agents walk exactly as without it.

Selecting the map's actor previews the cells the obstacles of the open scenes block (red) and steep ground blocks
(yellow). A grid can also be created in code before tick 0; `LockstepNavObstacleSystem` sizes its cell buffer on the
first navigation update, so the cells are empty until then.

## Moving agents

Set destinations from a system that updates **before** `LockstepNavSystemGroup`: the agent plans its path and takes its
first step on the same tick. Send the agents of one order as a group: each to its own place around the point of the
order, with the point as `groupGoal`, so the whole order costs one search.

```csharp
public struct MoveCommand
{
    public FixedVector3 target;
}

[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
[UpdateBefore(typeof(LockstepNavSystemGroup))]
public partial struct MoveOrderSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ids = SystemAPI.GetSingleton<LockstepEntityIdMap>();
        foreach (var (player, commands, commandData) in
                 SystemAPI.Query<RefRO<LockstepPlayer>, DynamicBuffer<LockstepCommand>, DynamicBuffer<LockstepCommandData>>())
        {
            for (var i = 0; i < commands.Length; i++)
            {
                if (!commands[i].TryGet<MoveCommand>(out var move))
                {
                    continue;
                }
                var unitIds = commands[i].GetData<uint>(commandData);
                for (var u = 0; u < unitIds.Length; u++)
                {
                    // Check the owner as well (a component of the game): a client may send any id.
                    if (!ids.TryGetEntity(unitIds[u], out var unit) || !SystemAPI.HasComponent<LockstepNavAgent>(unit))
                    {
                        continue;
                    }
                    var agent = SystemAPI.GetComponentRW<LockstepNavAgent>(unit);
                    agent.ValueRW.SetDestination(GetPlace(move.target, u, unitIds.Length, 2), move.target);
                }
            }
        }
    }

    // Places on a square around the point of the order, in the order of the ids: the same on every client.
    private static FixedVector3 GetPlace(FixedVector3 center, int index, int count, FixedPoint spacing)
    {
        var side = 1;
        while (side * side < count)
        {
            side++;
        }
        var x = index % side * 2 - (side - 1);
        var z = index / side * 2 - (side - 1);
        return new FixedVector3(center.x + spacing * x / 2, center.y, center.z + spacing * z / 2);
    }
}
```

- `SetDestination(destination)` sends an agent alone; it groups only with agents sent to the same cell. Spread the
  destinations of a group: agents sent to one point crowd around it and push each other.
- `status`: `Idle`, `Requested` (planned in the next navigation update), `Moving`, `Arrived`. `isPathPartial`: the
  destination cannot be reached and the path ends at the reachable cell closest to it. A destination in a blocked cell
  moves to the nearest free cell. React to arrival in a system after `LockstepNavSystemGroup`.
- `Stop()` stops an agent where it stands; `Replan()` plans again to the same destination with the same group.
- `pace` keeps an agent to the pace of the group it walks with (the slowest member's speed; 0 walks at its own);
  `WalkSpeed` is what it walks at. Set `speed` from a stat with a change-filtered job (`pragma-dots-stats`).
- **Never write the `LockstepTransform` of a walking agent** yourself (knockback, teleport): call `Stop()` first, then
  move it. Agents walk on X and Z; their Y is the ground (or stays as it is without heights), never the Y of the
  destination.
- `turnInPlaceAngle` (degrees in the part, radians in the component) makes a tracked vehicle stand and turn on the spot
  when its path leads further than that from where it faces; it needs an angular speed.
- Walking agents check the rest of their path when the grid changes (`LockstepNavGrid.version`) and plan again when it
  is blocked. An idle or arrived agent whose cell becomes blocked (a building placed on it, a unit spawned inside one)
  walks to the nearest free cell by itself.

## Reading the grid in gameplay

Take a `LockstepNavGridView` instead of fetching the grid, its cells and heights and checking each of them: build its
query in `OnCreate`, get the view in `OnUpdate`. Without a grid, or before its cells are built (the tick the first
player joins on), it is open ground: walkable and reachable everywhere, nothing moves.

```csharp
private EntityQuery _gridQuery;

public void OnCreate(ref SystemState state)
{
    _gridQuery = LockstepNavGridView.CreateQuery(ref state);
}

public void OnUpdate(ref SystemState state)
{
    var grid = LockstepNavGridView.Get(state.EntityManager, _gridQuery);
    var spawn = grid.ToGround(grid.ToPassable(exit, radius)); // a cell the body fits on, on the ground
    var canStand = grid.IsPassable(position, grid.GetClearance(radius));
}
```

The view reads the buffers in place: get it again after a structural change. It is read-only in jobs.

## Ground

With heights, positions depend on the ground. Put whatever you place on the map on it (`grid.ToGround(position)`, or
`LockstepNavigation.ToGround(grid, heights, position)`; unchanged without heights), and compare positions on X and Z
only: `FixedMath.DistanceSquared(a.position.Xz, b.position.Xz) < range * range`.

`GetHeight(grid, heights, xz)` is the height anywhere (bilinear inside a cell, the edge beyond the grid). Heights are
static: they are read when the cells are built.

## Obstacles and building placement

Obstacles block cells while their entity exists: spawning, moving, resizing or destroying one updates the cells on the
next navigation update. Check where a building may go with the footprint it would stamp:

```csharp
var grid = SystemAPI.GetSingleton<LockstepNavGrid>();
var cells = SystemAPI.GetSingletonBuffer<LockstepNavCell>(true).AsNativeArray();
var obstacle = SystemAPI.GetComponent<LockstepNavObstacle>(actors.GetPrefab(buildingType));
var footprint = LockstepNavObstacleFootprint.Create(obstacle, LockstepTransform.FromPositionRotation(place, rotation));
if (LockstepNavigation.IsClear(grid, cells, footprint))
{
    // Spawn it; its cells are blocked from the next navigation update.
}
```

- `IsClear` is true when every cell the footprint would block is inside the grid and walkable; it is false while the
  cell buffer is not sized yet (before the first navigation update).
- Two buildings placed on the same tick are not in the cells yet: compare the new footprint with those accepted on this
  tick through `GetCoverage(grid, footprint, out min, out max)` and `Covers(grid, footprint, cell)`.
- Units standing where a building goes walk out of it by themselves.
- Never stamp the simulation's cells yourself: `LockstepNavigation.Stamp` is for previews and tests. Cells you stamp
  into a copy need `UpdateClearance` afterwards.

## Working at a building

A footprint (`LockstepNavObstacleFootprint.Create(obstacle, transform)`) answers where a unit reaches a building:
`ClosestPoint(position)` (Y kept) and `DistanceSquared(position)` (zero inside). Units that work at it (melee
attackers, builders) should stand around it, each on its own place, instead of crowding at the nearest edge. A place is
an offset along the walk around the rectangle (`Perimeter`, from the local (-X, -Z) corner):

```csharp
// taken: the places already held around this building, the unit's own excluded (gameplay keeps who holds which).
var distance = LockstepNavigation.GetOpenDistance(navGrid, footprint); // or the unit's reach
if (grid.TryFindPerimeterPlace(footprint, unitPosition.Xz, radius, distance, taken, out var offset))
{
    var point = footprint.GetPerimeterPoint(offset, distance);
    agent.SetDestination(new FixedVector3(point.x, unitPosition.y, point.y));
}
```

- `GetOpenDistance` is the package's own rule of how far obstacles block, plus what a thin footprint grows by: at that
  distance the building leaves every cell walkable, however it lies on the grid.
- The search starts at the edge point nearest the unit and goes both ways round; bodies on taken places do not overlap
  along the perimeter, and the point must fit the body (`IsPerimeterPlaceOpen`). False: the building is surrounded.
- Keep a held place while `grid.IsPerimeterPlaceOpen(footprint, offset, distance, clearance)` and search again when it
  is not (another obstacle came there).

## Can an order reach its target?

`LockstepNavReach` searches one path per question and says whether it is `Complete`: reject a move order to a point in
an obstacle, beyond the edge or on ground cut off from the unit, instead of sending it to the nearest edge.

```csharp
var reach = LockstepNavReach.Create(grid, Allocator.Temp); // grid: a LockstepNavGridView
var isValid = reach.CanReach(firstUnitPosition, order.target);
reach.Dispose();
```

## Large bodies

A radius larger than the grid's agent radius needs clearance: `LockstepNavigation.GetClearance(grid, radius)` gives one
more ring of free cells for every cell size the radius exceeds the agent radius. Paths, steps, pushes and stepping out
of obstacles keep such an agent to cells with that clearance: a vehicle goes round through a wide gap where infantry
squeeze through a narrow one.

- Spawn a large agent on a cell it fits: `gridView.ToPassable(position, radius)`, or by hand
  `IsPassable(grid, cells, position, clearance)`, else `TryFindNearestPassable(grid, cells, cell, clearance, out nearest)`.
- A weapon that strikes from the centre of a large body needs a reach longer than the body and the clearance around it,
  or the vehicle cannot reach what stands next to a wall.

## Keeping apart and avoidance

- **Keeping apart** is always on for agents with a `radius`: after they walked, overlapping agents are pushed apart by
  half the overlap per tick (`LockstepNavSeparationSystem`). A walking agent takes most of the push from a standing one;
  two walking opposite ways only step aside and squeeze past each other in a passage one body wide. A push never ends
  on a cell the body does not fit in. A radius of 0 takes no part.
- **Avoidance** needs the `LockstepNavAvoidance` singleton and agents with a radius and a `LockstepNavVelocity` (the
  agent part adds it): walking agents pick a velocity that steers around their neighbours (reciprocal, right turns
  first), push through crowds they cannot go around and never turn back or wait. Paths go around standing crowds when
  that is shorter (`crowdCost` cells per covered cell; 0 plans through them).
- Tuning: `timeHorizon` (seconds ahead; longer turns earlier and wider), `neighbourDistance` (a larger one finds more
  neighbours in a crowd and costs more), `maxNeighbours` (at most 16). `Default`: 1.5 s, 5 units, 8, 4 cells.

## Queries for AI and rules

| Question | Call |
|---|---|
| Can a unit stand here? | `IsWalkable(grid, cells, cell or position)`; with a body, `IsPassable(..., clearance)` |
| Nearest place it can stand | `TryFindNearestWalkable`, `TryFindNearestPassable` |
| Is the way straight and free? | `HasLineOfSight(grid, cells, from, to)` (walls only, not vision); `HasLineOfSight(grid, cells, fromXz, toXz, clearance)` for a body |
| A step that slides along walls | `TryStep(grid, cells, from, step[, clearance], out position)` |
| Can a point be reached at all? | `LockstepNavReach.CanReach(start, target)` |
| A path without an agent | `LockstepPathfinder` (below) |

```csharp
// A path for the AI: one pathfinder per thread, disposed after use (or kept in a job and reused).
using (var pathfinder = new LockstepPathfinder(grid.CellCount, Allocator.Temp))
using (var waypoints = new NativeList<FixedVector3>(Allocator.Temp))
{
    var map = new LockstepNavPathMap(grid, cells, LockstepNavigation.GetClearance(grid, radius));
    var status = pathfinder.FindPath(map, from, to, waypoints); // Failed, Complete or Partial
}
```

In a job, mark the map field `[ReadOnly]` and create every one of its arrays. Read the cells with
`SystemAPI.GetSingletonBuffer<LockstepNavCell>(true).AsNativeArray()`, and check `cells.Length == grid.CellCount`
before the first navigation update.

## Presentation

Read, never write: `LockstepNavVelocity.value` drives walk animations, `status` idle poses, the `LockstepNavWaypoint`
buffer draws the path of a selected unit. A view part for them needs an update system per type (`pragma-dots-views`).

## Cost

A search visits each cell at most once; an unreachable destination visits the whole reachable area. Searches run in
parallel jobs, a group per thread. On a 256 x 256 grid with 1200 agents in eight armies: a path across the map about
0.25 ms, an army's group search about 2 ms, the tick with all eight orders about 8 ms (about 25 ms with every soldier
sent alone), a tick of 1200 agents walking with avoidance about 2 ms. Send orders as groups; keep cells no smaller than
the gaps need.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Agents walk straight through walls | No grid in the world (agents then walk straight), the wall has no *Navigation/Obstacle* part, or the box is thinner than intended on the actor's local axes |
| An agent stays `Requested` for a tick, or starts a tick late | The order system updates after `LockstepNavSystemGroup`: order it `[UpdateBefore(typeof(LockstepNavSystemGroup))]` |
| An agent does not move at all | No Transform part (no `LockstepTransform`), `speed` (or a `pace` above 0) too small, or the destination lies within its `stoppingDistance` |
| Agents stand inside each other, avoid nothing | `radius` 0; avoidance also needs the `LockstepNavAvoidance` singleton and `LockstepNavVelocity` |
| A vehicle refuses a gap infantry uses | Its radius exceeds the grid's agent radius: it needs clearance there. Widen the gap or lower its radius |
| A vehicle is stuck after spawning | Spawned on a cell without room for its body: spawn with `TryFindNearestPassable` |
| Units float or sink on hills | Spawned at a fixed Y: use `ToGround`; or the terrain does not cover the grid exactly |
| Two buildings overlap when placed on one tick | `IsClear` sees only stamped cells: compare the tick's footprints with `GetCoverage` / `Covers` |
| `IsClear` false on tick 0, or an index out of range in a query | The cells are sized on the first navigation update: check `cells.Length == grid.CellCount`, or read through `LockstepNavGridView` |
| Melee units crowd at one edge of a building | Each should hold its own place around it: `TryFindPerimeterPlace` with the places already taken |
| An agent jumps back after a knockback | Its `LockstepTransform` was written while it walked: `Stop()` it first |
| A group order is slow | Agents sent alone (`SetDestination(destination)`) or with different `groupGoal`s: give one order one goal |

## Before you finish

- [ ] Destinations are set before `LockstepNavSystemGroup`; arrivals are read after it.
- [ ] Orders of several units use `SetDestination(place, groupGoal)` with spread places.
- [ ] Agents that should not overlap have a `radius`; large bodies spawn on passable cells.
- [ ] Nothing writes the `LockstepTransform` of a walking agent or stamps the simulation's cells.
- [ ] With heights, placed things go through `ToGround` and distances compare X and Z.
- [ ] A session test with latency and jitter moves agents and shows no desync (`pragma-lockstep-desync`).
