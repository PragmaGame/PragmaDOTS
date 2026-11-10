using System;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// A 64-bit FNV-1a hash of raw values, for comparing simulation state between clients and replays: the same
    /// values added in the same order give the same hash everywhere, in Mono, IL2CPP and Burst.
    /// </summary>
    /// <remarks>
    /// Start from <see cref="Empty"/> and add every value that defines the state, in a fixed order. It is a mutable
    /// struct: keep it in a local and add to that local. Never hash <c>ToString</c> output or floats. A replay can store
    /// the hashes it recorded: the type serializes as its <see cref="value"/>.
    /// </remarks>
    [Serializable]
    [DataContract]
    public struct FixedHash : IEquatable<FixedHash>
    {
        private const ulong OFFSET_BASIS = 14695981039346656037UL;
        private const ulong PRIME = 1099511628211UL;

        [DataMember] public ulong value;

        /// <summary>The hash of nothing: the FNV-1a offset basis.</summary>
        public static FixedHash Empty => new FixedHash { value = OFFSET_BASIS };

        /// <summary>Adds the eight bytes of a raw value, lowest first.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(ulong raw)
        {
            var hash = value;
            for (var shift = 0; shift < 64; shift += 8)
            {
                hash = unchecked((hash ^ ((raw >> shift) & 0xFF)) * PRIME);
            }
            value = hash;
        }

        public void Add(long raw) => Add(unchecked((ulong)raw));

        public void Add(int raw) => Add((long)raw);

        public void Add(uint raw) => Add((ulong)raw);

        public void Add(bool flag) => Add(flag ? 1UL : 0UL);

        public void Add(FixedPoint x) => Add(x.rawValue);

        public void Add(FixedVector2 v)
        {
            Add(v.x.rawValue);
            Add(v.y.rawValue);
        }

        public void Add(FixedVector3 v)
        {
            Add(v.x.rawValue);
            Add(v.y.rawValue);
            Add(v.z.rawValue);
        }

        public void Add(FixedVector4 v)
        {
            Add(v.x.rawValue);
            Add(v.y.rawValue);
            Add(v.z.rawValue);
            Add(v.w.rawValue);
        }

        /// <summary>Adds the columns in order.</summary>
        public void Add(FixedMatrix3x3 m)
        {
            Add(m.c0);
            Add(m.c1);
            Add(m.c2);
        }

        /// <summary>Adds the columns in order.</summary>
        public void Add(FixedMatrix4x4 m)
        {
            Add(m.c0);
            Add(m.c1);
            Add(m.c2);
            Add(m.c3);
        }

        public void Add(FixedQuaternion q)
        {
            Add(q.x.rawValue);
            Add(q.y.rawValue);
            Add(q.z.rawValue);
            Add(q.w.rawValue);
        }

        public void Add(FixedPose pose)
        {
            Add(pose.position);
            Add(pose.rotation);
        }

        /// <summary>Adds the generator's state, so two hashes match only when the streams continue alike.</summary>
        public void Add(FixedRandom random)
        {
            Add(random.state);
            Add(random.increment);
        }

        public bool Equals(FixedHash other) => value == other.value;

        public override bool Equals(object obj) => obj is FixedHash other && value == other.value;

        public override int GetHashCode() => value.GetHashCode();

        public static bool operator ==(FixedHash a, FixedHash b) => a.value == b.value;

        public static bool operator !=(FixedHash a, FixedHash b) => a.value != b.value;

        public override string ToString() => value.ToString("X16");
    }
}
