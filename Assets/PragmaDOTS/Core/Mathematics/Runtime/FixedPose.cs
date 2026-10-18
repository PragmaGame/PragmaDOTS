using System;
using System.Diagnostics;
using System.Globalization;

namespace Pragma.Dots.Mathematics
{
    /// <summary>A position and a rotation of <see cref="FixedPoint"/>, like <c>UnityEngine.Pose</c>.</summary>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    public struct FixedPose : IEquatable<FixedPose>, IFormattable
    {
        public FixedVector3 position;
        public FixedQuaternion rotation;

        public FixedPose(FixedVector3 position, FixedQuaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }

        public static FixedPose Identity => new FixedPose(FixedVector3.Zero, FixedQuaternion.Identity);

        public FixedVector3 Forward => FixedMath.Rotate(rotation, FixedVector3.Forward);
        public FixedVector3 Up => FixedMath.Rotate(rotation, FixedVector3.Up);
        public FixedVector3 Right => FixedMath.Rotate(rotation, FixedVector3.Right);

        public static bool operator ==(FixedPose a, FixedPose b) => a.position == b.position && a.rotation == b.rotation;

        public static bool operator !=(FixedPose a, FixedPose b) => !(a == b);

        public bool Equals(FixedPose other) => this == other;

        public override bool Equals(object obj) => obj is FixedPose other && this == other;

        public override int GetHashCode() => unchecked(position.GetHashCode() * 397 ^ rotation.GetHashCode());

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedPose({position.ToString(format, formatProvider)}, {rotation.ToString(format, formatProvider)})";
        }
    }
}
