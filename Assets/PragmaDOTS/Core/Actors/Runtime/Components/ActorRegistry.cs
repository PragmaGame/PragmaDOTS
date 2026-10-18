using Unity.Entities;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// Marks the entity that holds the actor types (<see cref="ActorType"/>) and the tag table
    /// (<see cref="ActorTagName"/>). Read it through <see cref="ActorLookup"/>.
    /// </summary>
    public struct ActorRegistry : IComponentData
    {
    }
}
