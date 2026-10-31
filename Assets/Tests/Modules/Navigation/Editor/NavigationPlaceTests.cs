using NUnit.Framework;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using Pragma.Dots.Navigation;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    /// <summary>The geometry of footprints, places around them, the grid view and reaching points.</summary>
    public class NavigationPlaceTests
    {
        private const double TOLERANCE = 1e-3;

        // 40 x 40 cells of half a unit: X and Z from -10 to 10.
        private static LockstepNavGrid CreateGrid(FixedPoint agentRadius)
        {
            return new LockstepNavGrid
            {
                origin = new FixedVector2(-10, -10),
                cellSize = FixedPoint.Half,
                width = 40,
                height = 40,
                agentRadius = agentRadius,
            };
        }

        // A rectangle 4 wide along X and 2 deep along Z around the origin: a perimeter of 12.
        private static LockstepNavObstacleFootprint CreateFootprint()
        {
            return LockstepNavObstacleFootprint.Create(new LockstepNavObstacle { center = FixedVector2.Zero, size = new FixedVector2(4, 2) });
        }

        private static FixedPoint Value(double value) => (FixedPoint)value;

        private static void AreClose(FixedVector2 expected, FixedVector2 actual, string message = null)
        {
            Assert.AreEqual((double)expected.x, (double)actual.x, TOLERANCE, message);
            Assert.AreEqual((double)expected.y, (double)actual.y, TOLERANCE, message);
        }

        [Test]
        public void Footprint_GivesTheNearestPointAndDistance()
        {
            var footprint = CreateFootprint();
            Assert.AreEqual(new FixedVector2(2, 0), footprint.ClosestPoint(new FixedVector2(5, 0)));
            Assert.AreEqual(new FixedVector2(1, 0), footprint.ClosestPoint(new FixedVector2(1, 0)), "a point inside is its own nearest");
            Assert.AreEqual(new FixedVector3(-2, 7, 1), footprint.ClosestPoint(new FixedVector3(-6, 7, 3)), "Y is kept");
            Assert.AreEqual((FixedPoint)13, footprint.DistanceSquared(new FixedVector2(5, 3)));
            Assert.AreEqual(FixedPoint.Zero, footprint.DistanceSquared(new FixedVector3(1, 5, 0)), "inside, whatever the height");
        }

        [Test]
        public void Footprint_PerimeterOffsetsWalkTheSidesFromTheNearCorner()
        {
            var footprint = CreateFootprint();
            Assert.AreEqual((FixedPoint)12, footprint.Perimeter);
            Assert.AreEqual((FixedPoint)2, footprint.GetPerimeterOffset(new FixedVector2(0, -5)), "the middle of the -Z side");
            Assert.AreEqual(Value(5.5), footprint.GetPerimeterOffset(new FixedVector2(5, Value(0.5))), "on the +X side");
            Assert.AreEqual(new FixedVector2(0, -2), footprint.GetPerimeterPoint(2, 1));
            Assert.AreEqual(new FixedVector2(2, Value(0.5)), footprint.GetPerimeterPoint(Value(5.5), 0));
            Assert.AreEqual(footprint.GetPerimeterPoint(2, 1), footprint.GetPerimeterPoint(14, 1), "offsets wrap around");
            Assert.AreEqual((FixedPoint)2, footprint.GetPerimeterDistance(1, 11), "the shorter way round");

            for (var offset = FixedPoint.Zero; offset < footprint.Perimeter; offset += FixedPoint.Half)
            {
                var point = footprint.GetPerimeterPoint(offset, 1);
                Assert.AreEqual(offset, footprint.GetPerimeterOffset(point), $"offset {offset}");
                Assert.AreEqual(FixedPoint.One, footprint.DistanceSquared(point), $"the point of {offset} is one unit out");
            }
        }

        [Test]
        public void Footprint_OfATurnedObstacle_TurnsWithIt()
        {
            // A quarter turn: local +X points to world -Z, local +Z to world +X.
            var transform = new LockstepTransform
            {
                position = new FixedVector3(10, 0, 0),
                rotation = FixedQuaternion.RotateY(FixedMath.HalfPi),
                scale = FixedPoint.One,
            };
            var footprint = LockstepNavObstacleFootprint.Create(new LockstepNavObstacle { size = new FixedVector2(4, 2) }, transform);
            AreClose(new FixedVector2(10, -2), footprint.ClosestPoint(new FixedVector2(10, -5)));
            Assert.AreEqual(9.0, (double)footprint.DistanceSquared(new FixedVector2(10, -5)), 1e-2);
            AreClose(new FixedVector2(11, 0), footprint.GetPerimeterPoint(8, 0), "the middle of the local +Z side");
        }

        [Test]
        public void OpenDistance_KeepsEveryPlaceOnCellsTheFootprintLeavesWalkable()
        {
            // A thin wall grows to a cell's width with the small agent radius.
            var obstacles = new[]
            {
                new LockstepNavObstacle { size = new FixedVector2(3, 2) },
                new LockstepNavObstacle { size = new FixedVector2(Value(0.1), 4) },
            };
            foreach (var agentRadius in new[] { Value(0.1), Value(0.3) })
            {
                var grid = CreateGrid(agentRadius);
                using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
                {
                    AssertPlacesAreWalkable(grid, cells, obstacles);
                }
            }
            Assert.AreEqual(FixedPoint.Zero, LockstepNavigation.GetOpenDistance(default, CreateFootprint()), "no grid");
        }

        // Every place around each obstacle, turned by a few angles and off the cell corners, is on a walkable cell.
        private static void AssertPlacesAreWalkable(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, LockstepNavObstacle[] obstacles)
        {
            foreach (var obstacle in obstacles)
            {
                for (var degrees = 0; degrees < 90; degrees += 15)
                {
                    var transform = new LockstepTransform
                    {
                        position = new FixedVector3(Value(0.37), 0, Value(-0.21)),
                        rotation = FixedQuaternion.RotateY(FixedMath.ToRadians(degrees)),
                        scale = FixedPoint.One,
                    };
                    var footprint = LockstepNavObstacleFootprint.Create(obstacle, transform);
                    var distance = LockstepNavigation.GetOpenDistance(grid, footprint);
                    LockstepNavigation.Stamp(grid, cells, footprint, 1);
                    for (var offset = FixedPoint.Zero; offset < footprint.Perimeter; offset += Value(0.05))
                    {
                        var point = footprint.GetPerimeterPoint(offset, distance);
                        Assert.IsTrue(LockstepNavigation.IsWalkable(grid, cells, new FixedVector3(point.x, 0, point.y)),
                            $"{obstacle.size} at {degrees} degrees, agent radius {grid.agentRadius}, offset {offset}");
                    }
                    LockstepNavigation.Stamp(grid, cells, footprint, -1);
                }
            }
        }

        [Test]
        public void PerimeterPlaces_SurroundAFootprintWithoutOverlapping()
        {
            var grid = CreateGrid(Value(0.3));
            var footprint = CreateFootprint();
            var radius = Value(0.4);
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            using (var taken = new NativeList<LockstepNavPerimeterPlace>(Allocator.Temp))
            {
                LockstepNavigation.Stamp(grid, cells, footprint, 1);
                var view = new LockstepNavGridView(grid, cells, default);
                var distance = LockstepNavigation.GetOpenDistance(grid, footprint);
                var position = new FixedVector2(0, -6);

                Assert.IsTrue(view.TryFindPerimeterPlace(footprint, position, radius, distance, taken.AsArray(), out var first));
                Assert.AreEqual(footprint.GetPerimeterOffset(position), first, "the first place is the nearest");

                taken.Add(new LockstepNavPerimeterPlace(first, radius));
                while (view.TryFindPerimeterPlace(footprint, position, radius, distance, taken.AsArray(), out var offset))
                {
                    for (var i = 0; i < taken.Length; i++)
                    {
                        Assert.GreaterOrEqual(footprint.GetPerimeterDistance(offset, taken[i].offset), radius * 2, $"{offset} overlaps {taken[i].offset}");
                    }
                    taken.Add(new LockstepNavPerimeterPlace(offset, radius));
                    Assert.LessOrEqual(taken.Length, 15, "no more bodies than fit the perimeter");
                }
                Assert.GreaterOrEqual(taken.Length, 11, "the bodies go round the whole footprint");
            }
        }

        [Test]
        public void PerimeterPlaces_SkipBlockedPoints()
        {
            var grid = CreateGrid(Value(0.3));
            var footprint = CreateFootprint();
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            {
                LockstepNavigation.Stamp(grid, cells, footprint, 1);
                // A wall along the whole -Z side, where the places of that side would stand.
                LockstepNavigation.Stamp(grid, cells, LockstepNavObstacleFootprint.Create(new LockstepNavObstacle
                {
                    center = new FixedVector2(0, Value(-1.75)),
                    size = new FixedVector2(8, FixedPoint.Half),
                }), 1);
                var view = new LockstepNavGridView(grid, cells, default);
                var distance = LockstepNavigation.GetOpenDistance(grid, footprint);
                var position = new FixedVector2(0, -6);

                Assert.IsFalse(view.IsPerimeterPlaceOpen(footprint, footprint.GetPerimeterOffset(position), distance, 1));
                Assert.IsTrue(view.TryFindPerimeterPlace(footprint, position, Value(0.4), distance, default, out var offset));
                Assert.IsTrue(view.IsPerimeterPlaceOpen(footprint, offset, distance, 1));
                Assert.GreaterOrEqual((double)footprint.GetPerimeterPoint(offset, distance).y, -1.0, "on a side the wall leaves open");
            }
        }

        [Test]
        public void GridView_WithoutCells_IsOpenGround()
        {
            var view = default(LockstepNavGridView);
            var position = new FixedVector3(3, 2, 1);
            Assert.IsFalse(view.HasCells);
            Assert.IsFalse(view.HasHeights);
            Assert.AreEqual(1, view.GetClearance(5));
            Assert.IsTrue(view.IsWalkable(position));
            Assert.IsTrue(view.IsPassable(position, 3));
            Assert.AreEqual(position, view.ToPassable(position, 1));
            Assert.AreEqual(position, view.ToGround(position));
            Assert.IsTrue(view.TryFindPerimeterPlace(CreateFootprint(), FixedVector2.Zero, 1, 1, default, out _));
            using (var reach = LockstepNavReach.Create(view, Allocator.Temp))
            {
                Assert.IsTrue(reach.CanReach(position, new FixedVector3(100, 0, 100)));
            }
        }

        [Test]
        public void GridView_PutsBodiesOnCellsTheyFitAndOnTheGround()
        {
            var grid = CreateGrid(Value(0.25));
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            using (var heights = FlatGround(grid, 3))
            {
                LockstepNavigation.Stamp(grid, cells, CreateFootprint(), 1);
                LockstepNavigation.UpdateClearance(grid, cells);
                var view = new LockstepNavGridView(grid, cells, heights);
                Assert.IsTrue(view.HasCells);
                Assert.IsTrue(view.HasHeights);

                var inside = new FixedVector3(0, 1, Value(0.9));
                Assert.IsFalse(view.IsWalkable(inside));
                var moved = view.ToPassable(inside, Value(0.25));
                Assert.IsTrue(view.IsWalkable(moved));
                Assert.AreEqual(inside.y, moved.y, "the height is kept");
                Assert.Less((double)FixedMath.Distance(inside, moved), 1.5, "to the nearest cell");

                var large = view.ToPassable(inside, Value(1.2));
                Assert.IsTrue(view.IsPassable(large, view.GetClearance(Value(1.2))), "a large body gets a cell with room for it");
                Assert.AreEqual((FixedPoint)3, view.ToGround(moved).y);
            }
        }

        [Test]
        public void GridView_ReadsTheGridOfAWorld()
        {
            using (var world = new World("Navigation view"))
            {
                var entityManager = world.EntityManager;
                var grid = CreateGrid(Value(0.25));
                var entity = entityManager.CreateEntity(typeof(LockstepNavGrid));
                entityManager.SetComponentData(entity, grid);
                using (var query = entityManager.CreateEntityQuery(typeof(LockstepNavGrid)))
                {
                    var view = LockstepNavGridView.Get(entityManager, query);
                    Assert.AreEqual(grid.width, view.grid.width);
                    Assert.IsFalse(view.HasCells, "cells not built yet");

                    entityManager.AddBuffer<LockstepNavCell>(entity).ResizeUninitialized(grid.CellCount);
                    entityManager.AddBuffer<LockstepNavHeight>(entity).ResizeUninitialized(grid.CornerCount);
                    view = LockstepNavGridView.Get(entityManager, query);
                    Assert.IsTrue(view.HasCells);
                    Assert.IsTrue(view.HasHeights);

                    entityManager.DestroyEntity(entity);
                    Assert.IsFalse(LockstepNavGridView.Get(entityManager, query).HasCells, "no grid");
                }
            }
        }

        [Test]
        public void Reach_IsCompleteOnlyForPointsAPathGetsTo()
        {
            var grid = CreateGrid(FixedPoint.Zero);
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            {
                // A closed box around (5, 5) and a block at (-5, 0).
                Stamp(grid, cells, new FixedVector2(5, 3), new FixedVector2(5, 1));
                Stamp(grid, cells, new FixedVector2(5, 7), new FixedVector2(5, 1));
                Stamp(grid, cells, new FixedVector2(3, 5), new FixedVector2(1, 5));
                Stamp(grid, cells, new FixedVector2(7, 5), new FixedVector2(1, 5));
                Stamp(grid, cells, new FixedVector2(-5, 0), new FixedVector2(2, 2));
                var view = new LockstepNavGridView(grid, cells, default);
                var start = new FixedVector3(-8, 0, -8);
                using (var reach = LockstepNavReach.Create(view, Allocator.Temp))
                {
                    Assert.IsTrue(reach.CanReach(start, new FixedVector3(8, 0, -8)));
                    Assert.IsFalse(reach.CanReach(start, new FixedVector3(5, 0, 5)), "inside a closed box");
                    Assert.IsFalse(reach.CanReach(start, new FixedVector3(-5, 0, 0)), "in an obstacle");
                    Assert.IsFalse(reach.CanReach(start, new FixedVector3(20, 0, 0)), "beyond the edge");
                }
            }
        }

        private static NativeArray<LockstepNavHeight> FlatGround(in LockstepNavGrid grid, FixedPoint height)
        {
            var heights = new NativeArray<LockstepNavHeight>(grid.CornerCount, Allocator.Temp);
            for (var i = 0; i < heights.Length; i++)
            {
                heights[i] = new LockstepNavHeight { value = height };
            }
            return heights;
        }

        private static void Stamp(in LockstepNavGrid grid, NativeArray<LockstepNavCell> cells, FixedVector2 center, FixedVector2 size)
        {
            LockstepNavigation.Stamp(grid, cells, LockstepNavObstacleFootprint.Create(new LockstepNavObstacle { center = center, size = size }), 1);
        }
    }
}
