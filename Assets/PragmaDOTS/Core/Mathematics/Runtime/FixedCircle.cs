using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Serialization;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// A circle of <see cref="FixedVector2"/>, such as a unit's footprint or an area of effect on the XZ plane. Its edge
    /// belongs to it: a point on the edge is contained, and circles that touch overlap.
    /// </summary>
    /// <remarks>
    /// Containment and overlap are exact for any coordinates: they compare squared distances as 128-bit numbers.
    /// <see cref="TrySweep"/> and the ray tests take radii up to about 46000.
    /// </remarks>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    [DataContract]
    public struct FixedCircle : IEquatable<FixedCircle>, IFormattable
    {
        [DataMember] public FixedVector2 center;
        [DataMember] public FixedPoint radius;

        public FixedCircle(FixedVector2 center, FixedPoint radius)
        {
            this.center = center;
            this.radius = radius;
        }

        public FixedBounds2 Bounds => new FixedBounds2(center - new FixedVector2(radius), center + new FixedVector2(radius));

        public bool Contains(FixedVector2 point)
        {
            return FixedMath.CompareLengthSquared(point.x.rawValue - center.x.rawValue, point.y.rawValue - center.y.rawValue, radius.rawValue) <= 0;
        }

        public bool Overlaps(FixedCircle other)
        {
            return FixedMath.CompareLengthSquared(
                other.center.x.rawValue - center.x.rawValue, other.center.y.rawValue - center.y.rawValue, radius.rawValue + other.radius.rawValue) <= 0;
        }

        public bool Overlaps(FixedBounds2 bounds) => Contains(bounds.ClosestPoint(center));

        /// <summary>The point of the circle nearest to <paramref name="point"/>: the point itself when inside.</summary>
        public FixedVector2 ClosestPoint(FixedVector2 point)
        {
            if (Contains(point))
            {
                return point;
            }
            return center + FixedMath.Normalize(point - center) * radius;
        }

        /// <summary>
        /// Moves this circle by <paramref name="movement"/> and finds where it first touches <paramref name="target"/>:
        /// the fraction of the movement in [0, 1]. Already touching gives 0; <c>false</c> when it never touches.
        /// </summary>
        public bool TrySweep(FixedVector2 movement, FixedCircle target, out FixedPoint fraction)
        {
            var reach = new FixedCircle(target.center, radius + target.radius);
            return new FixedSegment2(center, center + movement).TryIntersect(reach, out fraction);
        }

        public static bool operator ==(FixedCircle a, FixedCircle b) => a.center == b.center && a.radius == b.radius;

        public static bool operator !=(FixedCircle a, FixedCircle b) => !(a == b);

        public bool Equals(FixedCircle other) => this == other;

        public override bool Equals(object obj) => obj is FixedCircle other && this == other;

        public override int GetHashCode() => unchecked(center.GetHashCode() * 397 ^ radius.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedCircle({center.ToString(format, formatProvider)}, {radius.ToString(format, formatProvider)})";
        }
    }
}
