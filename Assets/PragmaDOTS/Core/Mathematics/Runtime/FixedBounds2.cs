using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Serialization;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// An axis-aligned rectangle of <see cref="FixedVector2"/>, such as an area on the XZ plane. Its edges belong to it:
    /// a point on an edge is contained, and rectangles that touch overlap.
    /// </summary>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    [DataContract]
    public struct FixedBounds2 : IEquatable<FixedBounds2>, IFormattable
    {
        [DataMember] public FixedVector2 min;
        [DataMember] public FixedVector2 max;

        /// <summary>A rectangle from its corners as given; see <see cref="FromPoints"/> for any two corners.</summary>
        public FixedBounds2(FixedVector2 min, FixedVector2 max)
        {
            this.min = min;
            this.max = max;
        }

        /// <summary>The rectangle with these two opposite corners, in any order.</summary>
        public static FixedBounds2 FromPoints(FixedVector2 a, FixedVector2 b) => new FixedBounds2(FixedMath.Min(a, b), FixedMath.Max(a, b));

        public static FixedBounds2 FromCenterSize(FixedVector2 center, FixedVector2 size)
        {
            var extents = size / 2;
            return new FixedBounds2(center - extents, center + extents);
        }

        public FixedVector2 Center => min + (max - min) / 2;

        public FixedVector2 Size => max - min;

        /// <summary>Half the size.</summary>
        public FixedVector2 Extents => (max - min) / 2;

        /// <summary>Whether <see cref="min"/> is not above <see cref="max"/> on either axis.</summary>
        public bool IsValid => min.x <= max.x && min.y <= max.y;

        public bool Contains(FixedVector2 point) => point.x >= min.x && point.x <= max.x && point.y >= min.y && point.y <= max.y;

        public bool Contains(FixedBounds2 other) => Contains(other.min) && Contains(other.max);

        public bool Overlaps(FixedBounds2 other) => min.x <= other.max.x && max.x >= other.min.x && min.y <= other.max.y && max.y >= other.min.y;

        public bool Overlaps(FixedCircle circle) => circle.Overlaps(this);

        /// <summary>The point of the rectangle nearest to <paramref name="point"/>: the point itself when inside.</summary>
        public FixedVector2 ClosestPoint(FixedVector2 point) => FixedMath.Clamp(point, min, max);

        /// <summary>The smallest rectangle that holds this one and <paramref name="point"/>.</summary>
        public FixedBounds2 Encapsulate(FixedVector2 point) => new FixedBounds2(FixedMath.Min(min, point), FixedMath.Max(max, point));

        public FixedBounds2 Encapsulate(FixedBounds2 other) => new FixedBounds2(FixedMath.Min(min, other.min), FixedMath.Max(max, other.max));

        /// <summary>The rectangle grown by <paramref name="amount"/> on every side; a negative amount shrinks it.</summary>
        public FixedBounds2 Expand(FixedPoint amount) => new FixedBounds2(min - new FixedVector2(amount), max + new FixedVector2(amount));

        public static bool operator ==(FixedBounds2 a, FixedBounds2 b) => a.min == b.min && a.max == b.max;

        public static bool operator !=(FixedBounds2 a, FixedBounds2 b) => !(a == b);

        public bool Equals(FixedBounds2 other) => this == other;

        public override bool Equals(object obj) => obj is FixedBounds2 other && this == other;

        public override int GetHashCode() => unchecked(min.GetHashCode() * 397 ^ max.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedBounds2({min.ToString(format, formatProvider)}, {max.ToString(format, formatProvider)})";
        }
    }
}
