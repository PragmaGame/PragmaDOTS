using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Netcode;

namespace Pragma.Dots.Lockstep.Netcode
{
    /// <summary>Registers <see cref="LockstepDatagramRpc"/> and sends the entities that request it.</summary>
    [UpdateInGroup(typeof(RpcCommandRequestSystemGroup))]
    [CreateAfter(typeof(RpcSystem))]
    [BurstCompile]
    internal partial struct LockstepDatagramRpcRequestSystem : ISystem
    {
        private RpcCommandRequest<LockstepDatagramRpc, LockstepDatagramRpc> _request;

        [BurstCompile]
        private struct SendRpc : IJobChunk
        {
            public RpcCommandRequest<LockstepDatagramRpc, LockstepDatagramRpc>.SendRpcData data;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                data.Execute(chunk, unfilteredChunkIndex);
            }
        }

        public void OnCreate(ref SystemState state)
        {
            _request.OnCreate(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new SendRpc { data = _request.InitJobData(ref state) };
            state.Dependency = job.Schedule(_request.Query, state.Dependency);
        }
    }
}
