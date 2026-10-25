using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Mathematics.Editor
{
    /// <summary>A <see cref="FixedPoint"/> field with <see cref="FixedMinAttribute"/>: a number that an edit does not take below the limit.</summary>
    [CustomPropertyDrawer(typeof(FixedMinAttribute))]
    internal sealed class FixedMinDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (FixedPointDrawerUtility.CheckFixedPoint(position, property, label, "FixedMin"))
            {
                var min = ((FixedMinAttribute)attribute).Min;
                FixedPointDrawerUtility.DrawNumber(position, property, label, min, FixedPoint.MaxValue);
            }
        }
    }
}
