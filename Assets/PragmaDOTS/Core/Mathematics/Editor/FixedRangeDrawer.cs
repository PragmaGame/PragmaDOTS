using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Mathematics.Editor
{
    /// <summary>A <see cref="FixedPoint"/> field with <see cref="FixedRangeAttribute"/>: a slider with a number field, kept within the limits.</summary>
    [CustomPropertyDrawer(typeof(FixedRangeAttribute))]
    internal sealed class FixedRangeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (FixedPointDrawerUtility.CheckFixedPoint(position, property, label, "FixedRange"))
            {
                var range = (FixedRangeAttribute)attribute;
                FixedPointDrawerUtility.DrawSlider(position, property, label, range.Min, range.Max);
            }
        }
    }
}
