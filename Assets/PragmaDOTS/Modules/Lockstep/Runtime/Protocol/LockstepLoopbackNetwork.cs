using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// In-process network between one server endpoint (<see cref="LockstepServer"/>, <see cref="LockstepReplayHost"/>) and
    /// any number of clients (<see cref="LockstepClient"/>). Used for offline play, replays and tests; latency and jitter are
    /// simulated, order per connection is preserved.
    /// </summary>
    /// <remarks>
    /// <see cref="Loss"/> and <see cref="Reorder"/> make it an unreliable network, for testing a
    /// <see cref="LockstepRedundantChannel"/> over it; a session needs a reliable, ordered network otherwise. Packets are
    /// pooled: a steady session allocates nothing.
    /// </remarks>
    public sealed unsafe class LockstepLoopbackNetwork
    {
        private sealed class Packet
        {
            public double deliveryTime;
            public int connectionId;
            public bool toServer;
            public byte[] data = Array.Empty<byte>();
            public int length;
        }

        private sealed class Endpoint : ILockstepTransport
        {
            private readonly LockstepLoopbackNetwork _network;
            private readonly bool _toServer;
            private readonly int _clientConnectionId;

            public Endpoint(LockstepLoopbackNetwork network, bool toServer, int clientConnectionId)
            {
                _network = network;
                _toServer = toServer;
                _clientConnectionId = clientConnectionId;
            }

            public void Send(int connectionId, byte* data, int length)
            {
                _network.Enqueue(_toServer, _toServer ? _clientConnectionId : connectionId, data, length);
            }
        }

        private readonly List<Packet> _inFlight = new List<Packet>();
        private readonly Stack<Packet> _free = new Stack<Packet>();
        private readonly Dictionary<long, double> _lastDelivery = new Dictionary<long, double>();
        private readonly Dictionary<int, ILockstepClientEndpoint> _clients = new Dictionary<int, ILockstepClientEndpoint>();
        private readonly Random _random;
        private ILockstepServerEndpoint _server;
        private double _now;

        public LockstepLoopbackNetwork(int randomSeed = 1)
        {
            _random = new Random(randomSeed);
            ServerTransport = new Endpoint(this, false, -1);
        }

        /// <summary>One-way latency in seconds.</summary>
        public double Latency { get; set; }
        /// <summary>Extra random one-way delay in [0, Jitter) seconds; packet order is still preserved unless <see cref="Reorder"/> is set.</summary>
        public double Jitter { get; set; }
        /// <summary>Share of packets lost, in [0, 1], in each direction. Only for testing a <see cref="LockstepRedundantChannel"/>.</summary>
        public double Loss { get; set; }
        /// <summary>Lets the jitter reorder packets of a connection. Only for testing a <see cref="LockstepRedundantChannel"/>.</summary>
        public bool Reorder { get; set; }

        /// <summary>The transport to create the server with.</summary>
        public ILockstepTransport ServerTransport { get; }

        public int PacketsInFlight => _inFlight.Count;

        /// <summary>Packets handed to the network, lost ones included.</summary>
        public long PacketsSent { get; private set; }

        /// <summary>Bytes handed to the network, lost ones included.</summary>
        public long BytesSent { get; private set; }

        public void AttachServer(ILockstepServerEndpoint server) => _server = server;

        /// <summary>The transport to create a client with; <paramref name="connectionId"/> identifies it on the server.</summary>
        public ILockstepTransport CreateClientTransport(int connectionId) => new Endpoint(this, true, connectionId);

        public void AttachClient(int connectionId, ILockstepClientEndpoint client)
        {
            _clients[connectionId] = client;
            _server?.OnConnected(connectionId);
        }

        /// <summary>Drops the connection: packets in flight are lost and the server sees a disconnect.</summary>
        public void DetachClient(int connectionId)
        {
            _clients.Remove(connectionId);
            for (var i = _inFlight.Count - 1; i >= 0; i--)
            {
                if (_inFlight[i].connectionId == connectionId)
                {
                    _free.Push(_inFlight[i]);
                    _inFlight.RemoveAt(i);
                }
            }
            _server?.OnDisconnected(connectionId);
        }

        /// <summary>Delivers every packet due at <paramref name="now"/>, including replies sent while delivering.</summary>
        public void Deliver(double now)
        {
            _now = now;
            while (_inFlight.Count > 0 && _inFlight[0].deliveryTime <= now)
            {
                var packet = _inFlight[0];
                _inFlight.RemoveAt(0);
                fixed (byte* data = packet.data)
                {
                    if (packet.toServer)
                    {
                        _server?.OnPacket(packet.connectionId, data, packet.length, now);
                    }
                    else if (_clients.TryGetValue(packet.connectionId, out var client))
                    {
                        client.OnPacket(data, packet.length, now);
                    }
                }
                // Returned only after the endpoint is done with the bytes.
                _free.Push(packet);
            }
        }

        private void Enqueue(bool toServer, int connectionId, byte* data, int length)
        {
            PacketsSent++;
            BytesSent += length;
            if (Loss > 0 && _random.NextDouble() < Loss)
            {
                return;
            }

            var delay = Latency + (Jitter > 0 ? _random.NextDouble() * Jitter : 0);
            var deliveryTime = _now + delay;
            if (!Reorder)
            {
                var channel = ((long)connectionId << 1) | (toServer ? 1L : 0L);
                if (_lastDelivery.TryGetValue(channel, out var last) && last > deliveryTime)
                {
                    deliveryTime = last;
                }
                _lastDelivery[channel] = deliveryTime;
            }

            var packet = _free.Count > 0 ? _free.Pop() : new Packet();
            if (packet.data.Length < length)
            {
                packet.data = new byte[Math.Max(length, 256)];
            }
            fixed (byte* destination = packet.data)
            {
                UnsafeUtility.MemCpy(destination, data, length);
            }
            packet.length = length;
            packet.deliveryTime = deliveryTime;
            packet.connectionId = connectionId;
            packet.toServer = toServer;

            // Keep the list sorted by delivery time; a packet goes after the ones due at the same time, so ties keep the
            // send order.
            var index = _inFlight.Count;
            while (index > 0 && _inFlight[index - 1].deliveryTime > deliveryTime)
            {
                index--;
            }
            _inFlight.Insert(index, packet);
        }

        /// <summary>Sets the time used to stamp packets sent outside of <see cref="Deliver"/>.</summary>
        public void SetTime(double now) => _now = Math.Max(_now, now);
    }
}
