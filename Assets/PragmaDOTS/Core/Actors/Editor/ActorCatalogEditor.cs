using System.Collections.Generic;
using UnityEditor;

namespace Pragma.Dots.Actors.Editor
{
    /// <summary>Inspector of an <see cref="ActorCatalog"/>: the types, the tag table they make, and what would break the registry.</summary>
    [CustomEditor(typeof(ActorCatalog))]
    internal sealed class ActorCatalogEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var catalog = (ActorCatalog)target;

            var tags = new List<string>();
            catalog.GetTags(tags);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Tags ({tags.Count} of {ActorTagSet.CAPACITY})", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(tags.Count > 0 ? string.Join(", ", tags) : "(none)", EditorStyles.wordWrappedLabel);

            var errors = new List<string>();
            catalog.Validate(errors);
            foreach (var error in errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
        }
    }
}
