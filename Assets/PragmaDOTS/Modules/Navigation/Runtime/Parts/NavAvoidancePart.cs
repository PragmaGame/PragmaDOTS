using System;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using UnityEngine;

namespace Pragma.Dots.Navigation
{
    /// <summary>
    /// The <see cref="LockstepNavAvoidance"/> of a map: walking agents steer around each other instead of walking into each
    /// other, and paths go around crowds of standing agents. Put it on the actor placed with the navigation grid; leave it
    /// out to turn avoidance off.
    /// </summary>
    [Serializable]
    [ActorPartMenu("Navigation/Avoidance")]
    public sealed class NavAvoidancePart : ActorPart
    {
        [SerializeField, Tooltip("Seconds ahead an agent looks for collisions. Longer turns agents aside earlier and wider.")]
        private FixedPoint _timeHorizon = FixedPoint.FromFraction(3, 2);

        [SerializeField, Tooltip("World units: agents farther than this (centre to centre) are not avoided.")]
        private FixedPoint _neighbourDistance = 5;

        [SerializeField, Range(0, LockstepNavAvoidance.MAX_NEIGHBOURS), Tooltip("How many of the nearest neighbours an agent avoids.")]
        private int _maxNeighbours = 8;

        [SerializeField, Tooltip("What a standing agent costs a path through its cell, in cells of walking: paths go around crowds of standing agents when the way around is shorter. Zero plans through them.")]
        private FixedPoint _crowdCost = 4;

        public override void Build(IActorBuilder builder)
        {
            builder.AddComponent(new LockstepNavAvoidance
            {
                timeHorizon = _timeHorizon,
                neighbourDistance = _neighbourDistance,
                maxNeighbours = Mathf.Clamp(_maxNeighbours, 0, LockstepNavAvoidance.MAX_NEIGHBOURS),
                crowdCost = _crowdCost,
            });
        }
    }
}
