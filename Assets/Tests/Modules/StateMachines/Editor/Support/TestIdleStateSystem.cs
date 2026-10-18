using Unity.Burst;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    [UpdateInGroup(typeof(TestMachineGroup))]
    [BurstCompile]
    public partial struct TestIdleStateSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new Job().ScheduleParallel();
        }

        [BurstCompile(CompileSynchronously = true)]
        private partial struct Job : IJobEntity
        {
            private void Execute(ref TestMachine machine, ref TestMachineLog log)
            {
                TestMachineStates.Idle(ref machine.machine, ref log);
            }
        }
    }
}
