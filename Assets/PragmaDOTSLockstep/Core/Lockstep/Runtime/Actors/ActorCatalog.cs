using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.Lockstep
{
    /// <summary>
    /// The actor types of a game: the definitions the simulation can spawn, by index. It becomes the actor registry of the
    /// presentation world, baked by <c>ActorCatalogAuthoring</c> in a subscene or built at run time with
    /// <see cref="LockstepActorUtility.CreateRegistry"/>, and the registry is copied into every simulation world.
    /// </summary>
    /// <remarks>
    /// The index of a definition is its type. Once a game has shipped, only add definitions at the end (an empty slot keeps
    /// the place of a removed one), or old replays break. All the definitions together may use up to 128 tags.
    /// </remarks>
    [CreateAssetMenu(fileName = "ActorCatalog", menuName = "Pragma/Lockstep/Actor Catalog")]
    public sealed class ActorCatalog : ScriptableObject
    {
        [SerializeField, Tooltip("The actor types; the index is the type.")]
        private ActorDefinition[] _actors = Array.Empty<ActorDefinition>();

        public IReadOnlyList<ActorDefinition> Actors => _actors;

        public void SetActors(params ActorDefinition[] actors)
        {
            _actors = actors ?? Array.Empty<ActorDefinition>();
        }

        /// <summary>The type of the definition with <paramref name="key"/>; -1 when there is none.</summary>
        public int IndexOf(string key)
        {
            for (var i = 0; i < _actors.Length; i++)
            {
                if (_actors[i] != null && _actors[i].Key == key)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Every tag of the definitions, each once, in ordinal order: the tag table of the registry.</summary>
        public void GetTags(List<string> result)
        {
            var tags = new List<string>();
            foreach (var definition in _actors)
            {
                if (definition == null)
                {
                    continue;
                }
                tags.Clear();
                definition.GetTags(tags);
                foreach (var tag in tags)
                {
                    if (!result.Contains(tag))
                    {
                        result.Add(tag);
                    }
                }
            }
            result.Sort(StringComparer.Ordinal);
        }

        /// <summary>Problems that would make the registry differ from what the catalog says, for the inspector and tests.</summary>
        public void Validate(List<string> errors)
        {
            var keys = new Dictionary<string, int>();
            for (var i = 0; i < _actors.Length; i++)
            {
                var definition = _actors[i];
                if (definition == null)
                {
                    continue;
                }
                if (!definition.TryGetKey(out _))
                {
                    errors.Add($"{i} ({definition.name}): the key '{definition.Key}' must be 1 to 29 bytes of UTF-8.");
                }
                else if (keys.TryGetValue(definition.Key, out var other))
                {
                    errors.Add($"{i} ({definition.name}): the key '{definition.Key}' is already the key of {other}.");
                }
                else
                {
                    keys.Add(definition.Key, i);
                }

                var lineage = new List<ActorDefinition>();
                if (!definition.GetLineage(lineage))
                {
                    errors.Add($"{i} ({definition.name}): its parents form a cycle.");
                }
                foreach (var ancestor in lineage)
                {
                    ValidateOwn(ancestor, $"{i} ({ancestor.name})", errors);
                }
            }

            var tags = new List<string>();
            GetTags(tags);
            if (tags.Count > LockstepActorTagSet.CAPACITY)
            {
                errors.Add($"The definitions use {tags.Count} tags; the registry holds {LockstepActorTagSet.CAPACITY}.");
            }
        }

        /// <summary>Problems of the tags and parts of <paramref name="definition"/> itself, for its inspector.</summary>
        public static void ValidateOwn(ActorDefinition definition, string label, List<string> errors)
        {
            foreach (var tag in definition.OwnTags)
            {
                if (!ActorDefinition.TryCreateName(tag, out _))
                {
                    errors.Add($"{label}: the tag '{tag}' must be 1 to 29 bytes of UTF-8.");
                }
            }
            var parts = definition.OwnParts;
            for (var p = 0; p < parts.Count; p++)
            {
                if (parts[p] == null)
                {
                    errors.Add($"{label}: part {p} is empty, or its class was renamed or removed.");
                }
                else if (ActorParts.IndexOf(parts, parts[p].GetType()) != p)
                {
                    errors.Add($"{label}: two parts of type {parts[p].GetType().Name}; the second one replaces the first.");
                }
            }
        }

        // The tags of a definition as bits of the tag table.
        internal static LockstepActorTagSet GetTagSet(ActorDefinition definition, List<string> table)
        {
            var set = new LockstepActorTagSet();
            var tags = new List<string>();
            definition.GetTags(tags);
            foreach (var tag in tags)
            {
                var index = table.IndexOf(tag);
                if (index >= 0 && index < LockstepActorTagSet.CAPACITY)
                {
                    set.Add(new LockstepActorTag(index));
                }
            }
            return set;
        }
    }
}
