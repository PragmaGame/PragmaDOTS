using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Actors
{
    /// <summary>The tag table of the actor registry: the index of an element is the bit of the tag in a <see cref="ActorTagSet"/>.</summary>
    [InternalBufferCapacity(0)]
    public struct ActorTagName : IBufferElementData
    {
        public FixedString32Bytes name;
    }
}
