using Pragma.Dots.Lockstep;
using Pragma.Dots.Lockstep.Netcode;

#if PRAGMA_DOTS_UNITY_NETCODE
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Lockstep.Netcode;
using Pragma.Dots.Utils;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Entities;
using Unity.Netcode;
using Unity.Networking.Transport;
using UnityEngine.TestTools;

namespace Pragma.Dots.Tests
{
    /// <summary>A real Netcode for Entities server and clients in one process, connected through the IPC driver.</summary>
    public class NetcodeIntegrationTests
    {
        private readonly List<World> _worlds = new List<World>();
        private double _time;
        private bool _runInBackground;
        private ushort _autoConnectPort;
        private INetworkStreamDriverConstructor _driverConstructor;

        [SetUp]
        public void SetUp()
        {
            // Netcode logs an error when the application would stall without focus.
            _runInBackground = UnityEngine.Application.runInBackground;
            UnityEngine.Application.runInBackground = true;
            // Worlds created with a non-zero auto connect port listen and connect by themselves; the test does it.
            _autoConnectPort = ClientServerBootstrap.AutoConnectPort;
            ClientServerBootstrap.AutoConnectPort = 0;
            _driverConstructor = NetworkStreamReceiveSystem.DriverConstructor;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var world in _worlds)
            {
                if (world.IsCreated)
                {
                    world.Dispose();
                }
            }
            _worlds.Clear();
            UnityEngine.Application.runInBackground = _runInBackground;
            ClientServerBootstrap.AutoConnectPort = _autoConnectPort;
            NetworkStreamReceiveSystem.DriverConstructor = _driverConstructor;
        }

        private World Track(World world)
        {
            // Time is driven by the test, not by the editor frame.
            var timeSystem = world.GetExistingSystemManaged<UpdateWorldTimeSystem>();
            if (timeSystem != null)
            {
                timeSystem.Enabled = false;
            }
            // Game input systems of the project would write their own input structs into this test session.
            var inputGroup = world.GetExistingSystemManaged<LockstepInputSystemGroup>();
            if (inputGroup != null)
            {
                inputGroup.Enabled = false;
            }
            _worlds.Add(world);
            return world;
        }

        private void Tick(int frames, double deltaTime = 1 / 60.0)
        {
            for (var i = 0; i < frames; i++)
            {
                _time += deltaTime;
                foreach (var world in _worlds)
                {
                    world.SetTime(new TimeData(_time, (float)deltaTime));
                    world.Update();
                }
            }
        }

        /// <summary>
        /// A server world hosting a session for two players and two client worlds joining it, through the redundant channel
        /// over the unreliable out-of-band pipeline when <paramref name="redundant"/> (or over the reliable one, without
        /// <paramref name="unreliablePipeline"/>).
        /// </summary>
        private (World Server, World ClientA, World ClientB) StartSession(ushort port, bool redundant = false, bool unreliablePipeline = true)
        {
            if (redundant && unreliablePipeline)
            {
                NetworkStreamReceiveSystem.DriverConstructor = new LockstepDriverConstructor(_driverConstructor);
            }
            var server = Track(ClientServerBootstrap.CreateServerWorld("Lockstep Test Server"));
            var clientA = Track(ClientServerBootstrap.CreateClientWorld("Lockstep Test Client A"));
            var clientB = Track(ClientServerBootstrap.CreateClientWorld("Lockstep Test Client B"));

            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.MinPlayersToStart = 2;
            settings.ChecksumInterval = 10;
            settings.StartDelaySeconds = 0.2f;
            LockstepNetcode.HostSession(server, settings);
            LockstepNetcode.JoinSession(clientA, LockstepClientSettings.Default, redundantChannel: redundant);
            LockstepNetcode.JoinSession(clientB, LockstepClientSettings.Default, redundantChannel: redundant);

            var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(port);
            Assert.IsTrue(LockstepNetcode.Listen(server, endpoint));
            LockstepNetcode.Connect(clientA, endpoint);
            LockstepNetcode.Connect(clientB, endpoint);
            return (server, clientA, clientB);
        }

