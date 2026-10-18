using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// The actor registry of a world: types by key and index, their prefabs and tags. Every simulation world has one (empty
    /// when the presentation world had none).
    /// </summary>
    /// <remarks>
    /// It reads the registry buffers in place: get it where it is used, and do not keep it across structural changes.
    /// Look keys and tags up once and keep the indices and <see cref="LockstepActorTag"/> handles; the checks after that are
    /// an array read and a bit test. Burst-compatible.
    /// </remarks>
    public readonly struct LockstepActors
    {
        private readonly NativeArray<LockstepActorType> _types;
        private readonly NativeArray<LockstepActorTagName> _tags;

        public LockstepActors(DynamicBuffer<LockstepActorType> types, DynamicBuffer<LockstepActorTagName> tags)
        {
            _types = types.AsNativeArray();
            _tags = tags.AsNativeArray();
        }

        /// <summary>The registry of the world that runs <paramref name="state"/>.</summary>
        public static LockstepActors Get(ref SystemState state)
        {
            var query = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LockstepActorRegistry, LockstepActorType, LockstepActorTagName>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);
            var registry = query.GetSingletonEntity();
            var entityManager = state.EntityManager;
            return new LockstepActors(entityManager.GetBuffer<LockstepActorType>(registry, true), entityManager.GetBuffer<LockstepActorTagName>(registry, true));
        }

        /// <summary>The registry of the world of <paramref name="entityManager"/>, if it has one.</summary>
        public static bool TryGet(EntityManager entityManager, out LockstepActors actors)
        {
            using (var query = entityManager.CreateEntityQuery(new EntityQueryDesc
                   {
                       All = new[] { ComponentType.ReadOnly<LockstepActorRegistry>(), ComponentType.ReadOnly<LockstepActorType>(), ComponentType.ReadOnly<LockstepActorTagName>() },
                       Options = EntityQueryOptions.IncludeDisabledEntities,
                   }))
            {
                if (query.CalculateEntityCount() != 1)
                {
                    actors = default;
                    return false;
                }
                var registry = query.GetSingletonEntity();
                actors = new LockstepActors(entityManager.GetBuffer<LockstepActorType>(registry, true), entityManager.GetBuffer<LockstepActorTagName>(registry, true));
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

        public LockstepActorTagSet GetTags(int type) => _types[type].tags;

        /// <summary>The handle of the tag <paramref name="name"/>; false when no actor type has it.</summary>
        public bool TryGetTag(in FixedString32Bytes name, out LockstepActorTag tag)
        {
            for (var i = 0; i < _tags.Length; i++)
            {
                if (_tags[i].name == name)
                {
                    tag = new LockstepActorTag(i);
                    return true;
                }
            }
            tag = new LockstepActorTag(-1);
            return false;
        }

        public FixedString32Bytes GetTagName(LockstepActorTag tag) => _tags[tag.index].name;

        public bool HasTag(int type, LockstepActorTag tag) => type >= 0 && type < _types.Length && _types[type].tags.Contains(tag);

        /// <summary>True when the type of <paramref name="actor"/> has <paramref name="tag"/>.</summary>
        public bool HasTag(in LockstepActor actor, LockstepActorTag tag) => HasTag(actor.type, tag);

        /// <summary>Spawns an actor of <paramref name="type"/>; place it by setting its <see cref="LockstepTransform"/>.</summary>
        public Entity Instantiate(EntityManager entityManager, int type) => entityManager.Instantiate(_types[type].prefab);

        public Entity Instantiate(EntityCommandBuffer commandBuffer, int type) => commandBuffer.Instantiate(_types[type].prefab);

        public Entity Instantiate(EntityCommandBuffer.ParallelWriter commandBuffer, int sortKey, int type) => commandBuffer.Instantiate(sortKey, _types[type].prefab);
    }
}
