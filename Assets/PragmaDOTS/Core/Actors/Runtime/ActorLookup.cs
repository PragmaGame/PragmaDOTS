using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// The actor registry of a world: types by key and index, their prefabs and tags. With the Lockstep module every
    /// simulation world has one (empty when the presentation world had none).
    /// </summary>
    /// <remarks>
    /// It reads the registry buffers in place: get it where it is used, and do not keep it across structural changes.
    /// Look keys and tags up once and keep the indices and <see cref="ActorTag"/> handles; the checks after that are
    /// an array read and a bit test. Burst-compatible.
    /// </remarks>
    public readonly struct ActorLookup
    {
        private readonly NativeArray<ActorType> _types;
        private readonly NativeArray<ActorTagName> _tags;

        public ActorLookup(DynamicBuffer<ActorType> types, DynamicBuffer<ActorTagName> tags)
        {
            _types = types.AsNativeArray();
            _tags = tags.AsNativeArray();
        }

        /// <summary>
        /// The registry of the world that runs <paramref name="state"/>. It builds the registry query where it is called:
        /// a system that reads the registry on its updates creates the query in OnCreate with
        /// <see cref="ActorRegistryQuery"/> instead.
        /// </summary>
        public static ActorLookup Get(ref SystemState state)
        {
            return ActorRegistryQuery.Get(ref state, ActorRegistryQuery.Build(ref state));
        }

        /// <summary>The registry of the world of <paramref name="entityManager"/>, if it has one.</summary>
        public static bool TryGet(EntityManager entityManager, out ActorLookup actors)
        {
            using (var query = entityManager.CreateEntityQuery(new EntityQueryDesc
                   {
                       All = new[] { ComponentType.ReadOnly<ActorRegistry>(), ComponentType.ReadOnly<ActorType>(), ComponentType.ReadOnly<ActorTagName>() },
                       Options = EntityQueryOptions.IncludeDisabledEntities,
                   }))
            {
                if (query.CalculateEntityCount() != 1)
                {
                    actors = default;
                    return false;
                }
                var registry = query.GetSingletonEntity();
                actors = new ActorLookup(entityManager.GetBuffer<ActorType>(registry, true), entityManager.GetBuffer<ActorTagName>(registry, true));
                return true;
            }
        }

        public int TypeCount => _types.Length;

        public int TagCount => _tags.Length;

        /// <summary>The index of the type with <paramref name="key"/>.</summary>
        public bool TryGetType(in FixedString32Bytes key, out int type)
        {
            for (var i = 0; i < _types.Length; i++)
            {
                if (_types[i].key == key)
                {
                    type = i;
                    return true;
                }
            }
            type = -1;
            return false;
        }

        public FixedString32Bytes GetKey(int type) => _types[type].key;

        /// <summary>The prefab that spawns actors of <paramref name="type"/>; <c>Entity.Null</c> for an empty catalog slot.</summary>
        public Entity GetPrefab(int type) => _types[type].prefab;

        public ActorTagSet GetTags(int type) => _types[type].tags;

        /// <summary>The handle of the tag <paramref name="name"/>; false when no actor type has it.</summary>
        public bool TryGetTag(in FixedString32Bytes name, out ActorTag tag)
        {
            for (var i = 0; i < _tags.Length; i++)
            {
                if (_tags[i].name == name)
                {
                    tag = new ActorTag(i);
                    return true;
                }
            }
            tag = new ActorTag(-1);
            return false;
        }

        public FixedString32Bytes GetTagName(ActorTag tag) => _tags[tag.index].name;

        public bool HasTag(int type, ActorTag tag) => type >= 0 && type < _types.Length && _types[type].tags.Contains(tag);

        /// <summary>True when the type of <paramref name="actor"/> has <paramref name="tag"/>.</summary>
        public bool HasTag(in Actor actor, ActorTag tag) => HasTag(actor.type, tag);

        /// <summary>Spawns an actor of <paramref name="type"/>; place it by setting its transform.</summary>
        public Entity Instantiate(EntityManager entityManager, int type) => entityManager.Instantiate(_types[type].prefab);

        public Entity Instantiate(EntityCommandBuffer commandBuffer, int type) => commandBuffer.Instantiate(_types[type].prefab);

        public Entity Instantiate(EntityCommandBuffer.ParallelWriter commandBuffer, int sortKey, int type) => commandBuffer.Instantiate(sortKey, _types[type].prefab);
    }
}
