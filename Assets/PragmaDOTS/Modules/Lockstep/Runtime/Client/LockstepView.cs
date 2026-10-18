using Unity.Entities;

namespace Pragma.Dots.Lockstep
{
    /// <summary>Put on view entities spawned by <see cref="LockstepViewSystem"/>; points at the simulated entity.</summary>
    public struct LockstepView : IComponentData
    {
        public Entity simulationEntity;
    }
}
