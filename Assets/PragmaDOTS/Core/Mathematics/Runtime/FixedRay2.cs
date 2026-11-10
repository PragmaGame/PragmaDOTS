using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Serialization;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// A ray of <see cref="FixedVector2"/>: an origin and a unit direction, such as a line of sight or a hitscan shot on
    /// the XZ plane. The intersection tests return the distance from the origin to the first hit.
    /// </summary>
    /// <remarks>
    /// An origin inside the shape hits at distance 0. The distances are computed on scaled raw values, so far targets
    /// do not overflow; circles take radii up to about 46000.
    /// </remarks>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    [DataContract]
    public struct FixedRay2 : IEquatable<FixedRay2>, IFormattable
    {
        [DataMember] public FixedVector2 origin;
        /// <summary>Unit length, or zero for a ray that goes nowhere.</summary>
        [DataMember] public FixedVector2 direction;

        /// <summary>A ray from <paramref name="origin"/> along <paramref name="direction"/>, which is normalized.</summary>
        public FixedRay2(FixedVector2 origin, FixedVector2 direction)
        {
            this.origin = origin;
            this.direction = FixedMath.Normalize(direction);
        }

        public FixedVector2 GetPoint(FixedPoint distance) => origin + direction * distance;

        public bool TryIntersect(FixedCircle circle, out FixedPoint distance)
        {
            if (direction == FixedVector2.Zero)
            {
                distance = FixedPoint.Zero;
                return circle.Contains(origin);
            }
            return TryIntersectUnit(origin, direction, circle, out distance);
        }

        public bool TryIntersect(FixedBounds2 bounds, out FixedPoint distance)
        {
            distance = FixedPoint.Zero;
            if (bounds.Contains(origin))
            {
                return true;
            }
            var enter = FixedPoint.MinValue;
            var exit = FixedPoint.MaxValue;
            if (!Slab(origin.x, direction.x, bounds.min.x, bounds.max.x, ref enter, ref exit)
                || !Slab(origin.y, direction.y, bounds.min.y, bounds.max.y, ref enter, ref exit)
                || exit < enter || exit.rawValue < 0)
            {
                return false;
            }
            distance = FixedMath.Max(enter, FixedPoint.Zero);
            return true;
        }

        public bool TryIntersect(FixedSegment2 segment, out FixedPoint distance)
        {
            distance = FixedPoint.Zero;
            if (direction == FixedVector2.Zero)
            {
                return FixedMath.Orientation(segment.start, segment.end, origin) == 0 && segment.Bounds.Contains(origin);
            }

            long wx = segment.start.x.rawValue - origin.x.rawValue, wy = segment.start.y.rawValue - origin.y.rawValue;
            long sx = segment.end.x.rawValue - segment.start.x.rawValue, sy = segment.end.y.rawValue - segment.start.y.rawValue;
            var shift = FixedMath.ShiftBelow31Bits(wx, wy, sx, sy);
            wx >>= shift;
            wy >>= shift;
            sx >>= shift;
            sy >>= shift;
            long dx = direction.x.rawValue, dy = direction.y.rawValue;

            // origin + d t = start + s u: t = (w × s) / (d × s), u = (w × d) / (d × s).
            var denominator = dx * sy - dy * sx;
            var tNumerator = wx * sy - wy * sx;
            var uNumerator = wx * dy - wy * dx;
            if (denominator == 0)
            {
                return FixedMath.Orientation(segment.start, segment.end, origin) == 0 && TryIntersectCollinear(segment, out distance);
            }
            if (denominator < 0)
            {
                denominator = -denominator;
                tNumerator = -tNumerator;
                uNumerator = -uNumerator;
            }
            if (tNumerator < 0 || uNumerator < 0 || uNumerator > denominator)
            {
                return false;
            }
            // The numerator carries the scale twice and the denominator once: shift the quotient back.
            distance = FixedPoint.FromRaw(FixedMath.DivideRaw(tNumerator, denominator) << shift);
            return true;
        }

        // The distance along a unit direction from an origin to a circle; an origin inside gives 0.
        internal static bool TryIntersectUnit(FixedVector2 origin, FixedVector2 direction, FixedCircle circle, out FixedPoint distance)
        {
            distance = FixedPoint.Zero;
            if (circle.Contains(origin))
            {
                return true;
            }
            var toCenter = circle.center - origin;
            var along = FixedMath.Dot(toCenter, direction);
            if (along.rawValue < 0)
            {
                return false;
            }
            // Comparing the distance of the centre from the line with the radius needs no squares of far distances.
            var offLine = FixedMath.Abs(FixedMath.Cross(toCenter, direction));
            if (offLine > circle.radius)
            {
                return false;
            }
            var halfChord = FixedMath.Sqrt((circle.radius - offLine) * (circle.radius + offLine));
            distance = FixedMath.Max(along - halfChord, FixedPoint.Zero);
            return true;
        }

        private bool TryIntersectCollinear(FixedSegment2 segment, out FixedPoint distance)
        {
            var toStart = FixedMath.Dot(segment.start - origin, direction);
            var toEnd = FixedMath.Dot(segment.end - origin, direction);
            distance = FixedPoint.Zero;
            if (toStart.rawValue < 0 && toEnd.rawValue < 0)
            {
                return false;
            }
            if (toStart.rawValue >= 0 && toEnd.rawValue >= 0)
            {
                distance = FixedMath.Min(toStart, toEnd);
            }
            return true;
        }

        private static bool Slab(FixedPoint origin, FixedPoint direction, FixedPoint min, FixedPoint max, ref FixedPoint enter, ref FixedPoint exit)
        {
            if (direction.rawValue == 0)
            {
                return origin >= min && origin <= max;
            }
            var t1 = (min - origin) / direction;
            var t2 = (max - origin) / direction;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }
            enter = FixedMath.Max(enter, t1);
            exit = FixedMath.Min(exit, t2);
            return true;
        }

        public static bool operator ==(FixedRay2 a, FixedRay2 b) => a.origin == b.origin && a.direction == b.direction;

        public static bool operator !=(FixedRay2 a, FixedRay2 b) => !(a == b);

        public bool Equals(FixedRay2 other) => this == other;

        public override bool Equals(object obj) => obj is FixedRay2 other && this == other;

        public override int GetHashCode() => unchecked(origin.GetHashCode() * 397 ^ direction.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedRay2({origin.ToString(format, formatProvider)}, {direction.ToString(format, formatProvider)})";
        }
    }
}
