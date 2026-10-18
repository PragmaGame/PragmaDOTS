using System;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Dots.Actors
{
    /// <summary>Adds one component with the value set in the inspector. The part menu offers it for types with <see cref="ActorComponentAttribute"/>.</summary>
    [Serializable]
    public sealed class ActorComponentPart<T> : ActorPart where T : unmanaged, IComponentData
    {
        [SerializeField] private T _value;

        public ActorComponentPart()
        {
        }

        public ActorComponentPart(T value)
        {
            _value = value;
        }

        public T Value
        {
            get => _value;
            set => _value = value;
        }

        public override void Build(IActorBuilder builder)
        {
            builder.AddComponent(_value);
        }
    }
}
