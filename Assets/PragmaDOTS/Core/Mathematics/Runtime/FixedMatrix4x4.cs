using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Unity.Mathematics;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// Deterministic 4×4 matrix of <see cref="FixedPoint"/>, stored by columns like <c>Unity.Mathematics.float4x4</c>
    /// and <c>UnityEngine.Matrix4x4</c>: a transform made of translation, rotation and scale.
    /// </summary>
    /// <remarks>
    /// Vectors are columns: <c>m * v</c> transforms <c>v</c>, and <c>a * b</c> applies <c>b</c> first, so
    /// <c>parent * child</c> is the child's transform in the parent's space. Points go through
    /// <see cref="FixedMath.TransformPoint"/>, directions through <see cref="FixedMath.TransformDirection"/>.
    /// </remarks>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    [DataContract]
    public struct FixedMatrix4x4 : IEquatable<FixedMatrix4x4>, IFormattable
    {
        [DataMember] public FixedVector4 c0;
        [DataMember] public FixedVector4 c1;
        [DataMember] public FixedVector4 c2;
        [DataMember] public FixedVector4 c3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FixedMatrix4x4(FixedVector4 c0, FixedVector4 c1, FixedVector4 c2, FixedVector4 c3)
        {
            this.c0 = c0;
            this.c1 = c1;
            this.c2 = c2;
            this.c3 = c3;
        }

        /// <summary>A matrix from its elements in row-major order, the way it is written on paper.</summary>
        public FixedMatrix4x4(
            FixedPoint m00, FixedPoint m01, FixedPoint m02, FixedPoint m03,
            FixedPoint m10, FixedPoint m11, FixedPoint m12, FixedPoint m13,
            FixedPoint m20, FixedPoint m21, FixedPoint m22, FixedPoint m23,
            FixedPoint m30, FixedPoint m31, FixedPoint m32, FixedPoint m33)
        {
            c0 = new FixedVector4(m00, m10, m20, m30);
            c1 = new FixedVector4(m01, m11, m21, m31);
            c2 = new FixedVector4(m02, m12, m22, m32);
            c3 = new FixedVector4(m03, m13, m23, m33);
        }

        /// <summary>An affine transform from a linear part (rotation and scale) and a translation.</summary>
        public FixedMatrix4x4(FixedMatrix3x3 linear, FixedVector3 translation)
        {
            c0 = new FixedVector4(linear.c0, FixedPoint.Zero);
            c1 = new FixedVector4(linear.c1, FixedPoint.Zero);
            c2 = new FixedVector4(linear.c2, FixedPoint.Zero);
            c3 = new FixedVector4(translation, FixedPoint.One);
        }

        public static FixedMatrix4x4 Zero => default;

        public static FixedMatrix4x4 Identity => new FixedMatrix4x4(FixedMatrix3x3.Identity, FixedVector3.Zero);

        /// <summary>The column at <paramref name="index"/>.</summary>
        public FixedVector4 this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return c0;
                    case 1: return c1;
                    case 2: return c2;
                    case 3: return c3;
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
                    case 3: c3 = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        /// <summary>The translation of an affine transform: the last column.</summary>
        public FixedVector3 Translation => c3.Xyz;

        /// <summary>The upper-left 3×3 of an affine transform: its rotation and scale.</summary>
        public FixedMatrix3x3 Linear => new FixedMatrix3x3(c0.Xyz, c1.Xyz, c2.Xyz);

        /// <summary>Translation, rotation and scale, applied as scale first, then rotation, then translation.</summary>
        public static FixedMatrix4x4 TRS(FixedVector3 translation, FixedQuaternion rotation, FixedVector3 scale)
        {
            var r = FixedMatrix3x3.FromRotation(rotation);
            return new FixedMatrix4x4(new FixedMatrix3x3(r.c0 * scale.x, r.c1 * scale.y, r.c2 * scale.z), translation);
        }

        /// <summary>The transform of a pose: its position and rotation, without scale.</summary>
        public static FixedMatrix4x4 FromPose(FixedPose pose) => new FixedMatrix4x4(FixedMatrix3x3.FromRotation(pose.rotation), pose.position);

        public static FixedMatrix4x4 Translate(FixedVector3 translation) => new FixedMatrix4x4(FixedMatrix3x3.Identity, translation);

        public static FixedMatrix4x4 Rotate(FixedQuaternion rotation) => new FixedMatrix4x4(FixedMatrix3x3.FromRotation(rotation), FixedVector3.Zero);

        public static FixedMatrix4x4 Scale(FixedVector3 scale) => new FixedMatrix4x4(FixedMatrix3x3.Scale(scale), FixedVector3.Zero);

        public static explicit operator FixedMatrix4x4(float4x4 value) => new FixedMatrix4x4((FixedVector4)value.c0, (FixedVector4)value.c1, (FixedVector4)value.c2, (FixedVector4)value.c3);

        public static explicit operator float4x4(FixedMatrix4x4 value) => new float4x4((float4)value.c0, (float4)value.c1, (float4)value.c2, (float4)value.c3);

        public static explicit operator FixedMatrix4x4(UnityEngine.Matrix4x4 value)
        {
            return new FixedMatrix4x4(
                (FixedVector4)value.GetColumn(0), (FixedVector4)value.GetColumn(1), (FixedVector4)value.GetColumn(2), (FixedVector4)value.GetColumn(3));
        }

        public static explicit operator UnityEngine.Matrix4x4(FixedMatrix4x4 value)
        {
            return new UnityEngine.Matrix4x4(
                (UnityEngine.Vector4)value.c0, (UnityEngine.Vector4)value.c1, (UnityEngine.Vector4)value.c2, (UnityEngine.Vector4)value.c3);
        }

        /// <summary>Matrix product: applies <paramref name="b"/> first, then <paramref name="a"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedMatrix4x4 operator *(FixedMatrix4x4 a, FixedMatrix4x4 b) => FixedMath.Mul(a, b);

        /// <summary>Transforms a column vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedVector4 operator *(FixedMatrix4x4 m, FixedVector4 v) => FixedMath.Mul(m, v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedMatrix4x4 operator *(FixedMatrix4x4 m, FixedPoint s) => new FixedMatrix4x4(m.c0 * s, m.c1 * s, m.c2 * s, m.c3 * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedMatrix4x4 operator +(FixedMatrix4x4 a, FixedMatrix4x4 b) => new FixedMatrix4x4(a.c0 + b.c0, a.c1 + b.c1, a.c2 + b.c2, a.c3 + b.c3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedMatrix4x4 operator -(FixedMatrix4x4 a, FixedMatrix4x4 b) => new FixedMatrix4x4(a.c0 - b.c0, a.c1 - b.c1, a.c2 - b.c2, a.c3 - b.c3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FixedMatrix4x4 operator -(FixedMatrix4x4 m) => new FixedMatrix4x4(-m.c0, -m.c1, -m.c2, -m.c3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FixedMatrix4x4 a, FixedMatrix4x4 b) => a.c0 == b.c0 && a.c1 == b.c1 && a.c2 == b.c2 && a.c3 == b.c3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FixedMatrix4x4 a, FixedMatrix4x4 b) => !(a == b);

        public bool Equals(FixedMatrix4x4 other) => this == other;

        public override bool Equals(object obj) => obj is FixedMatrix4x4 other && this == other;

        public override int GetHashCode() => unchecked(((c0.GetHashCode() * 397 ^ c1.GetHashCode()) * 397 ^ c2.GetHashCode()) * 397 ^ c3.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedMatrix4x4({c0.ToString(format, formatProvider)}, {c1.ToString(format, formatProvider)}, {c2.ToString(format, formatProvider)}, {c3.ToString(format, formatProvider)})";
        }
    }
}
