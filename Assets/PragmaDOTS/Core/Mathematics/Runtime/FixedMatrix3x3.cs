using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Unity.Mathematics;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// Deterministic 3×3 matrix of <see cref="FixedPoint"/>, stored by columns like <c>Unity.Mathematics.float3x3</c>:
    /// rotation and scale, or any linear map of <see cref="FixedVector3"/>.
    /// </summary>
    /// <remarks>
    /// Vectors are columns: <c>m * v</c> transforms <c>v</c>, and <c>a * b</c> applies <c>b</c> first. The columns of a
    /// rotation are the rotated x, y and z axes.
    /// </remarks>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    [DataContract]
    public struct FixedMatrix3x3 : IEquatable<FixedMatrix3x3>, IFormattable
    {
        [DataMember] public FixedVector3 c0;
        [DataMember] public FixedVector3 c1;
        [DataMember] public FixedVector3 c2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedMatrix3x3(FixedVector3 c0, FixedVector3 c1, FixedVector3 c2)
        {
            this.c0 = c0;
            this.c1 = c1;
            this.c2 = c2;
        }

        /// <summary>A matrix from its elements in row-major order, the way it is written on paper.</summary>
        public FixedMatrix3x3(
            FixedPoint m00, FixedPoint m01, FixedPoint m02,
            FixedPoint m10, FixedPoint m11, FixedPoint m12,
            FixedPoint m20, FixedPoint m21, FixedPoint m22)
        {
            c0 = new FixedVector3(m00, m10, m20);
            c1 = new FixedVector3(m01, m11, m21);
            c2 = new FixedVector3(m02, m12, m22);
        }

        public static FixedMatrix3x3 Zero => default;

        public static FixedMatrix3x3 Identity => new FixedMatrix3x3(FixedVector3.Right, FixedVector3.Up, FixedVector3.Forward);

        /// <summary>The column at <paramref name="index"/>.</summary>
        public FixedVector3 this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return c0;
                    case 1: return c1;
                    case 2: return c2;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: c0 = value; break;
                    case 1: c1 = value; break;
                    case 2: c2 = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        /// <summary>The rotation matrix of a unit quaternion.</summary>
        public static FixedMatrix3x3 FromRotation(FixedQuaternion q)
        {
            var xx = q.x * q.x;
            var yy = q.y * q.y;
            var zz = q.z * q.z;
            var xy = q.x * q.y;
            var xz = q.x * q.z;
            var yz = q.y * q.z;
            var wx = q.w * q.x;
            var wy = q.w * q.y;
            var wz = q.w * q.z;
            return new FixedMatrix3x3(
                new FixedVector3(FixedPoint.One - (yy + zz) * 2, (xy + wz) * 2, (xz - wy) * 2),
                new FixedVector3((xy - wz) * 2, FixedPoint.One - (xx + zz) * 2, (yz + wx) * 2),
                new FixedVector3((xz + wy) * 2, (yz - wx) * 2, FixedPoint.One - (xx + yy) * 2));
        }

        /// <summary>A scale along each axis.</summary>
        public static FixedMatrix3x3 Scale(FixedVector3 scale)
        {
            return new FixedMatrix3x3(
                new FixedVector3(scale.x, FixedPoint.Zero, FixedPoint.Zero),
                new FixedVector3(FixedPoint.Zero, scale.y, FixedPoint.Zero),
                new FixedVector3(FixedPoint.Zero, FixedPoint.Zero, scale.z));
        }

        public static FixedMatrix3x3 Scale(FixedPoint scale) => Scale(new FixedVector3(scale));

        public static explicit operator FixedMatrix3x3(float3x3 value) => new FixedMatrix3x3((FixedVector3)value.c0, (FixedVector3)value.c1, (FixedVector3)value.c2);

        public static explicit operator float3x3(FixedMatrix3x3 value) => new float3x3((float3)value.c0, (float3)value.c1, (float3)value.c2);

        /// <summary>Matrix product: applies <paramref name="b"/> first, then <paramref name="a"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedMatrix3x3 operator *(FixedMatrix3x3 a, FixedMatrix3x3 b) => FixedMath.Mul(a, b);

        /// <summary>Transforms a column vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector3 operator *(FixedMatrix3x3 m, FixedVector3 v) => FixedMath.Mul(m, v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedMatrix3x3 operator *(FixedMatrix3x3 m, FixedPoint s) => new FixedMatrix3x3(m.c0 * s, m.c1 * s, m.c2 * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedMatrix3x3 operator +(FixedMatrix3x3 a, FixedMatrix3x3 b) => new FixedMatrix3x3(a.c0 + b.c0, a.c1 + b.c1, a.c2 + b.c2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedMatrix3x3 operator -(FixedMatrix3x3 a, FixedMatrix3x3 b) => new FixedMatrix3x3(a.c0 - b.c0, a.c1 - b.c1, a.c2 - b.c2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedMatrix3x3 operator -(FixedMatrix3x3 m) => new FixedMatrix3x3(-m.c0, -m.c1, -m.c2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FixedMatrix3x3 a, FixedMatrix3x3 b) => a.c0 == b.c0 && a.c1 == b.c1 && a.c2 == b.c2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FixedMatrix3x3 a, FixedMatrix3x3 b) => !(a == b);

        public bool Equals(FixedMatrix3x3 other) => this == other;

        public override bool Equals(object obj) => obj is FixedMatrix3x3 other && this == other;

        public override int GetHashCode() => unchecked((c0.GetHashCode() * 397 ^ c1.GetHashCode()) * 397 ^ c2.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedMatrix3x3({c0.ToString(format, formatProvider)}, {c1.ToString(format, formatProvider)}, {c2.ToString(format, formatProvider)})";
        }
    }
}
