using System;

namespace Pragma.Dots.Actors
{
    /// <summary>The tags of an actor type: one bit per tag of the registry's tag table, up to <see cref="CAPACITY"/> tags.</summary>
    public struct ActorTagSet : IEquatable<ActorTagSet>
    {
        public const int CAPACITY = 128;

        public ulong low;
        public ulong high;

        public bool IsEmpty => (low | high) == 0;

        public bool Contains(ActorTag tag)
        {
            var index = tag.index;
            if (index < 0 || index >= CAPACITY)
            {
                return false;
            }
            return index < 64 ? ((low >> index) & 1) != 0 : ((high >> (index - 64)) & 1) != 0;
        }

        /// <summary>True when the set has at least one tag of <paramref name="other"/>.</summary>
        public bool Overlaps(in ActorTagSet other) => ((low & other.low) | (high & other.high)) != 0;

        /// <summary>True when the set has every tag of <paramref name="other"/>.</summary>
        public bool ContainsAll(in ActorTagSet other) => (low & other.low) == other.low && (high & other.high) == other.high;

        public void Add(ActorTag tag)
        {
            var index = tag.index;
            if (index < 0 || index >= CAPACITY)
            {
                throw new ArgumentOutOfRangeException(nameof(tag), "An actor tag set holds tags 0 to 127.");
            }
            if (index < 64)
            {
                low |= 1UL << index;
            }
            else
            {
                high |= 1UL << (index - 64);
            }
        }

        public bool Equals(ActorTagSet other) => low == other.low && high == other.high;

        public override bool Equals(object obj) => obj is ActorTagSet other && Equals(other);

        public override int GetHashCode() => (low ^ (high * 31)).GetHashCode();
    }
}
