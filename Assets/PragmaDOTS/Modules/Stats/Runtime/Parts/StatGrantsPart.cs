using System;
using System.Collections.Generic;
using Pragma.Dots.Actors;
using UnityEngine;

namespace Pragma.Dots.Stats
{
    /// <summary>
    /// The grants of an actor (<see cref="LockstepStatGrant"/>): permanent modifiers it gives to every entity whose
    /// <see cref="LockstepStatGrantor"/> names it and whose targets include the grant's, such as a squad's banner or a
    /// commander's aura. Declare one subclass per game, closed over its enums:
    /// <c>[Serializable, ActorPartMenu("Stat Grants")] public sealed class UnitGrantsPart : StatGrantsPart&lt;StatType, StatTarget&gt; { }</c>.
    /// </summary>
    /// <remarks>
    /// The part merges (<see cref="IMergeableActorPart"/>): the grants part of a derived definition or a placement lists only
    /// the grants it adds or changes, a grant being known by its stat, its modifier type and its target; the
    /// <see cref="Source"/> is the override's. Every grant carries the source, so gameplay removes them together with
    /// <see cref="LockstepStats.RemoveGrants"/>.
    /// </remarks>
    [Serializable]
    public abstract class StatGrantsPart<TStat, TTarget> : ActorPart, IMergeableActorPart
        where TStat : unmanaged, Enum
        where TTarget : unmanaged, Enum
    {
        [SerializeField, Tooltip("What the grants come from: two numbers the game picks (the kind of effect, which one).")]
        private LockstepStatSource _source;

        [SerializeField] private List<StatGrantEntry<TStat, TTarget>> _grants = new List<StatGrantEntry<TStat, TTarget>>();

        public LockstepStatSource Source
        {
            get => _source;
            set => _source = value;
        }

        public List<StatGrantEntry<TStat, TTarget>> Grants => _grants;

        public override void Build(IActorBuilder builder)
        {
            var grants = builder.AddBuffer<LockstepStatGrant>();
            foreach (var grant in _grants)
            {
                var stat = LockstepStats.Id(grant.Stat);
                if (stat == LockstepStat.NONE)
                {
                    Debug.LogWarning($"[Stats] The actor '{builder.Key}' grants a modifier without a stat: it is left out.");
                    continue;
                }
                var modifier = new LockstepStatModifier
                {
                    stat = stat,
                    type = grant.Type,
                    stacking = grant.Stacking,
                    value = grant.Value,
                    source = _source,
                    endTick = LockstepStatModifier.PERMANENT,
                };
                grants.Add(LockstepStatGrant.Create(modifier, LockstepStats.Id(grant.Target)));
            }
        }

        public ActorPart MergeOnto(ActorPart inherited)
        {
            var merged = (StatGrantsPart<TStat, TTarget>)MemberwiseClone();
            merged._grants = inherited is StatGrantsPart<TStat, TTarget> parent
                ? new List<StatGrantEntry<TStat, TTarget>>(parent._grants)
                : new List<StatGrantEntry<TStat, TTarget>>();
            foreach (var grant in _grants)
            {
                var index = merged._grants.FindIndex(entry => IsSame(entry, grant));
                if (index >= 0)
                {
                    merged._grants[index] = grant;
                }
                else
                {
                    merged._grants.Add(grant);
                }
            }
            return merged;
        }

        public ActorPart CreateOverride()
        {
            var part = (StatGrantsPart<TStat, TTarget>)MemberwiseClone();
            part._grants = new List<StatGrantEntry<TStat, TTarget>>();
            return part;
        }

        private static bool IsSame(in StatGrantEntry<TStat, TTarget> a, in StatGrantEntry<TStat, TTarget> b)
        {
            return LockstepStats.Id(a.Stat) == LockstepStats.Id(b.Stat) && a.Type == b.Type && LockstepStats.Id(a.Target) == LockstepStats.Id(b.Target);
        }
    }
}
