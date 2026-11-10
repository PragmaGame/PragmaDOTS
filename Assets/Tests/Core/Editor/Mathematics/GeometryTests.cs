using System;
using System.Numerics;
using NUnit.Framework;
using Pragma.Dots.Mathematics;

namespace Pragma.Dots.Tests
{
    public class GeometryTests
    {
        [Test]
        public void Predicates_AreExact_ForAnyCoordinates()
        {
            var random = new System.Random(21);
            for (var i = 0; i < 20000; i++)
            {
                long a = NextLong(random), b = NextLong(random), c = NextLong(random), d = NextLong(random);
                var expected = (new BigInteger(a) * b - new BigInteger(c) * d).Sign;
                Assert.AreEqual(expected, FixedMath.CompareProducts(a, b, c, d), $"{a}*{b} - {c}*{d}");

                long x = NextLong(random) >> 1, y = NextLong(random) >> 1, r = NextLong(random) >> 1;
                var lengthSquared = (new BigInteger(x) * x + new BigInteger(y) * y - new BigInteger(r) * r).Sign;
                Assert.AreEqual(lengthSquared, FixedMath.CompareLengthSquared(x, y, r), $"{x}² + {y}² - {r}²");
            }

            // Points 1e12 apart with a turn of one step: no float or plain product could tell.
            var far = new FixedVector2((FixedPoint)1_000_000_000_000L, (FixedPoint)1_000_000_000_000L);
            var corner = FixedPoint.FromRaw(far.y.rawValue + 1);
            Assert.AreEqual(1, FixedMath.Orientation(FixedVector2.Zero, far, new FixedVector2(far.x, corner)));
            Assert.AreEqual(-1, FixedMath.Orientation(FixedVector2.Zero, far, new FixedVector2(corner, far.y)));
            Assert.AreEqual(0, FixedMath.Orientation(FixedVector2.Zero, far, far * 2));
        }

        [Test]
        public void Circle_ContainsAndOverlaps_Exactly()
        {
            var random = new FixedRandom(22);
            for (var i = 0; i < 5000; i++)
            {
                var circle = new FixedCircle(random.NextVector2(new FixedVector2(-100000), new FixedVector2(100000)), random.NextFixedPoint(0, 60000));
                var point = random.NextVector2(new FixedVector2(-160000), new FixedVector2(160000));
                Assert.AreEqual(ReferenceContains(circle, point), circle.Contains(point), $"{circle} {point}");

                var other = new FixedCircle(random.NextVector2(new FixedVector2(-100000), new FixedVector2(100000)), random.NextFixedPoint(0, 60000));
                var reach = new FixedCircle(circle.center, circle.radius + other.radius);
                Assert.AreEqual(ReferenceContains(reach, other.center), circle.Overlaps(other));
            }

            var unit = new FixedCircle(new FixedVector2(1_000_000_000, -1_000_000_000), 5);
            Assert.IsTrue(unit.Contains(unit.center + new FixedVector2(3, 4)), "the edge belongs to the circle, far from the origin too");
            Assert.IsFalse(unit.Contains(unit.center + new FixedVector2(3, FixedPoint.FromRaw(4 * FixedPoint.ONE_RAW + 1))));
            Assert.IsTrue(unit.Overlaps(new FixedCircle(unit.center + new FixedVector2(8, 6), 5)), "touching circles overlap");
        }

