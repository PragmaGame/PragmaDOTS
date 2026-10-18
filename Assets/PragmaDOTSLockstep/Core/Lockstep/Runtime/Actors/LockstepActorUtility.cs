using System;
using System.Collections.Generic;
using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Lockstep
{
    /// <summary>
    /// Builds actors at run time and carries the content of a presentation world into a simulation world: the actor
    /// registry and the scene entities (placed actors).
    /// </summary>
    /// <remarks>
    /// Baking and these methods build the same entities from the same parts, so a game can bake its catalog and maps into
    /// subscenes, or build them at run time without subscenes (from its own level data), or mix both.
    /// </remarks>
    public static class LockstepActorUtility
    {
        public static bool HasRegistry(EntityManager entityManager)
        {
            using (var query = CreateRegistryQuery(entityManager))
            {
                return !query.IsEmptyIgnoreFilter;
            }
        }

        /// <summary>
        /// Builds the actor registry of <paramref name="catalog"/> in the world of <paramref name="entityManager"/> (usually
        /// the presentation world, before the session starts): a prefab entity per definition, as baking would.
        /// </summary>
        /// <returns>The registry entity.</returns>
        public static Entity CreateRegistry(EntityManager entityManager, ActorCatalog catalog)
        {
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
                if (!definition.TryGetKey(out var key))
                {
                    Debug.LogError($"[Lockstep] The actor '{definition.name}' needs a key of 1 to 29 bytes of UTF-8; its slot stays empty.", definition);
                    continue;
                }

                var prefab = entityManager.CreateEntity();
                entityManager.SetName(prefab, definition.Key);
                entityManager.AddComponent<Prefab>(prefab);
                entityManager.AddComponentData(prefab, new LockstepActor { key = key, type = i });
                parts.Clear();
                definition.GetParts(parts);
                Build(new EntityManagerActorBuilder(entityManager, prefab, key, false, LockstepTransform.Identity, new FixedVector3(1, 1, 1), parts), parts, definition);
                types[i] = new LockstepActorType { key = key, prefab = prefab, tags = ActorCatalog.GetTagSet(definition, tags) };
            }

            var registry = entityManager.CreateEntity(ComponentType.ReadWrite<LockstepActorRegistry>());
            entityManager.SetName(registry, "LockstepActorRegistry");
            var tagNames = entityManager.AddBuffer<LockstepActorTagName>(registry);
            foreach (var tag in tags)
            {
                tagNames.Add(new LockstepActorTagName { name = new FixedString32Bytes(tag) });
            }
            entityManager.AddBuffer<LockstepActorType>(registry).AddRange(types);
            return registry;
        }

        /// <summary>
        /// Places an actor of <paramref name="definition"/> in the world of <paramref name="entityManager"/> as a scene
        /// entity, as an <see cref="ActorAuthoring"/> in a subscene would: every simulation created afterwards starts with it.
        /// </summary>
        /// <param name="placement">Where the actor stands.</param>
        /// <param name="placementScale">Its scale along its local axes, for parts that take a non-uniform one.</param>
        /// <param name="overrides">Parts that replace the definition's part of their type.</param>
        /// <param name="order">Where the actor goes among the scene entities: the same on every client, unique per map.</param>
        public static Entity CreatePlaced(EntityManager entityManager, ActorDefinition definition, in LockstepTransform placement, FixedVector3 placementScale,
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
            entityManager.AddComponentData(entity, new LockstepSceneEntity { order = order });
            entityManager.AddComponentData(entity, new LockstepActor { key = key, type = -1 });
            Build(new EntityManagerActorBuilder(entityManager, entity, key, true, placement, placementScale, parts), parts, definition);
            return entity;
        }

        /// <summary>Places the actor of <paramref name="authoring"/>, from a scene loaded without baking, as <see cref="CreatePlaced(EntityManager, ActorDefinition, in LockstepTransform, FixedVector3, IReadOnlyList{ActorPart}, ulong)"/>.</summary>
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
                    Debug.LogError($"[Lockstep] The part {parts[i].GetType().Name} of the actor '{builder.Key}' failed to build: {exception}", context);
                }
            }
        }

        /// <summary>
        /// Copies the actor registry of <paramref name="source"/> into <paramref name="destination"/>: every prefab (with its
        /// linked entities), under the same type indices, and the tag table.
        /// </summary>
        /// <returns>False when the source has no registry; the destination then gets an empty one.</returns>
        public static bool CopyRegistry(EntityManager source, EntityManager destination)
        {
            var sourceTypes = new NativeList<LockstepActorType>(Allocator.Temp);
            var tagNames = new NativeList<LockstepActorTagName>(Allocator.Temp);
            using (var query = CreateRegistryQuery(source))
            {
                var registries = query.ToEntityArray(Allocator.Temp);
                if (registries.Length > 1)
                {
                    Debug.LogWarning("[Lockstep] More than one actor registry found; only the first one is used.");
                }
                if (registries.Length > 0)
                {
                    sourceTypes.AddRange(source.GetBuffer<LockstepActorType>(registries[0], true).AsNativeArray());
                    tagNames.AddRange(source.GetBuffer<LockstepActorTagName>(registries[0], true).AsNativeArray());
                }
            }

            // Every entity of each prefab group must be copied in one call so internal references get remapped.
            var toCopy = new NativeList<Entity>(Allocator.Temp);
            var copyIndex = new NativeHashMap<Entity, int>(16, Allocator.Temp);
            for (var i = 0; i < sourceTypes.Length; i++)
            {
                var prefab = sourceTypes[i].prefab;
                if (prefab == Entity.Null || !source.Exists(prefab))
                {
                    continue;
                }
                if (source.HasBuffer<LinkedEntityGroup>(prefab))
                {
                    var group = source.GetBuffer<LinkedEntityGroup>(prefab, true);
                    for (var g = 0; g < group.Length; g++)
                    {
                        AddUnique(group[g].Value, toCopy, copyIndex);
                    }
                }
                AddUnique(prefab, toCopy, copyIndex);
            }

            var copies = new NativeArray<Entity>(toCopy.Length, Allocator.Temp);
            if (toCopy.Length > 0)
            {
                CreateArchetypes(source, destination, toCopy.AsArray());
                destination.CopyEntitiesFrom(source, toCopy.AsArray(), copies);
            }

            var types = new NativeArray<LockstepActorType>(sourceTypes.Length, Allocator.Temp);
            for (var i = 0; i < sourceTypes.Length; i++)
            {
                var type = sourceTypes[i];
                type.prefab = copyIndex.TryGetValue(type.prefab, out var index) ? copies[index] : Entity.Null;
                types[i] = type;
            }

            var registry = destination.CreateEntity(ComponentType.ReadWrite<LockstepActorRegistry>(), ComponentType.ReadWrite<LockstepActorType>(),
                ComponentType.ReadWrite<LockstepActorTagName>());
            destination.SetName(registry, "LockstepActorRegistry");
            destination.GetBuffer<LockstepActorTagName>(registry).AddRange(tagNames.AsArray());
            destination.GetBuffer<LockstepActorType>(registry).AddRange(types);
            return sourceTypes.Length > 0;
        }

        /// <summary>
        /// Copies every <see cref="LockstepSceneEntity"/> of <paramref name="source"/> (with its linked entities) into
        /// <paramref name="destination"/>, ordered by <see cref="LockstepSceneEntity.order"/>, and gives the placed actors
        /// the type of their key in the registry of <paramref name="destination"/> (copy the registry first). Prefabs are
        /// not scene entities and are skipped.
        /// </summary>
        /// <remarks>
        /// References between the copied entities are remapped to the copies; references to anything else, registry
        /// prefabs included, become <see cref="Entity.Null"/>, so scene entities name actor types by key.
        /// </remarks>
        /// <returns>The number of scene entities copied.</returns>
        public static int CopySceneEntities(EntityManager source, EntityManager destination)
        {
            var description = new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<LockstepSceneEntity>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            };
            NativeArray<Entity> entities;
            NativeArray<LockstepSceneEntity> markers;
            using (var query = source.CreateEntityQuery(description))
            {
                entities = query.ToEntityArray(Allocator.Temp);
                markers = query.ToComponentDataArray<LockstepSceneEntity>(Allocator.Temp);
            }
            if (entities.Length == 0)
            {
                return 0;
            }

            // The query order follows the order the subscenes loaded in, which may differ between clients.
            var order = new NativeArray<int>(entities.Length, Allocator.Temp);
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }
            order.Sort(new SceneEntityComparer { markers = markers });

            var toCopy = new NativeList<Entity>(Allocator.Temp);
            var copyIndex = new NativeHashMap<Entity, int>(entities.Length, Allocator.Temp);
            for (var i = 0; i < order.Length; i++)
            {
                var entity = entities[order[i]];
                AddUnique(entity, toCopy, copyIndex);
                if (source.HasBuffer<LinkedEntityGroup>(entity))
                {
                    var group = source.GetBuffer<LinkedEntityGroup>(entity, true);
                    for (var g = 0; g < group.Length; g++)
                    {
                        AddUnique(group[g].Value, toCopy, copyIndex);
                    }
                }
            }
            CreateArchetypes(source, destination, toCopy.AsArray());
            var copies = new NativeArray<Entity>(toCopy.Length, Allocator.Temp);
            destination.CopyEntitiesFrom(source, toCopy.AsArray(), copies);
            ResolveTypes(destination, copies);
            return entities.Length;
        }

        // A placed actor was baked or built without the catalog: its key finds its type in the registry.
        private static void ResolveTypes(EntityManager entityManager, NativeArray<Entity> entities)
        {
            var hasRegistry = LockstepActors.TryGet(entityManager, out var actors);
            for (var i = 0; i < entities.Length; i++)
            {
                if (!entityManager.HasComponent<LockstepActor>(entities[i]))
                {
                    continue;
                }
                var actor = entityManager.GetComponentData<LockstepActor>(entities[i]);
                if (hasRegistry && actors.TryGetType(actor.key, out var type))
                {
                    actor.type = type;
                }
                else
                {
                    actor.type = -1;
                    Debug.LogWarning($"[Lockstep] The placed actor '{actor.key}' has no type in the actor registry: it has no tags, and nothing spawns more of it.");
                }
                entityManager.SetComponentData(entities[i], actor);
            }
        }

        // CopyEntitiesFrom creates the archetypes of the copies in an order of its own: it moves them chunk by chunk, and
        // which chunk goes first depends on where the source allocated them, which differs between clients. The order of
        // archetypes is simulation state (queries and the checksum walk archetypes in creation order), so they are
        // created here first, in the order of the entities, and the copy only fills them.
        private static void CreateArchetypes(EntityManager source, EntityManager destination, NativeArray<Entity> entities)
        {
            for (var i = 0; i < entities.Length; i++)
            {
                using (var types = source.GetComponentTypes(entities[i], Allocator.Temp))
                {
                    destination.CreateArchetype(types);
                }
            }
        }

        private static void AddUnique(Entity entity, NativeList<Entity> list, NativeHashMap<Entity, int> index)
        {
            if (index.ContainsKey(entity))
            {
                return;
            }
            index.Add(entity, list.Length);
            list.Add(entity);
        }

        // By the baked order; equal orders keep the query order rather than whatever an unstable sort leaves.
        private struct SceneEntityComparer : IComparer<int>
        {
            public NativeArray<LockstepSceneEntity> markers;

            public int Compare(int x, int y)
            {
                var result = markers[x].order.CompareTo(markers[y].order);
                return result != 0 ? result : x.CompareTo(y);
            }
        }

        private static EntityQuery CreateRegistryQuery(EntityManager entityManager)
        {
            return entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<LockstepActorRegistry>(), ComponentType.ReadOnly<LockstepActorType>(), ComponentType.ReadOnly<LockstepActorTagName>() },
                Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities,
            });
        }
    }
}
