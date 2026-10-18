using System;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using UnityEngine;

namespace Pragma.Dots.Navigation
{
    /// <summary>
    /// A <see cref="LockstepNavAgent"/> with its empty path and its <see cref="LockstepNavVelocity"/>: the actor walks to
    /// destinations around the obstacles of the navigation grid and, with a radius, avoids other agents while the world has
    /// a <see cref="LockstepNavAvoidance"/>. Needs a <see cref="TransformPart"/>.
    /// </summary>
    [Serializable]
    [ActorPartMenu("Navigation/Agent")]
    public sealed class NavAgentPart : ActorPart
    {
        [SerializeField, Tooltip("World units per second.")]
        private FixedPoint _speed = 4;

        [SerializeField, Tooltip("Degrees per second the agent turns to face where it walks; 0 turns at once.")]
        private FixedPoint _angularSpeed;

        [SerializeField, Tooltip("Degrees, 0 to 180: an agent facing further than this from where its path leads stops and turns on the spot before it walks on, as a tracked vehicle does. 0 walks while it turns; needs an angular speed.")]
        private FixedPoint _turnInPlaceAngle;

        [SerializeField, Tooltip("The agent stops this close to the end of its path.")]
        private FixedPoint _stoppingDistance;

        [SerializeField, Tooltip("Radius of the agent's body: overlapping agents are pushed apart and walking ones avoid each other; a body larger than the grid's agent radius keeps to cells with room for it. 0 neither pushes nor is pushed nor avoids.")]
        private FixedPoint _radius;

        public FixedPoint Speed
        {
            get => _speed;
            set => _speed = value;
        }

        public FixedPoint AngularSpeed
        {
            get => _angularSpeed;
            set => _angularSpeed = value;
        }

        public FixedPoint TurnInPlaceAngle
        {
            get => _turnInPlaceAngle;
            set => _turnInPlaceAngle = value;
        }

        public FixedPoint StoppingDistance
        {
            get => _stoppingDistance;
            set => _stoppingDistance = value;
        }

        public FixedPoint Radius
        {
            get => _radius;
            set => _radius = value;
        }

        public override void Build(IActorBuilder builder)
        {
            builder.AddComponent(new LockstepNavAgent
            {
                speed = _speed,
                angularSpeed = FixedMath.ToRadians(_angularSpeed),
                turnInPlaceAngle = FixedMath.ToRadians(FixedMath.Clamp(_turnInPlaceAngle, FixedPoint.Zero, (FixedPoint)180)),
                stoppingDistance = _stoppingDistance,
                radius = _radius,
            });
            builder.AddBuffer<LockstepNavWaypoint>();
            builder.AddComponent(new LockstepNavVelocity());
        }

        public override void DrawGizmos(in ActorGizmoContext context)
        {
            if (context.IsSelected && _radius > FixedPoint.Zero)
            {
                ActorGizmos.DrawCircle(context.Transform.position, (float)_radius, new Color(0.3f, 0.8f, 1f, 0.8f));
            }
        }
    }
}
