using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// An actor type of the registry: the definition at the same index of the <see cref="ActorCatalog"/>. The registry of
    /// the presentation world is copied into every lockstep simulation world, so an index means the same type on every
    /// client.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct ActorType : IBufferElementData
    {
        public FixedString32Bytes key;

        /// <summary>The prefab entity that spawns actors of the type; <c>Entity.Null</c> for an empty catalog slot.</summary>
        public Entity prefab;

        public ActorTagSet tags;
    }
}
