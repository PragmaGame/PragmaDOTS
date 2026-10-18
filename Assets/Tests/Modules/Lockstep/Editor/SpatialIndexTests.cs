using System.Collections.Generic;
using NUnit.Framework;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using Unity.Collections;

namespace Pragma.Dots.Tests
{
    public class SpatialIndexTests
    {
        // Points scattered over a square around the origin, some on top of each other.
        private static NativeArray<FixedVector2> CreatePoints(int count, FixedPoint half, uint seed)
        {
            var random = FixedRandom.CreateFromIndex(42, seed);
            var points = new NativeArray<FixedVector2>(count, Allocator.Temp);
            for (var i = 0; i < count; i++)
            {
                points[i] = i % 17 == 5 ? points[i - 1] : random.NextVector2(new FixedVector2(-half, -half), new FixedVector2(half, half));
            }
            return points;
        }

        private static List<int> Collect(LockstepSpatialIndex.Enumerator query)
        {
            var found = new List<int>();
            foreach (var point in query)
            {
                found.Add(point);
            }
            return found;
        }

        [Test]
        public void Query_FindsEveryPointWithinTheRadius_OnceEach()
        {
            using (var points = CreatePoints(500, 60, 1))
            using (var index = new LockstepSpatialIndex(points, 8, 1 << 16, Allocator.Temp))
            {
                var random = FixedRandom.CreateFromIndex(7, 3);
                var radii = new[] { FixedPoint.Zero, FixedPoint.Half, (FixedPoint)3, (FixedPoint)17, (FixedPoint)45, (FixedPoint)200 };
                for (var probe = 0; probe < 40; probe++)
                {
                    var center = random.NextVector2(new FixedVector2(-70, -70), new FixedVector2(70, 70));
                    foreach (var radius in radii)
                    {
                        var found = Collect(index.Query(center, radius));
                        Assert.AreEqual(new HashSet<int>(found).Count, found.Count, "no point is visited twice");
                        for (var i = 0; i < points.Length; i++)
                        {
                            if (FixedMath.Distance(points[i], center) <= radius)
                            {
                                Assert.Contains(i, found, $"point {i} at {points[i]} lies within {radius} of {center}");
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void Query_VisitsCellsRowByRowAndPointsInIndexOrder()
        {
            using (var points = CreatePoints(300, 40, 2))
            using (var index = new LockstepSpatialIndex(points, 5, 1 << 16, Allocator.Temp))
            {
                var layout = index.Layout;
                var found = Collect(index.Query(FixedVector2.Zero, 1000));
                Assert.AreEqual(points.Length, found.Count, "a radius over the whole box visits every point");
                for (var i = 1; i < found.Count; i++)
                {
                    var previous = layout.GetIndex(layout.WorldToCell(points[found[i - 1]]));
                    var current = layout.GetIndex(layout.WorldToCell(points[found[i]]));
                    Assert.IsTrue(previous < current || previous == current && found[i - 1] < found[i], "cells in order, points of a cell by index");
                }
            }
        }

        [Test]
        public void FarApartPoints_GetLargerCells()
        {
            var corners = new[] { new FixedVector2(-30000, -30000), new FixedVector2(30000, 30000), new FixedVector2(1, 1) };
            using (var points = new NativeArray<FixedVector2>(corners, Allocator.Temp))
            {
                using (var index = new LockstepSpatialIndex(points, FixedPoint.Half, 1024, Allocator.Temp))
                {
                    Assert.LessOrEqual(index.Layout.CellCount, 1024, "the cells stay within the limit");
                    CollectionAssert.AreEquivalent(new[] { 2 }, Collect(index.Query(FixedVector2.Zero, 2)).FindAll(i => FixedMath.Distance(points[i], FixedVector2.Zero) <= 2));
                    CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, Collect(index.Query(FixedVector2.Zero, FixedPoint.MaxValue)), "the largest radius covers all");
                }
            }
        }

        [Test]
        public void EmptyIndex_FindsNothing()
        {
            using (var points = new NativeArray<FixedVector2>(0, Allocator.Temp))
            using (var index = new LockstepSpatialIndex(points, 4, 64, Allocator.Temp))
            {
                Assert.AreEqual(0, index.Count);
                Assert.IsEmpty(Collect(index.Query(FixedVector2.Zero, 100)));
                Assert.IsEmpty(Collect(default(LockstepSpatialIndex).Query(FixedVector2.Zero, 100)), "a default index is empty too");
            }
        }

        [Test]
        public void QueryAwayFromThePoints_FindsNothing()
        {
            using (var points = CreatePoints(50, 10, 4))
            using (var index = new LockstepSpatialIndex(points, 2, 1 << 12, Allocator.Temp))
            {
                Assert.IsEmpty(Collect(index.Query(new FixedVector2(500, -500), 20)));
                Assert.IsEmpty(Collect(index.Query(FixedVector2.Zero, FixedPoint.MinusOne)), "a negative radius finds nothing");
            }
        }
    }
}
