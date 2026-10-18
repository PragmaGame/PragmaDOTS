using System;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using UnityEngine;

namespace Pragma.Dots.Navigation
{
    /// <summary>
    /// A <see cref="LockstepNavObstacle"/>: a box whose local X and Z block the navigation grid while the actor exists.
    /// </summary>
    /// <remarks>
    /// With a <see cref="TransformPart"/> the box moves with the actor (a building spawned from the catalog). Without one
    /// the obstacle is static map content: the pose of the placement is kept, non-uniform scale included.
    /// </remarks>
    [Serializable]
    [ActorPartMenu("Navigation/Obstacle")]
    public sealed class NavObstaclePart : ActorPart
    {
        [SerializeField, Tooltip("Centre of the box on the actor's local X and Z.")]
        private FixedVector2 _center;

        [SerializeField, Tooltip("Size of the box along the actor's local X and Z.")]
        private FixedVector2 _size = new FixedVector2(1, 1);

        public FixedVector2 Center
        {
            get => _center;
            set => _center = value;
        }

        public FixedVector2 Size
        {
            get => _size;
            set => _size = value;
        }

        public override void Build(IActorBuilder builder)
        {
            var isMoving = builder.HasPart<TransformPart>();
            GetSimulationData(builder.Placement, builder.PlacementScale, isMoving, out var obstacle, out var transform);
            builder.AddComponent(obstacle);
            if (!isMoving)
            {
                builder.AddComponent(transform);
            }
        }

        /// <summary>The rectangle the simulation will stamp for an actor placed at <paramref name="source"/>, for previews.</summary>
        public LockstepNavObstacleFootprint GetFootprint(Transform source, bool isMoving)
        {
            GetSimulationData(ActorAuthoring.GetPlacement(source), ActorAuthoring.GetPlacementScale(source), isMoving, out var obstacle, out var transform);
            return LockstepNavObstacleFootprint.Create(obstacle, transform);
        }

        public override void DrawGizmos(in ActorGizmoContext context)
        {
            if (!context.IsSelected)
            {
                return;
            }
            var footprint = GetFootprint(context.Transform, context.HasPart<TransformPart>());
            var y = context.Transform.position.y;
            var center = new Vector3((float)footprint.center.x, y, (float)footprint.center.y);
            var right = new Vector3((float)footprint.right.x, 0f, (float)footprint.right.y) * (float)footprint.halfSize.x;
            var forward = new Vector3((float)footprint.Forward.x, 0f, (float)footprint.Forward.y) * (float)footprint.halfSize.y;
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 1f);
            Gizmos.DrawLine(center - right - forward, center + right - forward);
            Gizmos.DrawLine(center + right - forward, center + right + forward);
            Gizmos.DrawLine(center + right + forward, center - right + forward);
            Gizmos.DrawLine(center - right + forward, center - right - forward);
        }

        private void GetSimulationData(in LockstepTransform placement, FixedVector3 scale, bool isMoving, out LockstepNavObstacle obstacle,
            out LockstepTransform transform)
        {
            if (isMoving)
            {
                // The box stays local and scales with the actor's transform.
                transform = placement;
                obstacle = new LockstepNavObstacle { center = _center, size = _size };
                return;
            }

            transform = LockstepTransform.FromPositionRotation(placement.position, placement.rotation);
            obstacle = new LockstepNavObstacle
            {
                center = new FixedVector2(_center.x * scale.x, _center.y * scale.z),
                size = new FixedVector2(FixedMath.Abs(_size.x * scale.x), FixedMath.Abs(_size.y * scale.z)),
            };
        }
    }
}
