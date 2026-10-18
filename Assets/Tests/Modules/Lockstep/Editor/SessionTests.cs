using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pragma.Dots.Lockstep;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pragma.Dots.Tests
{
    public class SessionTests
    {
        private static LockstepServerSettings Settings(int minPlayers = 2, int tickRate = 30)
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = tickRate;
            settings.MaxPlayers = 4;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 1234;
            settings.MinPlayersToStart = minPlayers;
            settings.StartDelaySeconds = 0.3f;
            settings.ChecksumInterval = 10;
            return settings;
        }

        // Every client presses different keys over time, so the players really diverge.
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
        public void TwoClients_StayInSyncThroughLatencyAndJitter()
        {
            using (var session = new SessionHarness(Settings(), latency: 0.05, jitter: 0.04))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                session.Run(10, VaryingInput);

                Assert.AreEqual(LockstepClientState.Running, a.State);
                Assert.AreEqual(LockstepClientState.Running, b.State);
                Assert.Greater(a.SimulatedTicks, 250);
                Assert.Greater(b.SimulatedTicks, 250);
                Assert.IsEmpty(session.Desyncs);
                Assert.IsFalse(a.IsDesynced || b.IsDesynced);
                Assert.Greater(session.Server.VerifiedChecksumCount, 20);
                TestUtility.AssertSameChecksums(a, b, 20);

                var stateA = TestUtility.PlayerState(a.Simulation, b.LocalSlot);
                Assert.Greater(stateA.movingTicks, 0, "inputs of the other player reached this client");
                Assert.Greater(stateA.jumps, 0);
            }
        }

        [Test]
        public void InputClock_ConvergesSoInputsArriveInTime()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 1), latency: 0.15, jitter: 0.02))
            {
                var client = session.AddClient();
                session.Run(6, VaryingInput);
                var lateAfterWarmup = session.Server.LateInputCount;
                session.Run(6, VaryingInput);

                Assert.AreEqual(lateAfterWarmup, session.Server.LateInputCount, "no input should be late once the clock has converged");
                var lead = client.LastSentInputTick - session.Server.ClosedTicks;
                Assert.That(lead, Is.InRange(4, 12), "inputs are sent about one-way latency plus margin ahead");
                Assert.That(client.BufferedTicks, Is.InRange(0, 6));
            }
        }

        [Test]
        public void InputClock_WaitsOutTheStartCountdown()
        {
            var settings = Settings(minPlayers: 1);
            settings.StartDelaySeconds = 0.6f;
            using (var session = new SessionHarness(settings, latency: 0.05, jitter: 0.01))
            {
                var client = session.AddClient();
                var maxLead = 0;
                for (var i = 0; i < 150; i++)
                {
                    session.Step(0.01, VaryingInput);
                    if (client.State == LockstepClientState.Running)
                    {
                        maxLead = System.Math.Max(maxLead, client.LastSentInputTick - session.Server.ClosedTicks);
                    }
                }

                Assert.AreEqual(LockstepClientState.Running, client.State);
                Assert.Greater(session.Server.ClosedTicks, 10, "the match has started");
                // One-way latency plus the margin; a clock started at the join tick would run the countdown ahead (18 ticks).
                Assert.That(maxLead, Is.LessThanOrEqualTo(8), "inputs go out for the ticks the server is about to close");
            }
        }

        [Test]
        public void LateJoiner_ReplaysTheMatchAndMatchesTheOthers()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 1), latency: 0.03, jitter: 0.02))
            {
                var first = session.AddClient();
                session.Run(4, VaryingInput);
                var late = session.AddClient();
                session.Run(4, VaryingInput);

                Assert.AreEqual(LockstepClientState.Running, late.State);
                Assert.Greater(late.JoinTick, 100);
                Assert.That(first.SimulatedTicks - late.SimulatedTicks, Is.InRange(-3, 3), "the late joiner caught up");
                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(first, late, 20);
            }
        }

        [Test]
        public void StartData_ReachesTheSimulation()
        {
            var settings = Settings(minPlayers: 1);
            settings.StartData = LockstepBytes.ToFixedList128(new TestCommand { value = 42 });
            using (var session = new SessionHarness(settings, latency: 0.02))
            {
                var client = session.AddClient();
                session.Run(1, VaryingInput);

                Assert.AreEqual(LockstepClientState.Running, client.State);
                Assert.AreEqual(settings.StartData.Length, client.Config.StartData.Length);
                using (var query = client.Simulation.World.EntityManager.CreateEntityQuery(typeof(LockstepSessionInfo)))
                {
                    Assert.AreEqual(42, query.GetSingleton<LockstepSessionInfo>().GetStartData<TestCommand>().value);
                }
            }
        }

        [Test]
        public void Disconnect_IsAnnouncedAsLeave()
        {
            using (var session = new SessionHarness(Settings(), latency: 0.02))
            {
                var stays = session.AddClient();
                var leaves = session.AddClient();
                session.Run(2, VaryingInput);
                var leftSlot = leaves.LocalSlot;
                Assert.AreEqual(2, TestUtility.Count<LockstepPlayer>(stays.Simulation.World.EntityManager));

                session.Disconnect(leaves);
                session.Run(1, VaryingInput);

                Assert.AreEqual(1, TestUtility.Count<LockstepPlayer>(stays.Simulation.World.EntityManager));
                Assert.AreEqual(Unity.Entities.Entity.Null, TestUtility.PlayerEntity(stays.Simulation, leftSlot));
                Assert.AreEqual(1, session.Server.PlayerCount);
            }
        }

        [Test]
        public void Commands_AreDeliveredExactlyOnce()
        {
            using (var session = new SessionHarness(Settings(), latency: 0.06, jitter: 0.05))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                var sent = 0;
                session.Run(6, (client, frame) =>
                {
                    VaryingInput(client, frame);
                    if (client == a && frame % 37 == 0 && client.State == LockstepClientState.Running)
                    {
                        client.AddCommand(new TestCommand { value = frame });
                        sent += frame;
                    }
                });
                session.Run(2, VaryingInput);

                Assert.Greater(sent, 0);
                Assert.AreEqual(sent, TestUtility.PlayerState(a.Simulation, a.LocalSlot).commandSum);
                Assert.AreEqual(sent, TestUtility.PlayerState(b.Simulation, a.LocalSlot).commandSum);
            }
        }

        [Test]
        public void CommandsWithLargeData_ArriveWholeOnEveryClient()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 1), latency: 0.06, jitter: 0.05))
            {
                var a = session.AddClient();
                session.Run(1, VaryingInput);
                var b = session.AddClient();
                var sentCount = 0;
                var sentSum = 0L;
                session.Run(4, (client, frame) =>
                {
                    VaryingInput(client, frame);
                    if (client != a || frame % 41 != 0 || client.State != LockstepClientState.Running)
                    {
                        return;
                    }
                    // From nothing up to 64 KB: far beyond one packet, fragmented and reassembled on the way.
                    var values = new int[frame * 977 % 16384];
                    for (var i = 0; i < values.Length; i++)
                    {
                        values[i] = frame * 31 + i;
                        sentSum += values[i];
                    }
                    using (var data = new NativeArray<int>(values, Allocator.Temp))
                    {
                        client.AddCommand(new TestCommand { value = 1 }, data);
                    }
                    sentCount += values.Length;
                });
                session.Run(2, VaryingInput);
                // A late joiner gets every command again from the server's history.
                var late = session.AddClient();
                session.Run(4, VaryingInput);

                Assert.Greater(sentCount, 20000);
                Assert.IsEmpty(session.Desyncs);
                var expected = TestUtility.PlayerState(a.Simulation, a.LocalSlot);
                Assert.AreEqual(sentCount, expected.commandDataCount);
                Assert.AreEqual(sentSum, expected.commandDataSum);
                foreach (var client in new[] { b, late })
                {
                    var state = TestUtility.PlayerState(client.Simulation, a.LocalSlot);
                    Assert.AreEqual(expected.commandDataCount, state.commandDataCount);
                    Assert.AreEqual(expected.commandDataSum, state.commandDataSum);
                    Assert.AreEqual(expected.commandDataHash, state.commandDataHash, "the elements arrive in order");
                }
                TestUtility.AssertSameChecksums(a, b, 20);
                TestUtility.AssertSameChecksums(a, late, 20);
            }
        }

        [Test]
        public void TruncatedCommandData_IsAProtocolViolation()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 1)))
            {
                var client = session.AddClient();
                session.Run(1, VaryingInput);
                var violations = new List<string>();
                session.Server.ProtocolViolationEvent += (_, reason) => violations.Add(reason);

                // An input message whose only command announces 1000 bytes of data and carries 4.
                using (var packet = new NativeList<byte>(64, Allocator.Temp))
                {
                    var writer = new LockstepByteWriter(packet);
                    writer.WriteByte(0); // framer header: a complete message
                    writer.WriteByte((byte)LockstepMessageType.Input);
                    writer.WriteInt(session.Server.ClosedTicks + 2);
                    writer.WriteByte(1);
                    writer.WriteByte((byte)(LockstepInputTickFlags.Repeat | LockstepInputTickFlags.Commands));
                    writer.WriteByte(1);
                    writer.WriteInt(LockstepCommand.TypeHashOf<TestCommand>());
                    writer.WriteByte(4);
                    writer.WriteInt(1000);
                    writer.WriteVarUInt(1000);
                    writer.WriteInt(6);
                    LogAssert.Expect(LogType.Warning, new Regex("sent invalid data"));
                    unsafe
                    {
                        session.Server.OnPacket(1, packet.GetUnsafePtr(), packet.Length, session.Time);
                    }
                }
                session.Run(1, VaryingInput);

                Assert.AreEqual(1, violations.Count);
                Assert.AreEqual(LockstepClientState.Running, client.State);
                Assert.AreEqual(0, TestUtility.PlayerState(client.Simulation, client.LocalSlot).commandSum, "the broken command never reached a frame");
            }
        }

        [Test]
        public void CommandData_OverTheLimit_IsAProtocolViolation()
        {
            var settings = Settings(minPlayers: 1);
            settings.MaxCommandDataSize = 1024;
            using (var session = new SessionHarness(settings, latency: 0.03, jitter: 0.02))
            {
                var client = session.AddClient();
                session.Run(1, VaryingInput);
                var violations = new List<string>();
                session.Server.ProtocolViolationEvent += (_, reason) => violations.Add(reason);

                LogAssert.Expect(LogType.Warning, new Regex("sent invalid data: a command with 2000 bytes of data"));
                using (var large = new NativeArray<byte>(2000, Allocator.Temp))
                {
                    client.AddCommand(new TestCommand { value = 1 }, large);
                }
                session.Run(1, VaryingInput);
                using (var small = new NativeArray<byte>(800, Allocator.Temp))
                {
                    client.AddCommand(new TestCommand { value = 2 }, small);
                }
                session.Run(1, VaryingInput);

                Assert.AreEqual(1, violations.Count);
                var state = TestUtility.PlayerState(client.Simulation, client.LocalSlot);
                Assert.AreEqual(2, state.commandSum, "only the command within the limit reached a frame");
                Assert.AreEqual(LockstepClientState.Running, client.State);
            }
        }

        [Test]
        public void Commands_OverTheByteBudgetOfATick_GoIntoTheNextFrames()
        {
            var settings = Settings(minPlayers: 1);
            settings.MaxCommandBytesPerTick = 1000;
            using (var session = new SessionHarness(settings, latency: 0.03, jitter: 0.02))
            {
                var client = session.AddClient();
                session.Run(1, VaryingInput);
                for (var i = 0; i < 3; i++)
                {
                    using (var data = new NativeArray<int>(150, Allocator.Temp))
                    {
                        client.AddCommand(new TestCommand { value = 1 }, data);
                    }
                }
                session.Run(1, VaryingInput);

                var state = TestUtility.PlayerState(client.Simulation, client.LocalSlot);
                Assert.AreEqual(3, state.commandSum, "every command arrives");
                Assert.AreEqual(450, state.commandDataCount);
                var history = session.Server.History;
                for (var tick = history.FirstTick; tick < history.EndTick; tick++)
                {
                    unsafe
                    {
                        history.TryGet(tick, out _, out var length);
                        Assert.Less(length, 1300, $"tick {tick} carries at most two of the 600-byte commands");
                    }
                }
            }
        }

        [Test]
        public void BufferedCommands_OverTheLimit_AreAProtocolViolation()
        {
            var settings = Settings(minPlayers: 1);
            settings.MaxCommandDataSize = 1024;
            settings.MaxBufferedCommandBytes = 4096;
            using (var session = new SessionHarness(settings, latency: 0.03, jitter: 0.02))
            {
                var client = session.AddClient();
                session.Run(1, VaryingInput);
                var violations = new List<string>();
                session.Server.ProtocolViolationEvent += (_, reason) => violations.Add(reason);

                LogAssert.Expect(LogType.Warning, new Regex("bytes of commands waiting"));
                for (var i = 0; i < 10; i++)
                {
                    using (var data = new NativeArray<byte>(1000, Allocator.Temp))
                    {
                        client.AddCommand(new TestCommand { value = 1 }, data);
                    }
                }
                session.Run(1, VaryingInput);

                Assert.AreEqual(1, violations.Count);
                Assert.AreEqual(0, TestUtility.PlayerState(client.Simulation, client.LocalSlot).commandSum, "the whole batch is dropped");
                Assert.AreEqual(LockstepClientState.Running, client.State);
            }
        }

        [Test]
        public void Checksum_OfATickOffTheInterval_IsIgnored()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 1)))
            {
                var client = session.AddClient();
                session.Run(1, VaryingInput);
                using (var packet = new NativeList<byte>(32, Allocator.Temp))
                {
                    var writer = new LockstepByteWriter(packet);
                    writer.WriteByte(0); // framer header: a complete message
                    writer.WriteByte((byte)LockstepMessageType.Checksum);
                    writer.WriteInt(3);
                    writer.WriteULong(12345);
                    unsafe
                    {
                        session.Server.OnPacket(1, packet.GetUnsafePtr(), packet.Length, session.Time);
                    }
                }
                // Long enough for a round nobody completes to time out.
                session.Run(6, VaryingInput);

                using (var replay = LockstepReplay.Read(session.Server.ExportReplay()))
                {
                    Assert.IsFalse(replay.Checksums.ContainsKey(3));
                    foreach (var tick in replay.Checksums.Keys)
                    {
                        Assert.AreEqual(0, tick % 10, $"tick {tick}");
                    }
                }
                Assert.IsEmpty(session.Desyncs);
                Assert.IsFalse(client.IsDesynced);
            }
        }

        [Test]
        public void Start_ArrivingAfterLeave_DoesNotStartTheClient()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 2), latency: 0.1))
            {
                var leaver = session.AddClient();
                session.Run(0.5, VaryingInput);
                Assert.AreEqual(LockstepClientState.Lobby, leaver.State);

                // The second join starts the match; the start is on its way while the first player leaves.
                session.AddClient();
                while (session.Server.State != LockstepServerState.Running)
                {
                    session.Step(0.01, VaryingInput);
                }
                leaver.Leave();
                session.Run(1, VaryingInput);

                Assert.AreEqual(LockstepClientState.Ended, leaver.State);
                Assert.IsNull(leaver.Simulation, "no simulation without frames");
            }
        }

        [Test]
        public void ThinClient_WithoutReplay_KeepsNoFrames()
        {
            var thin = SessionHarness.ClientSettings;
            thin.Simulate = false;
            thin.RecordReplay = false;
            using (var session = new SessionHarness(Settings(minPlayers: 1), latency: 0.03))
            {
                var client = session.AddClient(settings: thin);
                session.Run(2, VaryingInput);

                Assert.AreEqual(LockstepClientState.Running, client.State);
                Assert.Greater(client.ConfirmedTicks, 40);
                Assert.Throws<System.InvalidOperationException>(() => client.ExportReplay(), "the frames were dropped");
            }
        }

        [Test]
        public void LateJoiner_DoesNotHoldTheFrames_UnderMaxInputWait()
        {
            var settings = Settings(minPlayers: 1);
            settings.MaxInputWaitTicks = 10;
            using (var session = new SessionHarness(settings, latency: 0.1, jitter: 0.01))
            {
                session.AddClient();
                session.Run(2, VaryingInput);
                session.AddClient();
                var maxLag = 0.0;
                for (var i = 0; i < 100; i++)
                {
                    session.Step(0.01, VaryingInput);
                    maxLag = System.Math.Max(maxLag, session.Server.GetTickPosition(session.Time) - session.Server.ClosedTicks);
                }

                Assert.Less(maxLag, 3, "the frames close on time while the joiner's first inputs travel");
            }
        }

        [Test]
        public void LostConnection_EndsTheSession_AndTheClientFinishesItsFrames()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 1), latency: 0.05))
            {
                var client = session.AddClient();
                var ended = 0;
                client.EndedEvent += _ => ended++;
                session.Run(2, VaryingInput);
                Assert.Greater(client.BufferedTicks + client.SimulatedTicks, 0);

                session.Network.DetachClient(1);
                client.OnDisconnected();
                Assert.AreEqual(LockstepClientState.Ended, client.State);
                Assert.AreEqual(LockstepEndReason.ConnectionLost, client.EndReason);
                Assert.AreEqual(1, ended);

                session.Run(1, VaryingInput);
                Assert.AreEqual(client.ConfirmedTicks, client.SimulatedTicks, "the frames it had are simulated");
            }
        }

        [Test]
        public void Settings_ThatCannotRun_AreRefused()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => default(LockstepClientSettings).Validate(), "settings never filled in");
            Assert.DoesNotThrow(() => LockstepClientSettings.Default.Validate());

            var server = LockstepServerSettings.Default;
            server.MaxCommandDataSize = 8 * 1024 * 1024;
            Assert.Throws<System.ArgumentOutOfRangeException>(() => server.Validate(), "a command larger than a message");
            server = LockstepServerSettings.Default;
            server.MaxSendBytesPerUpdate = 0;
            Assert.AreEqual(32 * 1024, server.EffectiveMaxSendBytesPerUpdate, "zero takes the default");
        }

        [Test]
        public void Desync_IsDetectedAndAttributedToTheOddClient()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 3), latency: 0.02))
            {
                var a = session.AddClient();
                session.AddClient();
                var odd = session.AddClient(TestUtility.Options(typeof(TestGameplaySystem), typeof(TestDesyncSystem)));
                var events = new List<(int Tick, bool Local)>();
                odd.DesyncedEvent += (tick, local) => events.Add((tick, local));
                a.DesyncedEvent += (tick, local) => events.Add((tick, local));

                // Every client logs an error per desync report; that is the expected outcome here.
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    session.Run(2.5, VaryingInput);
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = false;
                }

                Assert.AreEqual(1, session.Desyncs.Count, "reported once, not again on every later round");
                Assert.AreEqual(1UL << odd.LocalSlot, session.Desyncs[0].slotMask);
                Assert.IsTrue(odd.IsDesynced);
                Assert.IsTrue(a.IsDesynced, "every client is told");
                Assert.Contains((session.Desyncs[0].tick, true), events);
                Assert.Contains((session.Desyncs[0].tick, false), events);
            }
        }

        [Test]
        public void EndGame_InTheLobby_TellsTheWaitingPlayersWhy()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 3), latency: 0.05, jitter: 0.04))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                var ended = new List<LockstepClient>();
                a.EndedEvent += ended.Add;
                b.EndedEvent += ended.Add;
                session.Run(1, VaryingInput);
                Assert.AreEqual(LockstepClientState.Lobby, a.State);

                session.Server.EndGame(LockstepEndReason.ServerShutdown);
                session.Run(1, VaryingInput);

                Assert.AreEqual(LockstepServerState.Ended, session.Server.State);
                CollectionAssert.AreEquivalent(new[] { a, b }, ended, "each waiting player hears it once");
                foreach (var client in new[] { a, b })
                {
                    Assert.AreEqual(LockstepClientState.Ended, client.State);
                    Assert.AreEqual(LockstepEndReason.ServerShutdown, client.EndReason);
                    Assert.IsNull(client.Simulation, "a match that never started has no simulation");
                }

                var late = session.AddClient();
                session.Run(1, VaryingInput);
                Assert.AreEqual(LockstepClientState.Rejected, late.State);
                Assert.AreEqual(LockstepJoinRejectReason.GameEnded, late.RejectReason);
            }
        }

        [Test]
        public void EndGame_DuringTheMatch_LetsEveryClientFinishTheClosedTicks()
        {
            using (var session = new SessionHarness(Settings(), latency: 0.05, jitter: 0.04))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                session.Run(3, VaryingInput);
                Assert.AreEqual(LockstepClientState.Running, a.State);

                session.Server.EndGame();
                var closed = session.Server.ClosedTicks;
                session.Run(2, VaryingInput);

                Assert.AreEqual(closed, session.Server.ClosedTicks, "no tick is closed after the end");
                foreach (var client in new[] { a, b })
                {
                    Assert.AreEqual(LockstepClientState.Ended, client.State);
                    Assert.AreEqual(LockstepEndReason.Finished, client.EndReason);
                    Assert.AreEqual(closed, client.SimulatedTicks, "the client simulated every closed tick");
                }
                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(a, b, 5);
            }
        }

        [Test]
        public void Signals_ReachTheNextTick_TheSameOnEveryClient()
        {
            using (var session = new SessionHarness(Settings(minPlayers: 1), latency: 0.05, jitter: 0.04))
            {
                var first = session.AddClient(SignalOptions());
                session.Run(3, VaryingInput);
                // Replays the match from tick 0, many ticks per frame: the signals must not depend on the pace.
                var late = session.AddClient(SignalOptions());
                session.Run(4, VaryingInput);

                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(first, late, 20);
                foreach (var client in new[] { first, late })
                {
                    var entityManager = client.Simulation.World.EntityManager;
                    using (var counters = entityManager.CreateEntityQuery(typeof(TestTickSignalCounter)))
                    using (var signals = entityManager.CreateEntityQuery(typeof(TestTickSignal)))
                    {
                        var counter = counters.GetSingleton<TestTickSignalCounter>();
                        Assert.Greater(counter.received, 100);
                        Assert.AreEqual(0, counter.stale, "every signal was seen on the tick after it was published, and only then");
                        Assert.AreEqual(counter.sent - counter.received, signals.CalculateEntityCount(), "only the last tick's signals are left");
                    }
                }
            }
        }

        private static LockstepSimulationOptions SignalOptions()
        {
            return TestUtility.Options(typeof(TestGameplaySystem), typeof(TestTickSignalSystem));
        }

        [Test]
        public void FullSession_RejectsExtraPlayers()
        {
            var settings = Settings(minPlayers: 1);
            settings.MaxPlayers = 1;
            using (var session = new SessionHarness(settings))
            {
                session.AddClient();
                LogAssert.Expect(LogType.Warning, new Regex("SessionFull"));
                var extra = session.AddClient();
                session.Run(0.5);
                Assert.AreEqual(LockstepClientState.Rejected, extra.State);
                Assert.AreEqual(LockstepJoinRejectReason.SessionFull, extra.RejectReason);
            }
        }

        [Test]
        public void LateJoin_WhenItIsOff_IsRejected()
        {
            var settings = Settings(minPlayers: 1);
            settings.AllowLateJoin = false;
            using (var session = new SessionHarness(settings, latency: 0.03))
            {
                session.AddClient();
                session.Run(1, VaryingInput);
                LogAssert.Expect(LogType.Warning, new Regex("GameInProgress"));
                var late = session.AddClient();
                session.Run(0.5, VaryingInput);
                Assert.AreEqual(LockstepClientState.Rejected, late.State);
                Assert.AreEqual(LockstepJoinRejectReason.GameInProgress, late.RejectReason);
            }
        }

        [Test]
        public void Join_FromAnotherProtocolOrBuild_IsRejected()
        {
            Assert.AreEqual(LockstepJoinRejectReason.ProtocolMismatch,
                JoinWith((ushort)(LockstepProtocol.VERSION + 1), LockstepSimulation.DefaultSimulationHash));
            Assert.AreEqual(LockstepJoinRejectReason.SimulationMismatch,
                JoinWith(LockstepProtocol.VERSION, LockstepSimulation.DefaultSimulationHash ^ 1UL));
        }

        // A join request with the given version and simulation hash, as an older or a different build sends it.
        private static LockstepJoinRejectReason JoinWith(ushort version, ulong simulationHash)
        {
            using (var session = new SessionHarness(Settings(minPlayers: 1)))
            using (var client = new LockstepClient(SessionHarness.ClientSettings, session.Network.CreateClientTransport(5)))
            {
                session.Network.AttachClient(5, client);
                using (var packet = new NativeList<byte>(32, Allocator.Temp))
                {
                    var writer = new LockstepByteWriter(packet);
                    writer.WriteByte(0); // framer header: a complete message
                    writer.WriteByte((byte)LockstepMessageType.JoinRequest);
                    writer.WriteUShort(version);
                    writer.WriteULong(simulationHash);
                    writer.WriteByte((byte)LockstepJoinFlags.Simulates);
                    writer.WriteByte(0);
                    unsafe
                    {
                        session.Server.OnPacket(5, packet.GetUnsafePtr(), packet.Length, session.Time);
                    }
                }
                LogAssert.Expect(LogType.Warning, new Regex("refused to join"));
                session.Run(0.2);
                Assert.AreEqual(LockstepClientState.Rejected, client.State);
                Assert.AreEqual(0, session.Server.PlayerCount);
                return client.RejectReason;
            }
        }

        [Test]
        public void Replay_ReproducesTheMatch()
        {
            using (var session = new SessionHarness(Settings(), latency: 0.04, jitter: 0.03))
            {
                var a = session.AddClient();
                session.AddClient();
                session.Run(5, VaryingInput);

                var bytes = a.ExportReplay();
                using (var replay = LockstepReplay.Read(bytes))
                using (var player = new LockstepReplayPlayer(replay, TestUtility.Options(typeof(TestGameplaySystem))))
                {
                    Assert.Greater(replay.Checksums.Count, 10);
                    Assert.AreEqual(-1, player.SimulateToEnd());
                    Assert.AreEqual(a.ConfirmedTicks, player.Simulation.Tick);
                }

                var serverReplay = session.Server.ExportReplay();
                using (var replay = LockstepReplay.Read(serverReplay))
                using (var player = new LockstepReplayPlayer(replay, TestUtility.Options(typeof(TestGameplaySystem))))
                {
                    Assert.AreEqual(-1, player.SimulateToEnd(), "server-side recordings verify against the agreed checksums");
                }
            }
        }

        [Test]
        public void MaxInputWait_HoldsFramesForSlowInputs()
        {
            var settings = Settings(minPlayers: 1);
            settings.MaxInputWaitTicks = 30;
            using (var session = new SessionHarness(settings, latency: 0.05))
            {
                session.AddClient();
                session.Run(3, VaryingInput);
                var closedBefore = session.Server.ClosedTicks;

                // The client freezes; without waiting the server would close 15 ticks in half a second.
                session.StepServerOnly(0.5);
                Assert.LessOrEqual(session.Server.ClosedTicks, closedBefore + 8, "frames are held while the input is missing");

                // After MaxInputWaitTicks the missing input is repeated and ticks close again, 30 ticks behind.
                session.StepServerOnly(1.5);
                Assert.GreaterOrEqual(session.Server.ClosedTicks, closedBefore + 25);
            }
        }
    }
}
