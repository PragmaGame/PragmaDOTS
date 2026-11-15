using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Netcode;

namespace Pragma.Dots.Lockstep.Netcode
{
    /// <summary>
    /// Runs a <see cref="LockstepServer"/> in a Netcode server world while a <see cref="LockstepServerConfig"/> exists.
    /// </summary>
    /// <remarks>
    /// Every connection entity gets its own lockstep connection id. Packets travel as <see cref="LockstepPacketRpc"/>, or,
    /// for a client that talks through the redundant channel (<see cref="LockstepClientConfig.redundantChannel"/>), as
    /// datagrams of a <see cref="LockstepRedundantChannel"/> in <see cref="LockstepDatagramRpc"/>s: each client is answered
    /// the way it talks. The system runs in the server simulation group, so ticks are closed on the server's fixed tick
    /// rate; keep the lockstep tick rate at or below <c>ClientServerTickRate.SimulationTickRate</c>. A connection that
    /// breaks the protocol or times out (<see cref="LockstepServerSettings.PlayerTimeoutSeconds"/>) is disconnected.
    /// </remarks>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkReceiveSystemGroup))]
    public partial class LockstepNetcodeServerSystem : SystemBase
    {
        // Packets of the server: through the channel to the clients that talk through it, as reliable RPCs otherwise.
        private sealed unsafe class Transport : ILockstepTransport
        {
            public EntityManager entityManager;
            public EntityArchetype archetype;
            public NativeHashMap<int, Entity> connections;
            public NativeHashSet<int> redundantConnections;
            public LockstepRedundantChannel channel;

