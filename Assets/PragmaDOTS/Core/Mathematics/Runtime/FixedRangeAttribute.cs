using System;
using UnityEngine;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// The limits of a <see cref="FixedPoint"/> field in the inspector, which draws it as a slider with a number field:
    /// an edit outside them becomes the nearest limit.
    /// </summary>
    /// <remarks>
    /// Unity's <c>[Range]</c> does not work here: its drawer comes before the type's drawer, handles float and int fields
    /// only and replaces any other field with an error label. On an array the limits apply to every element.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FixedRangeAttribute : PropertyAttribute
    {
        /// <param name="min">The lower limit, rounded to the nearest fixed-point step.</param>
        /// <param name="max">The upper limit, rounded to the nearest fixed-point step.</param>
        public FixedRangeAttribute(double min, double max)
        {
            Min = (FixedPoint)min;
            Max = (FixedPoint)max;
        }

        public FixedPoint Min { get; }

        public FixedPoint Max { get; }

        /// <summary>The value moved to the nearest limit if it lies outside.</summary>
        public FixedPoint Clamp(FixedPoint value)
        {
            return FixedMath.Clamp(value, Min, Max);
        }
    }
}
