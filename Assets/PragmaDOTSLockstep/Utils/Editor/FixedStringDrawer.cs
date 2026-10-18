using System;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Pragma.Lockstep.Utils.Editor
{
    /// <summary>Shows fixed strings as text fields; text longer than the string's UTF-8 capacity is cut.</summary>
    [CustomPropertyDrawer(typeof(FixedString32Bytes))]
    [CustomPropertyDrawer(typeof(FixedString64Bytes))]
    [CustomPropertyDrawer(typeof(FixedString128Bytes))]
    [CustomPropertyDrawer(typeof(FixedString512Bytes))]
    [CustomPropertyDrawer(typeof(FixedString4096Bytes))]
    internal sealed class FixedStringDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var value = property.boxedValue;
            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var text = EditorGUI.TextField(position, label, value.ToString());
            if (EditorGUI.EndChangeCheck())
            {
                property.boxedValue = Create(value.GetType(), text);
            }
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        // The string constructors throw on text that does not fit while collection checks are on.
        private static object Create(Type type, string text)
        {
            var capacity = ((INativeList<byte>)Activator.CreateInstance(type)).Capacity;
            while (Encoding.UTF8.GetByteCount(text) > capacity)
            {
                var cut = char.IsLowSurrogate(text[text.Length - 1]) ? 2 : 1;
                text = text.Substring(0, text.Length - cut);
            }
            return Activator.CreateInstance(type, text);
        }
    }
}