        [TestCase(false, 7971)]
        [TestCase(true, 7974)]
        public void ServerAndClients_RunASessionOverRpcs(bool redundant, int port)
        {
            var (server, clientA, clientB) = StartSession((ushort)port, redundant);

            var desyncs = 0;
            Tick(30);
            Assert.IsTrue(LockstepNetcode.TryGetServer(server, out var lockstepServer), "the server session is up");
            lockstepServer.DesyncDetectedEvent += _ => desyncs++;

            Tick(60 * 6);

            Assert.IsTrue(LockstepNetcode.TryGetClient(clientA, out var a), "client A joined");
            Assert.IsTrue(LockstepNetcode.TryGetClient(clientB, out var b), "client B joined");
            Assert.AreEqual(LockstepClientState.Running, a.State);
            Assert.AreEqual(LockstepClientState.Running, b.State);
            Assert.AreNotEqual(a.LocalSlot, b.LocalSlot);
            Assert.Greater(a.SimulatedTicks, 120);
            Assert.Greater(b.SimulatedTicks, 120);
            Assert.AreEqual(2, TestUtility.Count<LockstepPlayer>(a.Simulation.World.EntityManager));
            Assert.Greater(lockstepServer.VerifiedChecksumCount, 5);
            Assert.AreEqual(0, desyncs);

            // Removing the config leaves the session; a new config joins again over the same connection.
            using (var query = clientB.EntityManager.CreateEntityQuery(typeof(LockstepClientConfig)))
            {
                clientB.EntityManager.DestroyEntity(query);
            }
            Tick(60);
            Assert.AreEqual(1, lockstepServer.PlayerCount, "the leave freed the slot");
            Assert.AreEqual(1, TestUtility.Count<LockstepPlayer>(a.Simulation.World.EntityManager));

            LockstepNetcode.JoinSession(clientB, LockstepClientSettings.Default, redundantChannel: redundant);
            Tick(60 * 3);
            Assert.IsTrue(LockstepNetcode.TryGetClient(clientB, out var rejoined), "client B joined again");
            Assert.AreEqual(LockstepClientState.Running, rejoined.State);
            Assert.AreEqual(2, TestUtility.Count<LockstepPlayer>(a.Simulation.World.EntityManager));
            var serverChannel = server.GetExistingSystemManaged<LockstepNetcodeServerSystem>().Channel;
            if (redundant)
            {
                Assert.Greater(serverChannel.DatagramsReceived, 100, "the clients talk through the channel");
                Assert.Greater(clientA.GetExistingSystemManaged<LockstepNetcodeClientSystem>().Channel.DatagramsReceived, 100, "and so does the server");
            }
            else
            {
                Assert.AreEqual(0, serverChannel.DatagramsReceived);
            }

            // Disconnecting a client is announced to the other one.
            using (var query = clientB.EntityManager.CreateEntityQuery(typeof(NetworkStreamConnection)))
            {
                clientB.EntityManager.AddComponent<NetworkStreamRequestDisconnect>(query);
            }
            Tick(60);
            Assert.AreEqual(1, TestUtility.Count<LockstepPlayer>(a.Simulation.World.EntityManager));
            Assert.IsTrue(LockstepNetcode.TryGetClient(clientB, out var lost), "the disconnected client keeps its session");
            Assert.AreEqual(LockstepClientState.Ended, lost.State);
            Assert.AreEqual(LockstepEndReason.ConnectionLost, lost.EndReason);
            Assert.AreEqual(lost.ConfirmedTicks, lost.SimulatedTicks, "and simulates the frames it got");

            // Removing the config disposes it.
            using (var query = clientB.EntityManager.CreateEntityQuery(typeof(LockstepClientConfig)))
            {
                clientB.EntityManager.DestroyEntity(query);
            }
            Tick(1);
            Assert.IsFalse(LockstepNetcode.TryGetClient(clientB, out _));
        }

