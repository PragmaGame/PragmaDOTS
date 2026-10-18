using System;
using Pragma.Lockstep.Mathematics;
using UnityEngine;

namespace Pragma.Lockstep.Vision
{
    /// <summary>
    /// A <see cref="LockstepVisionSource"/>: the actor lets a player slot see around itself. The slot is set by gameplay
    /// when the actor gets its owner; a radius that research or abilities change is kept current by gameplay too (from a
    /// stat), and this one is where it starts. Needs a <see cref="TransformPart"/>.
    /// </summary>
    [Serializable]
    [ActorPartMenu("Vision/Source")]
    public sealed class VisionSourcePart : ActorPart
    {
        [SerializeField, Tooltip("How far the actor sees, in world units.")]
        private FixedPoint _radius = 20;

        public FixedPoint Radius
        {
            get => _radius;
            set => _radius = value;
        }

        public override void Build(IActorBuilder builder)
        {
            builder.AddComponent(new LockstepVisionSource { radius = _radius, slot = -1 });
        }

        public override void DrawGizmos(in ActorGizmoContext context)
        {
            if (context.IsSelected)
            {
                ActorGizmos.DrawCircle(context.Transform.position, (float)_radius, new Color(0.9f, 0.85f, 0.3f, 0.8f));
            }
        }
    }
}
