using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Unity.Mathematics;

namespace Pragma.Dots.Mathematics
{
    /// <summary>Deterministic 4D vector of <see cref="FixedPoint"/>: a column of <see cref="FixedMatrix4x4"/>, or a homogeneous point.</summary>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    [DataContract]
    public struct FixedVector4 : IEquatable<FixedVector4>, IFormattable
    {
        [DataMember] public FixedPoint x;
        [DataMember] public FixedPoint y;
        [DataMember] public FixedPoint z;
        [DataMember] public FixedPoint w;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedVector4(FixedPoint x, FixedPoint y, FixedPoint z, FixedPoint w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedVector4(FixedPoint value)
        {
            x = value;
            y = value;
            z = value;
            w = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedVector4(FixedVector3 xyz, FixedPoint w)
        {
            x = xyz.x;
            y = xyz.y;
            z = xyz.z;
            this.w = w;
        }

        public static FixedVector4 Zero => default;
        public static FixedVector4 One => new FixedVector4(FixedPoint.One);

        public FixedPoint this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    case 2: return z;
                    case 3: return w;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    case 3: w = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public FixedVector3 Xyz => new FixedVector3(x, y, z);
        public FixedVector2 Xy => new FixedVector2(x, y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator FixedVector4(int4 value) => new FixedVector4(value.x, value.y, value.z, value.w);

        public static explicit operator FixedVector4(float4 value) => new FixedVector4((FixedPoint)value.x, (FixedPoint)value.y, (FixedPoint)value.z, (FixedPoint)value.w);

        public static explicit operator float4(FixedVector4 value) => new float4((float)value.x, (float)value.y, (float)value.z, (float)value.w);

        public static explicit operator FixedVector4(UnityEngine.Vector4 value) => new FixedVector4((FixedPoint)value.x, (FixedPoint)value.y, (FixedPoint)value.z, (FixedPoint)value.w);

        public static explicit operator UnityEngine.Vector4(FixedVector4 value) => new UnityEngine.Vector4((float)value.x, (float)value.y, (float)value.z, (float)value.w);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 operator +(FixedVector4 a, FixedVector4 b) => new FixedVector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 operator -(FixedVector4 a, FixedVector4 b) => new FixedVector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 operator -(FixedVector4 value) => new FixedVector4(-value.x, -value.y, -value.z, -value.w);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 operator *(FixedVector4 a, FixedVector4 b) => new FixedVector4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 operator *(FixedVector4 a, FixedPoint b) => new FixedVector4(a.x * b, a.y * b, a.z * b, a.w * b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 operator *(FixedPoint a, FixedVector4 b) => new FixedVector4(a * b.x, a * b.y, a * b.z, a * b.w);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 operator *(FixedVector4 a, int b) => new FixedVector4(a.x * b, a.y * b, a.z * b, a.w * b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 operator /(FixedVector4 a, FixedVector4 b) => new FixedVector4(a.x / b.x, a.y / b.y, a.z / b.z, a.w / b.w);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 operator /(FixedVector4 a, FixedPoint b) => new FixedVector4(a.x / b, a.y / b, a.z / b, a.w / b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 operator /(FixedVector4 a, int b) => new FixedVector4(a.x / b, a.y / b, a.z / b, a.w / b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FixedVector4 a, FixedVector4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FixedVector4 a, FixedVector4 b) => !(a == b);

        public bool Equals(FixedVector4 other) => this == other;

        public override bool Equals(object obj) => obj is FixedVector4 other && this == other;

        public override int GetHashCode() => unchecked(((x.GetHashCode() * 397 ^ y.GetHashCode()) * 397 ^ z.GetHashCode()) * 397 ^ w.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedVector4({x.ToString(format, formatProvider)}, {y.ToString(format, formatProvider)}, {z.ToString(format, formatProvider)}, {w.ToString(format, formatProvider)})";
        }
    }
}
