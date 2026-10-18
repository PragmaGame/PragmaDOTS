using System;
using Pragma.Dots.Actors;
using UnityEngine;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// A <see cref="LockstepTransform"/>: where a placed actor stands on the map, or identity on a prefab (spawning code sets
    /// it). The GameObject of a placed actor is converted to fixed point once, so every client starts from the same values.
    /// New definitions get it instead of Unity's <see cref="LocalTransformPart"/>.
    /// </summary>
    [Serializable]
    [ActorPartMenu("Transform")]
    [DefaultActorPart(Replaces = typeof(LocalTransformPart))]
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
            var transform = LockstepTransform.FromPose(builder.Placement, builder.PlacementScale.x);
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