        [Test]
        public void Bounds_CoverTheBasics()
        {
            var bounds = FixedBounds2.FromCenterSize(new FixedVector2(10, 20), new FixedVector2(4, 6));
            Assert.AreEqual(new FixedBounds2(new FixedVector2(8, 17), new FixedVector2(12, 23)), bounds);
            Assert.AreEqual(new FixedVector2(10, 20), bounds.Center);
            Assert.AreEqual(new FixedVector2(2, 3), bounds.Extents);
            Assert.IsTrue(bounds.Contains(new FixedVector2(12, 23)), "corners belong to it");
            Assert.IsFalse(bounds.Contains(new FixedVector2(FixedPoint.FromRaw(12 * FixedPoint.ONE_RAW + 1), 20)));
            Assert.IsTrue(bounds.Overlaps(new FixedBounds2(new FixedVector2(12, 23), new FixedVector2(20, 30))), "touching overlaps");
            Assert.AreEqual(new FixedVector2(12, 17), bounds.ClosestPoint(new FixedVector2(50, -50)));
            Assert.AreEqual(new FixedBounds2(new FixedVector2(0, 17), new FixedVector2(12, 23)), bounds.Encapsulate(new FixedVector2(0, 20)));
            Assert.AreEqual(new FixedBounds2(new FixedVector2(7, 16), new FixedVector2(13, 24)), bounds.Expand(1));
            Assert.AreEqual(FixedBounds2.FromPoints(new FixedVector2(5, 1), new FixedVector2(1, 5)), new FixedBounds2(new FixedVector2(1, 1), new FixedVector2(5, 5)));
            Assert.IsFalse(new FixedBounds2(new FixedVector2(1, 1), new FixedVector2(0, 2)).IsValid);

            Assert.IsTrue(new FixedCircle(new FixedVector2(15, 26), 5).Overlaps(bounds), "reaches the corner (12, 23) at distance 3√2");
            Assert.IsFalse(new FixedCircle(new FixedVector2(16, 27), 5).Overlaps(bounds), "corner at distance 4√2");
        }

        [Test]
        public void Segment_ClosestPointAndDistance_MatchADoubleReference()
        {
            var random = new FixedRandom(23);
            foreach (var reach in new[] { 10, 10000, 1000000 })
            {
                for (var i = 0; i < 2000; i++)
                {
                    var segment = new FixedSegment2(random.NextVector2(new FixedVector2(-reach), new FixedVector2(reach)), random.NextVector2(new FixedVector2(-reach), new FixedVector2(reach)));
                    var point = random.NextVector2(new FixedVector2(-reach), new FixedVector2(reach));
                    var expected = ReferenceDistance(segment, point);
                    Assert.AreEqual(expected, (double)segment.Distance(point), 1e-4 * reach + 1e-3, $"{segment} {point}");
                }
            }

            var horizontal = new FixedSegment2(new FixedVector2(0, 0), new FixedVector2(10, 0));
            Assert.AreEqual(new FixedVector2(4, 0), horizontal.ClosestPoint(new FixedVector2(4, 7)));
            Assert.AreEqual(FixedPoint.Zero, horizontal.ClosestParameter(new FixedVector2(-5, 1)));
            Assert.AreEqual(FixedPoint.One, horizontal.ClosestParameter(new FixedVector2(50, 1)));
            Assert.AreEqual((FixedPoint)5, new FixedSegment2(new FixedVector2(3, 3), new FixedVector2(3, 3)).Distance(new FixedVector2(6, 7)), "a point segment");
        }

