using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pragma.Dots.Lockstep;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pragma.Dots.Tests
{
    /// <summary>
    /// Players that leave once the match started but before their join reached a frame: during the start countdown, or a
    /// late joiner before its first tick. Every client hears of them as of anybody else: a join, then a leave on the
    /// next tick. And when the server has sent a leave (<see cref="LockstepServer.IsLeaveSent"/>).
    /// </summary>
    public class EarlyLeaveTests
    {
        // Long enough for a silent player to time out before tick 0.
        private const float START_DELAY_SECONDS = 4f;
        private const float PLAYER_TIMEOUT_SECONDS = 2f;

        private static LockstepServerSettings Settings(int minPlayers)
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.MaxPlayers = 4;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 77;
            settings.MinPlayersToStart = minPlayers;
            settings.StartDelaySeconds = START_DELAY_SECONDS;
            settings.PlayerTimeoutSeconds = PLAYER_TIMEOUT_SECONDS;
            settings.ChecksumInterval = 10;
            return settings;
        }

        private static void VaryingInput(LockstepClient client, int frame)
        {
            if (client.LocalSlot < 0)
            {
                return;
            }
            var slot = client.LocalSlot + 1;
            client.SetInput(new TestInput
            {
                moveX = (frame / (7 * slot)) % 3 - 1,
                moveY = slot,
                buttons = (frame / (5 + slot)) % 2,
            });
        }

        [Test]
        public void Leave_DuringTheCountdown_IsAnnouncedAfterTheJoin()
        {
            LeaveDuringTheCountdown((session, guest) => guest.Leave());
        }

        [Test]
        public void Disconnect_DuringTheCountdown_IsAnnouncedAfterTheJoin()
        {
            LeaveDuringTheCountdown((session, guest) => session.Disconnect(guest));
        }

        [Test]
        public void Timeout_DuringTheCountdown_IsAnnouncedAfterTheJoin()
        {
            LeaveDuringTheCountdown((session, guest) =>
            {
                LogAssert.Expect(LogType.Warning, new Regex("sent nothing for 2 s"));
                session.Freeze(guest);
            });
        }

        [Test]
        public void LateJoiner_LeavingBeforeItsFirstTick_IsAnnouncedAfterTheJoin()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 2), latency: 0.03, jitter: 0.02))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                session.Run(START_DELAY_SECONDS + 2, VaryingInput);

                // The server takes the join and starts the late joiner but closes no tick meanwhile: the join waits for a
                // frame.
                var closed = session.Server.ClosedTicks;
                var late = session.AddClient();
                // Its input and a command for the join tick reach the server before the leave, and go with the player.
                late.SetInput(new TestInput { moveX = 1, moveY = 5, buttons = 1 });
                late.AddCommand(new TestCommand { value = 9 });
                session.StepClientsOnly(0.15);
                Assert.AreEqual(LockstepClientState.Running, late.State, "the start reached the late joiner");
                Assert.AreEqual(closed, late.JoinTick);
                var slot = late.LocalSlot;

                late.Leave();
                session.StepClientsOnly(0.15);
                Assert.IsFalse(session.Server.IsSlotInUse(slot), "the server has the leave");
                Assert.AreEqual(closed, session.Server.ClosedTicks, "and the join has not reached a frame yet");

                session.Run(3, VaryingInput);

                Assert.AreEqual(2, session.Server.PlayerCount);
                AssertJoinedThenLeft(session.Server.ExportReplay(), slot, closed);
                foreach (var client in new[] { a, b })
                {
                    AssertFramesOfTheServer(session.Server, client);
                    Assert.AreEqual(2, TestUtility.Count<LockstepPlayer>(client.Simulation.World.EntityManager));
                    Assert.AreEqual(Entity.Null, TestUtility.PlayerEntity(client.Simulation, slot));
                }
                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(a, b, 10);
            }
        }

        [Test]
        public void Leave_InTheLobby_IsNeverAnnounced()
        {
            // No start on joins: the match starts when the test says so.
            using (var session = new SessionHarness(Settings(minPlayers: 0), latency: 0.03, jitter: 0.02))
            {
                var a = session.AddClient(slot: 0);
                session.AddClient(slot: 1);
                var guest = session.AddClient(slot: 2);
                session.Run(0.5, VaryingInput);
                Assert.AreEqual(LockstepClientState.Lobby, guest.State);
                Assert.IsFalse(session.Server.IsLeaveSent(2), "the guest is there");

                guest.Leave();
                session.Run(0.5, VaryingInput);
                Assert.IsFalse(session.Server.IsSlotInUse(2), "the server let the guest go");
                Assert.IsTrue(session.Server.IsLeaveSent(2), "nobody is to hear of the leave");
                Assert.IsFalse(session.Server.IsLeaveSent(3), "nobody left slot 3");

                session.Server.StartGame(session.Time);
                session.Run(START_DELAY_SECONDS + 1, VaryingInput);

                using (var replay = LockstepReplay.Read(session.Server.ExportReplay()))
                {
                    Assert.AreEqual(2, replay.Players.Count, "no frame names the player that left the lobby");
                    foreach (var player in replay.Players)
                    {
                        Assert.AreNotEqual(2, player.slot);
                    }
                }
                Assert.AreEqual(2, TestUtility.Count<LockstepPlayer>(a.Simulation.World.EntityManager));
            }
        }

        [Test]
        public void IsLeaveSent_OnceTheFrameOfTheLeaveWentOut()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 2), latency: 0.03, jitter: 0.02))
            {
                var leaver = session.AddClient();
                var stays = session.AddClient();
                StepUntil(session, () => leaver.State == LockstepClientState.Running, "the start reached the leaver");
                var slot = leaver.LocalSlot;
                Assert.IsFalse(session.Server.IsLeaveSent(slot), "the player is there");
                Assert.IsFalse(session.Server.IsLeaveSent(3), "nobody left slot 3");

                // Like the player of a host that leaves during the countdown: the server has the leave, and no frame
                // carries it yet.
                leaver.Leave();
                session.StepClientsOnly(0.1);
                Assert.IsFalse(session.Server.IsSlotInUse(slot));
                Assert.IsFalse(session.Server.IsLeaveSent(slot), "no frame announces the leave yet");

                // Frame 0 has the join and frame 1 the leave; the update that closes a frame sends it.
                StepUntil(session, () => session.Server.IsLeaveSent(slot), "the frame of the leave went out");
                Assert.AreEqual(2, session.Server.ClosedTicks);
                session.Run(1, VaryingInput);
                var players = TestUtility.Count<LockstepPlayer>(stays.Simulation.World.EntityManager);
                Assert.AreEqual(1, players, "the leave arrived");

                // A new player in the slot has not left.
                session.AddClient();
                StepUntil(session, () => session.Server.PlayerCount == 2, "the server took the join");
                Assert.IsTrue(session.Server.IsSlotInUse(slot));
                Assert.IsFalse(session.Server.IsLeaveSent(slot), "a new player holds the slot");
            }
        }

        [Test]
        public void IsLeaveSent_WaitsForALateJoinerCatchingUp()
        {
            var settings = Settings(minPlayers: 2);
            // A few frames per message and one message per update: a late joiner catches up for a while.
            settings.MaxPacketSize = 64;
            settings.MaxSendBytesPerUpdate = 1;
            using (var session = new SessionHarness(settings, latency: 0.03, jitter: 0.02))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                session.Run(START_DELAY_SECONDS + 10, VaryingInput);
                var slot = b.LocalSlot;

                var late = session.AddClient();
                StepUntil(session, () => session.Server.PlayerCount == 3, "the server took the late join");
                b.Leave();
                StepUntil(session, () => LeaveTick(session.Server, slot) >= 0, "a frame announces the leave");
                var leaveTick = LeaveTick(session.Server, slot);
                Assert.LessOrEqual(late.ConfirmedTicks, leaveTick, "the late joiner has not got that frame");
                Assert.IsFalse(session.Server.IsLeaveSent(slot), "so the leave has not gone to everybody");

                StepUntil(session, () => session.Server.IsLeaveSent(slot), "the late joiner was sent the frame too");
                session.Run(2, VaryingInput);
                Assert.AreEqual(2, TestUtility.Count<LockstepPlayer>(late.Simulation.World.EntityManager));
                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(a, late, 10);
            }
        }

        [Test]
        public void IsLeaveSent_AfterTheEnd_WaitsForNothing()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 2), latency: 0.03, jitter: 0.02))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                session.Run(START_DELAY_SECONDS + 1, VaryingInput);

                // The server has the leave, and the match ends before a frame carries it.
                var slotB = b.LocalSlot;
                b.Leave();
                session.StepClientsOnly(0.1);
                Assert.IsFalse(session.Server.IsLeaveSent(slotB), "a frame is still to carry the leave");
                session.Server.EndGame();
                Assert.IsTrue(session.Server.IsLeaveSent(slotB), "no frame closes after the end");

                // A player that leaves after the end frees its slot at once.
                var slotA = a.LocalSlot;
                session.Disconnect(a);
                Assert.IsFalse(session.Server.IsSlotInUse(slotA));
                Assert.IsTrue(session.Server.IsLeaveSent(slotA));
            }
        }

        // Two players and an observer (a thin client) stay; the start reaches the guest, which leaves before tick 0.
        private static void LeaveDuringTheCountdown(Action<SessionHarness, LockstepClient> leave)
        {
            var observerSettings = SessionHarness.ClientSettings;
            observerSettings.Simulate = false;
            using (var session = new SessionHarness(Settings(minPlayers: 4), latency: 0.03, jitter: 0.02))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                var observer = session.AddClient(settings: observerSettings);
                var guest = session.AddClient();
                StepUntil(session, () => guest.State == LockstepClientState.Running, "the start reached the guest");
                var slot = guest.LocalSlot;

                leave(session, guest);
                StepUntil(session, () => !session.Server.IsSlotInUse(slot), "the server let the guest go");
                Assert.AreEqual(0, session.Server.ClosedTicks, "the guest left during the countdown");

                session.Run(START_DELAY_SECONDS + 3, VaryingInput);

                Assert.AreEqual(3, session.Server.PlayerCount);
                AssertJoinedThenLeft(session.Server.ExportReplay(), slot, 0);
                foreach (var client in new[] { a, b, observer })
                {
                    Assert.AreEqual(LockstepClientState.Running, client.State);
                    AssertFramesOfTheServer(session.Server, client);
                }
                foreach (var client in new[] { a, b })
                {
                    Assert.AreEqual(3, TestUtility.Count<LockstepPlayer>(client.Simulation.World.EntityManager));
                    Assert.AreEqual(Entity.Null, TestUtility.PlayerEntity(client.Simulation, slot));
                }
                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(a, b, 5);

                // A player joining later, into the freed slot, re-simulates the same frames.
                var late = session.AddClient();
                session.Run(3, VaryingInput);
                Assert.AreEqual(slot, late.LocalSlot);
                AssertFramesOfTheServer(session.Server, late);
                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(a, late, 10);
            }
        }

        private static void StepUntil(SessionHarness session, Func<bool> condition, string what)
        {
            for (var i = 0; i < 500 && !condition(); i++)
            {
                session.Step(0.01, VaryingInput);
            }
            Assert.IsTrue(condition(), what);
        }

        // The tick of the frame that announced the last leave of the slot, or -1.
        private static int LeaveTick(LockstepServer server, int slot)
        {
            using (var replay = LockstepReplay.Read(server.ExportReplay()))
            {
                var tick = -1;
                foreach (var player in replay.Players)
                {
                    if (player.slot == slot && player.leaveTick >= 0)
                    {
                        tick = player.leaveTick;
                    }
                }
                return tick;
            }
        }

        /// <summary>
        /// The recording has the player of <paramref name="slot"/> join on <paramref name="joinTick"/> and leave on the next
        /// tick, a simulation of it has the player joined on the one tick, without input or commands, and leaving on the
        /// other, and re-simulating it reproduces the recorded checksums.
        /// </summary>
        private static void AssertJoinedThenLeft(byte[] recording, int slot, int joinTick)
        {
            using (var replay = LockstepReplay.Read(recording))
            {
                var found = false;
                foreach (var player in replay.Players)
                {
                    if (player.slot == slot && player.joinTick == joinTick)
                    {
                        Assert.AreEqual(joinTick + 1, player.leaveTick, "the leave comes on the tick after the join");
                        found = true;
                    }
                }
                Assert.IsTrue(found, $"slot {slot} joins on tick {joinTick}");

                var options = TestUtility.Options(typeof(TestGameplaySystem));
                using (var simulation = new LockstepSimulation(replay.Config, options))
                {
                    var entityManager = simulation.World.EntityManager;
                    while (simulation.Tick <= joinTick)
                    {
                        StepRecorded(simulation, replay);
                    }
                    var entity = TestUtility.PlayerEntity(simulation, slot);
                    Assert.AreNotEqual(Entity.Null, entity, "the player is in the simulation on its join tick");
                    Assert.IsTrue(entityManager.IsComponentEnabled<LockstepPlayerJoined>(entity));
                    Assert.IsFalse(entityManager.IsComponentEnabled<LockstepPlayerLeft>(entity));
                    Assert.AreEqual(default(TestInput), entityManager.GetComponentData<LockstepPlayerInput>(entity).Get<TestInput>(),
                        "the input of a player that left goes with it, also on its join tick");
                    Assert.AreEqual(0, entityManager.GetBuffer<LockstepCommand>(entity).Length, "and so do its commands");

                    StepRecorded(simulation, replay);
                    Assert.AreEqual(entity, TestUtility.PlayerEntity(simulation, slot));
                    Assert.IsFalse(entityManager.IsComponentEnabled<LockstepPlayerJoined>(entity));
                    Assert.IsTrue(entityManager.IsComponentEnabled<LockstepPlayerLeft>(entity), "and leaving on the next one");

                    StepRecorded(simulation, replay);
                    Assert.AreEqual(Entity.Null, TestUtility.PlayerEntity(simulation, slot), "then it is gone");
                }

                using (var replayPlayer = new LockstepReplayPlayer(replay, options))
                {
                    Assert.AreEqual(-1, replayPlayer.SimulateToEnd(), "the recording reproduces its checksums");
                }
            }
        }

        private static unsafe void StepRecorded(LockstepSimulation simulation, LockstepReplay replay)
        {
            Assert.IsTrue(replay.Frames.TryGet(simulation.Tick, out var frame, out var length), $"frame {simulation.Tick} was recorded");
            simulation.Step(frame, length);
        }

        /// <summary>Every frame the client received is the frame the server closed.</summary>
        private static unsafe void AssertFramesOfTheServer(LockstepServer server, LockstepClient client)
        {
            using (var replay = LockstepReplay.Read(client.ExportReplay()))
            {
                Assert.Greater(replay.FrameCount, 0);
                for (var tick = 0; tick < replay.FrameCount; tick++)
                {
                    replay.Frames.TryGet(tick, out var frame, out var length);
                    Assert.IsTrue(server.History.TryGet(tick, out var expected, out var expectedLength));
                    Assert.AreEqual(expectedLength, length, $"length of frame {tick}");
                    Assert.AreEqual(0, UnsafeUtility.MemCmp(frame, expected, length), $"frame {tick}");
                }
            }
        }
    }
}
