using Pragma.Dots.Lockstep.Netcode;
using Pragma.Dots.Utils;
using Unity.Entities;
using Unity.Netcode;

namespace Pragma.Dots.Tests
{
    /// <summary>
    /// Counts the received lockstep requests of a Netcode world right after the begin command buffer made them, before the
    /// transport system destroys them in the same frame, and how many of them would show in the Hierarchy window.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(BeginSimulationEntityCommandBufferSystem))]
    public partial class ReceivedRequestProbeSystem : SystemBase
    {
        private EntityQuery _received;
        private EntityQuery _shown;

        public int Received { get; private set; }

        public int Shown { get; private set; }

        protected override void OnCreate()
        {
            _received = GetEntityQuery(ComponentType.ReadOnly<LockstepPacketRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            EntityHierarchy.TryGetHiddenTag(out var hidden);
            _shown = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<LockstepPacketRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>() },
                None = new[] { hidden },
            });
        }

        protected override void OnUpdate()
        {
            Received += _received.CalculateEntityCount();
            Shown += _shown.CalculateEntityCount();
        }
    }
}
