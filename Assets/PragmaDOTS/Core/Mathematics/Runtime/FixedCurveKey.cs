using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Serialization;

namespace Pragma.Dots.Mathematics
{
    /// <summary>A key of <see cref="FixedCurve"/>: a value at a time, the slopes into and out of it, and how the curve goes on to the next key.</summary>
    [Serializable]
    [DebuggerDisplay("{ToString()}")]
    [DataContract]
    public struct FixedCurveKey : IEquatable<FixedCurveKey>, IFormattable
    {
        [DataMember] public FixedPoint time;
        [DataMember] public FixedPoint value;
        /// <summary>The slope (value per time) arriving at the key.</summary>
        [DataMember] public FixedPoint inTangent;
        /// <summary>The slope (value per time) leaving the key.</summary>
        [DataMember] public FixedPoint outTangent;
        /// <summary>How the curve goes from this key to the next one.</summary>
        [DataMember] public FixedCurveInterpolation interpolation;

        public FixedCurveKey(FixedPoint time, FixedPoint value, FixedCurveInterpolation interpolation = FixedCurveInterpolation.Linear)
        {
            this.time = time;
            this.value = value;
            inTangent = FixedPoint.Zero;
            outTangent = FixedPoint.Zero;
            this.interpolation = interpolation;
        }

        public FixedCurveKey(FixedPoint time, FixedPoint value, FixedPoint inTangent, FixedPoint outTangent)
        {
            this.time = time;
            this.value = value;
            this.inTangent = inTangent;
            this.outTangent = outTangent;
            interpolation = FixedCurveInterpolation.Cubic;
        }

        public static bool operator ==(FixedCurveKey a, FixedCurveKey b)
        {
            return a.time == b.time && a.value == b.value && a.inTangent == b.inTangent && a.outTangent == b.outTangent && a.interpolation == b.interpolation;
        }

        public static bool operator !=(FixedCurveKey a, FixedCurveKey b) => !(a == b);

        public bool Equals(FixedCurveKey other) => this == other;

        public override bool Equals(object obj) => obj is FixedCurveKey other && this == other;

        public override int GetHashCode() => unchecked((((time.GetHashCode() * 397 ^ value.GetHashCode()) * 397 ^ inTangent.GetHashCode()) * 397 ^ outTangent.GetHashCode()) * 397 ^ (int)interpolation);

        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return $"FixedCurveKey({time.ToString(format, formatProvider)}, {value.ToString(format, formatProvider)}, {inTangent.ToString(format, formatProvider)}, {outTangent.ToString(format, formatProvider)}, {interpolation})";
        }
    }
}
