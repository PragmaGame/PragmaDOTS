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

        /// <summary>
        /// The player of this process leaves the session of <paramref name="clientWorld"/> (on the first call) and the leave
        /// is followed until it has reached everyone: call it every frame until it returns true, then close the worlds. The
        /// other players then learn of the leave itself, not of a lost connection: Netcode drops what a world going away has
        /// not sent yet.
        /// </summary>
        /// <remarks>
        /// <para>A guest passes no server world: its leave has reached everyone once the server has it
        /// (<see cref="LockstepNetcodeClientSystem.IsDelivered"/>), and the server announces it to the others in a frame.</para>
        /// <para>A host passes its <paramref name="serverWorld"/>, which goes with it. Once the frame with the leave of
        /// <paramref name="slot"/> went out (<see cref="LockstepServer.IsLeaveSent"/>), the match ends on the next server
        /// update (<see cref="RequestEnd"/>, <see cref="LockstepEndReason.Finished"/>): no new frames, the closed ones and
        /// the end go out. The leave has reached everyone once the server has ended the match and every client has what it
        /// sent (<see cref="LockstepNetcodeServerSystem.IsDelivered"/>).</para>
        /// <para>How long to wait for a peer that went silent is the caller's: this only tells when the leave has
        /// arrived.</para>
        /// </remarks>
        /// <param name="serverWorld">The host's server world; null for a guest.</param>
        /// <param name="clientWorld">The client world of the player who leaves.</param>
        /// <param name="slot">The slot the player leaves, whose leave the host's server announces; a guest's is not used.</param>
        /// <returns>True once the worlds may go: the leave has reached everyone, or nothing is left that could carry it.</returns>
        public static bool UpdateLeave(World serverWorld, World clientWorld, int slot)
        {
            var isClientDelivered = true;
            if (IsAlive(clientWorld))
            {
                if (TryGetClient(clientWorld, out var client))
                {
                    client.Leave();
                }
                var clientSystem = clientWorld.GetExistingSystemManaged<LockstepNetcodeClientSystem>();
                isClientDelivered = clientSystem == null || clientSystem.IsDelivered;
            }

            if (!IsAlive(serverWorld) || !TryGetServer(serverWorld, out var server))
            {
                return isClientDelivered;
            }
            if (server.State != LockstepServerState.Ended)
            {
                if (server.IsLeaveSent(slot) && !HasEndRequest(serverWorld))
                {
                    RequestEnd(serverWorld);
                }
                return false;
            }
            var serverSystem = serverWorld.GetExistingSystemManaged<LockstepNetcodeServerSystem>();
            return isClientDelivered && (serverSystem == null || serverSystem.IsDelivered);
        }

        private static bool IsAlive(World world) => world != null && world.IsCreated;

        private static bool HasEndRequest(World serverWorld)
        {
            using (var query = serverWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LockstepEndGameRequest>()))
            {
                return !query.IsEmptyIgnoreFilter;
            }
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
