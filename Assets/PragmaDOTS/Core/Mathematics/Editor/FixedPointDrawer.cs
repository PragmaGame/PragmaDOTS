using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Mathematics.Editor
{
    /// <summary>Shows <see cref="FixedPoint"/> fields as decimal numbers; edits are rounded to the nearest fixed-point step.</summary>
    [CustomPropertyDrawer(typeof(FixedPoint))]
    internal sealed class FixedPointDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            FixedPointDrawerUtility.DrawNumber(position, property, label, FixedPoint.MinValue, FixedPoint.MaxValue);
        }
    }
}
