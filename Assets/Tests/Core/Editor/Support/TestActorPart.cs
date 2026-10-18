using System;
using Pragma.Dots.Actors;

namespace Pragma.Dots.Tests
{
    /// <summary>A part of the test's own: records the placement the builder hands it.</summary>
    [Serializable]
    public sealed class TestActorPart : ActorPart
    {
        public override void Build(IActorBuilder builder)
        {
            builder.AddComponent(new TestActorPlacement { isPlaced = builder.IsPlaced, pose = builder.Placement, scale = builder.PlacementScale });
        }
    }
}
