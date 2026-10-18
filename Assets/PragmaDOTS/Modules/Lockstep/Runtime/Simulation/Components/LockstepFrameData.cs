using Unity.Entities;

namespace Pragma.Dots.Lockstep
{
    /// <summary>The raw confirmed frame of the current tick, a singleton buffer in the simulation world.</summary>
    [InternalBufferCapacity(0)]
    public struct LockstepFrameData : IBufferElementData
    {
        public byte value;
    }
}
