using System.Collections.Generic;
using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep
{
    // Builds an actor at run time, through the EntityManager of the world it goes into.
    internal sealed class EntityManagerActorBuilder : IActorBuilder
    {
        private readonly EntityManager _entityManager;
        private readonly Entity _entity;
        private readonly IReadOnlyList<ActorPart> _parts;

        public EntityManagerActorBuilder(EntityManager entityManager, Entity entity, FixedString32Bytes key, bool isPlaced,
            LockstepTransform placement, FixedVector3 placementScale, IReadOnlyList<ActorPart> parts)
        {
            _entityManager = entityManager;
            _entity = entity;
            _parts = parts;
            Key = key;
            IsPlaced = isPlaced;
            Placement = placement;
            PlacementScale = placementScale;
        }

        public FixedString32Bytes Key { get; }

        public bool IsPlaced { get; }

        public LockstepTransform Placement { get; }

        public FixedVector3 PlacementScale { get; }

        public bool HasPart<TPart>() where TPart : ActorPart => ActorParts.TryGet<TPart>(_parts, out _);

        public void AddComponent<T>(in T component) where T : unmanaged, IComponentData
        {
            if (ComponentType.ReadWrite<T>().IsZeroSized)
            {
                _entityManager.AddComponent<T>(_entity);
            }
            else
            {
                _entityManager.AddComponentData(_entity, component);
            }
        }

        public DynamicBuffer<T> AddBuffer<T>() where T : unmanaged, IBufferElementData => _entityManager.AddBuffer<T>(_entity);

        public void SetComponentEnabled<T>(bool enabled) where T : unmanaged, IEnableableComponent
        {
            _entityManager.SetComponentEnabled<T>(_entity, enabled);
        }

        public void DependsOn(UnityEngine.Object asset)
        {
        }
    }
}
