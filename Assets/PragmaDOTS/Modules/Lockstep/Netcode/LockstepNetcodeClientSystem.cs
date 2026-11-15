using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Netcode;
using Unity.Networking.Transport;
using UnityEngine;

namespace Pragma.Dots.Lockstep.Netcode
{
    /// <summary>
    /// Runs a <see cref="LockstepClient"/> in a Netcode client world while a <see cref="LockstepClientConfig"/> exists
    /// and the world is connected. Thin clients join too but do not simulate.
    /// </summary>
    /// <remarks>
    /// The session is registered in <see cref="LockstepWorlds"/>, which is where the presentation systems find it.
    /// Losing the connection ends the session (<see cref="LockstepEndReason.ConnectionLost"/>), which still simulates the
    /// frames it has and stays registered until the config is removed or a new connection joins again. Packets travel as
    /// <see cref="LockstepPacketRpc"/>s, or through a <see cref="LockstepRedundantChannel"/> in <see cref="LockstepDatagramRpc"/>s
    /// with <see cref="LockstepClientConfig.redundantChannel"/>.
    /// </remarks>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkReceiveSystemGroup))]
    [UpdateAfter(typeof(LockstepInputSystemGroup))]
    [UpdateBefore(typeof(LockstepViewSystem))]
    public partial class LockstepNetcodeClientSystem : SystemBase
    {
        private sealed unsafe class Transport : ILockstepTransport
        {
            public EntityManager entityManager;
            public EntityArchetype archetype;
            public Entity connection;
            public bool datagrams;

