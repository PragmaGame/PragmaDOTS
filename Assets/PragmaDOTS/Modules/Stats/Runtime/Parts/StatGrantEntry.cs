using System;
using Pragma.Dots.Mathematics;
using UnityEngine;

namespace Pragma.Dots.Stats
{
    /// <summary>
    /// A grant of a <see cref="StatGrantsPart{TStat, TTarget}"/>: the modifier it gives (stat, type, value, stacking) and the
    /// group of receivers it reaches (the target's zero value reaches every receiver).
    /// </summary>
    [Serializable]
    public struct StatGrantEntry<TStat, TTarget> where TStat : unmanaged, Enum where TTarget : unmanaged, Enum
    {
        public StatGrantEntry(TStat stat, LockstepStatModifierType type, FixedPoint value, TTarget target,
            LockstepStatStacking stacking = LockstepStatStacking.Stack) : this()
        {
            Stat = stat;
            Type = type;
            Value = value;
            Target = target;
            Stacking = stacking;
        }

        [field: SerializeField] public TStat Stat { get; set; }

        [field: SerializeField] public LockstepStatModifierType Type { get; set; }

        [field: SerializeField] public FixedPoint Value { get; set; }

        [field: SerializeField] public LockstepStatStacking Stacking { get; set; }

        [field: SerializeField, Tooltip("The receivers it reaches: those with this target; the zero value reaches every receiver.")]
        public TTarget Target { get; set; }
    }
}
