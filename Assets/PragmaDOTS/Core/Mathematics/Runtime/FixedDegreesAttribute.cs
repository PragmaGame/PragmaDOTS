using System;
using UnityEngine;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// Shows a <see cref="FixedPoint"/> angle stored in radians as degrees in the inspector; with limits, as a slider.
    /// </summary>
    /// <remarks>
    /// The field keeps radians, which every angle of <see cref="FixedMath"/> takes. An edit is converted with
    /// <see cref="FixedMath.ToRadians"/>, so 90 is stored as <see cref="FixedMath.HalfPi"/> to the step.
    /// On an array it applies to every element.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FixedDegreesAttribute : PropertyAttribute
    {
        public FixedDegreesAttribute()
        {
            Min = double.NegativeInfinity;
            Max = double.PositiveInfinity;
        }

        /// <param name="min">The lower limit in degrees.</param>
        /// <param name="max">The upper limit in degrees.</param>
        public FixedDegreesAttribute(double min, double max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>The lower limit in degrees; negative infinity when there is none.</summary>
        public double Min { get; }

        /// <summary>The upper limit in degrees; positive infinity when there is none.</summary>
        public double Max { get; }

        public bool HasLimits => !double.IsInfinity(Min) && !double.IsInfinity(Max);
    }
}