        [TestCase(false, 7972)]
        [TestCase(true, 7975)]
        public void ACommandLargerThanTheReliableWindow_ReachesEveryClient(bool redundant, int port)
        {
            var (_, clientA, clientB) = StartSession((ushort)port, redundant);
            Tick(60 * 3);
            Assert.IsTrue(LockstepNetcode.TryGetClient(clientA, out var a), "client A joined");
            Assert.IsTrue(LockstepNetcode.TryGetClient(clientB, out var b), "client B joined");
            Assert.AreEqual(LockstepClientState.Running, a.State);
            Assert.AreEqual(LockstepClientState.Running, b.State);

            // 128 KB: 128 RPCs of 1 KB each way, several times what the reliable pipeline sends without acknowledgement.
            const int count = 32 * 1024;
            var values = new int[count];
            var sum = 0L;
            for (var i = 0; i < count; i++)
            {
                values[i] = i * 7;
                sum += values[i];
            }
            using (var data = new NativeArray<int>(values, Allocator.Temp))
            {
                a.AddCommand(new TestCommand { value = 1 }, data);
            }

            // The burst goes over real UDP on loopback: the OS may drop part of it, and the reliable pipeline resends on
            // real time, while the test ticks far faster than real time. Tick until both clients have the command, within
            // a real-time bound, instead of a fixed number of frames.
            var timer = Stopwatch.StartNew();
            do
            {
                Tick(60);
            }
            while ((ReceivedState(a, a.LocalSlot).commandDataCount != count || ReceivedState(b, a.LocalSlot).commandDataCount != count) &&
                   timer.Elapsed.TotalSeconds < 20);

            Assert.AreEqual(LockstepClientState.Running, a.State);
            Assert.AreEqual(LockstepClientState.Running, b.State);
            Assert.IsFalse(a.IsDesynced || b.IsDesynced);
            foreach (var client in new[] { a, b })
            {
                var state = ReceivedState(client, a.LocalSlot);
                Assert.AreEqual(count, state.commandDataCount);
                Assert.AreEqual(sum, state.commandDataSum);
            }
        }

        [TestCase(false, 7973)]
        [TestCase(true, 7976)]
        public void Requests_AreHiddenFromTheHierarchy(bool redundant, int port)
        {
            Assert.IsTrue(EntityHierarchy.TryGetHiddenTag(out var hidden), "the editor has the tag");
            var (server, clientA, clientB) = StartSession((ushort)port, redundant);
            var worlds = new[] { server, clientA, clientB };
            var probes = new List<ReceivedRequestProbeSystem>();

            // Received requests live only inside a frame: a probe right after the begin command buffer sees them.
            foreach (var world in worlds)
            {
                var probe = world.GetOrCreateSystemManaged<ReceivedRequestProbeSystem>();
                world.GetExistingSystemManaged<SimulationSystemGroup>().AddSystemToUpdateList(probe);
                probes.Add(probe);
            }

            // Netcode destroys a sent request only in the next frame: after each update that frame's requests remain.
            var requests = 0;
            for (var frame = 0; frame < 60 * 3; frame++)
            {
                Tick(1);
                foreach (var world in worlds)
                {
                    var entityManager = world.EntityManager;
                    var packets = new[] { ComponentType.ReadOnly<LockstepPacketRpc>(), ComponentType.ReadOnly<LockstepDatagramRpc>() };
                    using (var sent = entityManager.CreateEntityQuery(new EntityQueryDesc
                           {
                               All = new[] { ComponentType.ReadOnly<SendRpcCommandRequest>() },
                               Any = packets,
                           }))
                    using (var shown = entityManager.CreateEntityQuery(new EntityQueryDesc
                           {
                               All = new[] { ComponentType.ReadOnly<SendRpcCommandRequest>() },
                               Any = packets,
                               None = new[] { hidden },
                           }))
                    {
                        requests += sent.CalculateEntityCount();
                        Assert.AreEqual(0, shown.CalculateEntityCount(), $"a request in {world.Name} shows in the Hierarchy");
                    }
                }
            }
            Assert.Greater(requests, 0, "the session sent packets");

            foreach (var probe in probes)
            {
                Assert.Greater(probe.Received, 0, $"{probe.World.Name} received packets");
                Assert.AreEqual(0, probe.Shown, $"a received request in {probe.World.Name} shows in the Hierarchy");
            }
        }

