using Pragma.Dots.Lockstep;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    [LockstepChecksumIgnore]
    public struct ChecksumDebugOnly : IComponentData
    {
        public int value;
    }
}
