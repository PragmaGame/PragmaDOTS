using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Mathematics.Editor
{
    [CustomPropertyDrawer(typeof(FixedVector4))]
    internal sealed class FixedVector4Drawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            FixedPointDrawerUtility.DrawComponents(position, property, label, "x", "y", "z", "w");
        }
    }
}
