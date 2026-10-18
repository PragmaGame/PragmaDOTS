using Pragma.Dots.Lockstep;
using Pragma.Dots.Utils;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    /// <summary>Counts the signals of the previous tick, then publishes one per player through the end-of-tick buffer.</summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    public partial struct TestTickSignalSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepTime>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<TestTickSignalCounter>())
            {
                state.EntityManager.CreateEntity(typeof(TestTickSignalCounter));
            }
            var tick = SystemAPI.GetSingleton<LockstepTime>().tick;
            var counter = SystemAPI.GetSingleton<TestTickSignalCounter>();
            foreach (var signal in SystemAPI.Query<RefRO<TestTickSignal>>().WithAll<SignalTag>())
            {
                counter.received++;
                if (signal.ValueRO.tick != tick - 1)
                {
                    counter.stale++;
                }
            }

            var commandBuffer = SystemAPI.GetSingleton<LockstepEndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);
            foreach (var player in SystemAPI.Query<RefRO<LockstepPlayer>>())
            {
                commandBuffer.Publish(new TestTickSignal { tick = tick, slot = player.ValueRO.slot });
                counter.sent++;
            }
            SystemAPI.SetSingleton(counter);
        }
    }
}
