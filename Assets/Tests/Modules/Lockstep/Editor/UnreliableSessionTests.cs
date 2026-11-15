using System.Diagnostics;
using NUnit.Framework;
using Pragma.Dots.Lockstep;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Dots.Tests
{
    /// <summary>Sessions whose every side talks through a redundant channel over a network that loses and reorders datagrams.</summary>
    public class UnreliableSessionTests
    {
        private static LockstepServerSettings Settings(int minPlayers)
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.MaxPlayers = 4;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 4321;
            settings.MinPlayersToStart = minPlayers;
            settings.StartDelaySeconds = 0.3f;
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
        public void Clients_StayInSync_AndGetEveryCommandOnce_OverALossyNetwork()
        {
            using (var session = new SessionHarness(Settings(3), latency: 0.06, jitter: 0.04, redundant: true, loss: 0.05))
            {
                var clients = new[] { session.AddClient(), session.AddClient(), session.AddClient() };
                var sent = new int[LockstepProtocol.MAX_PLAYERS];
                session.Run(15, (client, frame) =>
                {
                    VaryingInput(client, frame);
                    if (client.State == LockstepClientState.Running && frame % 17 == client.LocalSlot)
                    {
                        client.AddCommand(new TestCommand { value = 1 });
                        sent[client.LocalSlot]++;
                    }
                });
                // The last commands arrive.
                session.Run(3, VaryingInput);

                Assert.IsEmpty(session.Desyncs);
                Assert.Greater(session.Server.VerifiedChecksumCount, 30);
                foreach (var client in clients)
                {
                    Assert.AreEqual(LockstepClientState.Running, client.State);
                    Assert.IsFalse(client.IsDesynced);
                    TestUtility.AssertSameChecksums(clients[0], client, 30);
                    foreach (var other in clients)
                    {
                        Assert.Greater(sent[other.LocalSlot], 10);
                        Assert.AreEqual(sent[other.LocalSlot], TestUtility.PlayerState(client.Simulation, other.LocalSlot).commandSum,
                            "every command of every player arrived once");
                    }
                }
                Assert.Greater(session.ServerChannel.DatagramsReceived, 0);
            }
        }

        private struct Measurement
        {
            public int stalls;
            public double inputLeadTicks;
            public double bytesPerSecond;
            public double packetsPerSecond;
            public double playoutDelayTicks;
            public double extraMarginTicks;
            public double stalledShare;
            public double longestStallSeconds;

            public override string ToString() =>
                $"stalls {stalls} ({stalledShare:P1} of the time, longest {longestStallSeconds * 1000:F0} ms), input lead {inputLeadTicks:F1} ticks, playout delay {playoutDelayTicks:F1}, extra margin {extraMarginTicks:F2}, " +
                $"{bytesPerSecond / 1024:F1} KB/s and {packetsPerSecond:F0} packets/s both ways";
        }

        // Two players over 40 seconds of a network that loses datagrams, after a warm-up: how often the picture stood still,
        // how far ahead orders are placed and what the traffic was.
        private static Measurement Measure(double loss, int copies)
        {
            var channel = LockstepRedundantChannelSettings.Default;
            channel.RedundantCopies = copies;
            using (var session = new SessionHarness(Settings(2), latency: 0.07, jitter: 0.02, seed: 11, redundant: true, loss: loss, channelSettings: channel))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                session.Run(5, VaryingInput);
                var stalls = a.StallCount + b.StallCount;
                var packets = session.Network.PacketsSent;
                var bytes = session.Network.BytesSent;
                var lead = 0.0;
                var delay = 0.0;
                var margin = 0.0;
                var stalledBefore = a.StalledSeconds;
                var stall = 0.0;
                var longest = 0.0;
                const int steps = 40 * 60;
                for (var step = 0; step < steps; step++)
                {
                    var stalled = a.StalledSeconds;
                    session.Step(1 / 60.0, VaryingInput);
                    lead += a.LastSentInputTick - a.SimulatedTicks;
                    delay += a.TargetPlayoutDelayTicks;
                    margin += a.ExtraInputMarginTicks;
                    stall = a.StalledSeconds > stalled ? stall + a.StalledSeconds - stalled : 0;
                    longest = System.Math.Max(longest, stall);
                }
                Assert.IsEmpty(session.Desyncs);
                return new Measurement
                {
                    stalls = a.StallCount + b.StallCount - stalls,
                    inputLeadTicks = lead / steps,
                    playoutDelayTicks = delay / steps,
                    extraMarginTicks = margin / steps,
                    stalledShare = (a.StalledSeconds - stalledBefore) / 40.0,
                    longestStallSeconds = longest,
                    bytesPerSecond = (session.Network.BytesSent - bytes) / 40.0,
                    packetsPerSecond = (session.Network.PacketsSent - packets) / 40.0,
                };
            }
        }

        [Test]
        public void Redundancy_StallsFarLessThanRetransmission()
        {
            var lossless = Measure(0, 3);
            TestContext.WriteLine($"No loss, 140 ms round trip: {lossless}.");
            Assert.AreEqual(0, lossless.stalls);
            foreach (var loss in new[] { 0.01, 0.05 })
            {
                // One copy: a lost datagram waits for a retransmission, as on a reliable pipeline.
                var retransmission = Measure(loss, 1);
                var redundancy = Measure(loss, 3);
                TestContext.WriteLine($"{loss:P0} loss, 140 ms round trip. Retransmission: {retransmission}. Redundancy: {redundancy}.");

                Assert.Greater(retransmission.longestStallSeconds, 0.1, "a retransmission stands the picture still for a round trip and more");
                Assert.Less(redundancy.longestStallSeconds, 0.05, "the next datagram brings what was lost");
                Assert.Less(redundancy.stalledShare * 5, retransmission.stalledShare);
                Assert.Less(redundancy.inputLeadTicks, retransmission.inputLeadTicks - 3, "orders are placed fewer ticks ahead");
                Assert.Less(redundancy.inputLeadTicks, lossless.inputLeadTicks + 2);
            }
        }

        [Test]
        public void ALateJoiner_CatchesUp_OverALossyNetwork()
        {
            using (var session = new SessionHarness(Settings(2), latency: 0.05, jitter: 0.03, redundant: true, loss: 0.05))
            {
                var first = session.AddClient();
                var second = session.AddClient();
                session.Run(10, VaryingInput);
                var late = session.AddClient();
                session.Run(10, VaryingInput);

                Assert.AreEqual(LockstepClientState.Running, late.State);
                Assert.Greater(late.JoinTick, 200);
                Assert.That(first.SimulatedTicks - late.SimulatedTicks, Is.InRange(-4, 4), "the late joiner caught up");
                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(first, late, 30);
                TestUtility.AssertSameChecksums(first, second, 30);
            }
        }

        [Test]
        public void ALargeCommand_ArrivesOnceAtEveryClient_OverALossyNetwork()
        {
            using (var session = new SessionHarness(Settings(2), latency: 0.05, jitter: 0.02, redundant: true, loss: 0.05))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                session.Run(3, VaryingInput);
                Assert.AreEqual(LockstepClientState.Running, a.State);

                const int count = 64 * 1024;
                var sum = 0L;
                var values = new int[count];
                for (var i = 0; i < count; i++)
                {
                    values[i] = i * 3;
                    sum += values[i];
                }
                using (var data = new NativeArray<int>(values, Allocator.Temp))
                {
                    a.AddCommand(new TestCommand { value = 1 }, data);
                }

                var timer = Stopwatch.StartNew();
                while ((TestUtility.PlayerState(a.Simulation, a.LocalSlot).commandDataCount != count ||
                        TestUtility.PlayerState(b.Simulation, a.LocalSlot).commandDataCount != count) && timer.Elapsed.TotalSeconds < 30)
                {
                    session.Run(1, VaryingInput);
                }

                foreach (var client in new[] { a, b })
                {
                    var state = TestUtility.PlayerState(client.Simulation, a.LocalSlot);
                    Assert.AreEqual(count, state.commandDataCount);
                    Assert.AreEqual(sum, state.commandDataSum);
                    Assert.AreEqual(1, state.commandSum);
                }
                Assert.IsEmpty(session.Desyncs);
            }
        }
    }
}
