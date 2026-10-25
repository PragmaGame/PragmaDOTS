using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Actors.Editor
{
    /// <summary>
    /// A key field with <see cref="ActorKeyAttribute"/>: a reference to the definition with that key, whose pick stores
    /// the key; a key no definition has gets a warning icon with the key in its tooltip.
    /// </summary>
    [CustomPropertyDrawer(typeof(ActorKeyAttribute))]
    internal sealed class ActorKeyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!IsKey(property))
            {
                EditorGUI.LabelField(position, label.text, "Use ActorKey with FixedString32Bytes.");
                return;
            }

            var key = property.boxedValue.ToString();
            var definition = ActorKeyIndex.Find(key);
            if (definition == null && key.Length > 0 && !property.hasMultipleDifferentValues)
            {
                label = new GUIContent(label.text, EditorGUIUtility.IconContent("console.warnicon.sml").image,
                    $"No actor definition has the key \"{key}\".");
            }

            label = EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            var picked = (ActorDefinition)EditorGUI.ObjectField(position, label, definition, typeof(ActorDefinition), false);
            if (EditorGUI.EndChangeCheck() && !Assign(property, picked))
            {
                Debug.LogWarning($"{picked.name} has no valid key (1 to 29 bytes of UTF-8): the field keeps its key.", picked);
            }
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public static bool IsKey(SerializedProperty property)
        {
            return property.propertyType == SerializedPropertyType.Generic && property.type == nameof(FixedString32Bytes);
        }

        /// <summary>Stores the key of <paramref name="definition"/>, or an empty key for none; false when its key is not valid.</summary>
        public static bool Assign(SerializedProperty property, ActorDefinition definition)
        {
            if (definition == null)
            {
                property.boxedValue = default(FixedString32Bytes);
                return true;
            }

            if (!definition.TryGetKey(out var key))
            {
                return false;
            }

            property.boxedValue = key;
            return true;
        }
    }
}
