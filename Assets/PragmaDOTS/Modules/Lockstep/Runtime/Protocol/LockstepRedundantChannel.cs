using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// A reliable, ordered channel over an unreliable datagram transport, made for lossy networks: every datagram repeats
    /// the latest packets the other side has not acknowledged, so a lost datagram is replaced by the next one instead of
    /// holding back everything behind it until a retransmission.
    /// </summary>
    /// <remarks>
    /// <para>The channel sits between a <see cref="LockstepServer"/> or <see cref="LockstepClient"/> and the transport. The
    /// session sends its packets to the channel (it is their <see cref="ILockstepTransport"/>), the channel sends datagrams
    /// through the transport it was created with, which may drop, duplicate and reorder them. Received datagrams go to
    /// <see cref="Receive"/>, the packets they complete come out of <see cref="TryRead"/> in order, exactly once, and go to
    /// the session's <c>OnPacket</c>. <see cref="Flush"/> sends after the session's update.</para>
    /// <para>A packet travels in up to <see cref="LockstepRedundantChannelSettings.RedundantCopies"/> datagrams in a row,
    /// and again after a timeout derived from the round trip until it is acknowledged. Acknowledgements ride on every
    /// datagram (the next sequence expected and a mask of the 32 after it); a side with nothing to send acknowledges
    /// on its own after a short delay. A side that hears no acknowledgement for a second stops sending new packets and
    /// backs its retransmissions off, so a peer that froze (a minimized app) is not flooded.</para>
    /// <para>Each side of a connection numbers its stream under a random epoch. A receiver that sees a new epoch (the other
    /// side was created anew over the same connection) starts that stream at the oldest packet the sender still has, and
    /// ignores the epochs it replaced.</para>
    /// </remarks>
    public sealed unsafe class LockstepRedundantChannel : ILockstepTransport, IDisposable
    {
        private const byte FORMAT = 1;
        // Format, sender epoch, acknowledged epoch, acknowledgement (var uint), mask, base sequence (var uint), count.
        private const int MAX_HEADER_SIZE = 1 + 4 + 4 + 5 + 4 + 5 + 1;
        // Sequence delta and length, both var uints.
        private const int MAX_ENTRY_HEADER_SIZE = 5 + 5;
        private const int MAX_ENTRIES = byte.MaxValue;
        private const int ACK_MASK_BITS = 32;
        private const int MAX_OUT_OF_ORDER = 1024;
        private const int RETIRED_EPOCHS = 4;
        private const double ACK_DELAY_SECONDS = 0.05;
        private const double INITIAL_RESEND_SECONDS = 0.25;
        private const double MIN_RESEND_SECONDS = 0.05;
        private const double MAX_RESEND_SECONDS = 2;
        private const double STALL_SECONDS = 1;
        private const int MAX_BACKOFF = 5;

        private struct OutPacket
        {
            public uint seq;
            public int offset;
            public int length;
            public int sendCount;
            public double lastSent;
            // Acknowledged by the mask while an earlier packet is still missing.
            public bool received;
        }

        private struct InPacket
        {
            public uint seq;
            public int offset;
            public int length;
        }

        private struct ReadyPacket
        {
            public int connectionId;
            public int offset;
            public int length;
        }

        private sealed class Peer
        {
            public int id;
            public uint outEpoch;
            public uint nextSeq;
            public NativeList<byte> outData;
            public NativeList<OutPacket> outPackets;
            // Bytes sent at least once and neither acknowledged nor received out of order.
            public int inFlightBytes;
            public double lastProgress;
            public int backoff;
            public double smoothedRtt = double.NaN;
            public double rttVariance;

            public uint inEpoch;
            public readonly uint[] retiredEpochs = new uint[RETIRED_EPOCHS];
            public int nextRetired;
            public uint nextExpected;
            public NativeList<byte> inData;
            // Received ahead of nextExpected, sorted by sequence.
            public NativeList<InPacket> inPackets;
            public bool ackPending;
            public double ackDue;
        }

        private readonly ILockstepTransport _transport;
        private readonly LockstepRedundantChannelSettings _settings;
        private readonly Dictionary<int, Peer> _peers = new Dictionary<int, Peer>();
        private readonly List<Peer> _peerList = new List<Peer>();
        private NativeList<byte> _datagram;
        private NativeList<byte> _readyData;
        private NativeList<ReadyPacket> _ready;
        private NativeList<byte> _scratch;
        private int _readIndex;
        private uint _lastEpoch;
        private bool _disposed;

        /// <param name="datagramTransport">Sends datagrams; it may lose, duplicate and reorder them.</param>
        public LockstepRedundantChannel(ILockstepTransport datagramTransport, in LockstepRedundantChannelSettings settings)
        {
            settings.Validate();
            _transport = datagramTransport ?? throw new ArgumentNullException(nameof(datagramTransport));
            _settings = settings;
            _datagram = new NativeList<byte>(settings.MaxDatagramSize, Allocator.Persistent);
            _readyData = new NativeList<byte>(1024, Allocator.Persistent);
            _ready = new NativeList<ReadyPacket>(16, Allocator.Persistent);
            _scratch = new NativeList<byte>(1024, Allocator.Persistent);
        }

        public LockstepRedundantChannel(ILockstepTransport datagramTransport) : this(datagramTransport, LockstepRedundantChannelSettings.Default)
        {
        }

        public LockstepRedundantChannelSettings Settings => _settings;

        /// <summary>The largest packet the channel carries: give it to the session as its <c>MaxPacketSize</c>.</summary>
        public int MaxPacketSize => _settings.MaxDatagramSize - MAX_HEADER_SIZE - MAX_ENTRY_HEADER_SIZE;

        /// <summary>Datagrams handed to the transport.</summary>
        public long DatagramsSent { get; private set; }

        /// <summary>Well-formed datagrams received.</summary>
        public long DatagramsReceived { get; private set; }

        /// <summary>Packets sent again after a timeout, because every redundant copy of them was lost.</summary>
        public long PacketsResent { get; private set; }

        /// <summary>Queues a packet for <paramref name="connectionId"/>; <see cref="Flush"/> sends it.</summary>
        public void Send(int connectionId, byte* data, int length)
        {
            if (_disposed)
            {
                return;
            }
            if (length < 0 || length > MaxPacketSize)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, $"The channel carries packets of up to {MaxPacketSize} bytes.");
            }
            var peer = GetOrAddPeer(connectionId);
            peer.outPackets.Add(new OutPacket { seq = peer.nextSeq++, offset = peer.outData.Length, length = length });
            if (length > 0)
            {
                peer.outData.AddRange(data, length);
            }
        }

        /// <summary>
        /// Takes a datagram received from <paramref name="connectionId"/>. The packets it completes are read with
        /// <see cref="TryRead"/>; read them all before the next call.
        /// </summary>
        /// <returns>False for a datagram that is not one of the channel's, which is ignored.</returns>
        public bool Receive(int connectionId, byte* datagram, int length, double now)
        {
            if (_disposed || datagram == null || length <= 0)
            {
                return false;
            }
            if (_readIndex >= _ready.Length)
            {
                ClearReady();
            }

            var reader = new LockstepByteReader(datagram, length);
            var format = reader.ReadByte();
            var senderEpoch = reader.ReadUInt();
            var ackEpoch = reader.ReadUInt();
            var ack = reader.ReadVarUInt();
            var mask = reader.ReadUInt();
            var baseSeq = reader.ReadVarUInt();
            var count = reader.ReadByte();
            if (reader.HasFailed || format != FORMAT || senderEpoch == 0)
            {
                return false;
            }

            var peer = GetOrAddPeer(connectionId);
            if (IsRetired(peer, senderEpoch))
            {
                return false;
            }
            DatagramsReceived++;
            if (senderEpoch != peer.inEpoch)
            {
                // The other side started a stream: take it from the oldest packet it still holds.
                if (peer.inEpoch != 0)
                {
                    peer.retiredEpochs[peer.nextRetired] = peer.inEpoch;
                    peer.nextRetired = (peer.nextRetired + 1) % RETIRED_EPOCHS;
                }
                peer.inEpoch = senderEpoch;
                peer.nextExpected = baseSeq;
                peer.inData.Clear();
                peer.inPackets.Clear();
            }
            if (ackEpoch == peer.outEpoch)
            {
                ProcessAcknowledgement(peer, ack, mask, now);
            }

            var seq = baseSeq;
            for (var i = 0; i < count; i++)
            {
                var delta = reader.ReadVarUInt();
                var packetLength = (int)reader.ReadVarUInt();
                var packet = reader.ReadBytesPtr(packetLength);
                if (reader.HasFailed || packet == null)
                {
                    return false;
                }
                seq += delta;
                Accept(peer, seq, packet, packetLength);
                if (!peer.ackPending)
                {
                    peer.ackPending = true;
                    peer.ackDue = now + ACK_DELAY_SECONDS;
                }
            }
            return true;
        }

        /// <summary>
        /// The next packet received in order, from any connection. The pointer stays valid until the next call of
        /// <see cref="TryRead"/> or <see cref="Receive"/>.
        /// </summary>
        public bool TryRead(out int connectionId, out byte* packet, out int length)
        {
            if (_readIndex >= _ready.Length)
            {
                ClearReady();
                connectionId = -1;
                packet = null;
                length = 0;
                return false;
            }
            var ready = _ready[_readIndex++];
            connectionId = ready.connectionId;
            packet = _readyData.GetUnsafePtr() + ready.offset;
            length = ready.length;
            return true;
        }

        /// <summary>Sends what is due: new packets, redundant copies, retransmissions and acknowledgements. Call it after the session's update.</summary>
        public void Flush(double now)
        {
            if (_disposed)
            {
                return;
            }
            for (var i = 0; i < _peerList.Count; i++)
            {
                FlushPeer(_peerList[i], now);
            }
        }

        /// <summary>Forgets a closed connection and what was queued for it.</summary>
        public void RemoveConnection(int connectionId)
        {
            if (!_peers.TryGetValue(connectionId, out var peer))
            {
                return;
            }
            DisposePeer(peer);
            _peers.Remove(connectionId);
            _peerList.Remove(peer);
        }

        /// <summary>Smoothed round-trip time of a connection in seconds, measured on acknowledgements; NaN before the first one.</summary>
        public double GetRoundTripTime(int connectionId) => _peers.TryGetValue(connectionId, out var peer) ? peer.smoothedRtt : double.NaN;

        /// <summary>Packets queued for a connection and not acknowledged yet.</summary>
        public int GetUnacknowledgedCount(int connectionId) => _peers.TryGetValue(connectionId, out var peer) ? peer.outPackets.Length : 0;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach (var peer in _peerList)
            {
                DisposePeer(peer);
            }
            _peers.Clear();
            _peerList.Clear();
            _datagram.Dispose();
            _readyData.Dispose();
            _ready.Dispose();
            _scratch.Dispose();
        }

        private Peer GetOrAddPeer(int connectionId)
        {
            if (_peers.TryGetValue(connectionId, out var peer))
            {
                return peer;
            }
            peer = new Peer
            {
                id = connectionId,
                outEpoch = CreateEpoch(),
                outData = new NativeList<byte>(1024, Allocator.Persistent),
                outPackets = new NativeList<OutPacket>(32, Allocator.Persistent),
                inData = new NativeList<byte>(256, Allocator.Persistent),
                inPackets = new NativeList<InPacket>(8, Allocator.Persistent),
            };
            _peers.Add(connectionId, peer);
            _peerList.Add(peer);
            return peer;
        }

        private uint CreateEpoch()
        {
            // Random, so that a side created anew is told apart from the one it replaces; never zero, which means none.
            uint epoch;
            do
            {
                epoch = (uint)Guid.NewGuid().GetHashCode();
            }
            while (epoch == 0 || epoch == _lastEpoch);
            _lastEpoch = epoch;
            return epoch;
        }

        private static bool IsRetired(Peer peer, uint epoch)
        {
            for (var i = 0; i < RETIRED_EPOCHS; i++)
            {
                if (peer.retiredEpochs[i] == epoch)
                {
                    return true;
                }
            }
            return false;
        }

        private void Accept(Peer peer, uint seq, byte* packet, int length)
        {
            var ahead = (int)(seq - peer.nextExpected);
            if (ahead < 0)
            {
                // Delivered already: a redundant copy.
                return;
            }
            if (ahead == 0)
            {
                Deliver(peer.id, packet, length);
                peer.nextExpected++;
                DeliverBuffered(peer);
                return;
            }
            if (ahead >= MAX_OUT_OF_ORDER)
            {
                return;
            }

            var index = peer.inPackets.Length;
            while (index > 0 && (int)(peer.inPackets[index - 1].seq - seq) > 0)
            {
                index--;
            }
            if (index > 0 && peer.inPackets[index - 1].seq == seq)
            {
                return;
            }
            var entry = new InPacket { seq = seq, offset = peer.inData.Length, length = length };
            if (length > 0)
            {
                peer.inData.AddRange(packet, length);
            }
            peer.inPackets.Add(default);
            for (var i = peer.inPackets.Length - 1; i > index; i--)
            {
                peer.inPackets[i] = peer.inPackets[i - 1];
            }
            peer.inPackets[index] = entry;
        }

        private void DeliverBuffered(Peer peer)
        {
            var delivered = 0;
            while (delivered < peer.inPackets.Length && peer.inPackets[delivered].seq == peer.nextExpected)
            {
                var entry = peer.inPackets[delivered];
                Deliver(peer.id, peer.inData.GetUnsafePtr() + entry.offset, entry.length);
                peer.nextExpected++;
                delivered++;
            }
            if (delivered == 0)
            {
                return;
            }
            peer.inPackets.RemoveRange(0, delivered);
            if (peer.inPackets.Length == 0)
            {
                peer.inData.Clear();
                return;
            }

            // Packets arrive out of order, so their bytes are not in sequence order: copy the ones still waiting together.
            _scratch.Clear();
            for (var i = 0; i < peer.inPackets.Length; i++)
            {
                var entry = peer.inPackets[i];
                var offset = _scratch.Length;
                if (entry.length > 0)
                {
                    _scratch.AddRange(peer.inData.GetUnsafePtr() + entry.offset, entry.length);
                }
                entry.offset = offset;
                peer.inPackets[i] = entry;
            }
            peer.inData.Clear();
            peer.inData.AddRange(_scratch.GetUnsafePtr(), _scratch.Length);
        }

        private void Deliver(int connectionId, byte* packet, int length)
        {
            _ready.Add(new ReadyPacket { connectionId = connectionId, offset = _readyData.Length, length = length });
            if (length > 0)
            {
                _readyData.AddRange(packet, length);
            }
        }

        private void ClearReady()
        {
            _ready.Clear();
            _readyData.Clear();
            _readIndex = 0;
        }

        private void ProcessAcknowledgement(Peer peer, uint ack, uint mask, double now)
        {
            // An acknowledgement of packets never sent comes from a corrupted or forged datagram.
            if ((int)(ack - peer.nextSeq) > 0)
            {
                return;
            }

            var progressed = false;
            var sample = double.NaN;
            var removed = 0;
            while (removed < peer.outPackets.Length && (int)(peer.outPackets[removed].seq - ack) < 0)
            {
                var packet = peer.outPackets[removed];
                if (packet.sendCount > 0 && !packet.received)
                {
                    peer.inFlightBytes -= packet.length;
                }
                // Karn's rule: only a packet sent once tells how long the round trip took.
                if (packet.sendCount == 1)
                {
                    sample = now - packet.lastSent;
                }
                removed++;
            }
            if (removed > 0)
            {
                progressed = true;
                peer.outPackets.RemoveRange(0, removed);
                var start = peer.outPackets.Length > 0 ? peer.outPackets[0].offset : peer.outData.Length;
                var remaining = peer.outData.Length - start;
                if (remaining > 0)
                {
                    UnsafeUtility.MemMove(peer.outData.GetUnsafePtr(), peer.outData.GetUnsafePtr() + start, remaining);
                }
                peer.outData.ResizeUninitialized(remaining);
                for (var i = 0; i < peer.outPackets.Length; i++)
                {
                    var packet = peer.outPackets[i];
                    packet.offset -= start;
                    peer.outPackets[i] = packet;
                }
            }

            if (mask != 0)
            {
                for (var i = 0; i < peer.outPackets.Length; i++)
                {
                    var packet = peer.outPackets[i];
                    var bit = (int)(packet.seq - ack) - 1;
                    if (bit >= ACK_MASK_BITS)
                    {
                        break;
                    }
                    if (bit < 0 || packet.received || (mask & (1u << bit)) == 0)
                    {
                        continue;
                    }
                    packet.received = true;
                    if (packet.sendCount > 0)
                    {
                        peer.inFlightBytes -= packet.length;
                    }
                    peer.outPackets[i] = packet;
                    progressed = true;
                }
            }

            if (!progressed)
            {
                return;
            }
            peer.lastProgress = now;
            peer.backoff = 0;
            if (double.IsNaN(sample) || sample < 0)
            {
                return;
            }
            if (double.IsNaN(peer.smoothedRtt))
            {
                peer.smoothedRtt = sample;
                peer.rttVariance = sample / 2;
            }
            else
            {
                peer.rttVariance += (Math.Abs(peer.smoothedRtt - sample) - peer.rttVariance) * 0.25;
                peer.smoothedRtt += (sample - peer.smoothedRtt) * 0.125;
            }
        }

        private double GetResendTimeout(Peer peer)
        {
            var timeout = double.IsNaN(peer.smoothedRtt) ? INITIAL_RESEND_SECONDS : peer.smoothedRtt + Math.Max(4 * peer.rttVariance, 0.03);
            timeout = Math.Min(Math.Max(timeout, MIN_RESEND_SECONDS), MAX_RESEND_SECONDS);
            return Math.Min(timeout * (1 << peer.backoff), MAX_RESEND_SECONDS);
        }

        private enum Eligibility
        {
            None,
            // Goes along when a datagram is sent anyway: a redundant copy.
            Copy,
            // Makes a datagram go out: a new packet, or one whose copies were all lost.
            Due,
        }

        private Eligibility GetEligibility(Peer peer, in OutPacket packet, double now, double timeout, bool stalled, bool resendRound)
        {
            if (packet.received)
            {
                return Eligibility.None;
            }
            if (packet.sendCount == 0)
            {
                if (stalled || (peer.inFlightBytes > 0 && peer.inFlightBytes + packet.length > _settings.MaxInFlightBytes))
                {
                    return Eligibility.None;
                }
                return Eligibility.Due;
            }
            // A round spares what went out in the last half timeout: a round larger than a flush goes on in the next
            // flushes without sending its start again.
            if (resendRound && now - packet.lastSent >= timeout * 0.5)
            {
                return Eligibility.Due;
            }
            return packet.sendCount < _settings.RedundantCopies ? Eligibility.Copy : Eligibility.None;
        }

        private bool HasDuePacket(Peer peer, int from, double now, double timeout, bool stalled, bool resendRound)
        {
            for (var i = from; i < peer.outPackets.Length; i++)
            {
                var packet = peer.outPackets[i];
                if (GetEligibility(peer, packet, now, timeout, stalled, resendRound) == Eligibility.Due)
                {
                    return true;
                }
                // Packets after one that waits for room in flight wait too: they are sent in order.
                if (packet.sendCount == 0)
                {
                    return false;
                }
            }
            return false;
        }

        // A packet unacknowledged for longer than the timeout lost every copy: everything in flight goes again at once,
        // which keeps the retransmissions of a silent peer together instead of one datagram per packet.
        private static bool IsResendDue(Peer peer, double now, double timeout)
        {
            for (var i = 0; i < peer.outPackets.Length; i++)
            {
                var packet = peer.outPackets[i];
                if (packet.sendCount == 0)
                {
                    return false;
                }
                if (!packet.received && now - packet.lastSent >= timeout)
                {
                    return true;
                }
            }
            return false;
        }

        private void FlushPeer(Peer peer, double now)
        {
            var timeout = GetResendTimeout(peer);
            var stalled = peer.inFlightBytes > 0 && now - peer.lastProgress > STALL_SECONDS;
            var resendRound = IsResendDue(peer, now, timeout);
            var ackDue = peer.ackPending && now >= peer.ackDue;
            var index = 0;
            for (var datagrams = 0; datagrams < _settings.MaxDatagramsPerFlush; datagrams++)
            {
                if (!HasDuePacket(peer, index, now, timeout, stalled, resendRound) && !(datagrams == 0 && ackDue))
                {
                    break;
                }

                var writer = BeginDatagram(peer, out var countPosition);
                var count = 0;
                var previousSeq = peer.outPackets.Length > 0 ? peer.outPackets[0].seq : peer.nextSeq;
                for (; index < peer.outPackets.Length && count < MAX_ENTRIES; index++)
                {
                    var packet = peer.outPackets[index];
                    var eligibility = GetEligibility(peer, packet, now, timeout, stalled, resendRound);
                    if (eligibility == Eligibility.None)
                    {
                        if (packet.sendCount == 0)
                        {
                            // Waiting for room in flight; so do the packets after it.
                            index = peer.outPackets.Length;
                            break;
                        }
                        continue;
                    }
                    if (writer.Length + MAX_ENTRY_HEADER_SIZE + packet.length > _settings.MaxDatagramSize)
                    {
                        break;
                    }

                    writer.WriteVarUInt(packet.seq - previousSeq);
                    writer.WriteVarUInt((uint)packet.length);
                    writer.WriteBytes(peer.outData.GetUnsafePtr() + packet.offset, packet.length);
                    previousSeq = packet.seq;
                    count++;

                    if (packet.sendCount == 0)
                    {
                        peer.inFlightBytes += packet.length;
                        if (peer.inFlightBytes == packet.length)
                        {
                            // The first packet in flight: silence is counted from now, not from the last acknowledgement.
                            peer.lastProgress = now;
                        }
                    }
                    else if (eligibility == Eligibility.Due)
                    {
                        PacketsResent++;
                    }
                    packet.sendCount++;
                    packet.lastSent = now;
                    peer.outPackets[index] = packet;
                }

                writer.SetByteAt(countPosition, (byte)count);
                _transport.Send(peer.id, _datagram.GetUnsafePtr(), _datagram.Length);
                DatagramsSent++;
                peer.ackPending = false;
            }
            if (resendRound && peer.backoff < MAX_BACKOFF)
            {
                peer.backoff++;
            }
        }

        private LockstepByteWriter BeginDatagram(Peer peer, out int countPosition)
        {
            _datagram.Clear();
            var writer = new LockstepByteWriter(_datagram);
            writer.WriteByte(FORMAT);
            writer.WriteUInt(peer.outEpoch);
            writer.WriteUInt(peer.inEpoch);
            writer.WriteVarUInt(peer.nextExpected);
            var mask = 0u;
            for (var i = 0; i < peer.inPackets.Length; i++)
            {
                var bit = (int)(peer.inPackets[i].seq - peer.nextExpected) - 1;
                if (bit >= ACK_MASK_BITS)
                {
                    break;
                }
                if (bit >= 0)
                {
                    mask |= 1u << bit;
                }
            }
            writer.WriteUInt(mask);
            writer.WriteVarUInt(peer.outPackets.Length > 0 ? peer.outPackets[0].seq : peer.nextSeq);
            countPosition = writer.Length;
            writer.WriteByte(0);
            return writer;
        }

        private static void DisposePeer(Peer peer)
        {
            peer.outData.Dispose();
            peer.outPackets.Dispose();
            peer.inData.Dispose();
            peer.inPackets.Dispose();
        }
    }
}
