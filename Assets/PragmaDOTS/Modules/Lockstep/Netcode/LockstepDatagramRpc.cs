using System;
using AOT;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Netcode;

namespace Pragma.Dots.Lockstep.Netcode
{
    /// <summary>
    /// The RPC that carries the datagrams of a <see cref="LockstepRedundantChannel"/>: an out-of-band RPC, sent on the
    /// pipeline <see cref="LockstepNetcode.UseUnreliableOutOfBandPipeline"/> makes unreliable.
    /// </summary>
    /// <remarks>
    /// Netcode sends every RPC reliably, and its out-of-band pipeline is a second reliable one that Netcode itself does not
    /// use. Made unreliable, it carries these RPCs without holding the next ones back when one is lost: the channel repeats
    /// what was not acknowledged in the next datagrams instead.
    /// </remarks>
    [BurstCompile]
    public unsafe struct LockstepDatagramRpc : IOutOfBandRpcCommand, IRpcCommandSerializer<LockstepDatagramRpc>
    {
        public const int CAPACITY = LockstepProtocol.DEFAULT_MAX_PACKET_SIZE;

        public ushort length;
        private fixed byte _data[CAPACITY];

        public static LockstepDatagramRpc Create(byte* data, int length)
        {
            if (length < 0 || length > CAPACITY)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, $"A lockstep datagram is limited to {CAPACITY} bytes.");
            }
            var rpc = new LockstepDatagramRpc { length = (ushort)length };
            UnsafeUtility.MemCpy(rpc._data, data, length);
            return rpc;
        }

        /// <summary>Pointer to the payload of an RPC that lives at a stable address (a native array element).</summary>
        public static byte* GetData(LockstepDatagramRpc* rpc) => rpc->_data;

        public void Serialize(ref DataStreamWriter writer, in RpcSerializerState state, in LockstepDatagramRpc data)
        {
            var copy = data;
            writer.WriteUShort(copy.length);
            writer.WriteBytesUnsafe(copy._data, copy.length);
        }

        public void Deserialize(ref DataStreamReader reader, in RpcDeserializerState state, ref LockstepDatagramRpc data)
        {
            var length = reader.ReadUShort();
            if (length > CAPACITY)
            {
                // Not a datagram of the channel: an empty one is ignored.
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
            var request = RpcExecutor.ExecuteCreateRequestComponent<LockstepDatagramRpc, LockstepDatagramRpc>(ref parameters);
            LockstepRpcHiddenTag.HideReceived(ref parameters, request);
        }

        private static readonly PortableFunctionPointer<RpcExecutor.ExecuteDelegate> InvokeExecutePointer =
            new PortableFunctionPointer<RpcExecutor.ExecuteDelegate>(InvokeExecute);

        public PortableFunctionPointer<RpcExecutor.ExecuteDelegate> CompileExecute() => InvokeExecutePointer;
    }
}
