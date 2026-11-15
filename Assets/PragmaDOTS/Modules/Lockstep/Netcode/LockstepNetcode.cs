using Pragma.Dots.Utils;
using Unity.Collections;
using Unity.Entities;
using Unity.Netcode;
using Unity.Networking.Transport;

namespace Pragma.Dots.Lockstep.Netcode
{
    /// <summary>Shortcuts for setting up lockstep sessions on top of Netcode for Entities worlds.</summary>
    public static class LockstepNetcode
    {
        /// <summary>Hosts a lockstep session in a server world (replaces the configuration if one exists).</summary>
        public static void HostSession(World serverWorld, in LockstepServerSettings settings)
        {
            SetSingleton(serverWorld.EntityManager, new LockstepServerConfig { settings = settings });
        }

        /// <summary>
        /// Makes a client world join the lockstep session of the server it is (or will be) connected to, in
        /// <paramref name="slot"/> (<see cref="LockstepProtocol.ANY_SLOT"/>: the first free one; see <see cref="LockstepClient.Join"/>).
        /// With <paramref name="redundantChannel"/> the session talks through a <see cref="LockstepRedundantChannel"/> over
        /// the out-of-band pipeline (see <see cref="LockstepClientConfig.redundantChannel"/>).
        /// </summary>
        public static void JoinSession(World clientWorld, in LockstepClientSettings settings, in FixedList64Bytes<byte> joinData = default, bool waitForActorRegistry = false,
                                       int slot = LockstepProtocol.ANY_SLOT, bool redundantChannel = false)
        {
            SetSingleton(clientWorld.EntityManager, new LockstepClientConfig
            {
                settings = settings,
                joinData = joinData,
                waitForActorRegistry = waitForActorRegistry,
                slot = slot == LockstepProtocol.ANY_SLOT ? default : new OptionalValue<int>(slot),
                redundantChannel = redundantChannel,
            });
        }

        /// <summary>
        /// Makes the out-of-band RPC pipeline of every driver in <paramref name="driverStore"/> unreliable (Netcode's
        /// unreliable fragmented pipeline, with the network simulator when the driver has it), so that the redundant channel
        /// of <see cref="LockstepDatagramRpc"/> is not held back by retransmissions. Call it where the drivers are built, after
        /// registering them and before the store is handed to Netcode: in an <c>INetworkStreamDriverConstructor</c>
        /// (<see cref="LockstepDriverConstructor"/> does it), or before <c>NetworkStreamDriver.ResetDriverStore</c>. Netcode
        /// itself sends nothing out of band; the game's own out-of-band RPCs become unreliable too. The server and every client
        /// need it: a datagram on a pipeline the receiver does not take for out-of-band trips an assertion of Netcode.
        /// </summary>
        public static void UseUnreliableOutOfBandPipeline(ref NetworkDriverStore driverStore)
        {
            for (var id = driverStore.FirstDriver; id < driverStore.LastDriver; id++)
            {
                ref var instance = ref driverStore.GetDriverInstanceRW(id);
                if (instance.driver.IsCreated && instance.unreliableFragmentedPipeline != NetworkPipeline.Null)
                {
                    instance.outOfBandPipeline = instance.unreliableFragmentedPipeline;
                }
            }
        }

        /// <summary>Starts listening on the server world's network driver.</summary>
        public static bool Listen(World serverWorld, NetworkEndpoint endpoint)
        {
            using (var query = serverWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            {
                return query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Listen(endpoint);
            }
        }

        /// <summary>Connects a client world to a server; returns the connection entity.</summary>
        public static Entity Connect(World clientWorld, NetworkEndpoint endpoint)
        {
            using (var query = clientWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            {
                return query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(clientWorld.EntityManager, endpoint);
            }
        }

        /// <summary>The server session running in <paramref name="serverWorld"/>, if any.</summary>
        public static bool TryGetServer(World serverWorld, out LockstepServer server) => LockstepWorlds.TryGetServer(serverWorld, out server);

        /// <summary>The client session running in <paramref name="clientWorld"/>, if any.</summary>
        public static bool TryGetClient(World clientWorld, out LockstepClient client) => LockstepWorlds.TryGetClient(clientWorld, out client);

        /// <summary>Starts the match on the next server update, whoever has joined.</summary>
        public static void RequestStart(World serverWorld)
        {
            serverWorld.EntityManager.CreateEntity(ComponentType.ReadWrite<LockstepStartGameRequest>());
        }

        /// <summary>Ends the match on the next server update.</summary>
        public static void RequestEnd(World serverWorld, LockstepEndReason reason = LockstepEndReason.Finished)
        {
            var entity = serverWorld.EntityManager.CreateEntity(ComponentType.ReadWrite<LockstepEndGameRequest>());
            serverWorld.EntityManager.SetComponentData(entity, new LockstepEndGameRequest { reason = reason });
        }

        private static void SetSingleton<T>(EntityManager entityManager, T value) where T : unmanaged, IComponentData
        {
            using (var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<T>()))
            {
                if (query.CalculateEntityCount() == 0)
                {
                    entityManager.CreateSingleton(value);
                }
                else
                {
                    query.SetSingleton(value);
                }
            }
        }
    }
}