            public void Send(int connectionId, byte* data, int length)
            {
                if (connection == Entity.Null)
                {
                    return;
                }
                var entity = entityManager.CreateEntity(archetype);
                if (datagrams)
                {
                    entityManager.SetComponentData(entity, LockstepDatagramRpc.Create(data, length));
                }
                else
                {
                    entityManager.SetComponentData(entity, LockstepPacketRpc.Create(data, length));
                }
                entityManager.SetComponentData(entity, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        // How long the channel of a client that left keeps sending its leave until the server acknowledges it.
        private const double LEAVE_SECONDS = 2;

        private LockstepClient _client;
        private LockstepRedundantChannel _channel;
        private Transport _transport;
        private LockstepRedundantChannel _leaving;
        private Entity _leavingConnection;
        private double _leavingUntil;
        private EntityQuery _connectionQuery;
        private EntityQuery _receivedQuery;
        private EntityQuery _receivedDatagramQuery;
        private EntityQuery _localInputQuery;

        /// <summary>The running client, or null.</summary>
        public LockstepClient Client => _client;

        /// <summary>The channel the client talks through, or null when it uses reliable RPCs.</summary>
        public LockstepRedundantChannel Channel => _channel;

        protected override void OnCreate()
        {
            _connectionQuery = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamConnection>());
            _receivedQuery = GetEntityQuery(ComponentType.ReadOnly<LockstepPacketRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            _receivedDatagramQuery = GetEntityQuery(ComponentType.ReadOnly<LockstepDatagramRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            _localInputQuery = GetEntityQuery(ComponentType.ReadWrite<LockstepLocalInput>(), ComponentType.ReadWrite<LockstepCommand>());
            LockstepRpcHiddenTag.Initialize();
        }

        protected override void OnDestroy()
        {
            Shutdown();
            DisposeLeaving();
        }

        protected override unsafe void OnUpdate()
        {
            var now = SystemAPI.Time.ElapsedTime;
            var hasConfig = SystemAPI.TryGetSingleton<LockstepClientConfig>(out var config);
            var connection = _connectionQuery.CalculateEntityCount() == 1 ? _connectionQuery.GetSingletonEntity() : Entity.Null;

            if (_client != null && !hasConfig)
            {
                // Removing the config on a live connection leaves the session: the server frees the slot, and a new
                // config can join again over the same connection.
                if (connection != Entity.Null && connection == _transport.connection)
                {
                    _client.Leave();
                    if (_channel != null)
                    {
                        // A datagram may be lost: the channel lives on until the server acknowledged the leave.
                        _channel.Flush(now);
                        DisposeLeaving();
                        _leaving = _channel;
                        _leavingConnection = connection;
                        _leavingUntil = now + LEAVE_SECONDS;
                        _channel = null;
                    }
                }
                Shutdown();
            }
            if (_client != null && connection != _transport.connection)
            {
                if (_transport.connection != Entity.Null)
                {
                    // Lost: the session ends, and the frames it has are still simulated (the end of a match the server
                    // ended before it dropped the connection).
                    _transport.connection = Entity.Null;
                    _client.OnDisconnected();
                }
                if (connection != Entity.Null)
                {
                    // A new connection joins as a new player.
                    Shutdown();
                }
            }
            if (_client == null && hasConfig && connection != Entity.Null)
            {
                DisposeLeaving();
                Startup(config, connection, now);
            }
            if (_client == null || _transport.connection == Entity.Null)
            {
                UpdateLeaving(connection, now);
                DestroyReceived();
                _client?.Update(now);
                return;
            }

            if (!_receivedQuery.IsEmptyIgnoreFilter)
            {
                var packets = _receivedQuery.ToComponentDataArray<LockstepPacketRpc>(Allocator.Temp);
                EntityManager.DestroyEntity(_receivedQuery);
                var packetPtr = (LockstepPacketRpc*)packets.GetUnsafeReadOnlyPtr();
                for (var i = 0; i < packets.Length; i++)
                {
                    _client.OnPacket(LockstepPacketRpc.GetData(packetPtr + i), packets[i].length, now);
                }
            }
            if (!_receivedDatagramQuery.IsEmptyIgnoreFilter)
            {
                var datagrams = _receivedDatagramQuery.ToComponentDataArray<LockstepDatagramRpc>(Allocator.Temp);
                EntityManager.DestroyEntity(_receivedDatagramQuery);
                if (_channel != null)
                {
                    var datagramPtr = (LockstepDatagramRpc*)datagrams.GetUnsafeReadOnlyPtr();
                    for (var i = 0; i < datagrams.Length; i++)
                    {
                        _channel.Receive(0, LockstepDatagramRpc.GetData(datagramPtr + i), datagrams[i].length, now);
                    }
                    while (_channel.TryRead(out _, out var packet, out var length))
                    {
                        _client.OnPacket(packet, length, now);
                    }
                }
            }

            LockstepClientWorldUtility.PushLocalInput(EntityManager, _localInputQuery, _client);
            _client.Update(now);
            _channel?.Flush(now);
        }

        private unsafe void UpdateLeaving(Entity connection, double now)
        {
            if (_leaving == null)
            {
                return;
            }
            if (connection != _leavingConnection || now > _leavingUntil)
            {
                DisposeLeaving();
                return;
            }
            if (!_receivedDatagramQuery.IsEmptyIgnoreFilter)
            {
                var datagrams = _receivedDatagramQuery.ToComponentDataArray<LockstepDatagramRpc>(Allocator.Temp);
                EntityManager.DestroyEntity(_receivedDatagramQuery);
                var datagramPtr = (LockstepDatagramRpc*)datagrams.GetUnsafeReadOnlyPtr();
                for (var i = 0; i < datagrams.Length; i++)
                {
                    _leaving.Receive(0, LockstepDatagramRpc.GetData(datagramPtr + i), datagrams[i].length, now);
                }
                // The session is gone: what the server still sends is dropped, only its acknowledgements count.
                while (_leaving.TryRead(out _, out _, out _))
                {
                }
            }
            if (_leaving.GetUnacknowledgedCount(0) == 0)
            {
                DisposeLeaving();
                return;
            }
            _leaving.Flush(now);
        }

        private void DisposeLeaving()
        {
            _leaving?.Dispose();
            _leaving = null;
            _leavingConnection = Entity.Null;
        }

        private void DestroyReceived()
        {
            if (!_receivedQuery.IsEmptyIgnoreFilter)
            {
                EntityManager.DestroyEntity(_receivedQuery);
            }
            if (!_receivedDatagramQuery.IsEmptyIgnoreFilter)
            {
                EntityManager.DestroyEntity(_receivedDatagramQuery);
            }
        }

        private void Startup(in LockstepClientConfig config, Entity connection, double now)
        {
            var settings = config.settings;
            if (World.IsThinClient())
            {
                settings.Simulate = false;
            }
            _transport = new Transport
            {
                entityManager = EntityManager,
                connection = connection,
                datagrams = config.redundantChannel,
            };
            ILockstepTransport transport = _transport;
            var maxPacketSize = LockstepPacketRpc.CAPACITY;
            if (config.redundantChannel)
            {
                _transport.archetype = LockstepRpcHiddenTag.CreateRequestArchetype<LockstepDatagramRpc>(EntityManager);
                _channel = new LockstepRedundantChannel(_transport);
                transport = _channel;
                maxPacketSize = _channel.MaxPacketSize;
                WarnIfOutOfBandIsReliable(connection);
            }
            else
            {
                _transport.archetype = LockstepRpcHiddenTag.CreateRequestArchetype<LockstepPacketRpc>(EntityManager);
            }
            if (settings.MaxPacketSize <= 0 || settings.MaxPacketSize > maxPacketSize)
            {
                settings.MaxPacketSize = maxPacketSize;
            }

            var options = LockstepClientWorldUtility.CreateSimulationOptions(World, config.waitForActorRegistry);
            _client = new LockstepClient(settings, transport, options);
            LockstepWorlds.RegisterClient(World, _client);
            _client.Join(config.joinData, config.slot.IsSet ? config.slot.Value : LockstepProtocol.ANY_SLOT);
            _channel?.Flush(now);
        }

        // The channel works over a reliable pipeline as well, only without its point: tell the game it missed a step.
        private void WarnIfOutOfBandIsReliable(Entity connection)
        {
            var streamConnection = EntityManager.GetComponentData<NetworkStreamConnection>(connection);
            ref var driverStore = ref SystemAPI.GetSingletonRW<NetworkStreamDriver>().ValueRW.DriverStore;
            ref var instance = ref driverStore.GetDriverInstanceRW(streamConnection.DriverId);
            NativeArray<byte> shared;
            try
            {
                instance.driver.GetPipelineBuffers(instance.outOfBandPipeline, NetworkPipelineStageId.Get<ReliableSequencedPipelineStage>(),
                    streamConnection.Value, out _, out _, out shared);
            }
            catch (System.InvalidOperationException)
            {
                // A connection the driver does not know any more: nothing to tell.
                return;
            }
            if (shared.IsCreated)
            {
                Debug.LogWarning("[Lockstep] The redundant channel runs over a reliable out-of-band pipeline, which holds packets back after a loss. " +
                                 "Make it unreliable where the drivers are built, on the server and the clients: LockstepNetcode.UseUnreliableOutOfBandPipeline " +
                                 "or LockstepDriverConstructor.");
            }
        }

        private void Shutdown()
        {
            if (_client == null)
            {
                return;
            }
            LockstepWorlds.UnregisterClient(World);
            _client.Dispose();
            _channel?.Dispose();
            _client = null;
            _channel = null;
            _transport = null;
        }
    }
}
