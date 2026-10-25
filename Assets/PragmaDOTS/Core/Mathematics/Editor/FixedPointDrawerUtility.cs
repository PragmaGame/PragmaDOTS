using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Mathematics.Editor
{
    internal static class FixedPointDrawerUtility
    {
        /// <summary>A <see cref="FixedPoint"/> property as a decimal number; an edit is kept within the limits.</summary>
        public static void DrawNumber(Rect position, SerializedProperty property, GUIContent label, FixedPoint min, FixedPoint max)
        {
            var raw = property.FindPropertyRelative(nameof(FixedPoint.rawValue));
            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.DoubleField(position, label, raw.longValue / (double)FixedPoint.ONE_RAW);
            if (EditorGUI.EndChangeCheck())
            {
                raw.longValue = FixedMath.Clamp((FixedPoint)value, min, max).rawValue;
            }
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// A <see cref="FixedPoint"/> property as a slider with a number field, laid out like Unity's slider; an edit is
        /// kept within the limits.
        /// </summary>
        public static void DrawSlider(Rect position, SerializedProperty property, GUIContent label, FixedPoint min, FixedPoint max)
        {
            var raw = property.FindPropertyRelative(nameof(FixedPoint.rawValue));
            EditorGUI.BeginProperty(position, label, property);
            var fields = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var numberWidth = EditorGUIUtility.fieldWidth;
            var bar = new Rect(fields.x, fields.y, Mathf.Max(0f, fields.width - numberWidth - 5f), fields.height);
            var number = new Rect(fields.xMax - numberWidth, fields.y, numberWidth, fields.height);
            var value = raw.longValue / (double)FixedPoint.ONE_RAW;
            var changed = false;

            // Separate checks: the slider returns a float even when it is not dragged, and that float must not replace
            // the exact value the number field shows.
            EditorGUI.BeginChangeCheck();
            var dragged = GUI.HorizontalSlider(bar, (float)value, (float)min, (float)max);
            if (EditorGUI.EndChangeCheck())
            {
                value = dragged;
                changed = true;
            }

            EditorGUI.BeginChangeCheck();
            var typed = EditorGUI.DoubleField(number, value);
            if (EditorGUI.EndChangeCheck())
            {
                value = typed;
                changed = true;
            }

            if (changed)
            {
                raw.longValue = FixedMath.Clamp((FixedPoint)value, min, max).rawValue;
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        /// <summary>Whether <paramref name="property"/> is a <see cref="FixedPoint"/>; if not, draws why the attribute does not apply.</summary>
        public static bool CheckFixedPoint(Rect position, SerializedProperty property, GUIContent label, string attribute)
        {
            if (property.type == nameof(FixedPoint))
            {
                return true;
            }

            EditorGUI.LabelField(position, label.text, $"Use {attribute} with FixedPoint.");
            return false;
        }

        public static void DrawComponents(Rect position, SerializedProperty property, GUIContent label, params string[] names)
        {
            EditorGUI.BeginProperty(position, label, property);
            var fields = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
            var indent = EditorGUI.indentLevel;
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = 13f;

            var width = (fields.width - (names.Length - 1) * 4f) / names.Length;
            for (var i = 0; i < names.Length; i++)
            {
                var rect = new Rect(fields.x + i * (width + 4f), fields.y, width, fields.height);
                var raw = property.FindPropertyRelative(names[i]).FindPropertyRelative(nameof(FixedPoint.rawValue));
                EditorGUI.BeginChangeCheck();
                var value = EditorGUI.DoubleField(rect, names[i].ToUpperInvariant(), raw.longValue / (double)FixedPoint.ONE_RAW);
                if (EditorGUI.EndChangeCheck())
                {
                    raw.longValue = ((FixedPoint)value).rawValue;
                }
            }

            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
