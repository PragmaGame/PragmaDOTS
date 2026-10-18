using Unity.Entities;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// Marks an actor placed on a map (a main building, a capture point) rather than spawned from the registry: baked from
    /// an <see cref="ActorAuthoring"/> in a subscene or built with <c>ActorUtility.CreatePlaced</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="order"/> is the place of the actor among the placed actors of its map, the same on every machine: baking
    /// takes it from the identity of the GameObject in its scene. The Lockstep module copies the placed actors of the
    /// client (or local) world into every simulation world before tick 0, in that order, so every client gets the same
    /// entities in the same chunk layout whatever order its subscenes loaded in. The copy keeps this component.
    /// </remarks>
    public struct PlacedActor : IComponentData
    {
        public ulong order;
    }
}