        [Test]
        public void Segments_Intersect_LikeADoubleReference()
        {
            var random = new FixedRandom(24);
            var checkedCount = 0;
            for (var i = 0; i < 20000; i++)
            {
                var a = new FixedSegment2(random.NextVector2(new FixedVector2(-100), new FixedVector2(100)), random.NextVector2(new FixedVector2(-100), new FixedVector2(100)));
                var b = new FixedSegment2(random.NextVector2(new FixedVector2(-100), new FixedVector2(100)), random.NextVector2(new FixedVector2(-100), new FixedVector2(100)));
                if (!ReferenceIntersection(a, b, out var hit, out var x, out var y))
                {
                    continue;
                }
                checkedCount++;
                Assert.AreEqual(hit, a.TryIntersect(b, out var point), $"{a} {b}");
                if (hit)
                {
                    Assert.AreEqual(x, (double)point.x, 1e-3, $"{a} {b}");
                    Assert.AreEqual(y, (double)point.y, 1e-3, $"{a} {b}");
                    Assert.AreEqual(FixedPoint.Zero, a.Distance(b));
                }
            }
            Assert.Greater(checkedCount, 19000, "the clear cases were checked");

            var a0 = new FixedSegment2(new FixedVector2(0, 0), new FixedVector2(10, 0));
            Assert.IsTrue(a0.TryIntersect(new FixedSegment2(new FixedVector2(10, 0), new FixedVector2(10, 5)), out var touch), "ends touch");
            Assert.AreEqual(new FixedVector2(10, 0), touch);
            Assert.IsTrue(a0.TryIntersect(new FixedSegment2(new FixedVector2(12, 0), new FixedVector2(4, 0)), out var overlap), "collinear overlap");
            Assert.AreEqual(new FixedVector2(4, 0), overlap, "the start of the overlap");
            Assert.IsFalse(a0.Intersects(new FixedSegment2(new FixedVector2(11, 0), new FixedVector2(20, 0))), "collinear apart");
            Assert.IsFalse(a0.Intersects(new FixedSegment2(new FixedVector2(0, 1), new FixedVector2(10, 1))), "parallel");
            Assert.AreEqual((FixedPoint)1, a0.Distance(new FixedSegment2(new FixedVector2(0, 1), new FixedVector2(10, 1))));

            var far = new FixedVector2(2_000_000_000, 2_000_000_000);
            Assert.IsTrue(new FixedSegment2(-far, far).TryIntersect(new FixedSegment2(new FixedVector2(-far.x, far.y), new FixedVector2(far.x, -far.y)), out var crossing), "long segments do not overflow");
            Assert.AreEqual(FixedVector2.Zero, crossing);
        }

        [Test]
        public void Ray_HitsCirclesBoundsAndSegments_LikeADoubleReference()
        {
            var random = new FixedRandom(25);
            for (var i = 0; i < 5000; i++)
            {
                var ray = new FixedRay2(random.NextVector2(new FixedVector2(-500), new FixedVector2(500)), random.NextDirection2());
                var circle = new FixedCircle(random.NextVector2(new FixedVector2(-500), new FixedVector2(500)), random.NextFixedPoint(1, 200));
                if (ReferenceRayCircle(ray, circle, out var hit, out var expected))
                {
                    Assert.AreEqual(hit, ray.TryIntersect(circle, out var distance), $"{ray} {circle}");
                    if (hit)
                    {
                        Assert.AreEqual(expected, (double)distance, RayTolerance(expected), $"{ray} {circle}");
                    }
                }

                var bounds = FixedBounds2.FromPoints(random.NextVector2(new FixedVector2(-500), new FixedVector2(500)), random.NextVector2(new FixedVector2(-500), new FixedVector2(500)));
                if (ReferenceRayBounds(ray, bounds, out hit, out expected))
                {
                    Assert.AreEqual(hit, ray.TryIntersect(bounds, out var distance), $"{ray} {bounds}");
                    if (hit)
                    {
                        Assert.AreEqual(expected, (double)distance, RayTolerance(expected), $"{ray} {bounds}");
                    }
                }

                var segment = new FixedSegment2(random.NextVector2(new FixedVector2(-500), new FixedVector2(500)), random.NextVector2(new FixedVector2(-500), new FixedVector2(500)));
                if (ReferenceRaySegment(ray, segment, out hit, out expected))
                {
                    Assert.AreEqual(hit, ray.TryIntersect(segment, out var distance), $"{ray} {segment}");
                    if (hit)
                    {
                        Assert.AreEqual(expected, (double)distance, RayTolerance(expected), $"{ray} {segment}");
                    }
                }
            }

            var fromFar = new FixedRay2(new FixedVector2(-1_000_000_000, 3), new FixedVector2(1, 0));
            Assert.IsTrue(fromFar.TryIntersect(new FixedCircle(new FixedVector2(0, 0), 5), out var farDistance), "a far origin does not overflow");
            Assert.AreEqual(1_000_000_000 - 4.0, (double)farDistance, 1e-2);
            Assert.IsTrue(fromFar.TryIntersect(new FixedSegment2(new FixedVector2(0, -10), new FixedVector2(0, 10)), out farDistance));
            Assert.AreEqual(1e9, (double)farDistance, 1e-2);
            Assert.IsTrue(new FixedRay2(FixedVector2.Zero, new FixedVector2(1, 0)).TryIntersect(new FixedCircle(new FixedVector2(1, 1), 2), out var inside));
            Assert.AreEqual(FixedPoint.Zero, inside, "an origin inside hits at once");
            Assert.IsFalse(new FixedRay2(FixedVector2.Zero, FixedVector2.Zero).TryIntersect(new FixedCircle(new FixedVector2(5, 0), 1), out _), "a ray that goes nowhere");
        }

