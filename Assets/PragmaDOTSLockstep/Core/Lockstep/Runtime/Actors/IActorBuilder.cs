using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// What an <see cref="ActorPart"/> builds into: the root entity of an actor, while a subscene bakes or while actors are
    /// built at run time.
    /// </summary>
    /// <remarks>
    /// A buffer returned by <see cref="AddBuffer{T}"/> is valid until the next component is added: fill it right away.
    /// Each component type may be added once per actor.
    /// </remarks>
    public interface IActorBuilder
    {
        /// <summary>The key of the actor's definition.</summary>
        FixedString32Bytes Key { get; }

        /// <summary>True for an actor placed on the map (a scene entity), false for the prefab of an actor type.</summary>
        bool IsPlaced { get; }

        /// <summary>Where a placed actor stands, with its uniform scale; <see cref="LockstepTransform.Identity"/> for a prefab.</summary>
        LockstepTransform Placement { get; }

        /// <summary>The scale of a placed actor along its local axes, for parts that take a non-uniform one; one for a prefab.</summary>
        FixedVector3 PlacementScale { get; }

        /// <summary>True when the actor has a part of type <typeparamref name="TPart"/>.</summary>
        bool HasPart<TPart>() where TPart : ActorPart;

        void AddComponent<T>(in T component) where T : unmanaged, IComponentData;

        DynamicBuffer<T> AddBuffer<T>() where T : unmanaged, IBufferElementData;

        void SetComponentEnabled<T>(bool enabled) where T : unmanaged, IEnableableComponent;

        /// <summary>Bakes the actor again when <paramref name="asset"/> changes; nothing at run time.</summary>
        void DependsOn(UnityEngine.Object asset);
    }
}