            public void Send(int connectionId, byte* data, int length)
            {
                if (redundantConnections.Contains(connectionId))
                {
                    channel.Send(connectionId, data, length);
                    return;
                }
                if (!connections.TryGetValue(connectionId, out var connection))
                {
                    return;
                }
                var entity = entityManager.CreateEntity(archetype);
                entityManager.SetComponentData(entity, LockstepPacketRpc.Create(data, length));
                entityManager.SetComponentData(entity, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        // Datagrams of the channel, as out-of-band RPCs.
        private sealed unsafe class DatagramTransport : ILockstepTransport
        {
            public EntityManager entityManager;
            public EntityArchetype archetype;
            public NativeHashMap<int, Entity> connections;

            public void Send(int connectionId, byte* data, int length)
            {
                if (!connections.TryGetValue(connectionId, out var connection))
                {
                    return;
                }
                var entity = entityManager.CreateEntity(archetype);
                entityManager.SetComponentData(entity, LockstepDatagramRpc.Create(data, length));
                entityManager.SetComponentData(entity, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        private LockstepServer _server;
        private LockstepRedundantChannel _channel;
        private Transport _transport;
        private Entity _handle;
        private EntityQuery _connectionQuery;
        private EntityQuery _receivedQuery;
        private EntityQuery _receivedDatagramQuery;
        private EntityQuery _startRequestQuery;
        private EntityQuery _endRequestQuery;
        private NativeHashMap<int, Entity> _connectionsById;
        private NativeHashMap<Entity, int> _idsByConnection;
        private NativeHashSet<int> _redundantConnections;
        private readonly List<int> _removed = new List<int>();
        private int _lastConnectionId;
        private readonly List<int> _violations = new List<int>();
        private readonly List<int> _timedOut = new List<int>();

        /// <summary>The running server, or null.</summary>
        public LockstepServer Server => _server;

        /// <summary>The channel of the clients that talk through it, or null.</summary>
        public LockstepRedundantChannel Channel => _channel;

        protected override void OnCreate()
        {
            _connectionQuery = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamConnection>());
            _receivedQuery = GetEntityQuery(ComponentType.ReadOnly<LockstepPacketRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            _receivedDatagramQuery = GetEntityQuery(ComponentType.ReadOnly<LockstepDatagramRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            _startRequestQuery = GetEntityQuery(ComponentType.ReadOnly<LockstepStartGameRequest>());
            _endRequestQuery = GetEntityQuery(ComponentType.ReadOnly<LockstepEndGameRequest>());
            _connectionsById = new NativeHashMap<int, Entity>(16, Allocator.Persistent);
            _idsByConnection = new NativeHashMap<Entity, int>(16, Allocator.Persistent);
            _redundantConnections = new NativeHashSet<int>(16, Allocator.Persistent);
            LockstepRpcHiddenTag.Initialize();
        }

        protected override void OnDestroy()
        {
            Shutdown();
            _connectionsById.Dispose();
            _idsByConnection.Dispose();
            _redundantConnections.Dispose();
        }

        protected override unsafe void OnUpdate()
        {
            var hasConfig = SystemAPI.TryGetSingleton<LockstepServerConfig>(out var config);
            if (!hasConfig && _server != null)
            {
                Shutdown();
            }
            if (hasConfig && _server == null)
            {
                Startup(config.settings);
            }
            if (_server == null)
            {
                // Not hosting: still consume stray lockstep packets so they do not pile up.
                if (!_receivedQuery.IsEmptyIgnoreFilter)
                {
                    EntityManager.DestroyEntity(_receivedQuery);
                }
                if (!_receivedDatagramQuery.IsEmptyIgnoreFilter)
                {
                    EntityManager.DestroyEntity(_receivedDatagramQuery);
                }
                return;
            }

            var now = SystemAPI.Time.ElapsedTime;
            RefreshConnections();

            if (!_receivedQuery.IsEmptyIgnoreFilter)
            {
                var packets = _receivedQuery.ToComponentDataArray<LockstepPacketRpc>(Allocator.Temp);
                var requests = _receivedQuery.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                EntityManager.DestroyEntity(_receivedQuery);
                var packetPtr = (LockstepPacketRpc*)packets.GetUnsafeReadOnlyPtr();
                for (var i = 0; i < packets.Length; i++)
                {
                    if (_idsByConnection.TryGetValue(requests[i].SourceConnection, out var connectionId))
                    {
                        _server.OnPacket(connectionId, LockstepPacketRpc.GetData(packetPtr + i), packets[i].length, now);
                    }
                }
            }
            if (!_receivedDatagramQuery.IsEmptyIgnoreFilter)
            {
                var datagrams = _receivedDatagramQuery.ToComponentDataArray<LockstepDatagramRpc>(Allocator.Temp);
                var requests = _receivedDatagramQuery.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                EntityManager.DestroyEntity(_receivedDatagramQuery);
                var datagramPtr = (LockstepDatagramRpc*)datagrams.GetUnsafeReadOnlyPtr();
                for (var i = 0; i < datagrams.Length; i++)
                {
                    if (_idsByConnection.TryGetValue(requests[i].SourceConnection, out var connectionId) &&
                        _channel.Receive(connectionId, LockstepDatagramRpc.GetData(datagramPtr + i), datagrams[i].length, now))
                    {
                        _redundantConnections.Add(connectionId);
                    }
                }
                while (_channel.TryRead(out var connectionId, out var packet, out var length))
                {
                    _server.OnPacket(connectionId, packet, length, now);
                }
            }

            if (!_startRequestQuery.IsEmptyIgnoreFilter)
            {
                EntityManager.DestroyEntity(_startRequestQuery);
                _server.StartGame(now);
            }
            if (!_endRequestQuery.IsEmptyIgnoreFilter)
            {
                var reason = _endRequestQuery.ToComponentDataArray<LockstepEndGameRequest>(Allocator.Temp)[0].reason;
                EntityManager.DestroyEntity(_endRequestQuery);
                _server.EndGame(reason);
            }

            _server.Update(now);
            _channel.Flush(now);

            Disconnect(_violations, NetworkStreamDisconnectReason.InvalidRpc);
            Disconnect(_timedOut, NetworkStreamDisconnectReason.Timeout);

            SystemAPI.SetSingleton(new LockstepServerStatus
            {
                state = _server.State,
                closedTicks = _server.ClosedTicks,
                playerCount = _server.PlayerCount,
            });
        }

        private void Disconnect(List<int> connectionIds, NetworkStreamDisconnectReason reason)
        {
            foreach (var connectionId in connectionIds)
            {
                if (_connectionsById.TryGetValue(connectionId, out var connection) && EntityManager.Exists(connection) &&
                    !EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection))
                {
                    EntityManager.AddComponentData(connection, new NetworkStreamRequestDisconnect { Reason = reason });
                }
            }
            connectionIds.Clear();
        }

        private void Startup(in LockstepServerSettings settings)
        {
            var archetype = LockstepRpcHiddenTag.CreateRequestArchetype<LockstepPacketRpc>(EntityManager);
            _channel = new LockstepRedundantChannel(new DatagramTransport
            {
                entityManager = EntityManager,
                archetype = LockstepRpcHiddenTag.CreateRequestArchetype<LockstepDatagramRpc>(EntityManager),
                connections = _connectionsById,
            });
            _transport = new Transport
            {
                entityManager = EntityManager,
                archetype = archetype,
                connections = _connectionsById,
                redundantConnections = _redundantConnections,
                channel = _channel,
            };
            // One packet size for every connection: it has to fit the channel's datagrams too.
            var effective = settings;
            if (effective.MaxPacketSize <= 0 || effective.MaxPacketSize > _channel.MaxPacketSize)
            {
                effective.MaxPacketSize = _channel.MaxPacketSize;
            }
            _server = new LockstepServer(effective, _transport);
            _server.ProtocolViolationEvent += (connectionId, _) => _violations.Add(connectionId);
            _server.ConnectionTimedOutEvent += connectionId => _timedOut.Add(connectionId);
            _connectionsById.Clear();
            _idsByConnection.Clear();
            _redundantConnections.Clear();

            _handle = EntityManager.CreateEntity(ComponentType.ReadWrite<LockstepServerStatus>());
            EntityManager.SetName(_handle, "LockstepServer");
            LockstepWorlds.RegisterServer(World, _server);
        }

        private void Shutdown()
        {
            if (_server == null)
            {
                return;
            }
            LockstepWorlds.UnregisterServer(World);
            _server.EndGame(LockstepEndReason.ServerShutdown);
            // One datagram, which may be lost: a client that misses it ends when its connection drops.
            _channel.Flush(World.Time.ElapsedTime);
            _server.Dispose();
            _channel.Dispose();
            _server = null;
            _channel = null;
            _transport = null;
            if (EntityManager.Exists(_handle))
            {
                EntityManager.DestroyEntity(_handle);
            }
            _handle = Entity.Null;
        }

        // Connections are tracked by entity rather than NetworkId, which netcode may hand to a new connection.
        // A connection entity that is gone means a disconnect.
        private void RefreshConnections()
        {
            var entities = _connectionQuery.ToEntityArray(Allocator.Temp);
            var alive = new NativeHashSet<Entity>(entities.Length + 1, Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                alive.Add(entities[i]);
            }

            _removed.Clear();
            foreach (var pair in _idsByConnection)
            {
                if (!alive.Contains(pair.Key))
                {
                    _removed.Add(pair.Value);
                }
            }
            foreach (var connectionId in _removed)
            {
                if (_connectionsById.TryGetValue(connectionId, out var entity))
                {
                    _idsByConnection.Remove(entity);
                }
                _connectionsById.Remove(connectionId);
                _redundantConnections.Remove(connectionId);
                _channel.RemoveConnection(connectionId);
                _server.OnDisconnected(connectionId);
            }

            for (var i = 0; i < entities.Length; i++)
            {
                if (_idsByConnection.ContainsKey(entities[i]))
                {
                    continue;
                }
                var connectionId = ++_lastConnectionId;
                _idsByConnection.Add(entities[i], connectionId);
                _connectionsById.Add(connectionId, entities[i]);
                _server.OnConnected(connectionId);
            }
        }
    }
}
