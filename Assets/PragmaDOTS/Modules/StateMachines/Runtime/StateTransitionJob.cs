using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Dots.StateMachines
{
    /// <summary>Applies the requested transitions of the machines of one component, entity by entity.</summary>
    [BurstCompile]
    internal struct StateTransitionJob : IJobChunk
    {
        public DynamicComponentTypeHandle machines;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            // A machine component is a StateMachine and nothing else (StateMachineLayout).
            var states = chunk.GetDynamicComponentDataArrayReinterpret<StateMachine>(ref machines, UnsafeUtility.SizeOf<StateMachine>());
            var entities = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (entities.NextEntityIndex(out var i))
            {
                var machine = states[i];
                machine.Advance();
                states[i] = machine;
            }
        }
    }
}
