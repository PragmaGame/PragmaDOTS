using System;
using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Mathematics.Editor
{
    /// <summary>A <see cref="FixedVector2"/> with <see cref="FixedDirectionAttribute"/>: one angle in degrees, stored as a unit vector.</summary>
    [CustomPropertyDrawer(typeof(FixedDirectionAttribute))]
    internal sealed class FixedDirectionDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!FixedPointDrawerUtility.CheckType(position, property, label, "FixedDirection", nameof(FixedVector2)))
            {
                return;
            }

            var convention = (FixedDirectionAttribute)attribute;
            var x = property.FindPropertyRelative(nameof(FixedVector2.x));
            var y = property.FindPropertyRelative(nameof(FixedVector2.y));
            var direction = new FixedVector2(FixedPointDrawerUtility.GetValue(x), FixedPointDrawerUtility.GetValue(y));
            var degrees = Math.Round((double)FixedMath.ToDegrees(convention.ToAngle(direction)), 3);

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            degrees = EditorGUI.DoubleField(position, label, degrees);
            if (EditorGUI.EndChangeCheck())
            {
                var edited = convention.ToDirection(FixedMath.ToRadians((FixedPoint)degrees));
                FixedPointDrawerUtility.SetValue(x, edited.x);
                FixedPointDrawerUtility.SetValue(y, edited.y);
            }
            EditorGUI.EndProperty();
        }
    }
}
