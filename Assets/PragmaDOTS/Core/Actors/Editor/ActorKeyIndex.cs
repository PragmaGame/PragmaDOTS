using System;
using System.Collections.Generic;
using UnityEditor;

namespace Pragma.Dots.Actors.Editor
{
    /// <summary>The actor definitions of the project by key, for the fields that show a key as its definition.</summary>
    internal static class ActorKeyIndex
    {
        private static Dictionary<string, ActorDefinition> _definitions;

        /// <summary>The definition with the key, or null; with several, the first the asset database lists.</summary>
        public static ActorDefinition Find(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            // A definition found earlier may have been deleted or given another key since.
            var definitions = _definitions;
            if (definitions == null || definitions.TryGetValue(key, out var cached) && (cached == null || cached.Key != key))
            {
                definitions = Build();
                _definitions = definitions;
            }

            return definitions.TryGetValue(key, out var definition) ? definition : null;
        }

        /// <summary>Forgets the index: the next search lists the definitions again.</summary>
        public static void Reset()
        {
            _definitions = null;
        }

        // Built aside: loading a definition validates it, and its EditorChanged resets the index while it is being built.
        private static Dictionary<string, ActorDefinition> Build()
        {
            var definitions = new Dictionary<string, ActorDefinition>(StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(ActorDefinition)))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ActorDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null && !string.IsNullOrEmpty(definition.Key) && !definitions.ContainsKey(definition.Key))
                {
                    definitions.Add(definition.Key, definition);
                }
            }
            return definitions;
        }

        // A key typed into a definition or a new definition must be found without waiting for a domain reload.
        [InitializeOnLoadMethod]
        private static void ResetOnChanges()
        {
            EditorApplication.projectChanged += Reset;
            ActorDefinition.EditorChanged += _ => Reset();
        }
    }
}
