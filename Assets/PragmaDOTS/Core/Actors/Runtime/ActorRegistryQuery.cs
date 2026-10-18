using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// The actor registry for a system that reads it on its updates: <see cref="Create"/> builds the registry query in
    /// <c>OnCreate</c> and requires it for the update, <see cref="Get"/> reads the registry through it in <c>OnUpdate</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="ActorLookup.Get(ref SystemState)"/> builds the query where it is called: in <c>OnUpdate</c> Entities
    /// looks the query up on every call and warns once per system that it creates a query during OnUpdate.
    /// Burst-compatible.
    /// </remarks>
    public static class ActorRegistryQuery
    {
        /// <summary>The query of the registry of the system's world, required for its update: call it in OnCreate and keep it.</summary>
        public static EntityQuery Create(ref SystemState state)
        {
            var query = Build(ref state);
            state.RequireForUpdate(query);
            return query;
        }

        /// <summary>
        /// The registry through <paramref name="query"/> from <see cref="Create"/>; it reads the buffers in place, so get it
        /// again after a structural change.
        /// </summary>
        public static ActorLookup Get(ref SystemState state, EntityQuery query)
        {
            var registry = query.GetSingletonEntity();
            var entityManager = state.EntityManager;
            return new ActorLookup(entityManager.GetBuffer<ActorType>(registry, true), entityManager.GetBuffer<ActorTagName>(registry, true));
        }

        internal static EntityQuery Build(ref SystemState state)
        {
            return new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ActorRegistry, ActorType, ActorTagName>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(ref state);
        }
    }
}
