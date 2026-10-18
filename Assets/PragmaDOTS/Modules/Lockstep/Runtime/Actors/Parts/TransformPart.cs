using System;
using UnityEngine;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// A <see cref="LockstepTransform"/>: where a placed actor stands on the map, or identity on a prefab (spawning code sets
    /// it). The GameObject of a placed actor is converted to fixed point once, so every client starts from the same values.
    /// </summary>
    [Serializable]
    [ActorPartMenu("Transform")]
    public sealed class TransformPart : ActorPart
    {
        [SerializeField, Tooltip("Adds LockstepTransformPrevious, so views of the actor are interpolated between ticks.")]
        private bool _interpolate = true;

        public bool Interpolate
        {
            get => _interpolate;
            set => _interpolate = value;
        }

        public override void Build(IActorBuilder builder)
        {
            var transform = builder.Placement;
            builder.AddComponent(transform);
            if (_interpolate)
            {
                builder.AddComponent(new LockstepTransformPrevious
                {
                    position = transform.position,
                    rotation = transform.rotation,
                    scale = transform.scale,
                });
            }
        }
    }
}
