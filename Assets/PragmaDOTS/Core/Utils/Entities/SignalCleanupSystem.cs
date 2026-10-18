using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Utils
{
    /// <summary>
    /// Destroys every signal (<see cref="SignalTag"/>) at the end of <c>SimulationSystemGroup</c>, before the end-of-update
    /// command buffer plays back, so that the signals that buffer publishes live through the next update.
    /// </summary>
    /// <remarks>
    /// Once per update of the group, not per frame: a Netcode server may run the group several times in one frame, and
    /// each run sees its own signals.
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
    [BurstCompile]
    public partial struct SignalCleanupSystem : ISystem
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
