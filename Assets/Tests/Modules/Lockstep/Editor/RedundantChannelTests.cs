using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pragma.Dots.Lockstep;

namespace Pragma.Dots.Tests
{
    public unsafe class RedundantChannelTests
    {
        private static byte[] RandomPacket(Random random, int maxSize)
        {
            var size = random.Next(10) == 0 ? random.Next(maxSize + 1) : random.Next(1, 64);
            var packet = new byte[size];
            random.NextBytes(packet);
            return packet;
        }

        private static void AssertSamePackets(List<byte[]> sent, List<byte[]> received)
        {
            Assert.AreEqual(sent.Count, received.Count, "every packet arrives exactly once");
            for (var i = 0; i < sent.Count; i++)
            {
                CollectionAssert.AreEqual(sent[i], received[i], $"packet {i} arrives in order and intact");
            }
        }

        [Test]
        public void Packets_ArriveInOrderExactlyOnce_OverALossyLink()
        {
            using (var link = new DatagramLink(5) { Loss = 0.2, Duplication = 0.1, Jitter = 0.08 })
            {
                var random = new Random(9);
                var sentByA = new List<byte[]>();
                var sentByB = new List<byte[]>();
                for (var step = 0; step < 2000; step++)
                {
                    if (random.Next(3) == 0)
                    {
                        var packet = RandomPacket(random, link.A.MaxPacketSize);
                        sentByA.Add(packet);
                        link.SendFromA(packet);
                    }
                    if (random.Next(4) == 0)
                    {
                        var packet = RandomPacket(random, link.B.MaxPacketSize);
                        sentByB.Add(packet);
                        link.SendFromB(packet);
                    }
                    link.Step(0.016);
                }
                for (var step = 0; step < 3000 && (link.ReceivedByB.Count < sentByA.Count || link.ReceivedByA.Count < sentByB.Count); step++)
                {
                    link.Step(0.016);
                }

                AssertSamePackets(sentByA, link.ReceivedByB);
                AssertSamePackets(sentByB, link.ReceivedByA);
            }
        }

        [Test]
        public void ALostDatagram_IsReplacedByTheNextOne()
        {
            using (var link = new DatagramLink(1))
            {
                var first = new byte[] { 1, 2, 3 };
                var second = new byte[] { 4, 5 };
                link.DropNextFromA = true;
                link.SendFromA(first);
                link.Step(0.001);
                link.Step(0.033);
                Assert.IsEmpty(link.ReceivedByB, "the only datagram with the first packet was lost");

                // The next datagram carries the first packet again with the second one, long before any timeout.
                link.SendFromA(second);
                link.Step(0.001);
                link.Step(link.Latency);

                AssertSamePackets(new List<byte[]> { first, second }, link.ReceivedByB);
                Assert.AreEqual(0, link.A.PacketsResent, "no packet waited for a timeout");
            }
        }

        [Test]
        public void APacketWhoseCopiesAreAllLost_IsSentAgainAfterATimeout()
        {
            var settings = LockstepRedundantChannelSettings.Default;
            settings.RedundantCopies = 1;
            using (var link = new DatagramLink(1, settings))
            {
                var packet = new byte[] { 7 };
                link.DropNextFromA = true;
                link.SendFromA(packet);
                link.Step(0.001);
                link.Step(0.1);
                Assert.IsEmpty(link.ReceivedByB);

                for (var step = 0; step < 50 && link.ReceivedByB.Count == 0; step++)
                {
                    link.Step(0.02);
                }

                AssertSamePackets(new List<byte[]> { packet }, link.ReceivedByB);
                Assert.AreEqual(1, link.A.PacketsResent);
            }
        }

        [Test]
        public void ABurstLargerThanTheWindow_Arrives()
        {
            using (var link = new DatagramLink(3) { Loss = 0.1, Jitter = 0.02 })
            {
                var random = new Random(4);
                var sent = new List<byte[]>();
                for (var i = 0; i < 1200; i++)
                {
                    var packet = new byte[link.A.MaxPacketSize];
                    random.NextBytes(packet);
                    sent.Add(packet);
                    link.SendFromA(packet);
                }
                for (var step = 0; step < 5000 && link.ReceivedByB.Count < sent.Count; step++)
                {
                    link.Step(0.016);
                }
                // The last acknowledgements are still on their way.
                for (var step = 0; step < 60; step++)
                {
                    link.Step(0.016);
                }

                AssertSamePackets(sent, link.ReceivedByB);
                Assert.AreEqual(0, link.A.GetUnacknowledgedCount(DatagramLink.B_ID), "everything was acknowledged");
            }
        }

