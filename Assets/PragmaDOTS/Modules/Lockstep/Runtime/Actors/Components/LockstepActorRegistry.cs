using Unity.Entities;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// Marks the entity that holds the actor types (<see cref="LockstepActorType"/>) and the tag table
    /// (<see cref="LockstepActorTagName"/>). Read it through <see cref="LockstepActors"/>.
    /// </summary>
    public struct LockstepActorRegistry : IComponentData
    {
    }
}
