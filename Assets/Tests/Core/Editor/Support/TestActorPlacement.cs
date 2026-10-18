using Pragma.Dots.Mathematics;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    /// <summary>What <see cref="TestActorPart"/> got from the builder.</summary>
    public struct TestActorPlacement : IComponentData
    {
        public bool isPlaced;
        public FixedPose pose;
        public FixedVector3 scale;
    }
}
