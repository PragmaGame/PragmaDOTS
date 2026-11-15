using System;
using System.Collections.Generic;
using Pragma.Dots.Lockstep;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Dots.Tests
{
    /// <summary>
    /// Two redundant channels joined by a link that loses, duplicates and reorders datagrams, stepped with simulated time.
    /// Side A knows side B as connection <see cref="B_ID"/>, side B knows A as <see cref="A_ID"/>.
    /// </summary>
    internal sealed unsafe class DatagramLink : IDisposable
    {
        public const int A_ID = 3;
        public const int B_ID = 8;

        private sealed class Datagram
        {
            public double time;
            public bool toB;
            public byte[] data;
        }

        private sealed class Side : ILockstepTransport
        {
            private readonly DatagramLink _link;
            private readonly bool _isA;

            public Side(DatagramLink link, bool isA)
            {
                _link = link;
                _isA = isA;
            }

            public void Send(int connectionId, byte* data, int length)
            {
                _link.Enqueue(_isA, data, length);
            }
        }

        private readonly List<Datagram> _inFlight = new List<Datagram>();
        private readonly Random _random;
        private readonly LockstepRedundantChannelSettings _settings;

        public DatagramLink(int seed, LockstepRedundantChannelSettings? settings = null)
        {
            _random = new Random(seed);
            _settings = settings ?? LockstepRedundantChannelSettings.Default;
            A = new LockstepRedundantChannel(new Side(this, true), _settings);
            B = new LockstepRedundantChannel(new Side(this, false), _settings);
        }

        public LockstepRedundantChannel A { get; private set; }
        public LockstepRedundantChannel B { get; private set; }
        public double Time { get; private set; } = 100;
        public double Latency { get; set; } = 0.05;
        public double Jitter { get; set; }
        public double Loss { get; set; }
        public double Duplication { get; set; }
        /// <summary>Datagrams towards B are lost while set.</summary>
        public bool CutToB { get; set; }
        /// <summary>Drops the next datagram A sends.</summary>
        public bool DropNextFromA { get; set; }
        public List<byte[]> ReceivedByA { get; } = new List<byte[]>();
        public List<byte[]> ReceivedByB { get; } = new List<byte[]>();
        public int DatagramsFromA { get; private set; }
        /// <summary>The last datagram B sent, as it went out.</summary>
        public byte[] LastFromB { get; private set; }

        public void SendFromA(byte[] packet) => Send(A, B_ID, packet);

        public void SendFromB(byte[] packet) => Send(B, A_ID, packet);

        /// <summary>Delivers what is due, then both sides flush.</summary>
        public void Step(double deltaTime)
        {
            Time += deltaTime;
            for (var i = 0; i < _inFlight.Count;)
            {
                var datagram = _inFlight[i];
                if (datagram.time > Time)
                {
                    i++;
                    continue;
                }
                _inFlight.RemoveAt(i);
                Deliver(datagram.toB, datagram.data);
            }
            A.Flush(Time);
            B.Flush(Time);
        }

        /// <summary>Hands a datagram to a side as if it had arrived now.</summary>
        public void Inject(bool toB, byte[] data) => Deliver(toB, data);

        /// <summary>Replaces side B with a new channel, as a client created anew over the same connection.</summary>
        public void RecreateB()
        {
            B.Dispose();
            B = new LockstepRedundantChannel(new Side(this, false), _settings);
            ReceivedByB.Clear();
        }

        public void Dispose()
        {
            A.Dispose();
            B.Dispose();
        }

        private static void Send(LockstepRedundantChannel channel, int connectionId, byte[] packet)
        {
            fixed (byte* data = packet)
            {
                channel.Send(connectionId, data, packet.Length);
            }
        }

        private void Deliver(bool toB, byte[] data)
        {
            var channel = toB ? B : A;
            var received = toB ? ReceivedByB : ReceivedByA;
            var connectionId = toB ? A_ID : B_ID;
            fixed (byte* bytes = data)
            {
                channel.Receive(connectionId, bytes, data.Length, Time);
            }
            while (channel.TryRead(out var from, out var packet, out var length))
            {
                if (from != connectionId)
                {
                    throw new InvalidOperationException($"A packet came from connection {from}, not {connectionId}.");
                }
                var copy = new byte[length];
                fixed (byte* destination = copy)
                {
                    UnsafeUtility.MemCpy(destination, packet, length);
                }
                received.Add(copy);
            }
        }

        private void Enqueue(bool fromA, byte* data, int length)
        {
            var bytes = new byte[length];
            fixed (byte* destination = bytes)
            {
                UnsafeUtility.MemCpy(destination, data, length);
            }
            if (fromA)
            {
                DatagramsFromA++;
                if (DropNextFromA)
                {
                    DropNextFromA = false;
                    return;
                }
            }
            else
            {
                LastFromB = bytes;
            }
            if (fromA && CutToB)
            {
                return;
            }
            var copies = _random.NextDouble() < Duplication ? 2 : 1;
            for (var i = 0; i < copies; i++)
            {
                if (_random.NextDouble() < Loss)
                {
                    continue;
                }
                _inFlight.Add(new Datagram { time = Time + Latency + _random.NextDouble() * Jitter, toB = fromA, data = bytes });
            }
        }
    }
}
