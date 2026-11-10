using System;
using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Mathematics.Editor
{
    /// <summary>A <see cref="FixedPoint"/> angle in radians with <see cref="FixedDegreesAttribute"/>: edited in degrees, stored in radians.</summary>
    [CustomPropertyDrawer(typeof(FixedDegreesAttribute))]
    internal sealed class FixedDegreesDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!FixedPointDrawerUtility.CheckFixedPoint(position, property, label, "FixedDegrees"))
            {
                return;
            }

            var limits = (FixedDegreesAttribute)attribute;
            // Shown to a thousandth of a degree: a stored right angle is 89.99998 after the round trip through radians.
            var degrees = Math.Round((double)FixedMath.ToDegrees(FixedPointDrawerUtility.GetValue(property)), 3);

            EditorGUI.BeginProperty(position, label, property);
            bool changed;
            if (limits.HasLimits)
            {
                degrees = FixedPointDrawerUtility.SliderField(position, label, degrees, limits.Min, limits.Max, out changed);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                degrees = EditorGUI.DoubleField(position, label, degrees);
                changed = EditorGUI.EndChangeCheck();
            }

            if (changed)
            {
                degrees = Math.Max(limits.Min, Math.Min(limits.Max, degrees));
                FixedPointDrawerUtility.SetValue(property, FixedMath.ToRadians((FixedPoint)degrees));
            }
            EditorGUI.EndProperty();
        }
    }
}
