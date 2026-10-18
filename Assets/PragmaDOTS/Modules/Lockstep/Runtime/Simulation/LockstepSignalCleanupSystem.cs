using Pragma.Dots.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// Destroys every signal (<see cref="SignalTag"/>) at the end of the tick, before the end-of-tick command buffer plays
    /// back: the simulation world has no <c>SimulationSystemGroup</c>, whose <see cref="SignalCleanupSystem"/> does this
    /// elsewhere. Signals published through the end-of-tick buffer live through the next tick, and are hashed with the
    /// state in between.
    /// </summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(LockstepEndSimulationEntityCommandBufferSystem))]
    [BurstCompile]
    public partial struct LockstepSignalCleanupSystem : ISystem
    {
        private EntityQuery _signals;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _signals = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<SignalTag>()
                .Build(ref state);
            state.RequireForUpdate(_signals);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.DestroyEntity(_signals);
        }
    }
}
