using System;
using AOT;
using Pragma.Dots.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Netcode;

namespace Pragma.Dots.Lockstep.Netcode
{
    /// <summary>
    /// The single RPC that carries every lockstep packet over Netcode for Entities' reliable, ordered channel.
    /// </summary>
    /// <remarks>
    /// Serialization is written by hand so that only <see cref="length"/> bytes go on the wire. The protocol never
    /// produces packets above <see cref="CAPACITY"/>: larger messages are fragmented by <see cref="LockstepPacketFramer"/>.
    /// </remarks>
    [BurstCompile]
    public unsafe struct LockstepPacketRpc : IComponentData, IRpcCommandSerializer<LockstepPacketRpc>
    {
        public const int CAPACITY = LockstepProtocol.DEFAULT_MAX_PACKET_SIZE;

        // The hidden tag for InvokeExecute, a Burst function pointer, where it cannot be looked up by name; Null outside the
        // editor or before a transport system handed it over (HideReceivedRequests). A class of its own: in the static
        // constructor of this struct, next to InvokeExecutePointer, Burst would find a circular initialization.
        private static class HiddenTag
        {
            public static readonly SharedStatic<TypeIndex> Index = SharedStatic<TypeIndex>.GetOrCreate<HiddenTagKey>();
        }

        private struct HiddenTagKey
        {
        }

        public ushort length;
        private fixed byte _data[CAPACITY];

        public static LockstepPacketRpc Create(byte* data, int length)
        {
            if (length < 0 || length > CAPACITY)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, $"A lockstep packet is limited to {CAPACITY} bytes.");
            }
            var rpc = new LockstepPacketRpc { length = (ushort)length };
            UnsafeUtility.MemCpy(rpc._data, data, length);
            return rpc;
        }

        /// <summary>Pointer to the payload of an RPC that lives at a stable address (a native array element).</summary>
        public static byte* GetData(LockstepPacketRpc* rpc) => rpc->_data;

        /// <summary>
        /// The archetype of a send request. Netcode sends a request in the frame it is made but destroys it through the
        /// next frame's begin command buffer, so every packet leaves an entity behind for a frame; in the editor the
        /// requests are hidden from the Hierarchy window, which would otherwise flicker with them.
        /// </summary>
        internal static EntityArchetype CreateRequestArchetype(EntityManager entityManager)
        {
            var packet = ComponentType.ReadWrite<LockstepPacketRpc>();
            var request = ComponentType.ReadWrite<SendRpcCommandRequest>();
            if (EntityHierarchy.TryGetHiddenTag(out var hidden))
            {
                return entityManager.CreateArchetype(packet, request, hidden);
            }
            return entityManager.CreateArchetype(packet, request);
        }

        /// <summary>
        /// Hides received requests from the Hierarchy window too, in the editor. Netcode makes a received request through the
        /// begin command buffer of the next frame and the transport system destroys it later in that frame, but the window
        /// takes its snapshot inside the world update (after the transform group), which may fall between the two: a
        /// request for every packet would flicker in it. The transport systems call this when they are created, so the tag
        /// is there before the first packet arrives.
        /// </summary>
        internal static void HideReceivedRequests()
        {
            HiddenTag.Index.Data = EntityHierarchy.TryGetHiddenTag(out var hidden) ? hidden.TypeIndex : TypeIndex.Null;
        }

        public void Serialize(ref DataStreamWriter writer, in RpcSerializerState state, in LockstepPacketRpc data)
        {
            var copy = data;
            writer.WriteUShort(copy.length);
            writer.WriteBytesUnsafe(copy._data, copy.length);
        }

        public void Deserialize(ref DataStreamReader reader, in RpcDeserializerState state, ref LockstepPacketRpc data)
        {
            var length = reader.ReadUShort();
            if (length > CAPACITY)
            {
                // Corrupted stream: deliver an empty packet, the protocol ignores it.
                data.length = 0;
                return;
            }
            fixed (byte* destination = data._data)
            {
                reader.ReadBytesUnsafe(destination, length);
            }
            data.length = length;
        }

        [BurstCompile(DisableDirectCall = true)]
        [MonoPInvokeCallback(typeof(RpcExecutor.ExecuteDelegate))]
        private static void InvokeExecute(ref RpcExecutor.Parameters parameters)
        {
            var request = RpcExecutor.ExecuteCreateRequestComponent<LockstepPacketRpc, LockstepPacketRpc>(ref parameters);
            var hidden = HiddenTag.Index.Data;

            if (hidden != TypeIndex.Null)
            {
                parameters.CommandBuffer.AddComponent(parameters.JobIndex, request, ComponentType.FromTypeIndex(hidden));
            }
        }

        private static readonly PortableFunctionPointer<RpcExecutor.ExecuteDelegate> InvokeExecutePointer =
            new PortableFunctionPointer<RpcExecutor.ExecuteDelegate>(InvokeExecute);

        public PortableFunctionPointer<RpcExecutor.ExecuteDelegate> CompileExecute() => InvokeExecutePointer;
    }
}
