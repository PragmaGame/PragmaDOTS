using System;
using UnityEngine;

namespace Pragma.Dots.Lockstep
{
    /// <summary>Configuration of a <see cref="LockstepRedundantChannel"/>.</summary>
    [Serializable]
    public struct LockstepRedundantChannelSettings
    {
        /// <summary>The largest datagram handed to the transport; keep it below the path MTU so that no datagram is fragmented.</summary>
        [field: SerializeField] public int MaxDatagramSize { get; set; }
        /// <summary>
        /// How many datagrams in a row carry a packet before only a timeout sends it again: a lost datagram is replaced
        /// by the next one, without waiting for a round trip. One sends every packet once.
        /// </summary>
        [field: SerializeField] public int RedundantCopies { get; set; }
        /// <summary>Bytes sent and not acknowledged yet, per connection; newer packets wait until acknowledgements free room.</summary>
        [field: SerializeField] public int MaxInFlightBytes { get; set; }
        /// <summary>Datagrams sent per connection and flush at most; a burst (a late join, a large command) spreads over flushes.</summary>
        [field: SerializeField] public int MaxDatagramsPerFlush { get; set; }

        public static LockstepRedundantChannelSettings Default => new LockstepRedundantChannelSettings
        {
            MaxDatagramSize = LockstepProtocol.DEFAULT_MAX_PACKET_SIZE,
            RedundantCopies = 3,
            MaxInFlightBytes = 64 * 1024,
            MaxDatagramsPerFlush = 8,
        };

        public void Validate()
        {
            if (MaxDatagramSize < 128)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxDatagramSize), MaxDatagramSize, "A datagram must hold at least 128 bytes.");
            }
            if (RedundantCopies < 1 || RedundantCopies > 16)
            {
                throw new ArgumentOutOfRangeException(nameof(RedundantCopies), RedundantCopies, "Copies must be in [1, 16].");
            }
            if (MaxInFlightBytes < MaxDatagramSize)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxInFlightBytes), MaxInFlightBytes, "At least one datagram must fit in flight.");
            }
            if (MaxDatagramsPerFlush < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxDatagramsPerFlush), MaxDatagramsPerFlush, "At least one datagram per flush.");
            }
        }
    }
}
