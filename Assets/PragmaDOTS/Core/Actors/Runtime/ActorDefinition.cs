using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using UnityEngine;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// A kind of actor: its key, its tags and its parts. List it in an <see cref="ActorCatalog"/> to make it a type the
    /// game spawns, and place it on a map with an <see cref="ActorAuthoring"/>.
    /// </summary>
    /// <remarks>
    /// A definition with a parent inherits the parent's tags and parts: its own tags are added to them, and its own part
    /// replaces the parent's part of the same type. The key is not inherited: every definition of a catalog is a type of
    /// its own. A parent needs no key when only other definitions derive from it.
    /// </remarks>
    [CreateAssetMenu(fileName = "Actor", menuName = "Pragma/DOTS/Actor Definition")]
    public sealed class ActorDefinition : ScriptableObject
    {
        // A parent chain longer than this has a cycle.
        private const int MAX_DEPTH = 32;

        [SerializeField, Tooltip("Unique name of the actor type, 1 to 29 bytes of UTF-8 (\"rifleman\"). Code, commands and views refer to the type by it.")]
        private string _key = string.Empty;

        [SerializeField, Tooltip("The definition this one derives from: its tags and parts are inherited, own parts replace those of the same type.")]
        private ActorDefinition _parent;

        [SerializeField, Tooltip("Groups the actor belongs to (\"unit\", \"player\"), 1 to 29 bytes each; added to the parent's.")]
        private string[] _tags = Array.Empty<string>();

        [SerializeReference] private ActorPart[] _parts = Array.Empty<ActorPart>();

        public string Key
        {
            get => _key;
            set => _key = value ?? string.Empty;
        }

        public ActorDefinition Parent
        {
            get => _parent;
            set => _parent = value;
        }

        /// <summary>The tags of this definition alone, without the inherited ones.</summary>
        public IReadOnlyList<string> OwnTags => _tags;

        /// <summary>The parts of this definition alone, without the inherited ones.</summary>
        public IReadOnlyList<ActorPart> OwnParts => _parts;

        public void SetTags(params string[] tags)
        {
            _tags = tags ?? Array.Empty<string>();
        }

        public void SetParts(params ActorPart[] parts)
        {
            _parts = parts ?? Array.Empty<ActorPart>();
        }

        /// <summary>The ancestors of the definition, the root first, and the definition itself last.</summary>
        /// <returns>False when the parent chain has a cycle; <paramref name="result"/> then stops before it.</returns>
        public bool GetLineage(List<ActorDefinition> result)
        {
            var start = result.Count;
            for (var definition = this; definition != null; definition = definition._parent)
            {
                if (result.Count - start >= MAX_DEPTH || result.IndexOf(definition, start) >= 0)
                {
                    result.Reverse(start, result.Count - start);
                    return false;
                }
                result.Add(definition);
            }
            result.Reverse(start, result.Count - start);
            return true;
        }

        /// <summary>The tags of the definition with the inherited ones, each once, the parent's first.</summary>
        public void GetTags(List<string> result)
        {
            var lineage = new List<ActorDefinition>();
            GetLineage(lineage);
            foreach (var definition in lineage)
            {
                foreach (var tag in definition._tags)
                {
                    if (!string.IsNullOrEmpty(tag) && !result.Contains(tag))
                    {
                        result.Add(tag);
                    }
                }
            }
        }

        /// <summary>The parts the actor is built from: the parent's parts with the ones of this definition applied.</summary>
        public void GetParts(List<ActorPart> result)
        {
            var lineage = new List<ActorDefinition>();
            GetLineage(lineage);
            foreach (var definition in lineage)
            {
                ActorParts.Override(result, definition._parts);
            }
        }

        public bool TryGetPart<TPart>(out TPart part) where TPart : ActorPart
        {
            var parts = new List<ActorPart>();
            GetParts(parts);
            return ActorParts.TryGet(parts, out part);
        }

        /// <summary>The key as stored in <see cref="Actor"/>; false when it is empty or longer than 29 bytes of UTF-8.</summary>
        public bool TryGetKey(out FixedString32Bytes key) => TryCreateName(_key, out key);

        /// <summary>A key or tag name as the registry stores it; false when it is empty or longer than 29 bytes of UTF-8.</summary>
        public static bool TryCreateName(string value, out FixedString32Bytes name)
        {
            if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) > FixedString32Bytes.UTF8MaxLengthInBytes)
            {
                name = default;
                return false;
            }
            name = new FixedString32Bytes(value);
            return true;
        }

        // New definitions start with the parts marked as default (with the Lockstep module, a transform and an entity id).
        private void Reset()
        {
#if UNITY_EDITOR
            var candidates = new List<Type>();
            foreach (var type in UnityEditor.TypeCache.GetTypesWithAttribute<DefaultActorPartAttribute>())
            {
                if (typeof(ActorPart).IsAssignableFrom(type) && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                {
                    candidates.Add(type);
                }
            }
            var types = SelectDefaultParts(candidates);
            _parts = new ActorPart[types.Count];
            for (var i = 0; i < types.Count; i++)
            {
                _parts[i] = (ActorPart)Activator.CreateInstance(types[i]);
            }
#endif
        }

        // The default part types among the candidates, in order: a part another candidate replaces is left out.
        internal static List<Type> SelectDefaultParts(IEnumerable<Type> candidates)
        {
            var types = new List<Type>(candidates);
            var replaced = new HashSet<Type>();
            foreach (var type in types)
            {
                var replaces = GetDefaultAttribute(type).Replaces;
                if (replaces != null && replaces != type)
                {
                    replaced.Add(replaces);
                }
            }
            types.RemoveAll(replaced.Contains);
            types.Sort((a, b) =>
            {
                var order = GetDefaultAttribute(a).Order.CompareTo(GetDefaultAttribute(b).Order);
                return order != 0 ? order : string.CompareOrdinal(a.FullName, b.FullName);
            });
            return types;
        }

        private static DefaultActorPartAttribute GetDefaultAttribute(Type type)
        {
            return (DefaultActorPartAttribute)Attribute.GetCustomAttribute(type, typeof(DefaultActorPartAttribute));
        }

#if UNITY_EDITOR

        /// <summary>Raised in the editor when a definition is edited, for tools that show actors (previews).</summary>
        public static event Action<ActorDefinition> EditorChanged;

        private void OnValidate()
        {
            EditorChanged?.Invoke(this);
        }
#endif
    }
}
