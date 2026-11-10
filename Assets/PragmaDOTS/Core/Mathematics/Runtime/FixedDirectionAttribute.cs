using System;
using UnityEngine;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// Shows a <see cref="FixedVector2"/> direction as one angle in degrees in the inspector and stores the unit
    /// vector of the edited angle.
    /// </summary>
    /// <remarks>
    /// By default the angle is counter-clockwise from +X, as <see cref="FixedMath.Direction"/> and
    /// <see cref="FixedMath.Angle(FixedVector2)"/> take it. With <c>heading</c> the vector holds world X and Z and
    /// the angle is the heading on the ground: from +Z toward +X, as <see cref="FixedMath.Yaw(FixedVector2)"/>
    /// and <see cref="FixedQuaternion.RotateY"/> take it. A zero vector shows as 0.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FixedDirectionAttribute : PropertyAttribute
    {
        /// <param name="heading">The vector holds world X and Z, and the angle is measured from +Z toward +X.</param>
        public FixedDirectionAttribute(bool heading = false)
        {
            Heading = heading;
        }

        public bool Heading { get; }

        /// <summary>The angle of a direction in radians, by this attribute's convention.</summary>
        public FixedPoint ToAngle(FixedVector2 direction)
        {
            return Heading ? FixedMath.Yaw(direction) : FixedMath.Angle(direction);
        }

        /// <summary>The unit direction at an angle in radians, by this attribute's convention.</summary>
        public FixedVector2 ToDirection(FixedPoint angle)
        {
            var direction = FixedMath.Direction(angle);
            return Heading ? direction.Yx : direction;
        }
    }
}
