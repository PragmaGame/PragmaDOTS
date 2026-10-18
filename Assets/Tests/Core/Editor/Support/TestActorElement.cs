using System;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    [Serializable]
    public struct TestActorElement : IBufferElementData
    {
        public int value;
    }
}
