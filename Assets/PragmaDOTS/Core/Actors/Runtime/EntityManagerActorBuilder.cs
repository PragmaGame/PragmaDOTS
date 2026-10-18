using System;
using System.Collections.Generic;
using Pragma.Dots.Mathematics;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Actors
{
    // Builds an actor at run time, through the EntityManager of the world it goes into. A type the entity has already is
    // refused, as a baker refuses it, so that an actor built either way comes out the same.
    internal sealed class EntityManagerActorBuilder : IActorBuilder
    {
        private readonly EntityManager _entityManager;
        private readonly Entity _entity;
        private readonly IReadOnlyList<ActorPart> _parts;

        public EntityManagerActorBuilder(EntityManager entityManager, Entity entity, FixedString32Bytes key, bool isPlaced,
            FixedPose placement, FixedVector3 placementScale, IReadOnlyList<ActorPart> parts)
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

        public FixedPose Placement { get; }

        public FixedVector3 PlacementScale { get; }

        public bool HasPart<TPart>() where TPart : ActorPart => ActorParts.TryGet<TPart>(_parts, out _);

        public void AddComponent<T>(in T component) where T : unmanaged, IComponentData
        {
            CheckMissing<T>();
            if (ComponentType.ReadWrite<T>().IsZeroSized)
            {
                _entityManager.AddComponent<T>(_entity);
            }
            else
            {
                _entityManager.AddComponentData(_entity, component);
            }
        }

        public DynamicBuffer<T> AddBuffer<T>() where T : unmanaged, IBufferElementData
        {
            CheckMissing<T>();
            return _entityManager.AddBuffer<T>(_entity);
        }

        public void SetComponentEnabled<T>(bool enabled) where T : unmanaged, IEnableableComponent
        {
            _entityManager.SetComponentEnabled<T>(_entity, enabled);
        }

        public void DependsOn(UnityEngine.Object asset)
        {
        }

        private void CheckMissing<T>()
        {
            if (_entityManager.HasComponent(_entity, ComponentType.ReadWrite<T>()))
            {
                throw new InvalidOperationException($"The actor '{Key}' has {typeof(T).Name} already: each component type may be added once per actor.");
            }
        }
    }
}
