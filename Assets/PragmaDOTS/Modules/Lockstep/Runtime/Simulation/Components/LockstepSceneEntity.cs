using Unity.Entities;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// Marks an entity of the client (or local) world that every simulation world starts with: an actor placed on the map
    /// (a main building, a capture point), baked from an <see cref="ActorAuthoring"/> in a subscene or built with
    /// <c>LockstepActorUtility.CreatePlaced</c>.
    /// The built-in systems copy these entities into the simulation before tick 0, together with the actor registry.
    /// </summary>
    /// <remarks>
    /// The copies are made in <see cref="order"/> order, so every client gets the same entities in the same chunk layout,
    /// whatever order its subscenes loaded in. Baking takes the order from the identity of the GameObject in its scene.
    /// The copy keeps this component.
    /// </remarks>
    public struct LockstepSceneEntity : IComponentData
    {
        public ulong order;
    }
}
