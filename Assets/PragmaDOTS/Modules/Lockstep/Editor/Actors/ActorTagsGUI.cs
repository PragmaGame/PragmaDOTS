using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Lockstep.Editor
{
    /// <summary>Inspector of the tags of a definition: the inherited ones, the own ones, and a menu of the tags the project uses.</summary>
    internal static class ActorTagsGUI
    {
        public static void Draw(SerializedProperty tags, IReadOnlyList<string> inherited)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tags", EditorStyles.boldLabel);
            if (inherited.Count > 0)
            {
                EditorGUILayout.LabelField("Inherited", string.Join(", ", inherited), EditorStyles.wordWrappedLabel);
            }
            for (var i = 0; i < tags.arraySize; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var tag = tags.GetArrayElementAtIndex(i);
                    EditorGUILayout.PropertyField(tag, GUIContent.none);
                    if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        tags.DeleteArrayElementAtIndex(i);
                        tags.serializedObject.ApplyModifiedProperties();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            var content = new GUIContent("Add Tag");
            var rect = GUILayoutUtility.GetRect(content, EditorStyles.miniPullDown);
            if (!EditorGUI.DropdownButton(rect, content, FocusType.Passive, EditorStyles.miniPullDown))
            {
                return;
            }
            var present = new HashSet<string>(inherited);
            for (var i = 0; i < tags.arraySize; i++)
            {
                present.Add(tags.GetArrayElementAtIndex(i).stringValue);
            }
            var serializedObject = tags.serializedObject;
            var path = tags.propertyPath;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("New Tag"), false, () => Add(serializedObject, path, string.Empty));
            var known = GetProjectTags();
            if (known.Count > 0)
            {
                menu.AddSeparator(string.Empty);
            }
            foreach (var tag in known)
            {
                if (present.Contains(tag))
                {
                    menu.AddDisabledItem(new GUIContent(tag));
                }
                else
                {
                    var value = tag;
                    menu.AddItem(new GUIContent(tag), false, () => Add(serializedObject, path, value));
                }
            }
            menu.DropDown(rect);
        }

        /// <summary>Every tag of the actor definitions of the project, sorted.</summary>
        public static List<string> GetProjectTags()
        {
            var result = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(ActorDefinition)))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ActorDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null)
                {
                    continue;
                }
                foreach (var tag in definition.OwnTags)
                {
                    if (!string.IsNullOrEmpty(tag))
                    {
                        result.Add(tag);
                    }
                }
            }
            return new List<string>(result);
        }

        private static void Add(SerializedObject serializedObject, string path, string value)
        {
            serializedObject.Update();
            var tags = serializedObject.FindProperty(path);
            tags.arraySize++;
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = value;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
