using System;
using Pragma.Dots.Mathematics;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    [Serializable]
    public struct TestActorValue : IComponentData
    {
        public FixedPoint value;
    }
}