        [Test]
        public void UnacknowledgedCount_AddsUpEveryConnection_AndEmptiesWithTheAcknowledgements()
        {
            using (var channel = new LockstepRedundantChannel(new Silence()))
            {
                var packet = new byte[] { 1, 2 };
                fixed (byte* data = packet)
                {
                    channel.Send(1, data, packet.Length);
                    channel.Send(1, data, packet.Length);
                    channel.Send(2, data, packet.Length);
                }
                channel.Flush(1);
                Assert.AreEqual(2, channel.GetUnacknowledgedCount(1));
                Assert.AreEqual(3, channel.GetUnacknowledgedCount(), "the packets of every connection");

                channel.RemoveConnection(1);
                Assert.AreEqual(1, channel.GetUnacknowledgedCount(), "a closed connection takes its packets along");
            }

            using (var link = new DatagramLink(10) { Loss = 0.2, Jitter = 0.02 })
            {
                for (byte i = 0; i < 20; i++)
                {
                    link.SendFromA(new[] { i });
                }
                link.Step(0.016);
                Assert.Greater(link.A.GetUnacknowledgedCount(), 0, "nothing has arrived yet");

                for (var step = 0; step < 500 && link.A.GetUnacknowledgedCount() > 0; step++)
                {
                    link.Step(0.016);
                }
                Assert.AreEqual(0, link.A.GetUnacknowledgedCount(), "every packet was acknowledged");
                Assert.AreEqual(20, link.ReceivedByB.Count);
            }
        }

        [Test]
        public void ASideCreatedAnew_GetsAStreamOfItsOwn()
        {
            using (var link = new DatagramLink(2))
            {
                var fromA = new List<byte[]>();
                for (byte i = 0; i < 5; i++)
                {
                    fromA.Add(new[] { i });
                    link.SendFromA(fromA[i]);
                }
                var fromB = new List<byte[]> { new byte[] { 100 }, new byte[] { 101 } };
                link.SendFromB(fromB[0]);
                link.SendFromB(fromB[1]);
                for (var step = 0; step < 30; step++)
                {
                    link.Step(0.02);
                }
                AssertSamePackets(fromA, link.ReceivedByB);
                AssertSamePackets(fromB, link.ReceivedByA);
                var stale = link.LastFromB;

                // B is created anew over the same connection: it takes A's stream from what was not acknowledged yet, and A
                // takes B's new stream from its start.
                link.RecreateB();
                var later = new byte[] { 50 };
                var fresh = new byte[] { 200 };
                link.SendFromA(later);
                link.SendFromB(fresh);
                for (var step = 0; step < 30; step++)
                {
                    link.Step(0.02);
                }
                AssertSamePackets(new List<byte[]> { later }, link.ReceivedByB);
                AssertSamePackets(new List<byte[]> { fromB[0], fromB[1], fresh }, link.ReceivedByA);

                // A datagram of the stream B replaced, arriving late, is ignored.
                link.Inject(false, stale);
                link.Step(0.02);
                Assert.AreEqual(3, link.ReceivedByA.Count);
            }
        }

