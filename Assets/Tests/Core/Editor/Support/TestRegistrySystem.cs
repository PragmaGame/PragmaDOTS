using Pragma.Dots.Actors;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    /// <summary>Spawns a "rifleman" on every update, reading the registry through the query it created in OnCreate.</summary>
    public partial struct TestRegistrySystem : ISystem
    {
        private EntityQuery _registry;

        public void OnCreate(ref SystemState state)
        {
            _registry = ActorRegistryQuery.Create(ref state);
        }

        public void OnUpdate(ref SystemState state)
        {
            var actors = ActorRegistryQuery.Get(ref state, _registry);
            if (actors.TryGetType(new FixedString32Bytes("rifleman"), out var rifleman))
            {
                actors.Instantiate(state.EntityManager, rifleman);
            }
        }
    }
}
