using Pragma.Dots.Actors;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    /// <summary>
    /// Spawns a "rifleman" on tick 5 and a "tank" on tick 6, then every tick moves the actors tagged "player" along X and
    /// those tagged "vehicle" along Z, so the state depends on the registry of every client.
    /// </summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    public partial struct TestActorSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var tick = SystemAPI.GetSingleton<LockstepTime>().tick;
            var actors = ActorLookup.Get(ref state);
            if (tick == 5 || tick == 6)
            {
                var key = new FixedString32Bytes(tick == 5 ? "rifleman" : "tank");
                if (actors.TryGetType(key, out var type))
                {
                    var spawned = actors.Instantiate(state.EntityManager, type);
                    state.EntityManager.SetComponentData(spawned, LockstepTransform.FromPosition(new FixedVector3(tick, 0, 0)));
                    actors = ActorLookup.Get(ref state);
                }
            }

            if (!actors.TryGetTag("player", out var player) || !actors.TryGetTag("vehicle", out var vehicle))
            {
                return;
            }
            foreach (var (actor, transform) in SystemAPI.Query<RefRO<Actor>, RefRW<LockstepTransform>>())
            {
                if (actors.HasTag(actor.ValueRO, player))
                {
                    transform.ValueRW.position.x += FixedPoint.One;
                }
                if (actors.HasTag(actor.ValueRO, vehicle))
                {
                    transform.ValueRW.position.z += FixedPoint.One;
                }
            }
        }
    }
}
