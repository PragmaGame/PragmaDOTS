using System.Collections.Generic;
using UnityEditor;

namespace Pragma.Dots.Lockstep.Editor
{
    /// <summary>Inspector of an <see cref="ActorDefinition"/>: key, parent, tags and parts with what the parent gives.</summary>
    [CustomEditor(typeof(ActorDefinition))]
    internal sealed class ActorDefinitionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var definition = (ActorDefinition)target;

            var key = serializedObject.FindProperty("_key");
            EditorGUILayout.PropertyField(key);
            if (string.IsNullOrEmpty(key.stringValue))
            {
                EditorGUILayout.HelpBox("No key: a catalog cannot list it, only definitions derived from it.", MessageType.Info);
            }
            else if (!ActorDefinition.TryCreateName(key.stringValue, out _))
            {
                EditorGUILayout.HelpBox("The key must be at most 29 bytes of UTF-8.", MessageType.Error);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_parent"));

            var lineage = new List<ActorDefinition>();
            var inheritedTags = new List<string>();
            var parent = definition.Parent;
            var hasCycle = false;
            if (parent != null)
            {
                hasCycle = !parent.GetLineage(lineage) || lineage.Contains(definition);
                if (!hasCycle)
                {
                    parent.GetTags(inheritedTags);
                }
                else
                {
                    lineage.Clear();
                }
            }

            ActorTagsGUI.Draw(serializedObject.FindProperty("_tags"), inheritedTags);
            ActorPartsGUI.Draw(serializedObject.FindProperty("_parts"), ActorPartsGUI.GetInherited(lineage));
            serializedObject.ApplyModifiedProperties();

            var errors = new List<string>();
            if (hasCycle)
            {
                errors.Add("The parents form a cycle: nothing is inherited.");
            }
            ActorCatalog.ValidateOwn(definition, definition.name, errors);
            foreach (var error in errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
        }
    }
}
