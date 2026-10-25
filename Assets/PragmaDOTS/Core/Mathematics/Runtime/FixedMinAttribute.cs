using System;
using UnityEngine;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// The smallest value of a <see cref="FixedPoint"/> field in the inspector: an edit below it becomes the limit.
    /// </summary>
    /// <remarks>
    /// Unity's <c>[Min]</c> does not work here: its drawer comes before the type's drawer, handles float, int and vector
    /// fields only and draws any other field as a raw struct. On an array the limit applies to every element.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FixedMinAttribute : PropertyAttribute
    {
        /// <param name="min">The limit, rounded to the nearest fixed-point step.</param>
        public FixedMinAttribute(double min)
        {
            Min = (FixedPoint)min;
        }

        public FixedPoint Min { get; }

        /// <summary>The value raised to <see cref="Min"/> if it is below.</summary>
        public FixedPoint Clamp(FixedPoint value)
        {
            return FixedMath.Max(value, Min);
        }
    }
}
