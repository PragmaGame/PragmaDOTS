using Unity.Entities;

namespace Pragma.Dots.Lockstep
{
    // Put on the entities copied into a simulation for a moment, to move them into chunks in a known order.
    internal struct LockstepCopyOrder : IComponentData
    {
    }
}
