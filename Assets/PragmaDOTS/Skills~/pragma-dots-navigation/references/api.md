# Navigation API reference

Namespace and assembly `Pragma.Dots.Navigation`: the Navigation module (`PRAGMA_DOTS_NAVIGATION`, needs Lockstep).
Fields of components are public and camelCase.

## Contents

- Systems and order
- Components
- Queries
- Search
- Actor parts

## Systems and order

`LockstepNavSystemGroup` is in `LockstepSimulationSystemGroup`, in the default slot: order your systems against it.

| System | Does |
|---|---|
| `LockstepNavObstacleSystem` | `OrderFirst`. Sizes the cell buffer, stamps new obstacles, moves changed ones, releases destroyed ones; a new grid or one whose layout changed is stamped again from scratch, steep cells with one permanent blocker. When a cell changed: computes the clearance again and changes `version` |
| `LockstepNavPathSystem` | Groups the tick's requests by the cell of `groupGoal` and the body's clearance, one search per group, paths in parallel; walking agents check their remaining path when `version` changed |
| `LockstepNavAvoidanceSystem` | With a `LockstepNavAvoidance`: picks the velocity of every walking agent with a radius and a `LockstepNavVelocity`, in a parallel job |
| `LockstepNavMoveSystem` | Walks agents along their waypoints, turns them, puts them on the ground, sets `Arrived` |
| `LockstepNavSeparationSystem` | Pushes overlapping agents with a radius apart; opposite walkers only sideways |

## Components

| Type | Members |
|---|---|
| `LockstepNavGrid` | Singleton. `FixedVector2 origin` (outer corner of cell 0, 0 on X and Z), `FixedPoint cellSize`, `int width` (along X), `int height` (along Z), `FixedPoint agentRadius`, `FixedPoint maxSlope` (rise over run; steeper cells are blocked, 0 none), `uint version` (changes with any cell); `Layout` (the `FixedGrid` of its cells), `CellCount`, `CornerCount`, `IsValid`, `Contains`, `GetIndex`, `GetCornerIndex`, `GetCell`, `WorldToCell` (`FixedVector2` or `FixedVector3`, may lie outside), `GetCellCenter`, `ClampToGrid` |
| `LockstepNavCell` | Buffer on the grid entity, row by row along X: `ushort blockers` (obstacles over the cell), `byte clearance` (rings of free cells to the nearest blocked cell or the edge: 0 blocked, 1 next to one), `IsWalkable`. Empty until the first navigation update |
| `LockstepNavHeight` | Optional buffer on the grid entity: `FixedPoint value`, world Y of the ground at each cell corner, `CornerCount` of them row by row along X. Static: read when the cells are built |
| `LockstepNavObstacle` | `FixedVector2 center`, `FixedVector2 size`: a rectangle in the space of the entity's `LockstepTransform` (yaw, uniform scale), world space without one. Blocks every cell whose centre is within the agent radius of it; thinner than a cell with that radius, it counts as that thick |
| `LockstepNavObstacleFootprint` | Cleanup component the obstacle system writes: the stamped world rectangle (`center`, `right`, `halfSize`, `Forward`); `Create(obstacle, transform)`, `Create(obstacle)` (world space). A destroyed obstacle lingers with it until its cells are released |
| `LockstepNavAgent` | `speed`, `pace` (walks no faster while above 0), `angularSpeed` (radians per second, 0 turns at once), `turnInPlaceAngle` (radians; 0 walks while it turns), `stoppingDistance`, `radius` (0 takes no part in keeping apart and avoidance), `destination`, `groupGoal`, `status`, `isPathPartial`, `waypointIndex`, `gridVersion`; `SetDestination(destination)` (alone), `SetDestination(destination, groupGoal)` (with a group), `Replan()`, `Stop()` (its cell is checked again: stopped inside an obstacle, it walks out), `MustTurnInPlace(rotation, heading)`, `IsMoving`, `WalkSpeed` |
| `LockstepNavStatus` | `Idle`, `Requested` (planned in the next navigation update), `Moving`, `Arrived` |
| `LockstepNavWaypoint` | Buffer: `FixedVector3 position`; the path, end included, with the Y of the destination (not walked to) |
| `LockstepNavVelocity` | `FixedVector2 value` (what the agent walked on its last step, units per second on X and Z), `FixedVector2 desired` (what avoidance picked for this step). With a radius, the agent takes part in avoidance |
| `LockstepNavAvoidance` | Singleton that turns avoidance on: `FixedPoint timeHorizon` (seconds ahead), `FixedPoint neighbourDistance` (centre to centre), `int maxNeighbours` (at most `MAX_NEIGHBOURS` = 16), `FixedPoint crowdCost` (cells a standing agent adds to a path through the cells its body covers; 0 plans through crowds); `Default` (1.5 s, 5 units, 8, 4 cells) |

Read them with `SystemAPI.GetSingleton<LockstepNavGrid>()`, `SystemAPI.GetSingletonBuffer<LockstepNavCell>(true)` and,
for the ground, `SystemAPI.TryGetSingletonBuffer<LockstepNavHeight>(out var heights, true)` (`SystemAPI.GetBuffer` has
no read-only overload).

`FixedGrid` (`Pragma.Dots.Mathematics`, the core) is the cell math both the navigation and vision grids use: see the
`pragma-lockstep-gameplay` skill, `references/fixed-point.md`.

## Queries

