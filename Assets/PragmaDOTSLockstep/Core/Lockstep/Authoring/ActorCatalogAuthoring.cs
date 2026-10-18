using System.Collections.Generic;
using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep.Authoring
{
    /// <summary>
    /// Bakes an <see cref="ActorCatalog"/> into the actor registry: a prefab entity per definition, built from its parts,
    /// and the tag table. Put it in a subscene of the client (or local) world; the registry is copied into every simulation
    /// world. Start sessions with <c>waitForActorRegistry</c>, so the subscene has loaded when the simulation is created.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Pragma/Lockstep/Actor Catalog")]
    public sealed class ActorCatalogAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("The actor types this registry spawns.")]
        private ActorCatalog _catalog;

        public ActorCatalog Catalog
        {
            get => _catalog;
            set => _catalog = value;
        }

        private sealed class Baker : Baker<ActorCatalogAuthoring>
        {
            public override void Bake(ActorCatalogAuthoring authoring)
            {
                var catalog = DependsOn(authoring._catalog);
                if (catalog == null)
                {
                    Debug.LogError($"[Lockstep] {nameof(ActorCatalogAuthoring)} on '{authoring.name}' has no catalog.", authoring);
                    return;
                }

                var tags = new List<string>();
                catalog.GetTags(tags);
                if (tags.Count > LockstepActorTagSet.CAPACITY)
                {
                    Debug.LogError($"[Lockstep] The actor catalog '{catalog.name}' uses {tags.Count} tags; only the first {LockstepActorTagSet.CAPACITY} are kept.", catalog);
                    tags.RemoveRange(LockstepActorTagSet.CAPACITY, tags.Count - LockstepActorTagSet.CAPACITY);
                }

                var definitions = catalog.Actors;
                var types = new NativeArray<LockstepActorType>(definitions.Count, Allocator.Temp);
                var parts = new List<ActorPart>();
                for (var i = 0; i < definitions.Count; i++)
                {
                    var definition = definitions[i];
                    if (definition == null)
                    {
                        continue;
                    }
                    ActorBakingUtility.DependsOnLineage(this, definition);
                    if (!definition.TryGetKey(out var key))
                    {
                        Debug.LogError($"[Lockstep] The actor '{definition.name}' needs a key of 1 to 29 bytes of UTF-8; its slot stays empty.", definition);
                        continue;
                    }

                    var prefab = CreateAdditionalEntity(TransformUsageFlags.None, false, definition.Key);
                    AddComponent<Prefab>(prefab);
                    AddComponent(prefab, new LockstepActor { key = key, type = i });
                    parts.Clear();
                    definition.GetParts(parts);
                    var builder = new BakerActorBuilder(this, prefab, key, false, LockstepTransform.Identity, new FixedVector3(1, 1, 1), parts);
                    LockstepActorUtility.Build(builder, parts, definition);
                    types[i] = new LockstepActorType { key = key, prefab = prefab, tags = ActorCatalog.GetTagSet(definition, tags) };
                }

                var registry = GetEntity(TransformUsageFlags.None);
                AddComponent<LockstepActorRegistry>(registry);
                var tagNames = AddBuffer<LockstepActorTagName>(registry);
                foreach (var tag in tags)
                {
                    tagNames.Add(new LockstepActorTagName { name = new FixedString32Bytes(tag) });
                }
                AddBuffer<LockstepActorType>(registry).AddRange(types);
            }
        }
    }
}
