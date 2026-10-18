using Pragma.Dots.Lockstep;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    public struct ChecksumLink : IComponentData
    {
        public Entity target;
        [LockstepChecksumIgnore] public int debugCounter;
    }
}
