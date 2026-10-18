using System;
using Pragma.Dots.Mathematics;
using UnityEngine;

namespace Pragma.Dots.Stats
{
    /// <summary>
    /// A resource of a <see cref="StatsPart{TStat, TTarget}"/>: the stat, the attribute that caps it (none for an uncapped
    /// one), what it does when the cap changes, and the amount an uncapped one starts with. A capped resource starts full.
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

        public StatResourceEntry(TStat stat, FixedPoint amount) : this()
        {
            Stat = stat;
            Amount = amount;
        }

        [field: SerializeField] public TStat Stat { get; set; }

        [field: SerializeField, Tooltip("The attribute of this actor that caps the amount; none for an uncapped resource such as money.")]
        public TStat Cap { get; set; }

        [field: SerializeField] public LockstepStatCapPolicy CapPolicy { get; set; }

        [field: SerializeField, Tooltip("The amount an uncapped resource starts with; a capped one starts full.")]
        public FixedPoint Amount { get; set; }
    }
}
