using Unity.Entities;

namespace Pragma.Dots.Tests
{
    public struct ChecksumPadded : IComponentData
    {
        public byte small;
        // Three padding bytes sit here.
        public int large;
    }
}
