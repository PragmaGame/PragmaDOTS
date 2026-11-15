using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pragma.Dots.Lockstep;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pragma.Dots.Tests
{
    /// <summary>Timeouts, stalls, the input margin and the garbage of a running session.</summary>
    public class SessionTimingTests
    {
        private static LockstepServerSettings Settings(int minPlayers)
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.MaxPlayers = 4;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 99;
            settings.MinPlayersToStart = minPlayers;
            settings.StartDelaySeconds = 0.3f;
            settings.ChecksumInterval = 10;
            return settings;
        }

        private static void ConstantInput(LockstepClient client, int frame)
        {
            client.SetInput(new TestInput { moveX = 1, moveY = client.LocalSlot });
        }

        [Test]
        public void ASilentPlayer_TimesOut_AndItsLeaveReachesTheOthers()
        {
            var settings = Settings(2);
            settings.PlayerTimeoutSeconds = 2;
            using (var session = new SessionHarness(settings, latency: 0.03))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                session.Run(2, ConstantInput);
                var timedOut = -1;
                session.Server.ConnectionTimedOutEvent += id => timedOut = id;

                session.Freeze(b);
                session.Run(1.5, ConstantInput);
                Assert.AreEqual(-1, timedOut, "not yet");
                Assert.AreEqual(2, session.Server.PlayerCount);

                LogAssert.Expect(LogType.Warning, new Regex("sent nothing for 2 s"));
                session.Run(1.5, ConstantInput);
                Assert.AreEqual(2, timedOut, "the second connection timed out");
                Assert.AreEqual(1, session.Server.PlayerCount);
                Assert.AreEqual(1, TestUtility.Count<LockstepPlayer>(a.Simulation.World.EntityManager), "the leave was announced");
            }
        }

        [Test]
        public void AServerThatStoodStill_TimesNobodyOut()
        {
            var settings = Settings(2);
            settings.PlayerTimeoutSeconds = 2;
            using (var session = new SessionHarness(settings, latency: 0.03))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                session.Run(2, ConstantInput);
                var timedOut = 0;
                session.Server.ConnectionTimedOutEvent += _ => timedOut++;

                // The host's app was paused for five seconds while the remote clients went on.
                session.StepClientsOnly(5);
                session.Run(3, ConstantInput);

                Assert.AreEqual(0, timedOut);
                Assert.AreEqual(2, session.Server.PlayerCount);
                Assert.AreEqual(LockstepClientState.Running, a.State);
                Assert.AreEqual(LockstepClientState.Running, b.State);
                Assert.AreEqual(2, TestUtility.Count<LockstepPlayer>(a.Simulation.World.EntityManager));
                Assert.IsEmpty(session.Desyncs);
            }
        }

        [Test]
        public void AStall_RaisesThePlayoutDelay_WhichDecaysAfterwards()
        {
            using (var session = new SessionHarness(Settings(1), latency: 0.04))
            {
                var client = session.AddClient();
                session.Run(4, ConstantInput);
                var stalls = client.StallCount;
                var delay = client.TargetPlayoutDelayTicks;

                // No frames for half a second: the picture stands still once.
                session.StepClientsOnly(0.5);
                session.Run(0.3, ConstantInput);
                Assert.Greater(client.StallCount, stalls);
                Assert.Greater(client.TargetPlayoutDelayTicks, delay + 0.5f);

                session.Run(15, ConstantInput);
                Assert.Less(client.TargetPlayoutDelayTicks, delay + 0.3f, "the delay decays while frames come in time");
            }
        }

        [Test]
        public void AStall_LeavesTheDelayAlone_WithoutStallDelay()
        {
            using (var session = new SessionHarness(Settings(1), latency: 0.04))
            {
                var settings = SessionHarness.ClientSettings;
                settings.MaxStallDelayTicks = 0;
                var client = session.AddClient(settings: settings);
                session.Run(4, ConstantInput);
                var delay = client.TargetPlayoutDelayTicks;

                session.StepClientsOnly(0.5);
                session.Run(0.3, ConstantInput);
                Assert.Greater(client.StallCount, 0);
                Assert.Less(client.TargetPlayoutDelayTicks, delay + 0.3f);
            }
        }

        [Test]
        public void LateInputs_RaiseTheInputMargin_WithinItsCap_AndItDecays()
        {
            using (var session = new SessionHarness(Settings(1), latency: 0.1))
            {
                var client = session.AddClient();
                session.Run(4, ConstantInput);

                var highest = 0f;
                for (var freeze = 0; freeze < 8; freeze++)
                {
                    // The client stands still for a moment: the inputs it sends afterwards come too late.
                    session.StepServerOnly(0.3);
                    for (var step = 0; step < 30; step++)
                    {
                        session.Step(1 / 60.0, ConstantInput);
                        highest = Math.Max(highest, client.ExtraInputMarginTicks);
                    }
                }
                Assert.Greater(session.Server.LateInputCount, 0);
                Assert.GreaterOrEqual(highest, 1f, "late inputs raised the margin");
                Assert.LessOrEqual(highest, LockstepClientSettings.Default.MaxExtraInputMarginTicks, "but no further than the cap");

                // The input clock takes a few seconds to settle after the last freeze; the margin is gone a few after.
                session.Run(10, ConstantInput);
                Assert.AreEqual(0f, client.ExtraInputMarginTicks, "and it decayed once inputs came in time again");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ASteadySession_AllocatesNothing(bool redundant)
        {
            using (var session = new SessionHarness(Settings(2), latency: 0.03, redundant: redundant))
            {
                session.AddClient();
                session.AddClient();
                Action<LockstepClient, int> input = ConstantInput;
                for (var step = 0; step < 600; step++)
                {
                    session.Step(1 / 60.0, input);
                }

                // Collections that keep the whole match (checksums, frames) grow now and then; nothing else allocates.
                var allocatingSteps = 0;
                for (var step = 0; step < 600; step++)
                {
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    session.Step(1 / 60.0, input);
                    if (GC.GetAllocatedBytesForCurrentThread() != before)
                    {
                        allocatingSteps++;
                    }
                }
                Assert.LessOrEqual(allocatingSteps, 4);
                Assert.IsEmpty(session.Desyncs);
                Assert.Greater(session.Server.VerifiedChecksumCount, 50);
            }
        }
    }
}
