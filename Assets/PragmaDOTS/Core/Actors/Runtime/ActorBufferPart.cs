using System;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Dots.Actors
{
    /// <summary>Adds a dynamic buffer with the elements set in the inspector. The part menu offers it for types with <see cref="ActorComponentAttribute"/>.</summary>
    [Serializable]
    public sealed class ActorBufferPart<T> : ActorPart where T : unmanaged, IBufferElementData
    {
        [SerializeField] private T[] _elements = Array.Empty<T>();

        public ActorBufferPart()
        {
        }

        public ActorBufferPart(params T[] elements)
        {
            _elements = elements ?? Array.Empty<T>();
        }

        public T[] Elements
        {
            get => _elements;
            set => _elements = value ?? Array.Empty<T>();
        }

        public override void Build(IActorBuilder builder)
        {
            var buffer = builder.AddBuffer<T>();
            for (var i = 0; i < _elements.Length; i++)
            {
                buffer.Add(_elements[i]);
            }
        }
    }
}
