using Pragma.Dots.Mathematics;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Navigation
{
    /// <summary>
    /// The navigation grid of a world as gameplay reads it: the <see cref="LockstepNavGrid"/>, its cells and its ground
    /// heights in one value, so that systems pass one argument instead of a grid, its buffers and whether they exist.
    /// </summary>
    /// <remarks>
    /// <para>A world without a grid, or with a grid whose cells are not built yet (they are sized by
    /// <see cref="LockstepNavObstacleSystem"/> on its first update, later in the tick a player joins on), is open ground:
    /// every position is walkable and passable, nothing moves to another cell, and every path reaches its target. Ground
    /// heights are separate: without them positions keep their Y.</para>
    /// <para>The view reads the buffers in place: get it where it is used (<see cref="Get"/> in <c>OnUpdate</c>, with the
    /// query from <see cref="CreateQuery"/>), and get it again after a structural change. Integer and fixed-point math only;
    /// Burst-compatible, and read-only in jobs.</para>
    /// </remarks>
    public readonly struct LockstepNavGridView
    {
        // Places around a footprint are tried every half body, but never finer than this.
        private const long MIN_PLACE_STEP_RAW = FixedPoint.ONE_RAW / 16;

        public readonly LockstepNavGrid grid;

        /// <summary>The cells of the grid, or an array that was never created when <see cref="HasCells"/> is false.</summary>
        [ReadOnly]
        public readonly NativeArray<LockstepNavCell> cells;

        /// <summary>The ground heights, or an array that was never created when the grid has none.</summary>
        [ReadOnly]
        public readonly NativeArray<LockstepNavHeight> heights;

        public LockstepNavGridView(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, NativeArray<LockstepNavHeight> heights)
        {
            this.grid = grid;
            this.cells = cells;
            this.heights = heights;
        }

        /// <summary>The grid has cells to answer from; without them the view is open ground.</summary>
        public bool HasCells => grid.IsValid && cells.IsCreated && cells.Length == grid.CellCount;

        /// <summary>The grid has ground heights, one per cell corner.</summary>
        public bool HasHeights => heights.IsCreated && LockstepNavigation.HasHeights(grid, heights);

        /// <summary>
        /// The query of the grid entity for <see cref="Get"/>: build it in <c>OnCreate</c> and keep it. It does not require the
        /// grid for the update: a world without one is open ground.
        /// </summary>
        public static EntityQuery CreateQuery(ref SystemState state)
        {
            return new EntityQueryBuilder(Allocator.Temp).WithAll<LockstepNavGrid>().Build(ref state);
        }

        /// <summary>
        /// The grid of the world of <paramref name="entityManager"/> through <paramref name="query"/> (from
        /// <see cref="CreateQuery"/>, or any query of <see cref="LockstepNavGrid"/>), with the cells and heights it has; open
        /// ground when the world has no grid or more than one.
        /// </summary>
        public static LockstepNavGridView Get(EntityManager entityManager, EntityQuery query)
        {
            if (query.CalculateEntityCount() != 1)
            {
                return default;
            }
            var entity = query.GetSingletonEntity();
            var grid = entityManager.GetComponentData<LockstepNavGrid>(entity);
            var cells = entityManager.HasBuffer<LockstepNavCell>(entity)
                ? entityManager.GetBuffer<LockstepNavCell>(entity, true).AsNativeArray()
                : default;
            var heights = entityManager.HasBuffer<LockstepNavHeight>(entity)
                ? entityManager.GetBuffer<LockstepNavHeight>(entity, true).AsNativeArray()
                : default;
            return new LockstepNavGridView(grid, cells, heights);
        }

        /// <summary>The clearance a cell needs for a body of <paramref name="radius"/> (<see cref="LockstepNavigation.GetClearance"/>); 1 without cells.</summary>
        public int GetClearance(FixedPoint radius)
        {
            return HasCells ? LockstepNavigation.GetClearance(grid, radius) : 1;
        }

        /// <summary>The cell under the position is walkable (<see cref="LockstepNavigation.IsWalkable(in LockstepNavGrid, NativeArray{LockstepNavCell}, FixedVector3)"/>); Y is ignored.</summary>
        public bool IsWalkable(FixedVector3 position)
        {
            return !HasCells || LockstepNavigation.IsWalkable(grid, cells, position);
        }

        /// <summary>A body that needs <paramref name="clearance"/> fits on the cell under the position (<see cref="LockstepNavigation.IsPassable(in LockstepNavGrid, NativeArray{LockstepNavCell}, FixedVector3, int)"/>).</summary>
        public bool IsPassable(FixedVector3 position, int clearance)
        {
            return !HasCells || LockstepNavigation.IsPassable(grid, cells, position, clearance);
        }

        /// <summary>The position on the ground (<see cref="LockstepNavigation.ToGround"/>); without heights its Y is kept.</summary>
        public FixedVector3 ToGround(FixedVector3 position)
        {
            return HasHeights ? new FixedVector3(position.x, LockstepNavigation.GetHeight(grid, heights, position.Xz), position.z) : position;
        }

        /// <summary>
        /// Where a body of <paramref name="radius"/> can stand at or near the position: the position itself when its cell
        /// fits the body, else the centre of the nearest cell that does (<see cref="LockstepNavigation.TryFindNearestPassable"/>,
        /// from the nearest cell inside the grid), with the position's Y. The position itself when no cell fits the body.
        /// Use it for what is spawned or placed on the map, then <see cref="ToGround"/>.
        /// </summary>
        public FixedVector3 ToPassable(FixedVector3 position, FixedPoint radius)
        {
            if (!HasCells)
            {
                return position;
            }
            var clearance = LockstepNavigation.GetClearance(grid, radius);
            if (LockstepNavigation.IsPassable(grid, cells, position, clearance) ||
                !LockstepNavigation.TryFindNearestPassable(grid, cells, grid.ClampToGrid(grid.WorldToCell(position)), clearance, out var nearest))
            {
                return position;
            }
            var center = grid.GetCellCenter(nearest);
            return new FixedVector3(center.x, position.y, center.y);
        }

        /// <summary>
        /// The point of the place at <paramref name="offset"/> on the perimeter of the footprint, <paramref name="distance"/>
        /// out from its edge (<see cref="LockstepNavObstacleFootprint.GetPerimeterPoint"/>), lies on a cell a body that needs
        /// <paramref name="clearance"/> fits on: a unit can stand there.
        /// </summary>
        public bool IsPerimeterPlaceOpen(in LockstepNavObstacleFootprint footprint, FixedPoint offset, FixedPoint distance, int clearance)
        {
            if (!HasCells)
            {
                return true;
            }
            var point = footprint.GetPerimeterPoint(offset, distance);
            return LockstepNavigation.IsPassable(grid, cells, grid.WorldToCell(point), clearance);
        }

        /// <summary>
        /// A free place around the footprint for a body of <paramref name="radius"/> at <paramref name="position"/> (X and Z):
        /// the offset on the perimeter (<see cref="LockstepNavObstacleFootprint"/>) nearest along it to the edge point nearest
        /// the body, whose body does not overlap the <paramref name="taken"/> places along the perimeter and whose point,
        /// <paramref name="distance"/> out from the edge, the body fits on (<see cref="IsPerimeterPlaceOpen"/>). Melee units
        /// surrounding a building or builders around a site each take their own place this way instead of crowding at the
        /// nearest edge: the first ones take the near side, the next ones the sides further round.
        /// </summary>
        /// <param name="taken">The places already taken around this footprint, the body's own excluded.</param>
        /// <param name="distance">How far out from the edge the body stands: its reach, or <see cref="LockstepNavigation.GetOpenDistance"/> to stand on cells the footprint never blocks.</param>
        /// <returns>False when every place is taken or blocked: the footprint is surrounded.</returns>
        public bool TryFindPerimeterPlace(in LockstepNavObstacleFootprint footprint,
                                          FixedVector2 position,
                                          FixedPoint radius,
                                          FixedPoint distance,
                                          NativeArray<LockstepNavPerimeterPlace> taken,
                                          out FixedPoint offset)
        {
            offset = FixedPoint.Zero;
            var perimeter = footprint.Perimeter;
            if (perimeter.rawValue <= 0)
            {
                return false;
            }

            var step = FixedMath.Max(radius / 2, FixedPoint.FromRaw(MIN_PLACE_STEP_RAW));
            var preferred = footprint.GetPerimeterOffset(position);
            var half = perimeter / 2;
            var clearance = GetClearance(radius);
            // From the edge point nearest the body both ways round, until the whole perimeter is walked.
            for (var shift = FixedPoint.Zero; shift <= half; shift += step)
            {
                for (var side = 0; side < 2; side++)
                {
                    if (side == 1 && shift.rawValue == 0)
                    {
                        continue;
                    }
                    var candidate = FixedMath.Mod(side == 0 ? preferred + shift : preferred - shift, perimeter);
                    if (IsPerimeterPlaceFree(footprint, taken, candidate, radius) && IsPerimeterPlaceOpen(footprint, candidate, distance, clearance))
                    {
                        offset = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsPerimeterPlaceFree(in LockstepNavObstacleFootprint footprint, NativeArray<LockstepNavPerimeterPlace> taken, FixedPoint offset,
                                                 FixedPoint radius)
        {
            for (var i = 0; i < taken.Length; i++)
            {
                if (footprint.GetPerimeterDistance(offset, taken[i].offset) < radius + taken[i].radius)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
