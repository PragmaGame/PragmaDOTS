using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Pragma.Dots.Mathematics
{
    public static partial class FixedMath
    {
        // ---------- FixedVector4 ----------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 Abs(FixedVector4 v) => new FixedVector4(Abs(v.x), Abs(v.y), Abs(v.z), Abs(v.w));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 Min(FixedVector4 a, FixedVector4 b) => new FixedVector4(Min(a.x, b.x), Min(a.y, b.y), Min(a.z, b.z), Min(a.w, b.w));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 Max(FixedVector4 a, FixedVector4 b) => new FixedVector4(Max(a.x, b.x), Max(a.y, b.y), Max(a.z, b.z), Max(a.w, b.w));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 Clamp(FixedVector4 v, FixedVector4 a, FixedVector4 b) => Max(a, Min(b, v));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 Lerp(FixedVector4 a, FixedVector4 b, FixedPoint t) => a + (b - a) * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint Dot(FixedVector4 a, FixedVector4 b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        /// <summary>Squared length. Overflows for components above about 23000; prefer <see cref="Length(FixedVector4)"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedPoint LengthSquared(FixedVector4 v) => Dot(v, v);

        /// <summary>Length computed on raw values with automatic scaling, so it does not overflow.</summary>
        public static FixedPoint Length(FixedVector4 v) => FixedPoint.FromRaw(LengthRaw(v.x.rawValue, v.y.rawValue, v.z.rawValue, v.w.rawValue));

        public static FixedPoint Distance(FixedVector4 a, FixedVector4 b) => Length(b - a);

        /// <summary>Unit vector; zero for a zero vector.</summary>
        public static FixedVector4 Normalize(FixedVector4 v) => NormalizeSafe(v, FixedVector4.Zero);

        /// <summary>Unit vector, or <paramref name="defaultValue"/> when the vector is zero.</summary>
        public static FixedVector4 NormalizeSafe(FixedVector4 v, FixedVector4 defaultValue = default)
        {
            var len = Length(v);
            return len.rawValue == 0 ? defaultValue : new FixedVector4(v.x / len, v.y / len, v.z / len, v.w / len);
        }

        // ---------- FixedMatrix3x3 ----------

        /// <summary>Matrix product: applies <paramref name="b"/> first, then <paramref name="a"/>.</summary>
        public static FixedMatrix3x3 Mul(FixedMatrix3x3 a, FixedMatrix3x3 b) => new FixedMatrix3x3(Mul(a, b.c0), Mul(a, b.c1), Mul(a, b.c2));

        /// <summary>The column vector <paramref name="v"/> transformed by <paramref name="m"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 Mul(FixedMatrix3x3 m, FixedVector3 v) => m.c0 * v.x + m.c1 * v.y + m.c2 * v.z;

        public static FixedMatrix3x3 Transpose(FixedMatrix3x3 m)
        {
            return new FixedMatrix3x3(
                new FixedVector3(m.c0.x, m.c1.x, m.c2.x),
                new FixedVector3(m.c0.y, m.c1.y, m.c2.y),
                new FixedVector3(m.c0.z, m.c1.z, m.c2.z));
        }

        public static FixedPoint Determinant(FixedMatrix3x3 m) => Dot(m.c0, Cross(m.c1, m.c2));

        /// <summary>The inverse, or <c>false</c> and a zero matrix when the determinant is zero.</summary>
        /// <remarks>
        /// <para>Each element is accurate to a step at any scale: the cofactors and the determinant are taken on the columns
        /// shifted up to a length between 128 and 256 (exact shifts, undone on the rows of the inverse), and each element is
        /// one 128-bit division. Longer columns are taken as they are: the products of the column lengths have to stay
        /// below about 2·10⁹, so three columns of about 1300 are the limit.</para>
        /// <para>The inverse of a rotation is its transpose: <see cref="Transpose(FixedMatrix3x3)"/> is exact and cheaper.</para>
        /// </remarks>
        public static bool TryInverse(FixedMatrix3x3 m, out FixedMatrix3x3 inverse)
        {
            if (!TryAdjugate(m, out var rows, out var shifts, out var determinant))
            {
                inverse = FixedMatrix3x3.Zero;
                return false;
            }
            inverse = InverseOfAdjugate(rows, shifts, determinant);
            return true;
        }

        /// <summary>The inverse; a zero matrix when the determinant is zero.</summary>
        public static FixedMatrix3x3 Inverse(FixedMatrix3x3 m)
        {
            TryInverse(m, out var inverse);
            return inverse;
        }

        // ---------- FixedMatrix4x4 ----------

        /// <summary>Matrix product: applies <paramref name="b"/> first, then <paramref name="a"/>.</summary>
        public static FixedMatrix4x4 Mul(FixedMatrix4x4 a, FixedMatrix4x4 b) => new FixedMatrix4x4(Mul(a, b.c0), Mul(a, b.c1), Mul(a, b.c2), Mul(a, b.c3));

        /// <summary>The column vector <paramref name="v"/> transformed by <paramref name="m"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 Mul(FixedMatrix4x4 m, FixedVector4 v) => m.c0 * v.x + m.c1 * v.y + m.c2 * v.z + m.c3 * v.w;

        /// <summary>A point transformed by an affine matrix: rotated, scaled and translated. The bottom row is ignored.</summary>
        public static FixedVector3 TransformPoint(FixedMatrix4x4 m, FixedVector3 point)
        {
            return m.c0.Xyz * point.x + m.c1.Xyz * point.y + m.c2.Xyz * point.z + m.c3.Xyz;
        }

        /// <summary>A direction transformed by an affine matrix: rotated and scaled, not translated.</summary>
        public static FixedVector3 TransformDirection(FixedMatrix4x4 m, FixedVector3 direction)
        {
            return m.c0.Xyz * direction.x + m.c1.Xyz * direction.y + m.c2.Xyz * direction.z;
        }

        public static FixedMatrix4x4 Transpose(FixedMatrix4x4 m)
        {
            return new FixedMatrix4x4(
                new FixedVector4(m.c0.x, m.c1.x, m.c2.x, m.c3.x),
                new FixedVector4(m.c0.y, m.c1.y, m.c2.y, m.c3.y),
                new FixedVector4(m.c0.z, m.c1.z, m.c2.z, m.c3.z),
                new FixedVector4(m.c0.w, m.c1.w, m.c2.w, m.c3.w));
        }

        public static FixedPoint Determinant(FixedMatrix4x4 m)
        {
            Minors(m, out var s0, out var s1, out var s2, out var s3, out var s4, out var s5,
                out var k0, out var k1, out var k2, out var k3, out var k4, out var k5);
            return s0 * k5 - s1 * k4 + s2 * k3 + s3 * k2 - s4 * k1 + s5 * k0;
        }

        /// <summary>The inverse of any invertible matrix, or <c>false</c> and a zero matrix when the determinant is zero.</summary>
        /// <remarks>
        /// An affine matrix (bottom row 0, 0, 0, 1) goes through <see cref="TryInverseAffine"/>. Any other, such as a
        /// projection, goes through its cofactors, taken on the columns shifted up to a length between 16 and 32, and one
        /// 128-bit division per element: accurate to about 10⁻⁵ of each element. Its determinant multiplies four elements
        /// together, so elements above about 100 overflow it.
        /// </remarks>
        public static bool TryInverse(FixedMatrix4x4 m, out FixedMatrix4x4 inverse)
        {
            if (m.c0.w.rawValue == 0 && m.c1.w.rawValue == 0 && m.c2.w.rawValue == 0 && m.c3.w.rawValue == FixedPoint.ONE_RAW)
            {
                return TryInverseAffine(m, out inverse);
            }

            var shift0 = UpShift(m.c0.x.rawValue, m.c0.y.rawValue, m.c0.z.rawValue, m.c0.w.rawValue, INVERSE4_BITS);
            var shift1 = UpShift(m.c1.x.rawValue, m.c1.y.rawValue, m.c1.z.rawValue, m.c1.w.rawValue, INVERSE4_BITS);
            var shift2 = UpShift(m.c2.x.rawValue, m.c2.y.rawValue, m.c2.z.rawValue, m.c2.w.rawValue, INVERSE4_BITS);
            var shift3 = UpShift(m.c3.x.rawValue, m.c3.y.rawValue, m.c3.z.rawValue, m.c3.w.rawValue, INVERSE4_BITS);
            m = new FixedMatrix4x4(ShiftLeft(m.c0, shift0), ShiftLeft(m.c1, shift1), ShiftLeft(m.c2, shift2), ShiftLeft(m.c3, shift3));

            Minors(m, out var s0, out var s1, out var s2, out var s3, out var s4, out var s5,
                out var k0, out var k1, out var k2, out var k3, out var k4, out var k5);
            var determinant = s0 * k5 - s1 * k4 + s2 * k3 + s3 * k2 - s4 * k1 + s5 * k0;
            if (determinant.rawValue == 0)
            {
                inverse = FixedMatrix4x4.Zero;
                return false;
            }

            // Row R of the inverse is scaled back by the shift of column R.
            var row0 = ShiftScale(shift0);
            var row1 = ShiftScale(shift1);
            var row2 = ShiftScale(shift2);
            var row3 = ShiftScale(shift3);
            // mRC is row R, column C.
            FixedPoint m00 = m.c0.x, m10 = m.c0.y, m20 = m.c0.z, m30 = m.c0.w;
            FixedPoint m01 = m.c1.x, m11 = m.c1.y, m21 = m.c1.z, m31 = m.c1.w;
            FixedPoint m02 = m.c2.x, m12 = m.c2.y, m22 = m.c2.z, m32 = m.c2.w;
            FixedPoint m03 = m.c3.x, m13 = m.c3.y, m23 = m.c3.z, m33 = m.c3.w;
            inverse = new FixedMatrix4x4(
                MulDiv(m11 * k5 - m12 * k4 + m13 * k3, row0, determinant),
                MulDiv(-m01 * k5 + m02 * k4 - m03 * k3, row0, determinant),
                MulDiv(m31 * s5 - m32 * s4 + m33 * s3, row0, determinant),
                MulDiv(-m21 * s5 + m22 * s4 - m23 * s3, row0, determinant),
                MulDiv(-m10 * k5 + m12 * k2 - m13 * k1, row1, determinant),
                MulDiv(m00 * k5 - m02 * k2 + m03 * k1, row1, determinant),
                MulDiv(-m30 * s5 + m32 * s2 - m33 * s1, row1, determinant),
                MulDiv(m20 * s5 - m22 * s2 + m23 * s1, row1, determinant),
                MulDiv(m10 * k4 - m11 * k2 + m13 * k0, row2, determinant),
                MulDiv(-m00 * k4 + m01 * k2 - m03 * k0, row2, determinant),
                MulDiv(m30 * s4 - m31 * s2 + m33 * s0, row2, determinant),
                MulDiv(-m20 * s4 + m21 * s2 - m23 * s0, row2, determinant),
                MulDiv(-m10 * k3 + m11 * k1 - m12 * k0, row3, determinant),
                MulDiv(m00 * k3 - m01 * k1 + m02 * k0, row3, determinant),
                MulDiv(-m30 * s3 + m31 * s1 - m32 * s0, row3, determinant),
                MulDiv(m20 * s3 - m21 * s1 + m22 * s0, row3, determinant));
            return true;
        }

        /// <summary>The inverse; a zero matrix when the determinant is zero.</summary>
        public static FixedMatrix4x4 Inverse(FixedMatrix4x4 m)
        {
            TryInverse(m, out var inverse);
            return inverse;
        }

        /// <summary>
        /// The inverse of an affine transform (bottom row 0, 0, 0, 1): the inverse of its 3×3 part and the translation
        /// taken back through it. <c>false</c> and a zero matrix when the 3×3 part is singular, such as a zero scale.
        /// </summary>
        /// <remarks>
        /// The 3×3 part is computed like <see cref="TryInverse(FixedMatrix3x3, out FixedMatrix3x3)"/> and has its range.
        /// The translation is taken back with 128-bit products of the cofactors, so it is accurate to about a step
        /// rather than to a fraction of its size, and takes any translation.
        /// </remarks>
        public static bool TryInverseAffine(FixedMatrix4x4 m, out FixedMatrix4x4 inverse)
        {
            if (!TryAdjugate(m.Linear, out var rows, out var shifts, out var determinant))
            {
                inverse = FixedMatrix4x4.Zero;
                return false;
            }
            var t = m.Translation;
            var translation = new FixedVector3(
                TakeBack(rows.c0, t, shifts.x, determinant),
                TakeBack(rows.c1, t, shifts.y, determinant),
                TakeBack(rows.c2, t, shifts.z, determinant));
            inverse = new FixedMatrix4x4(InverseOfAdjugate(rows, shifts, determinant), translation);
            return true;
        }

        /// <summary>The inverse of an affine transform; a zero matrix when its 3×3 part is singular.</summary>
        public static FixedMatrix4x4 InverseAffine(FixedMatrix4x4 m)
        {
            TryInverseAffine(m, out var inverse);
            return inverse;
        }

        /// <summary>
        /// Splits an affine transform built by <see cref="FixedMatrix4x4.TRS"/> back into translation, rotation and
        /// scale. A mirror comes back as a negative x scale. <c>false</c> when a scale is zero; shear is not represented.
        /// </summary>
        public static bool TryDecompose(FixedMatrix4x4 m, out FixedVector3 translation, out FixedQuaternion rotation, out FixedVector3 scale)
        {
            translation = m.Translation;
            var x = m.c0.Xyz;
            var y = m.c1.Xyz;
            var z = m.c2.Xyz;
            scale = new FixedVector3(Length(x), Length(y), Length(z));
            if (scale.x.rawValue == 0 || scale.y.rawValue == 0 || scale.z.rawValue == 0)
            {
                rotation = FixedQuaternion.Identity;
                return false;
            }
            var right = x / scale.x;
            var up = y / scale.y;
            var forward = z / scale.z;
            // A mirror is a negative determinant. Taken on the unit axes it stays near ±1 for any scale: on the matrix
            // itself it would round to zero for a small scale and overflow for a large one.
            if (Dot(right, Cross(up, forward)).rawValue < 0)
            {
                scale.x = -scale.x;
                right = -right;
            }
            rotation = FixedQuaternion.FromBasis(right, up, forward);
            return true;
        }

        // Raw bits of the longest element of a column after the shift up: 2^23 to 2^24 is 128 to 256, where the 3×3
        // cofactors and determinant keep every bit and still fit; the 4×4 determinant multiplies four elements, so 16 to 32.
        private const int INVERSE3_BITS = 24;
        private const int INVERSE4_BITS = 21;

        // The adjugate of m by rows, taken on its columns shifted up by `shifts`, and the determinant of those columns:
        // row i of the inverse is rows[i] · 2^shifts[i] / determinant.
        private static bool TryAdjugate(FixedMatrix3x3 m, out FixedMatrix3x3 rows, out int3 shifts, out FixedPoint determinant)
        {
            shifts = new int3(
                UpShift(m.c0.x.rawValue, m.c0.y.rawValue, m.c0.z.rawValue, 0, INVERSE3_BITS),
                UpShift(m.c1.x.rawValue, m.c1.y.rawValue, m.c1.z.rawValue, 0, INVERSE3_BITS),
                UpShift(m.c2.x.rawValue, m.c2.y.rawValue, m.c2.z.rawValue, 0, INVERSE3_BITS));
            var c0 = ShiftLeft(m.c0, shifts.x);
            var c1 = ShiftLeft(m.c1, shifts.y);
            var c2 = ShiftLeft(m.c2, shifts.z);
            rows = new FixedMatrix3x3(Cross(c1, c2), Cross(c2, c0), Cross(c0, c1));
            determinant = Dot(c0, rows.c0);
            return determinant.rawValue != 0;
        }

        private static FixedMatrix3x3 InverseOfAdjugate(FixedMatrix3x3 rows, int3 shifts, FixedPoint determinant)
        {
            return Transpose(new FixedMatrix3x3(
                DivideRow(rows.c0, shifts.x, determinant), DivideRow(rows.c1, shifts.y, determinant), DivideRow(rows.c2, shifts.z, determinant)));
        }

        // row · 2^shift / determinant, each element one 128-bit division.
        private static FixedVector3 DivideRow(FixedVector3 row, int shift, FixedPoint determinant)
        {
            var scale = ShiftScale(shift);
            return new FixedVector3(MulDiv(row.x, scale, determinant), MulDiv(row.y, scale, determinant), MulDiv(row.z, scale, determinant));
        }

        // -(row · t) · 2^shift / determinant with the products and their sum in 128 bits: a translation taken back.
        private static FixedPoint TakeBack(FixedVector3 row, FixedVector3 t, int shift, FixedPoint determinant)
        {
            ulong hi = 0, lo = 0;
            AddProduct(row.x.rawValue, t.x.rawValue, ref hi, ref lo);
            AddProduct(row.y.rawValue, t.y.rawValue, ref hi, ref lo);
            AddProduct(row.z.rawValue, t.z.rawValue, ref hi, ref lo);
            var sumNegative = (long)hi < 0;
            if (sumNegative)
            {
                Negate(ref hi, ref lo);
            }
            if ((hi | lo) == 0)
            {
                return FixedPoint.Zero;
            }

            // The sum has 32 fractional bits; scaled by 2^shift and divided by the raw determinant it has 16.
            var negative = !sumNegative ^ (determinant.rawValue < 0);
            var saturated = FixedPoint.FromRaw(negative ? FixedPoint.MIN_RAW : FixedPoint.MAX_RAW);
            if (shift > 0)
            {
                if (hi >> (64 - shift) != 0)
                {
                    return saturated;
                }
                hi = (hi << shift) | (lo >> (64 - shift));
                lo <<= shift;
            }
            var divisor = UnsignedAbs(determinant.rawValue);
            if (hi >= divisor)
            {
                return saturated;
            }
            var quotient = DivideWide(hi, lo, divisor);
            if (quotient > FixedPoint.MAX_RAW)
            {
                return saturated;
            }
            return FixedPoint.FromRaw(negative ? -(long)quotient : (long)quotient);
        }

        // Adds a · b to the two's complement 128-bit (hi, lo).
        private static void AddProduct(long a, long b, ref ulong hi, ref ulong lo)
        {
            Multiply(UnsignedAbs(a), UnsignedAbs(b), out var productHi, out var productLo);
            if ((a < 0) ^ (b < 0))
            {
                Negate(ref productHi, ref productLo);
            }
            var sumLo = lo + productLo;
            hi += productHi + (sumLo < lo ? 1UL : 0UL);
            lo = sumLo;
        }

        private static void Negate(ref ulong hi, ref ulong lo)
        {
            lo = ~lo + 1UL;
            hi = ~hi + (lo == 0 ? 1UL : 0UL);
        }

        // 2^shift as a FixedPoint: MulDiv(a, ShiftScale(k), d) is a · 2^k / d.
        private static FixedPoint ShiftScale(int shift) => FixedPoint.FromRaw(1L << (FixedPoint.FRACTIONAL_BITS + shift));

        // The left shift that brings the longest element of a column to `bits` raw bits; zero for a longer or a zero column.
        private static int UpShift(long a, long b, long c, long d, int bits)
        {
            var largest = math.max(math.max(UnsignedAbs(a), UnsignedAbs(b)), math.max(UnsignedAbs(c), UnsignedAbs(d)));
            if (largest == 0)
            {
                return 0;
            }
            var length = 64 - math.lzcnt(largest);
            return length < bits ? bits - length : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FixedVector3 ShiftLeft(FixedVector3 v, int shift)
        {
            return new FixedVector3(
                FixedPoint.FromRaw(v.x.rawValue << shift), FixedPoint.FromRaw(v.y.rawValue << shift), FixedPoint.FromRaw(v.z.rawValue << shift));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FixedVector4 ShiftLeft(FixedVector4 v, int shift)
        {
            return new FixedVector4(
                FixedPoint.FromRaw(v.x.rawValue << shift), FixedPoint.FromRaw(v.y.rawValue << shift),
                FixedPoint.FromRaw(v.z.rawValue << shift), FixedPoint.FromRaw(v.w.rawValue << shift));
        }

        // The 2×2 minors of the top two rows (s) and of the bottom two rows (k) that the 4×4 determinant and inverse share.
        private static void Minors(FixedMatrix4x4 m,
            out FixedPoint s0, out FixedPoint s1, out FixedPoint s2, out FixedPoint s3, out FixedPoint s4, out FixedPoint s5,
            out FixedPoint k0, out FixedPoint k1, out FixedPoint k2, out FixedPoint k3, out FixedPoint k4, out FixedPoint k5)
        {
            FixedPoint m00 = m.c0.x, m10 = m.c0.y, m20 = m.c0.z, m30 = m.c0.w;
            FixedPoint m01 = m.c1.x, m11 = m.c1.y, m21 = m.c1.z, m31 = m.c1.w;
            FixedPoint m02 = m.c2.x, m12 = m.c2.y, m22 = m.c2.z, m32 = m.c2.w;
            FixedPoint m03 = m.c3.x, m13 = m.c3.y, m23 = m.c3.z, m33 = m.c3.w;
            s0 = m00 * m11 - m10 * m01;
            s1 = m00 * m12 - m10 * m02;
            s2 = m00 * m13 - m10 * m03;
            s3 = m01 * m12 - m11 * m02;
            s4 = m01 * m13 - m11 * m03;
            s5 = m02 * m13 - m12 * m03;
            k0 = m20 * m31 - m30 * m21;
            k1 = m20 * m32 - m30 * m22;
            k2 = m20 * m33 - m30 * m23;
            k3 = m21 * m32 - m31 * m22;
            k4 = m21 * m33 - m31 * m23;
            k5 = m22 * m33 - m32 * m23;
        }
    }
}
