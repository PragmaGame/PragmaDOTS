using Unity.Entities;

namespace Pragma.Dots.Tests
{
    [InternalBufferCapacity(2)]
    public struct ChecksumElement : IBufferElementData
    {
        public int value;
    }
}
