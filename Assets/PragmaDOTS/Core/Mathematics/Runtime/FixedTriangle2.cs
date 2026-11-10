using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Serialization;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// A triangle of <see cref="FixedVector2"/>, such as a cone of vision or a navigation cell on the XZ plane. Its edges
    /// belong to it. For other polygons see <see cref="FixedMath.PolygonContains"/>.
    /// </summary>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    [DataContract]
    public struct FixedTriangle2 : IEquatable<FixedTriangle2>, IFormattable
    {
        [DataMember] public FixedVector2 a;
        [DataMember] public FixedVector2 b;
        [DataMember] public FixedVector2 c;

        public FixedTriangle2(FixedVector2 a, FixedVector2 b, FixedVector2 c)
        {
            this.a = a;
            this.b = b;
            this.c = c;
        }

        /// <summary>The area, positive when a, b, c run counter-clockwise. Overflows for sides above about 46000.</summary>
        public FixedPoint SignedArea => FixedMath.Cross(b - a, c - a) / 2;

        public FixedPoint Area => FixedMath.Abs(SignedArea);

        public FixedVector2 Centroid => (a + b + c) / 3;

        public FixedBounds2 Bounds => new FixedBounds2(FixedMath.Min(a, FixedMath.Min(b, c)), FixedMath.Max(a, FixedMath.Max(b, c)));

        /// <summary>Whether the point is inside or on an edge. Exact for any coordinates; a degenerate triangle contains nothing.</summary>
        public bool Contains(FixedVector2 point)
        {
            var winding = FixedMath.Orientation(a, b, c);
            if (winding == 0)
            {
                return false;
            }
            return FixedMath.Orientation(a, b, point) * winding >= 0
                && FixedMath.Orientation(b, c, point) * winding >= 0
                && FixedMath.Orientation(c, a, point) * winding >= 0;
        }

        public static bool operator ==(FixedTriangle2 x, FixedTriangle2 y) => x.a == y.a && x.b == y.b && x.c == y.c;

        public static bool operator !=(FixedTriangle2 x, FixedTriangle2 y) => !(x == y);

        public bool Equals(FixedTriangle2 other) => this == other;

        public override bool Equals(object obj) => obj is FixedTriangle2 other && this == other;

        public override int GetHashCode() => unchecked((a.GetHashCode() * 397 ^ b.GetHashCode()) * 397 ^ c.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedTriangle2({a.ToString(format, formatProvider)}, {b.ToString(format, formatProvider)}, {c.ToString(format, formatProvider)})";
        }
    }
}
