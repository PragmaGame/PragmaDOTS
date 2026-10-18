using System.Collections.Generic;
using Pragma.Dots.Mathematics;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Actors.Authoring
{
    // Builds an actor while a subscene bakes, through the baker that owns its entity.
    internal sealed class BakerActorBuilder : IActorBuilder
    {
        private readonly IBaker _baker;
        private readonly Entity _entity;
        private readonly IReadOnlyList<ActorPart> _parts;

        public BakerActorBuilder(IBaker baker, Entity entity, FixedString32Bytes key, bool isPlaced, FixedPose placement,
            FixedVector3 placementScale, IReadOnlyList<ActorPart> parts)
        {
            _baker = baker;
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
            if (ComponentType.ReadWrite<T>().IsZeroSized)
            {
                _baker.AddComponent(_entity, ComponentType.ReadWrite<T>());
            }
            else
            {
                _baker.AddComponent(_entity, component);
            }
        }

        public DynamicBuffer<T> AddBuffer<T>() where T : unmanaged, IBufferElementData => _baker.AddBuffer<T>(_entity);

        public void SetComponentEnabled<T>(bool enabled) where T : unmanaged, IEnableableComponent
        {
            _baker.SetComponentEnabled<T>(_entity, enabled);
        }

        public void DependsOn(UnityEngine.Object asset)
        {
            if (asset != null)
            {
                _baker.DependsOn(asset);
            }
        }
    }
}
