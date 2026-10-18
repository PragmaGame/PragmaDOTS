using System.Collections.Generic;
using Pragma.Dots.Actors;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// Carries the actors of a presentation world into a simulation world: the actor registry and the placed actors.
    /// <see cref="LockstepClientWorldUtility.CreateSimulationOptions"/> copies both before tick 0.
    /// </summary>
    public static class LockstepActorUtility
    {
        /// <summary>
        /// Copies the actor registry of <paramref name="source"/> into <paramref name="destination"/>: every prefab (with its
        /// linked entities), under the same type indices, and the tag table.
        /// </summary>
        /// <returns>False when the source has no registry; the destination then gets an empty one.</returns>
        public static bool CopyRegistry(EntityManager source, EntityManager destination)
        {
            var sourceTypes = new NativeList<ActorType>(Allocator.Temp);
            var tagNames = new NativeList<ActorTagName>(Allocator.Temp);
            using (var query = CreateRegistryQuery(source))
            {
                var registries = query.ToEntityArray(Allocator.Temp);
                if (registries.Length > 1)
                {
                    Debug.LogWarning("[Lockstep] More than one actor registry found; only the first one is used.");
                }
                if (registries.Length > 0)
                {
                    sourceTypes.AddRange(source.GetBuffer<ActorType>(registries[0], true).AsNativeArray());
                    tagNames.AddRange(source.GetBuffer<ActorTagName>(registries[0], true).AsNativeArray());
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
                CopyInOrder(source, destination, toCopy.AsArray(), copies);
            }

            var types = new NativeArray<ActorType>(sourceTypes.Length, Allocator.Temp);
            for (var i = 0; i < sourceTypes.Length; i++)
            {
                var type = sourceTypes[i];
                type.prefab = copyIndex.TryGetValue(type.prefab, out var index) ? copies[index] : Entity.Null;
                types[i] = type;
            }

            var registry = destination.CreateEntity(ComponentType.ReadWrite<ActorRegistry>(), ComponentType.ReadWrite<ActorType>(),
                ComponentType.ReadWrite<ActorTagName>());
            destination.SetName(registry, "ActorRegistry");
            destination.GetBuffer<ActorTagName>(registry).AddRange(tagNames.AsArray());
            destination.GetBuffer<ActorType>(registry).AddRange(types);
            return sourceTypes.Length > 0;
        }

        /// <summary>
        /// Copies every <see cref="PlacedActor"/> of <paramref name="source"/> (with its linked entities) into
        /// <paramref name="destination"/>, ordered by <see cref="PlacedActor.order"/>, and gives the actors the type of their
        /// key in the registry of <paramref name="destination"/> (copy the registry first). Prefabs are skipped.
        /// </summary>
        /// <remarks>
        /// References between the copied entities are remapped to the copies; references to anything else, registry
        /// prefabs included, become <see cref="Entity.Null"/>, so placed actors name actor types by key.
        /// </remarks>
        /// <returns>The number of placed actors copied.</returns>
        public static int CopyPlacedActors(EntityManager source, EntityManager destination)
        {
            var description = new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacedActor>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            };
            NativeArray<Entity> entities;
            NativeArray<PlacedActor> markers;
            using (var query = source.CreateEntityQuery(description))
            {
                entities = query.ToEntityArray(Allocator.Temp);
                markers = query.ToComponentDataArray<PlacedActor>(Allocator.Temp);
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
            order.Sort(new PlacedActorComparer { markers = markers });

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
            var copies = new NativeArray<Entity>(toCopy.Length, Allocator.Temp);
            CopyInOrder(source, destination, toCopy.AsArray(), copies);
            // A placed actor was baked or built without the catalog: its key finds its type in the registry.
            ActorUtility.ResolveTypes(destination, copies);
            return entities.Length;
        }

        // CopyEntitiesFrom moves the copies chunk by chunk, and which chunk goes first depends on where the source
        // allocated them: chunk indices are shared by every world of the process and differ between clients. Archetypes
        // and the chunks inside them are simulation state (queries and the checksum walk them in order), so the archetypes
        // are created first, in the order of the entities, and after the copy the entities are moved out and back one by
        // one in that order, which fills fresh chunks in that order.
        private static void CopyInOrder(EntityManager source, EntityManager destination, NativeArray<Entity> entities, NativeArray<Entity> copies)
        {
            for (var i = 0; i < entities.Length; i++)
            {
                using (var types = source.GetComponentTypes(entities[i], Allocator.Temp))
                {
                    destination.CreateArchetype(types);
                }
            }
            destination.CopyEntitiesFrom(source, entities, copies);
            for (var i = 0; i < copies.Length; i++)
            {
                destination.AddComponent<LockstepCopyOrder>(copies[i]);
            }
            for (var i = 0; i < copies.Length; i++)
            {
                destination.RemoveComponent<LockstepCopyOrder>(copies[i]);
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

        private static EntityQuery CreateRegistryQuery(EntityManager entityManager)
        {
            return entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ActorRegistry>(), ComponentType.ReadOnly<ActorType>(), ComponentType.ReadOnly<ActorTagName>() },
                Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities,
            });
        }

        // By the baked order; equal orders keep the query order rather than whatever an unstable sort leaves.
        private struct PlacedActorComparer : IComparer<int>
        {
            public NativeArray<PlacedActor> markers;

            public int Compare(int x, int y)
            {
                var result = markers[x].order.CompareTo(markers[y].order);
                return result != 0 ? result : x.CompareTo(y);
            }
        }
    }
}
