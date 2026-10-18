using System;
using System.Collections.Generic;
using Pragma.Dots.Mathematics;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Dots.Actors
{
    /// <summary>Builds actors at run time: the actor registry of a catalog, and actors placed on a map.</summary>
    /// <remarks>
    /// Baking and these methods build the same entities from the same parts, so a game can bake its catalog and maps into
    /// subscenes, or build them at run time without subscenes (from its own level data), or mix both.
    /// </remarks>
    public static class ActorUtility
    {
        public static bool HasRegistry(EntityManager entityManager)
        {
            using (var query = CreateRegistryQuery(entityManager))
            {
                return !query.IsEmptyIgnoreFilter;
            }
        }

        /// <summary>
        /// Builds the actor registry of <paramref name="catalog"/> in the world of <paramref name="entityManager"/> (with the
        /// Lockstep module, the presentation world before the session starts): a prefab entity per definition, as baking
        /// would.
        /// </summary>
        /// <returns>The registry entity.</returns>
        public static Entity CreateRegistry(EntityManager entityManager, ActorCatalog catalog)
        {
            var tags = new List<string>();
            catalog.GetTags(tags);
            if (tags.Count > ActorTagSet.CAPACITY)
            {
                Debug.LogError($"[Actors] The actor catalog '{catalog.name}' uses {tags.Count} tags; only the first {ActorTagSet.CAPACITY} are kept.", catalog);
                tags.RemoveRange(ActorTagSet.CAPACITY, tags.Count - ActorTagSet.CAPACITY);
            }

            var definitions = catalog.Actors;
            var types = new NativeArray<ActorType>(definitions.Count, Allocator.Temp);
            var parts = new List<ActorPart>();
            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }
                if (!definition.TryGetKey(out var key))
                {
                    Debug.LogError($"[Actors] The actor '{definition.name}' needs a key of 1 to 29 bytes of UTF-8; its slot stays empty.", definition);
                    continue;
                }

                var prefab = entityManager.CreateEntity();
                entityManager.SetName(prefab, definition.Key);
                entityManager.AddComponent<Prefab>(prefab);
                entityManager.AddComponentData(prefab, new Actor { key = key, type = i });
                parts.Clear();
                definition.GetParts(parts);
                Build(new EntityManagerActorBuilder(entityManager, prefab, key, false, FixedPose.Identity, new FixedVector3(1, 1, 1), parts), parts, definition);
                types[i] = new ActorType { key = key, prefab = prefab, tags = ActorCatalog.GetTagSet(definition, tags) };
            }

            var registry = entityManager.CreateEntity(ComponentType.ReadWrite<ActorRegistry>());
            entityManager.SetName(registry, "ActorRegistry");
            var tagNames = entityManager.AddBuffer<ActorTagName>(registry);
            foreach (var tag in tags)
            {
                tagNames.Add(new ActorTagName { name = new FixedString32Bytes(tag) });
            }
            entityManager.AddBuffer<ActorType>(registry).AddRange(types);
            return registry;
        }

        /// <summary>
        /// Places an actor of <paramref name="definition"/> in the world of <paramref name="entityManager"/>, as an
        /// <see cref="ActorAuthoring"/> in a subscene would: a <see cref="PlacedActor"/> whose type is found by key later
        /// (<see cref="ResolveTypes(EntityManager)"/>; the Lockstep module does it when it copies the placed actors into a
        /// simulation, so every simulation created afterwards starts with this one).
        /// </summary>
        /// <param name="placement">Where the actor stands.</param>
        /// <param name="placementScale">Its scale along its local axes.</param>
        /// <param name="overrides">Parts that replace the definition's part of their type.</param>
        /// <param name="order">Where the actor goes among the placed actors: the same on every machine, unique per map.</param>
        public static Entity CreatePlaced(EntityManager entityManager, ActorDefinition definition, in FixedPose placement, FixedVector3 placementScale,
            IReadOnlyList<ActorPart> overrides, ulong order)
        {
            if (!definition.TryGetKey(out var key))
            {
                throw new ArgumentException($"The actor '{definition.name}' needs a key of 1 to 29 bytes of UTF-8.", nameof(definition));
            }
            var parts = new List<ActorPart>();
            definition.GetParts(parts);
            ActorParts.Override(parts, overrides);

            var entity = entityManager.CreateEntity();
            entityManager.SetName(entity, definition.Key);
            entityManager.AddComponentData(entity, new PlacedActor { order = order });
            entityManager.AddComponentData(entity, new Actor { key = key, type = -1 });
            Build(new EntityManagerActorBuilder(entityManager, entity, key, true, placement, placementScale, parts), parts, definition);
            return entity;
        }

        /// <summary>Places the actor of <paramref name="authoring"/>, from a scene loaded without baking, as <see cref="CreatePlaced(EntityManager, ActorDefinition, in FixedPose, FixedVector3, IReadOnlyList{ActorPart}, ulong)"/>.</summary>
        public static Entity CreatePlaced(EntityManager entityManager, ActorAuthoring authoring, ulong order)
        {
            return CreatePlaced(entityManager, authoring.Definition, authoring.GetPlacement(), authoring.GetPlacementScale(), authoring.Overrides, order);
        }

        /// <summary>
        /// Builds <paramref name="parts"/> into an actor; a part that throws is logged against <paramref name="context"/>
        /// and the others still build.
        /// </summary>
        public static void Build(IActorBuilder builder, IReadOnlyList<ActorPart> parts, UnityEngine.Object context)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                if (parts[i] == null)
                {
                    continue;
                }
                try
                {
                    parts[i].Build(builder);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[Actors] The part {parts[i].GetType().Name} of the actor '{builder.Key}' failed to build: {exception}", context);
                }
            }
        }

        /// <summary>
        /// Gives every placed actor of the world of <paramref name="entityManager"/> the type of its key in the registry of
        /// that world. Baking and <see cref="CreatePlaced(EntityManager, ActorDefinition, in FixedPose, FixedVector3, IReadOnlyList{ActorPart}, ulong)"/>
        /// do not know the catalog and leave the type at -1; call it once the registry and the maps are loaded.
        /// </summary>
        public static void ResolveTypes(EntityManager entityManager)
        {
            using (var query = entityManager.CreateEntityQuery(new EntityQueryDesc
                   {
                       All = new[] { ComponentType.ReadOnly<PlacedActor>(), ComponentType.ReadWrite<Actor>() },
                       Options = EntityQueryOptions.IncludeDisabledEntities,
                   }))
            {
                ResolveTypes(entityManager, query.ToEntityArray(Allocator.Temp));
            }
        }

        /// <summary>
        /// Gives the actors among <paramref name="entities"/> the type of their key in the registry of their world; a key
        /// no type has gets -1 and a warning. Entities that are not actors are skipped.
        /// </summary>
        public static void ResolveTypes(EntityManager entityManager, NativeArray<Entity> entities)
        {
            var hasRegistry = ActorLookup.TryGet(entityManager, out var actors);
            for (var i = 0; i < entities.Length; i++)
            {
                if (!entityManager.HasComponent<Actor>(entities[i]))
                {
                    continue;
                }
                var actor = entityManager.GetComponentData<Actor>(entities[i]);
                if (hasRegistry && actors.TryGetType(actor.key, out var type))
                {
                    actor.type = type;
                }
                else
                {
                    actor.type = -1;
                    Debug.LogWarning($"[Actors] The placed actor '{actor.key}' has no type in the actor registry: it has no tags, and nothing spawns more of it.");
                }
                entityManager.SetComponentData(entities[i], actor);
            }
        }

        private static EntityQuery CreateRegistryQuery(EntityManager entityManager)
        {
            return entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ActorRegistry>(), ComponentType.ReadOnly<ActorType>(), ComponentType.ReadOnly<ActorTagName>() },
                Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities,
            });
        }
    }
}
