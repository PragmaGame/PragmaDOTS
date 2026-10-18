using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Actors.Editor
{
    /// <summary>
    /// Inspector of a part list: the inherited parts read-only with an Override button, the own parts editable with Remove
    /// or Revert, and the menu that adds parts.
    /// </summary>
    internal static class ActorPartsGUI
    {
        private const string INHERITED_FOLDOUT = "Pragma.Dots.ActorPart.";

        /// <summary>A part a definition of the lineage gives, the closest definition winning.</summary>
        public readonly struct Inherited
        {
            public Inherited(ActorDefinition source, int index, Type type)
            {
                Source = source;
                Index = index;
                Type = type;
            }

            public ActorDefinition Source { get; }

            public int Index { get; }

            public Type Type { get; }
        }

        /// <summary>The parts <paramref name="lineage"/> (root first) gives, one per type.</summary>
        public static List<Inherited> GetInherited(IReadOnlyList<ActorDefinition> lineage)
        {
            var result = new List<Inherited>();
            foreach (var definition in lineage)
            {
                var parts = definition.OwnParts;
                for (var i = 0; i < parts.Count; i++)
                {
                    if (parts[i] == null)
                    {
                        continue;
                    }
                    var type = parts[i].GetType();
                    var entry = new Inherited(definition, i, type);
                    var index = result.FindIndex(inherited => inherited.Type == type);
                    if (index >= 0)
                    {
                        result[index] = entry;
                    }
                    else
                    {
                        result.Add(entry);
                    }
                }
            }
            return result;
        }

        /// <summary>Draws the inherited parts and the own parts of <paramref name="parts"/>, an array of <c>[SerializeReference] ActorPart</c>.</summary>
        public static void Draw(SerializedProperty parts, List<Inherited> inherited)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parts", EditorStyles.boldLabel);
            var ownTypes = GetOwnTypes(parts);
            var sources = new Dictionary<ActorDefinition, SerializedObject>();
            foreach (var entry in inherited)
            {
                if (!ownTypes.Contains(entry.Type))
                {
                    DrawInherited(parts, entry, sources);
                }
            }
            for (var i = 0; i < parts.arraySize; i++)
            {
                if (DrawOwn(parts, i, inherited))
                {
                    // The array changed under the loop; the next repaint draws the rest.
                    GUIUtility.ExitGUI();
                }
            }
            DrawAddButton(parts, inherited, ownTypes);
        }

        private static void DrawInherited(SerializedProperty parts, Inherited entry, Dictionary<ActorDefinition, SerializedObject> sources)
        {
            if (!sources.TryGetValue(entry.Source, out var source))
            {
                source = new SerializedObject(entry.Source);
                sources.Add(entry.Source, source);
            }
            var element = source.FindProperty("_parts").GetArrayElementAtIndex(entry.Index);
            var key = INHERITED_FOLDOUT + entry.Source.GetEntityId() + "." + entry.Index;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool expanded;
                using (new EditorGUILayout.HorizontalScope())
                {
                    expanded = EditorGUILayout.Foldout(SessionState.GetBool(key, false), ActorPartTypes.GetTitle(entry.Type), true);
                    SessionState.SetBool(key, expanded);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("from " + entry.Source.name, EditorStyles.miniLabel);
                    if (GUILayout.Button(new GUIContent("Override", "Copy the part here to change it."), EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        Add(parts, Clone(entry.Source.OwnParts[entry.Index]));
                        GUIUtility.ExitGUI();
                    }
                }
                if (expanded)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        DrawFields(element);
                    }
                }
            }
        }

        // True when the part was removed.
        private static bool DrawOwn(SerializedProperty parts, int index, List<Inherited> inherited)
        {
            var element = parts.GetArrayElementAtIndex(index);
            var part = element.managedReferenceValue as ActorPart;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (part == null)
                    {
                        EditorGUILayout.LabelField("Empty part: its class was renamed or removed.", EditorStyles.boldLabel);
                    }
                    else
                    {
                        element.isExpanded = EditorGUILayout.Foldout(element.isExpanded, ActorPartTypes.GetTitle(part.GetType()), true);
                    }
                    GUILayout.FlexibleSpace();
                    var overrides = part != null && inherited.Exists(entry => entry.Type == part.GetType());
                    if (overrides)
                    {
                        GUILayout.Label("override", EditorStyles.miniLabel);
                    }
                    var label = overrides ? new GUIContent("Revert", "Remove the override: the inherited part applies again.") : new GUIContent("Remove");
                    if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        RemoveAt(parts, index);
                        return true;
                    }
                }
                if (part != null && element.isExpanded)
                {
                    DrawFields(element);
                }
            }
            return false;
        }

        private static void DrawFields(SerializedProperty element)
        {
            var type = element.managedReferenceValue?.GetType();
            var root = element;
            if (type != null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ActorComponentPart<>))
            {
                root = element.FindPropertyRelative("_value");
                if (root == null)
                {
                    EditorGUILayout.HelpBox($"{type.GetGenericArguments()[0].Name} is not [Serializable]: Unity keeps none of its values.", MessageType.Warning);
                    return;
                }
            }

            var child = root.Copy();
            var end = root.GetEndProperty();
            if (!child.NextVisible(true) || SerializedProperty.EqualContents(child, end))
            {
                EditorGUILayout.LabelField("No settings.", EditorStyles.miniLabel);
                return;
            }
            EditorGUI.indentLevel++;
            do
            {
                EditorGUILayout.PropertyField(child, true);
            }
            while (child.NextVisible(false) && !SerializedProperty.EqualContents(child, end));
            EditorGUI.indentLevel--;
        }

        private static void DrawAddButton(SerializedProperty parts, List<Inherited> inherited, HashSet<Type> ownTypes)
        {
            var content = new GUIContent("Add Part");
            var rect = GUILayoutUtility.GetRect(content, EditorStyles.miniPullDown);
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Passive, EditorStyles.miniPullDown))
            {
                return;
            }
            var serializedObject = parts.serializedObject;
            var path = parts.propertyPath;
            var menu = new GenericMenu();
            foreach (var (type, menuPath) in ActorPartTypes.All)
            {
                if (ownTypes.Contains(type))
                {
                    menu.AddDisabledItem(new GUIContent(menuPath));
                    continue;
                }
                var label = inherited.Exists(entry => entry.Type == type) ? menuPath + " (override)" : menuPath;
                var partType = type;
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    serializedObject.Update();
                    Add(serializedObject.FindProperty(path), (ActorPart)Activator.CreateInstance(partType));
                });
            }
            menu.DropDown(rect);
        }

        private static void Add(SerializedProperty parts, ActorPart part)
        {
            var index = parts.arraySize;
            parts.arraySize++;
            var element = parts.GetArrayElementAtIndex(index);
            element.managedReferenceValue = part;
            element.isExpanded = true;
            parts.serializedObject.ApplyModifiedProperties();
        }

        private static void RemoveAt(SerializedProperty parts, int index)
        {
            var size = parts.arraySize;
            parts.DeleteArrayElementAtIndex(index);
            if (parts.arraySize == size)
            {
                parts.DeleteArrayElementAtIndex(index);
            }
            parts.serializedObject.ApplyModifiedProperties();
        }

        private static ActorPart Clone(ActorPart part)
        {
            var copy = (ActorPart)Activator.CreateInstance(part.GetType());
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(part), copy);
            return copy;
        }

        private static HashSet<Type> GetOwnTypes(SerializedProperty parts)
        {
            var types = new HashSet<Type>();
            for (var i = 0; i < parts.arraySize; i++)
            {
                if (parts.GetArrayElementAtIndex(i).managedReferenceValue is ActorPart part)
                {
                    types.Add(part.GetType());
                }
            }
            return types;
        }
    }
}
