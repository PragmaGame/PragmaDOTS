using Unity.Collections;
using Unity.Entities;

namespace Pragma.Lockstep
{
    /// <summary>
    /// The root entity of an actor: a game object of the simulation built from an <see cref="ActorDefinition"/>, such as a
    /// unit, a building or a capture point. Its type gives its key and tags (<see cref="LockstepActors"/>).
    /// </summary>
    public struct LockstepActor : IComponentData
    {
        /// <summary>The key of the actor's definition.</summary>
        public FixedString32Bytes key;

        /// <summary>Index of the type in the actor registry; -1 when the registry has no type with <see cref="key"/>.</summary>
        public int type;
    }
}
