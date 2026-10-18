using System;
using Pragma.Dots.Mathematics;
using UnityEngine;

namespace Pragma.Dots.Stats
{
    /// <summary>An attribute of a <see cref="StatsPart{TStat, TTarget}"/>: the stat and its base value.</summary>
    [Serializable]
    public struct StatAttributeEntry<TStat> where TStat : unmanaged, Enum
    {
        public StatAttributeEntry(TStat stat, FixedPoint baseValue) : this()
        {
            Stat = stat;
            BaseValue = baseValue;
        }

        [field: SerializeField] public TStat Stat { get; set; }

        [field: SerializeField] public FixedPoint BaseValue { get; set; }
    }
}