        [Test]
        public void Sweep_StopsWhereCirclesTouch()
        {
            var mover = new FixedCircle(new FixedVector2(0, 0), 1);
            var target = new FixedCircle(new FixedVector2(10, 0), 2);
            Assert.IsTrue(mover.TrySweep(new FixedVector2(20, 0), target, out var fraction));
            Assert.AreEqual(7.0 / 20, (double)fraction, 1e-4, "the edges meet after 7 of 20 units");
            Assert.IsFalse(mover.TrySweep(new FixedVector2(5, 0), target, out _), "stops short");
            Assert.IsFalse(mover.TrySweep(new FixedVector2(-20, 0), target, out _), "moves away");
            Assert.IsFalse(mover.TrySweep(new FixedVector2(20, 20), target, out _), "passes by");
            Assert.IsTrue(new FixedCircle(new FixedVector2(8, 0), 1).TrySweep(new FixedVector2(-20, 0), target, out fraction));
            Assert.AreEqual(FixedPoint.Zero, fraction, "already touching");

            var random = new FixedRandom(26);
            for (var i = 0; i < 2000; i++)
            {
                var a = new FixedCircle(random.NextVector2(new FixedVector2(-100), new FixedVector2(100)), random.NextFixedPoint(1, 10));
                var b = new FixedCircle(random.NextVector2(new FixedVector2(-100), new FixedVector2(100)), random.NextFixedPoint(1, 10));
                var movement = random.NextVector2(new FixedVector2(-200), new FixedVector2(200));
                if (a.TrySweep(movement, b, out fraction) && fraction.rawValue > 0)
                {
                    var moved = new FixedCircle(a.center + movement * fraction, a.radius);
                    Assert.AreEqual((double)(a.radius + b.radius), (double)FixedMath.Distance(moved.center, b.center), 2e-2, "they touch where the sweep stops");
                }
            }
        }

        [Test]
        public void TriangleAndPolygon_ContainPoints()
        {
            var triangle = new FixedTriangle2(new FixedVector2(0, 0), new FixedVector2(10, 0), new FixedVector2(0, 10));
            Assert.IsTrue(triangle.Contains(new FixedVector2(2, 2)));
            Assert.IsTrue(triangle.Contains(new FixedVector2(5, 5)), "on the hypotenuse");
            Assert.IsFalse(triangle.Contains(new FixedVector2(6, 5)));
            Assert.AreEqual((FixedPoint)50, triangle.SignedArea);
            Assert.AreEqual((FixedPoint)(-50), new FixedTriangle2(triangle.a, triangle.c, triangle.b).SignedArea, "clockwise");
            Assert.IsTrue(new FixedTriangle2(triangle.a, triangle.c, triangle.b).Contains(new FixedVector2(2, 2)), "either winding");
            Assert.IsFalse(new FixedTriangle2(FixedVector2.Zero, new FixedVector2(5, 5), new FixedVector2(10, 10)).Contains(new FixedVector2(5, 5)), "degenerate");

            // An L shape: the notch at the top right is outside.
            var shape = new[]
            {
                new FixedVector2(0, 0), new FixedVector2(10, 0), new FixedVector2(10, 4), new FixedVector2(4, 4), new FixedVector2(4, 10), new FixedVector2(0, 10),
            };
            Assert.IsTrue(FixedMath.PolygonContains(shape, new FixedVector2(2, 8)));
            Assert.IsTrue(FixedMath.PolygonContains(shape, new FixedVector2(8, 2)));
            Assert.IsFalse(FixedMath.PolygonContains(shape, new FixedVector2(8, 8)));
            Assert.IsFalse(FixedMath.PolygonContains(shape, new FixedVector2(-1, 5)));

            var random = new FixedRandom(27);
            for (var i = 0; i < 5000; i++)
            {
                var t = new FixedTriangle2(random.NextVector2(new FixedVector2(-50), new FixedVector2(50)), random.NextVector2(new FixedVector2(-50), new FixedVector2(50)), random.NextVector2(new FixedVector2(-50), new FixedVector2(50)));
                var p = random.NextVector2(new FixedVector2(-60), new FixedVector2(60));
                if (ReferenceTriangle(t, p, out var inside))
                {
                    Assert.AreEqual(inside, t.Contains(p), $"{t} {p}");
                    Assert.AreEqual(inside, FixedMath.PolygonContains(new[] { t.a, t.b, t.c }, p), $"polygon {t} {p}");
                }
            }
        }

