using System;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    [Serializable]
    public struct TestMergedEntry : IBufferElementData
    {
        public int key;
        public int value;
    }
}
