using System;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// A tag of the actor registry, found by name with <see cref="ActorLookup.TryGetTag"/>. Look tags up once (when the
    /// registry is there) and keep the handle: checking one is a bit test.
    /// </summary>
    public readonly struct ActorTag : IEquatable<ActorTag>
    {
        /// <summary>The index of the tag in the registry's tag table.</summary>
        public readonly int index;

        public ActorTag(int index)
        {
            this.index = index;
        }

        /// <summary>False for <c>default</c>-looking handles that no lookup produced (negative index).</summary>
        public bool IsValid => index >= 0;

        public bool Equals(ActorTag other) => index == other.index;

        public override bool Equals(object obj) => obj is ActorTag other && Equals(other);

        public override int GetHashCode() => index;
    }
}
