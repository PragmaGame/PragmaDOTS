using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>The tag table of the actor registry: the index of an element is the bit of the tag in a <see cref="LockstepActorTagSet"/>.</summary>
    [InternalBufferCapacity(0)]
    public struct LockstepActorTagName : IBufferElementData
    {
        public FixedString32Bytes name;
    }
}