        [Test]
        public void TheRedundantChannel_KeepsASessionInSync_ThroughPacketLoss()
        {
            // A tenth of the clients' packets is lost both ways, in Transport's simulator.
            NetworkStreamReceiveSystem.DriverConstructor = new LossyDriverConstructor(10);
            var (server, clientA, clientB) = StartSession(7978, redundant: true, unreliablePipeline: false);

            // Netcode's handshake goes over its reliable pipeline, which resends on real time while the test ticks faster.
            var timer = Stopwatch.StartNew();
            LockstepClient a = null;
            LockstepClient b = null;
            do
            {
                Tick(60);
                LockstepNetcode.TryGetClient(clientA, out a);
                LockstepNetcode.TryGetClient(clientB, out b);
            }
            while ((a == null || b == null || a.SimulatedTicks < 600 || b.SimulatedTicks < 600) && timer.Elapsed.TotalSeconds < 30);

            Assert.IsNotNull(a, "client A joined");
            Assert.IsNotNull(b, "client B joined");
            Assert.AreEqual(LockstepClientState.Running, a.State);
            Assert.AreEqual(LockstepClientState.Running, b.State);
            Assert.GreaterOrEqual(a.SimulatedTicks, 600);
            Assert.GreaterOrEqual(b.SimulatedTicks, 600);
            Assert.IsFalse(a.IsDesynced || b.IsDesynced);
            Assert.IsTrue(LockstepNetcode.TryGetServer(server, out var lockstepServer));
            Assert.Greater(lockstepServer.VerifiedChecksumCount, 10);
            TestUtility.AssertSameChecksums(a, b, 10);
            Assert.Greater(server.GetExistingSystemManaged<LockstepNetcodeServerSystem>().Channel.DatagramsReceived, 500);
        }

        [Test]
        public void TheRedundantChannel_OverAReliablePipeline_WarnsAndStillWorks()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("runs over a reliable out-of-band pipeline"));
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("runs over a reliable out-of-band pipeline"));
            var (_, clientA, clientB) = StartSession(7977, redundant: true, unreliablePipeline: false);
            Tick(60 * 4);

            Assert.IsTrue(LockstepNetcode.TryGetClient(clientA, out var a), "client A joined");
            Assert.IsTrue(LockstepNetcode.TryGetClient(clientB, out var b), "client B joined");
            Assert.AreEqual(LockstepClientState.Running, a.State);
            Assert.AreEqual(LockstepClientState.Running, b.State);
            Assert.Greater(a.SimulatedTicks, 60);
            Assert.IsFalse(a.IsDesynced || b.IsDesynced);
        }

        /// <summary>
        /// The state of <paramref name="slot"/> after what <paramref name="client"/> has received so far. The sessions run
        /// without test systems: their replay is played with them.
        /// </summary>
        private static TestPlayerState ReceivedState(LockstepClient client, int slot)
        {
            using (var replay = LockstepReplay.Read(client.ExportReplay()))
            using (var player = new LockstepReplayPlayer(replay, TestUtility.Options(typeof(TestGameplaySystem))) { VerifyChecksums = false })
            {
                player.SimulateToEnd();
                return TestUtility.PlayerState(player.Simulation, slot);
            }
        }
    }
}
#endif
