using System;
using Unity.Mathematics;

namespace Pragma.Dots.Mathematics
{
    public static partial class FixedMath
    {
        /// <summary>
        /// Which side of the line from <paramref name="a"/> through <paramref name="b"/> the point <paramref name="c"/> is on:
        /// 1 to the left (counter-clockwise), -1 to the right, 0 on the line. Exact for any coordinates.
        /// </summary>
        public static int Orientation(FixedVector2 a, FixedVector2 b, FixedVector2 c)
        {
            return CompareProducts(
                b.x.rawValue - a.x.rawValue, c.y.rawValue - a.y.rawValue,
                b.y.rawValue - a.y.rawValue, c.x.rawValue - a.x.rawValue);
        }

        /// <summary>
        /// Whether a point is inside a simple polygon given by its vertices in order (either winding), by the even-odd
        /// rule. Exact for any coordinates; a point exactly on an edge may count as either side.
        /// </summary>
        public static bool PolygonContains(ReadOnlySpan<FixedVector2> polygon, FixedVector2 point)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                var vi = polygon[i];
                var vj = polygon[j];
                if (vi.y.rawValue > point.y.rawValue == vj.y.rawValue > point.y.rawValue)
                {
                    continue;
                }
                // The edge crosses the horizontal line through the point; it counts when the crossing is right of the point.
                var dy = vi.y.rawValue - vj.y.rawValue;
                var side = CompareProducts(
                    point.x.rawValue - vj.x.rawValue, dy,
                    point.y.rawValue - vj.y.rawValue, vi.x.rawValue - vj.x.rawValue);
                if (dy > 0 ? side < 0 : side > 0)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        // Sign of a * b - c * d, exact for any long values: the products are compared as 128-bit numbers.
        internal static int CompareProducts(long a, long b, long c, long d)
        {
            SignedProduct(a, b, out var negative1, out var hi1, out var lo1);
            SignedProduct(c, d, out var negative2, out var hi2, out var lo2);
            if (negative1 != negative2)
            {
                return negative1 ? -1 : 1;
            }
            var magnitude = hi1 != hi2 ? (hi1 < hi2 ? -1 : 1) : lo1 != lo2 ? (lo1 < lo2 ? -1 : 1) : 0;
            return negative1 ? -magnitude : magnitude;
        }

        // Sign of x² + y² - r², exact for any long values.
        internal static int CompareLengthSquared(long x, long y, long r)
        {
            Multiply(UnsignedAbs(x), UnsignedAbs(x), out var xHi, out var xLo);
            Multiply(UnsignedAbs(y), UnsignedAbs(y), out var yHi, out var yLo);
            var lo = xLo + yLo;
            var hi = xHi + yHi + (lo < xLo ? 1UL : 0UL);
            Multiply(UnsignedAbs(r), UnsignedAbs(r), out var rHi, out var rLo);
            return hi != rHi ? (hi < rHi ? -1 : 1) : lo != rLo ? (lo < rLo ? -1 : 1) : 0;
        }

        // The right shift that brings every value below 2^31 in magnitude, so that a product of two fits in 63 bits.
        internal static int ShiftBelow31Bits(long a, long b, long c, long d)
        {
            var largest = math.max(math.max(UnsignedAbs(a), UnsignedAbs(b)), math.max(UnsignedAbs(c), UnsignedAbs(d)));
            var bits = 64 - math.lzcnt(largest);
            return bits > 31 ? bits - 31 : 0;
        }

        // The parameter in [0, 1] (raw) of the point of segment a→b closest to p.
        internal static long ClosestParameterRaw(FixedVector2 a, FixedVector2 b, FixedVector2 p)
        {
            ClosestRatio(a, b, p, out var dot, out var lengthSquared);
            return dot == lengthSquared ? FixedPoint.ONE_RAW : DivideRaw(dot, lengthSquared);
        }

        // The point of segment a→b closest to p. The offset from a is a 128-bit delta · dot / length², so it is
        // accurate to a step for any length, unlike a rounded parameter times the delta.
        internal static FixedVector2 ClosestPoint(FixedVector2 a, FixedVector2 b, FixedVector2 p)
        {
            ClosestRatio(a, b, p, out var dot, out var lengthSquared);
            return a + Scale(b - a, dot, lengthSquared);
        }

        // delta · numerator / denominator per component, with a 128-bit intermediate; 0 <= numerator <= denominator.
        internal static FixedVector2 Scale(FixedVector2 delta, long numerator, long denominator)
        {
            if (numerator == 0)
            {
                return FixedVector2.Zero;
            }
            if (numerator == denominator)
            {
                return delta;
            }
            var n = FixedPoint.FromRaw(numerator);
            var d = FixedPoint.FromRaw(denominator);
            return new FixedVector2(MulDiv(delta.x, n, d), MulDiv(delta.y, n, d));
        }

        // Where p projects onto segment a→b, as dot / lengthSquared clamped to [0, 1]; both are on scaled raw values,
        // 0 / 1 for a point segment.
        private static void ClosestRatio(FixedVector2 a, FixedVector2 b, FixedVector2 p, out long dot, out long lengthSquared)
        {
            var dx = b.x.rawValue - a.x.rawValue;
            var dy = b.y.rawValue - a.y.rawValue;
            var px = p.x.rawValue - a.x.rawValue;
            var py = p.y.rawValue - a.y.rawValue;
            var shift = ShiftBelow31Bits(dx, dy, px, py);
            dx >>= shift;
            dy >>= shift;
            px >>= shift;
            py >>= shift;
            lengthSquared = dx * dx + dy * dy;
            dot = dx * px + dy * py;
            if (lengthSquared == 0 || dot <= 0)
            {
                dot = 0;
                lengthSquared = 1;
            }
            else if (dot > lengthSquared)
            {
                dot = lengthSquared;
            }
        }

        private static void SignedProduct(long a, long b, out bool negative, out ulong hi, out ulong lo)
        {
            Multiply(UnsignedAbs(a), UnsignedAbs(b), out hi, out lo);
            negative = (a < 0) ^ (b < 0) && (hi | lo) != 0;
        }
    }
}