        // A unit direction in Q16 is accurate to about 2e-5, and so is a distance along it.
        private static double RayTolerance(double distance) => 5e-5 * Math.Abs(distance) + 1e-2;

        private static long NextLong(System.Random random)
        {
            var bytes = new byte[8];
            random.NextBytes(bytes);
            return BitConverter.ToInt64(bytes, 0) >> random.Next(0, 40);
        }

        private static bool ReferenceContains(FixedCircle circle, FixedVector2 point)
        {
            var dx = new BigInteger(point.x.rawValue) - circle.center.x.rawValue;
            var dy = new BigInteger(point.y.rawValue) - circle.center.y.rawValue;
            return dx * dx + dy * dy <= new BigInteger(circle.radius.rawValue) * circle.radius.rawValue;
        }

        private static double ReferenceDistance(FixedSegment2 s, FixedVector2 p)
        {
            double ax = (double)s.start.x, ay = (double)s.start.y, bx = (double)s.end.x, by = (double)s.end.y, px = (double)p.x, py = (double)p.y;
            double dx = bx - ax, dy = by - ay;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared == 0 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / lengthSquared));
            return Math.Sqrt(Math.Pow(ax + dx * t - px, 2) + Math.Pow(ay + dy * t - py, 2));
        }

        // false when the case is too close to a touch for doubles to decide.
        private static bool ReferenceIntersection(FixedSegment2 a, FixedSegment2 b, out bool hit, out double x, out double y)
        {
            double px = (double)a.start.x, py = (double)a.start.y, rx = (double)a.end.x - px, ry = (double)a.end.y - py;
            double qx = (double)b.start.x, qy = (double)b.start.y, sx = (double)b.end.x - qx, sy = (double)b.end.y - qy;
            var denominator = rx * sy - ry * sx;
            hit = false;
            x = y = 0;
            if (Math.Abs(denominator) < 1e-3)
            {
                return false;
            }
            var t = ((qx - px) * sy - (qy - py) * sx) / denominator;
            var u = ((qx - px) * ry - (qy - py) * rx) / denominator;
            const double margin = 1e-4;
            if (Math.Abs(t) < margin || Math.Abs(t - 1) < margin || Math.Abs(u) < margin || Math.Abs(u - 1) < margin)
            {
                return false;
            }
            hit = t > 0 && t < 1 && u > 0 && u < 1;
            x = px + rx * t;
            y = py + ry * t;
            return true;
        }

        private static bool ReferenceRayCircle(FixedRay2 ray, FixedCircle circle, out bool hit, out double distance)
        {
            double ox = (double)ray.origin.x, oy = (double)ray.origin.y, dx = (double)ray.direction.x, dy = (double)ray.direction.y;
            double cx = (double)circle.center.x - ox, cy = (double)circle.center.y - oy, r = (double)circle.radius;
            var length = Math.Sqrt(dx * dx + dy * dy);
            dx /= length;
            dy /= length;
            var along = cx * dx + cy * dy;
            var offLine = Math.Abs(cx * dy - cy * dx);
            var inside = cx * cx + cy * cy <= r * r;
            hit = inside || (along >= 0 && offLine <= r);
            distance = inside ? 0 : along - Math.Sqrt(Math.Max(0, r * r - offLine * offLine));
            var nearEdge = Math.Abs(Math.Sqrt(cx * cx + cy * cy) - r) < 1e-2;
            // Near a tangent the chord amplifies the direction's rounding: those rays are checked by the exact cases.
            return Math.Abs(offLine - r) > 0.5 && Math.Abs(along) > 1e-2 && !nearEdge;
        }

        private static bool ReferenceRayBounds(FixedRay2 ray, FixedBounds2 bounds, out bool hit, out double distance)
        {
            double ox = (double)ray.origin.x, oy = (double)ray.origin.y, dx = (double)ray.direction.x, dy = (double)ray.direction.y;
            double minX = (double)bounds.min.x, minY = (double)bounds.min.y, maxX = (double)bounds.max.x, maxY = (double)bounds.max.y;
            var enter = double.NegativeInfinity;
            var exit = double.PositiveInfinity;
            hit = false;
            distance = 0;
            if (Math.Abs(dx) < 1e-3 || Math.Abs(dy) < 1e-3)
            {
                return false;
            }
            var tx1 = (minX - ox) / dx;
            var tx2 = (maxX - ox) / dx;
            var ty1 = (minY - oy) / dy;
            var ty2 = (maxY - oy) / dy;
            enter = Math.Max(Math.Min(tx1, tx2), Math.Min(ty1, ty2));
            exit = Math.Min(Math.Max(tx1, tx2), Math.Max(ty1, ty2));
            hit = exit >= enter && exit >= 0;
            distance = Math.Max(enter, 0);
            return Math.Abs(exit - enter) > 1e-2 && Math.Abs(exit) > 1e-2 && Math.Abs(enter) > 1e-2;
        }

        private static bool ReferenceRaySegment(FixedRay2 ray, FixedSegment2 segment, out bool hit, out double distance)
        {
            double ox = (double)ray.origin.x, oy = (double)ray.origin.y, dx = (double)ray.direction.x, dy = (double)ray.direction.y;
            double qx = (double)segment.start.x - ox, qy = (double)segment.start.y - oy, sx = (double)segment.end.x - (double)segment.start.x, sy = (double)segment.end.y - (double)segment.start.y;
            var denominator = dx * sy - dy * sx;
            hit = false;
            distance = 0;
            if (Math.Abs(denominator) < 1e-2)
            {
                return false;
            }
            var t = (qx * sy - qy * sx) / denominator;
            var u = (qx * dy - qy * dx) / denominator;
            hit = t >= 0 && u >= 0 && u <= 1;
            distance = t;
            return Math.Abs(t) > 1e-2 && Math.Abs(u) > 1e-4 && Math.Abs(u - 1) > 1e-4;
        }

        private static bool ReferenceTriangle(FixedTriangle2 t, FixedVector2 p, out bool inside)
        {
            double Cross(FixedVector2 a, FixedVector2 b, FixedVector2 c) => ((double)b.x - (double)a.x) * ((double)c.y - (double)a.y) - ((double)b.y - (double)a.y) * ((double)c.x - (double)a.x);
            var winding = Cross(t.a, t.b, t.c);
            var d1 = Cross(t.a, t.b, p) * Math.Sign(winding);
            var d2 = Cross(t.b, t.c, p) * Math.Sign(winding);
            var d3 = Cross(t.c, t.a, p) * Math.Sign(winding);
            inside = d1 > 0 && d2 > 0 && d3 > 0;
            return Math.Abs(winding) > 1 && Math.Abs(d1) > 1e-3 && Math.Abs(d2) > 1e-3 && Math.Abs(d3) > 1e-3;
        }
    }
}
