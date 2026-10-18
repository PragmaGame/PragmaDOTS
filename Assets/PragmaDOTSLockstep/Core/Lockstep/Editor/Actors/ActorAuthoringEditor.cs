using System.Collections.Generic;
using UnityEditor;

namespace Pragma.Lockstep.Editor
{
    /// <summary>Inspector of a placed actor: its definition, what the definition gives, and the parts this placement overrides.</summary>
    [CustomEditor(typeof(ActorAuthoring))]
    internal sealed class ActorAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var authoring = (ActorAuthoring)target;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_definition"));

            var definition = authoring.Definition;
            var lineage = new List<ActorDefinition>();
            if (definition == null)
            {
                EditorGUILayout.HelpBox("Choose the actor type placed here.", MessageType.Warning);
            }
            else
            {
                if (!definition.GetLineage(lineage))
                {
                    EditorGUILayout.HelpBox($"The parents of '{definition.name}' form a cycle.", MessageType.Error);
                }
                var tags = new List<string>();
                definition.GetTags(tags);
                EditorGUILayout.LabelField("Key", string.IsNullOrEmpty(definition.Key) ? "(none)" : definition.Key);
                EditorGUILayout.LabelField("Tags", tags.Count > 0 ? string.Join(", ", tags) : "(none)", EditorStyles.wordWrappedLabel);
                if (!definition.TryGetKey(out _))
                {
                    EditorGUILayout.HelpBox("The definition needs a key of 1 to 29 bytes of UTF-8 to be placed.", MessageType.Error);
                }
            }

            ActorPartsGUI.Draw(serializedObject.FindProperty("_overrides"), ActorPartsGUI.GetInherited(lineage));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