        [Test]
        public void ForeignAndTruncatedDatagrams_AreIgnored()
        {
            using (var link = new DatagramLink(6))
            {
                var random = new Random(8);
                for (var i = 0; i < 500; i++)
                {
                    var garbage = new byte[random.Next(1, 200)];
                    random.NextBytes(garbage);
                    garbage[0] = (byte)(random.Next(2, 256));
                    fixed (byte* data = garbage)
                    {
                        Assert.IsFalse(link.A.Receive(DatagramLink.B_ID, data, garbage.Length, link.Time));
                    }
                }
                Assert.IsFalse(link.A.TryRead(out _, out _, out _));

                // Truncated copies of real datagrams leave the stream intact.
                var sent = new List<byte[]>();
                for (var step = 0; step < 200; step++)
                {
                    if (step < 100)
                    {
                        var packet = RandomPacket(random, 100);
                        sent.Add(packet);
                        link.SendFromB(packet);
                    }
                    link.Step(0.016);
                    var datagram = link.LastFromB;
                    if (datagram != null && datagram.Length > 2)
                    {
                        var truncated = new byte[random.Next(1, datagram.Length)];
                        Array.Copy(datagram, truncated, truncated.Length);
                        link.Inject(false, truncated);
                    }
                }
                AssertSamePackets(sent, link.ReceivedByA);
            }
        }

        [Test]
        public void APeerThatFroze_IsNotFlooded_AndCatchesUpAfterwards()
        {
            using (var link = new DatagramLink(7))
            {
                var sent = new List<byte[]>();
                for (var step = 0; step < 60; step++)
                {
                    var packet = new byte[] { (byte)step, 1 };
                    sent.Add(packet);
                    link.SendFromA(packet);
                    link.Step(1 / 30.0);
                }

                // B freezes for 20 seconds while A queues a packet every tick.
                link.CutToB = true;
                var before = link.DatagramsFromA;
                for (var step = 0; step < 600; step++)
                {
                    var packet = new byte[] { (byte)step, 2 };
                    sent.Add(packet);
                    link.SendFromA(packet);
                    link.Step(1 / 30.0);
                }
                var duringFreeze = link.DatagramsFromA - before;
                Assert.Less(duringFreeze, 80, "after a second without acknowledgements only backed-off retransmissions go out");

                link.CutToB = false;
                for (var step = 0; step < 3000 && link.ReceivedByB.Count < sent.Count; step++)
                {
                    link.Step(1 / 30.0);
                }
                AssertSamePackets(sent, link.ReceivedByB);
            }
        }

        [Test]
        public void SteadyTraffic_AllocatesNothing()
        {
            using (var a = new DirectLink())
            {
                var packet = new byte[40];
                var time = 0.0;
                for (var step = 0; step < 200; step++)
                {
                    time = a.Exchange(packet, time);
                }
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                for (var step = 0; step < 300; step++)
                {
                    time = a.Exchange(packet, time);
                }
                // Measured before the assertion, which boxes its arguments.
                var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Assert.AreEqual(0L, bytes);
                Assert.Greater(a.Received, 450);
            }
        }

        // A network that loses everything: nothing is ever acknowledged.
        private sealed class Silence : ILockstepTransport
        {
            public void Send(int connectionId, byte* data, int length)
            {
            }
        }

        // Two channels that hand datagrams to each other at once, without allocating.
        private sealed class DirectLink : ILockstepTransport, IDisposable
        {
            private readonly LockstepRedundantChannel _a;
            private readonly LockstepRedundantChannel _b;
            private readonly Forward _toA;
            private double _time;

            private sealed class Forward : ILockstepTransport
            {
                public LockstepRedundantChannel target;
                public DirectLink link;

                public void Send(int connectionId, byte* data, int length)
                {
                    target.Receive(1, data, length, link._time);
                    while (target.TryRead(out _, out _, out _))
                    {
                        link.Received++;
                    }
                }
            }

            public DirectLink()
            {
                _toA = new Forward { link = this };
                _a = new LockstepRedundantChannel(this);
                _b = new LockstepRedundantChannel(_toA);
                _toA.target = _a;
            }

            public int Received { get; private set; }

            public double Exchange(byte[] packet, double time)
            {
                _time = time + 1 / 30.0;
                fixed (byte* data = packet)
                {
                    _a.Send(1, data, packet.Length);
                    _b.Send(1, data, packet.Length);
                }
                _a.Flush(_time);
                _b.Flush(_time);
                return _time;
            }

            // A sends to B.
            public void Send(int connectionId, byte* data, int length)
            {
                _b.Receive(1, data, length, _time);
                while (_b.TryRead(out _, out _, out _))
                {
                    Received++;
                }
            }

            public void Dispose()
            {
                _a.Dispose();
                _b.Dispose();
            }
        }
    }
}
