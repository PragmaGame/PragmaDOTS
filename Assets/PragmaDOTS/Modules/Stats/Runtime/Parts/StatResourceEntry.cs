using System;
using Pragma.Dots.Mathematics;
using UnityEngine;

namespace Pragma.Dots.Stats
{
    /// <summary>
    /// A resource of a <see cref="StatsPart{TStat, TTarget}"/>: the stat, the attribute that caps it (none for an uncapped
    /// one), what it does when the cap changes, the amount it starts with and the attribute it regenerates by. A capped
    /// resource without an amount starts full; with one, it starts with that much, at most the cap.
    /// </summary>
    [Serializable]
    public struct StatResourceEntry<TStat> where TStat : unmanaged, Enum
    {
        public StatResourceEntry(TStat stat, TStat cap, LockstepStatCapPolicy capPolicy = LockstepStatCapPolicy.KeepRatio) : this()
        {
            Stat = stat;
            Cap = cap;
            CapPolicy = capPolicy;
        }

        public StatResourceEntry(TStat stat, TStat cap, LockstepStatCapPolicy capPolicy, FixedPoint amount) : this(stat, cap, capPolicy)
        {
            Amount = amount;
        }

        public StatResourceEntry(TStat stat, FixedPoint amount) : this()
        {
            Stat = stat;
            Amount = amount;
        }

        [field: SerializeField] public TStat Stat { get; set; }

        [field: SerializeField, Tooltip("The attribute of this actor that caps the amount; none for an uncapped resource such as money.")]
        public TStat Cap { get; set; }

        [field: SerializeField] public LockstepStatCapPolicy CapPolicy { get; set; }

        [field: SerializeField, Tooltip("The amount it starts with. A capped resource gets at most its cap, and starts full without an amount (zero).")]
        public FixedPoint Amount { get; set; }

        [field: SerializeField, Tooltip("The attribute of this actor whose value the amount grows by every second while it is below its cap; none for a resource that does not regenerate.")]
        public TStat Regen { get; set; }
    }
}
