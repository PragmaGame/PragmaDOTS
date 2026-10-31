using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Netcode;

namespace Pragma.Dots.Lockstep.Netcode
{
    /// <summary>
    /// Runs a <see cref="LockstepClient"/> in a Netcode client world while a <see cref="LockstepClientConfig"/> exists
    /// and the world is connected. Thin clients join too but do not simulate.
    /// </summary>
    /// <remarks>
    /// The session is registered in <see cref="LockstepWorlds"/>, which is where the presentation systems find it.
    /// Losing the connection ends the session (<see cref="LockstepEndReason.ConnectionLost"/>), which still simulates the
    /// frames it has and stays registered until the config is removed or a new connection joins again.
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

            public void Send(int connectionId, byte* data, int length)
            {
                if (connection == Entity.Null)
                {
                    return;
                }
                var entity = entityManager.CreateEntity(archetype);
                entityManager.SetComponentData(entity, LockstepPacketRpc.Create(data, length));
                entityManager.SetComponentData(entity, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        private LockstepClient _client;
        private Transport _transport;
        private EntityQuery _connectionQuery;
        private EntityQuery _receivedQuery;
        private EntityQuery _localInputQuery;

        /// <summary>The running client, or null.</summary>
        public LockstepClient Client => _client;

        protected override void OnCreate()
        {
            _connectionQuery = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamConnection>());
            _receivedQuery = GetEntityQuery(ComponentType.ReadOnly<LockstepPacketRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            _localInputQuery = GetEntityQuery(ComponentType.ReadWrite<LockstepLocalInput>(), ComponentType.ReadWrite<LockstepCommand>());
        }

        protected override void OnDestroy() => Shutdown();

        protected override unsafe void OnUpdate()
        {
            var hasConfig = SystemAPI.TryGetSingleton<LockstepClientConfig>(out var config);
            var connection = _connectionQuery.CalculateEntityCount() == 1 ? _connectionQuery.GetSingletonEntity() : Entity.Null;

            if (_client != null && !hasConfig)
            {
                // Removing the config on a live connection leaves the session: the server frees the slot, and a new
                // config can join again over the same connection.
                if (connection != Entity.Null && connection == _transport.connection)
                {
                    _client.Leave();
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
                Startup(config, connection);
            }
            if (_client == null || _transport.connection == Entity.Null)
            {
                if (!_receivedQuery.IsEmptyIgnoreFilter)
                {
                    EntityManager.DestroyEntity(_receivedQuery);
                }
                _client?.Update(SystemAPI.Time.ElapsedTime);
                return;
            }

            var now = SystemAPI.Time.ElapsedTime;
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

            LockstepClientWorldUtility.PushLocalInput(EntityManager, _localInputQuery, _client);
            _client.Update(now);
        }

        private void Startup(in LockstepClientConfig config, Entity connection)
        {
            _transport = new Transport
            {
                entityManager = EntityManager,
                archetype = EntityManager.CreateArchetype(ComponentType.ReadWrite<LockstepPacketRpc>(), ComponentType.ReadWrite<SendRpcCommandRequest>()),
                connection = connection,
            };
            var settings = config.settings;
            if (settings.MaxPacketSize <= 0 || settings.MaxPacketSize > LockstepPacketRpc.CAPACITY)
            {
                settings.MaxPacketSize = LockstepPacketRpc.CAPACITY;
            }
            if (World.IsThinClient())
            {
                settings.Simulate = false;
            }

            var options = LockstepClientWorldUtility.CreateSimulationOptions(World, config.waitForActorRegistry);
            _client = new LockstepClient(settings, _transport, options);
            LockstepWorlds.RegisterClient(World, _client);
            _client.Join(config.joinData);
        }

        private void Shutdown()
        {
            if (_client == null)
            {
                return;
            }
            LockstepWorlds.UnregisterClient(World);
            _client.Dispose();
            _client = null;
            _transport = null;
        }
    }
}
