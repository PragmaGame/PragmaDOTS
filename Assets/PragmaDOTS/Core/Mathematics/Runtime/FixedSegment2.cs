using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Serialization;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// A line segment of <see cref="FixedVector2"/>, such as a projectile's path within a tick on the XZ plane, or the
    /// spine of a capsule: a capsule is a segment and a radius, tested with <see cref="Distance(FixedVector2)"/>.
    /// </summary>
    /// <remarks>
    /// Whether segments intersect is decided exactly for any coordinates; where they meet, the closest point and the
    /// distances are computed on scaled raw values, so long segments do not overflow.
    /// </remarks>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    [DataContract]
    public struct FixedSegment2 : IEquatable<FixedSegment2>, IFormattable
    {
        [DataMember] public FixedVector2 start;
        [DataMember] public FixedVector2 end;

        public FixedSegment2(FixedVector2 start, FixedVector2 end)
        {
            this.start = start;
            this.end = end;
        }

        public FixedVector2 Delta => end - start;

        public FixedPoint Length => FixedMath.Distance(start, end);

        public FixedBounds2 Bounds => FixedBounds2.FromPoints(start, end);

        /// <summary>The point at <paramref name="t"/> of the way from <see cref="start"/> to <see cref="end"/>.</summary>
        public FixedVector2 PointAt(FixedPoint t) => start + (end - start) * t;

        /// <summary>Where the point of the segment nearest to <paramref name="point"/> lies: 0 at the start, 1 at the end.</summary>
        public FixedPoint ClosestParameter(FixedVector2 point) => FixedPoint.FromRaw(FixedMath.ClosestParameterRaw(start, end, point));

        public FixedVector2 ClosestPoint(FixedVector2 point) => FixedMath.ClosestPoint(start, end, point);

        /// <summary>The distance from a point to the segment; a capsule of this segment contains the point when it is at most the radius.</summary>
        public FixedPoint Distance(FixedVector2 point) => FixedMath.Distance(point, ClosestPoint(point));

        /// <summary>The distance between two segments; zero when they intersect. Two capsules overlap when it is at most their radii together.</summary>
        public FixedPoint Distance(FixedSegment2 other)
        {
            if (Intersects(other))
            {
                return FixedPoint.Zero;
            }
            var fromOther = FixedMath.Min(Distance(other.start), Distance(other.end));
            var fromThis = FixedMath.Min(other.Distance(start), other.Distance(end));
            return FixedMath.Min(fromOther, fromThis);
        }

        public bool Intersects(FixedSegment2 other) => TryIntersect(other, out _);

        /// <summary>
        /// Whether the segments share a point, ends included, and the first such point from <see cref="start"/>. Exact
        /// for any coordinates; collinear segments that overlap give the start of the overlap.
        /// </summary>
        public bool TryIntersect(FixedSegment2 other, out FixedVector2 point)
        {
            var o1 = FixedMath.Orientation(start, end, other.start);
            var o2 = FixedMath.Orientation(start, end, other.end);
            if (o1 == 0 && o2 == 0)
            {
                return TryIntersectCollinear(other, out point);
            }

            var o3 = FixedMath.Orientation(other.start, other.end, start);
            var o4 = FixedMath.Orientation(other.start, other.end, end);
            if (o1 * o2 > 0 || o3 * o4 > 0)
            {
                point = default;
                return false;
            }

            long rx = end.x.rawValue - start.x.rawValue, ry = end.y.rawValue - start.y.rawValue;
            long sx = other.end.x.rawValue - other.start.x.rawValue, sy = other.end.y.rawValue - other.start.y.rawValue;
            long wx = other.start.x.rawValue - start.x.rawValue, wy = other.start.y.rawValue - start.y.rawValue;
            var shift = Math.Max(FixedMath.ShiftBelow31Bits(rx, ry, sx, sy), FixedMath.ShiftBelow31Bits(wx, wy, 0, 0));
            rx >>= shift;
            ry >>= shift;
            sx >>= shift;
            sy >>= shift;
            wx >>= shift;
            wy >>= shift;

            // start + r t = other.start + s u, so t = (w × s) / (r × s).
            var denominator = rx * sy - ry * sx;
            var numerator = wx * sy - wy * sx;
            if (denominator < 0)
            {
                denominator = -denominator;
                numerator = -numerator;
            }
            if (denominator == 0 || numerator <= 0)
            {
                point = start;
                return true;
            }
            point = start + FixedMath.Scale(end - start, Math.Min(numerator, denominator), denominator);
            return true;
        }

        /// <summary>Whether the segment touches or crosses a circle, or lies inside it.</summary>
        public bool Intersects(FixedCircle circle) => circle.Contains(ClosestPoint(circle.center));

        /// <summary>
        /// Where the segment, followed from <see cref="start"/>, first meets a circle: the fraction of its length in [0, 1].
        /// A start inside the circle gives 0. Takes radii up to about 46000.
        /// </summary>
        public bool TryIntersect(FixedCircle circle, out FixedPoint fraction)
        {
            fraction = FixedPoint.Zero;
            if (circle.Contains(start))
            {
                return true;
            }
            var delta = end - start;
            var length = FixedMath.Length(delta);
            if (length.rawValue == 0 || !FixedRay2.TryIntersectUnit(start, delta / length, circle, out var distance) || distance > length)
            {
                return false;
            }
            fraction = FixedMath.Min(distance / length, FixedPoint.One);
            return true;
        }

        // Both ends of the other segment are on this segment's line, or this segment is a single point.
        private bool TryIntersectCollinear(FixedSegment2 other, out FixedVector2 point)
        {
            point = default;
            if (start == end)
            {
                point = start;
                return FixedMath.Orientation(other.start, other.end, start) == 0 && other.Bounds.Contains(start);
            }
            if (!Bounds.Overlaps(other.Bounds))
            {
                return false;
            }
            if (other.Bounds.Contains(start))
            {
                point = start;
                return true;
            }
            // The overlap starts at whichever end of the other segment lies on this one nearer to the start.
            var bounds = Bounds;
            var startIn = bounds.Contains(other.start);
            var endIn = bounds.Contains(other.end);
            if (startIn && endIn)
            {
                var toStart = FixedMath.ClosestParameterRaw(start, end, other.start);
                var toEnd = FixedMath.ClosestParameterRaw(start, end, other.end);
                point = toStart <= toEnd ? other.start : other.end;
            }
            else
            {
                point = startIn ? other.start : other.end;
            }
            return true;
        }

        public static bool operator ==(FixedSegment2 a, FixedSegment2 b) => a.start == b.start && a.end == b.end;

        public static bool operator !=(FixedSegment2 a, FixedSegment2 b) => !(a == b);

        public bool Equals(FixedSegment2 other) => this == other;

        public override bool Equals(object obj) => obj is FixedSegment2 other && this == other;

        public override int GetHashCode() => unchecked(start.GetHashCode() * 397 ^ end.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedSegment2({start.ToString(format, formatProvider)}, {end.ToString(format, formatProvider)})";
        }
    }
}