`LockstepNavigation`, Burst-compatible, every function taking the grid and its whole cell (or height) array:

| Function | Notes |
|---|---|
| `IsWalkable(grid, cells, cell or position)` | Inside the grid and no obstacle over it; Y ignored |
| `GetClearance(grid, radius)` | The clearance a body needs: 1 up to the agent radius, one more per cell size larger, rounded; at most 255 |
| `IsPassable(grid, cells, cell or position, clearance)` | A body of that clearance fits on the cell |
| `UpdateClearance(grid, cells)` | After stamping cells of your own (previews, tests) |
| `HasLineOfSight(grid, cells, from, to)` | `FixedVector3` or X and Z: no blocked cell between; exact through cell borders, a segment through a corner counts both cells beside it |
| `HasLineOfSight(grid, cells, fromXz, toXz, clearance)` | The same for a body |
| `TryFindNearestWalkable(grid, cells, cell, out nearest)`, `TryFindNearestPassable(grid, cells, cell, clearance, out nearest)` | By distance between centres; the cell itself when it fits; ties to the first found, rings row by row |
| `TryStep(grid, cells, from, step[, clearance], out position)` | The passable part of a step: whole, else its X part, else its Z part; never through the corner between blocked cells |
| `GetStepClearance(grid, cells, from, clearance)` | The clearance a step from there has to end on: the body's own, or 1 when it already stands on a cell too narrow for it and is on its way out |
| `IsClear(grid, cells, footprint)` | Every cell the footprint would block is inside the grid and walkable: where a building may go; false while the cells do not match the grid |
| `Covers(grid, footprint, cell)`, `GetCoverage(grid, footprint, out min, out max)` | The cells a footprint blocks, outside the grid too: compare footprints not stamped yet |
| `Stamp(grid, cells, footprint, count)` | Adds (or with a negative count removes) blockers: previews and tests only |
| `HasHeights(grid, heights)`, `GetHeight(grid, heights, xz)`, `ToGround(grid, heights, position)`, `IsSteep(grid, heights, cell)` | The ground: bilinear inside a cell, the edge beyond the grid; `ToGround` returns the position unchanged without heights |

## Search

| Type | Members |
|---|---|
| `LockstepNavPathMap` | The grid as one agent plans on it: `grid`, `cells`, `clearance` (`GetClearance` of its radius), `crowd` (standing agents per cell, empty for none), `int crowdCost`; `new LockstepNavPathMap(grid, cells, clearance = 1)`, `IsValid`, `HasCrowd`, `IsPassable(cell)`. In a job, mark the field `[ReadOnly]` and create every array |
| `LockstepPathfinder` | `new LockstepPathfinder(cellCount, allocator)`, `IsCreated`, `Dispose()`; keeps scratch memory, one per thread. `FindPath(grid, cells, start, destination, waypoints)` or `FindPath(map, start, destination, waypoints)` into a `NativeList<FixedVector3>` or a `DynamicBuffer<LockstepNavWaypoint>`; a group: `SearchGroup(map, goalCell, startCells)` (one search back from the goal to all starts), `GetChain(startCell, chain)`, `FollowChain(map, start, destination, chain, waypoints)` (`Failed` when the chain does not lead there: plan alone); static `TryGetStartCell(map, position, out cell)`, `TryGetEndCell(map, position, out cell, out isOpen)`. `STRAIGHT_COST` = 10 (14 diagonal) |
| `LockstepPathStatus` | `Failed`, `Complete`, `Partial` (ends at the reachable cell closest to the destination) |

A* over the eight neighbours with an octile heuristic; a diagonal step needs both cells beside it free. A heap with a
strict total order and integer costs: the same path on every machine, thread count and in Burst and Mono.

## Actor parts

| Part (menu) | Fields (default) | Adds |
|---|---|---|
| `NavGridPart` (*Navigation/Grid*) | `Size` (100, 100) centred on the placement, `CellSize` (0.5, at least 0.05), `AgentRadius` (0.5), `Terrain` (none: flat, agents keep their Y), `TerrainHeight` (world Y of the terrain object), `MaxSlope` (35 degrees, up to 89; 0 every slope; only with a terrain) | `LockstepNavGrid`, an empty `LockstepNavCell` buffer, with a terrain the `LockstepNavHeight` buffer (sampled in fixed point, `DependsOn` the terrain). `CreateGrid(center)`. Selected: previews blocked (red) and steep (yellow) cells |
| `NavObstaclePart` (*Navigation/Obstacle*) | `Center` (0, 0), `Size` (1, 1) on the actor's local X and Z | `LockstepNavObstacle`. With a Transform part the box stays local and follows the actor; without one the part adds a `LockstepTransform` at the placement and scales the box by the placement's X and Z scale. `GetFootprint(transform, isMoving)` for previews |
| `NavAgentPart` (*Navigation/Agent*) | `Speed` (4), `AngularSpeed` (0 degrees per second), `TurnInPlaceAngle` (0, 0 to 180 degrees), `StoppingDistance` (0), `Radius` (0) | `LockstepNavAgent` (angles in radians), an empty `LockstepNavWaypoint` buffer, `LockstepNavVelocity`. Needs a Transform part |
| `NavAvoidancePart` (*Navigation/Avoidance*) | `TimeHorizon` (1.5), `NeighbourDistance` (5), `MaxNeighbours` (8, 0 to 16), `CrowdCost` (4) | The `LockstepNavAvoidance` singleton: put it on the map's actor, once |
