using System;
using System.Collections.Generic;
using Pragma.Dots.Actors;
using UnityEngine;

namespace Pragma.Dots.Stats
{
    /// <summary>
    /// The stats of an actor: its attributes and resources (<see cref="LockstepStat"/>), the groups grants can target it as
    /// (<see cref="LockstepStatTarget"/>) and whether it receives grants. Declare one subclass per game, closed over its
    /// enums: <c>[Serializable, ActorPartMenu("Stats")] public sealed class UnitStatsPart : StatsPart&lt;StatType, StatTarget&gt; { }</c>
    /// (<see cref="NoStatTarget"/> for a game whose grants reach every receiver).
    /// </summary>
    /// <remarks>
    /// <para>The part merges (<see cref="IMergeableActorPart"/>): the stats part of a derived definition or a placement lists
    /// only the stats it adds or changes (by stat) and the targets it adds; <see cref="ReceivesGrants"/> is the override's.</para>
    /// <para>Besides the stats it adds the buffers of what the actor takes part in: <see cref="LockstepStatModifier"/> with
    /// any attribute, <see cref="LockstepStatChange"/> with any resource, <see cref="LockstepStatTarget"/> with any target
    /// and <see cref="LockstepStatGrantor"/> when it receives grants (gameplay names the grantors where the actor is created).</para>
    /// </remarks>
    [Serializable]
    public abstract class StatsPart<TStat, TTarget> : ActorPart, IMergeableActorPart
        where TStat : unmanaged, Enum
        where TTarget : unmanaged, Enum
    {
        [SerializeField] private List<StatAttributeEntry<TStat>> _attributes = new List<StatAttributeEntry<TStat>>();
        [SerializeField] private List<StatResourceEntry<TStat>> _resources = new List<StatResourceEntry<TStat>>();

        [SerializeField, Tooltip("The groups of this actor that grants can target: its kind, its type.")]
        private List<TTarget> _targets = new List<TTarget>();

        [SerializeField, Tooltip("Adds LockstepStatGrantor, so that the actor receives the grants of the entities gameplay names there: its player, its squad.")]
        private bool _receivesGrants = true;

        public List<StatAttributeEntry<TStat>> Attributes => _attributes;

        public List<StatResourceEntry<TStat>> Resources => _resources;

        public List<TTarget> Targets => _targets;

        public bool ReceivesGrants
        {
            get => _receivesGrants;
            set => _receivesGrants = value;
        }

        public override void Build(IActorBuilder builder)
        {
            var stats = Collect(builder.Key.ToString());
            var hasAttribute = false;
            var hasResource = false;
            var buffer = builder.AddBuffer<LockstepStat>();
            foreach (var stat in stats)
            {
                buffer.Add(stat);
                hasAttribute |= stat.IsAttribute;
                hasResource |= stat.IsResource;
            }

            // Every buffer is filled as soon as it is added: adding another one invalidates it.
            if (hasAttribute)
            {
                builder.AddBuffer<LockstepStatModifier>();
            }
            if (hasResource)
            {
                builder.AddBuffer<LockstepStatChange>();
            }
            if (_receivesGrants)
            {
                builder.AddBuffer<LockstepStatGrantor>();
            }
            var targets = new List<int>();
            foreach (var target in _targets)
            {
                var id = LockstepStats.Id(target);
                if (id != LockstepStatGrant.ANY && !targets.Contains(id))
                {
                    targets.Add(id);
                }
            }
            if (targets.Count > 0)
            {
                var targetBuffer = builder.AddBuffer<LockstepStatTarget>();
                foreach (var target in targets)
                {
                    targetBuffer.Add(new LockstepStatTarget(target));
                }
            }
        }

        public ActorPart MergeOnto(ActorPart inherited)
        {
            var merged = (StatsPart<TStat, TTarget>)MemberwiseClone();
            var parent = inherited as StatsPart<TStat, TTarget>;
            merged._attributes = parent != null ? new List<StatAttributeEntry<TStat>>(parent._attributes) : new List<StatAttributeEntry<TStat>>();
            merged._resources = parent != null ? new List<StatResourceEntry<TStat>>(parent._resources) : new List<StatResourceEntry<TStat>>();
            merged._targets = parent != null ? new List<TTarget>(parent._targets) : new List<TTarget>();

            // A stat is an attribute or a resource: the override decides which, and with what values.
            foreach (var attribute in _attributes)
            {
                var stat = LockstepStats.Id(attribute.Stat);
                merged._resources.RemoveAll(resource => LockstepStats.Id(resource.Stat) == stat);
                Replace(merged._attributes, entry => LockstepStats.Id(entry.Stat) == stat, attribute);
            }
            foreach (var resource in _resources)
            {
                var stat = LockstepStats.Id(resource.Stat);
                merged._attributes.RemoveAll(attribute => LockstepStats.Id(attribute.Stat) == stat);
                Replace(merged._resources, entry => LockstepStats.Id(entry.Stat) == stat, resource);
            }
            foreach (var target in _targets)
            {
                if (!merged._targets.Contains(target))
                {
                    merged._targets.Add(target);
                }
            }
            return merged;
        }

        public ActorPart CreateOverride()
        {
            var part = (StatsPart<TStat, TTarget>)MemberwiseClone();
            part._attributes = new List<StatAttributeEntry<TStat>>();
            part._resources = new List<StatResourceEntry<TStat>>();
            part._targets = new List<TTarget>();
            return part;
        }

        // The stats in the order they are listed, each once (the last entry of a stat counts), with what is wrong reported.
        private List<LockstepStat> Collect(string actor)
        {
            var stats = new List<LockstepStat>();
            foreach (var attribute in _attributes)
            {
                Add(stats, LockstepStat.Attribute(LockstepStats.Id(attribute.Stat), attribute.BaseValue), attribute.Stat, actor);
            }
            foreach (var resource in _resources)
            {
                var stat = LockstepStats.Id(resource.Stat);
                var cap = LockstepStats.Id(resource.Cap);
                var created = cap == LockstepStat.NONE ? LockstepStat.Resource(stat, resource.Amount)
                    : resource.Amount.rawValue > 0 ? LockstepStat.Resource(stat, cap, resource.CapPolicy, resource.Amount)
                    : LockstepStat.Resource(stat, cap, resource.CapPolicy);
                Add(stats, created, resource.Stat, actor);
            }
            foreach (var resource in _resources)
            {
                var cap = LockstepStats.Id(resource.Cap);
                if (cap != LockstepStat.NONE && !stats.Exists(stat => stat.type == cap && stat.IsAttribute))
                {
                    Debug.LogWarning($"[Stats] The resource {resource.Stat} of the actor '{actor}' is capped by {resource.Cap}, " +
                        "which is not an attribute of the actor: the resource is uncapped.");
                }
            }
            return stats;
        }

        private static void Add(List<LockstepStat> stats, LockstepStat stat, TStat name, string actor)
        {
            if (stat.type == LockstepStat.NONE)
            {
                Debug.LogWarning($"[Stats] The actor '{actor}' lists a stat without a type: it is left out.");
                return;
            }
            var index = stats.FindIndex(existing => existing.type == stat.type);
            if (index >= 0)
            {
                Debug.LogWarning($"[Stats] The actor '{actor}' lists the stat {name} twice: the last one counts.");
                stats[index] = stat;
                return;
            }
            stats.Add(stat);
        }

        // In place of the first entry of the stat; the inherited list may list it twice (which Build warns about), and the
        // override has to win over every one of them.
        private static void Replace<T>(List<T> entries, Predicate<T> isSame, T entry)
        {
            var index = entries.FindIndex(isSame);
            if (index < 0)
            {
                entries.Add(entry);
                return;
            }
            entries[index] = entry;
            for (var i = entries.Count - 1; i > index; i--)
            {
                if (isSame(entries[i]))
                {
                    entries.RemoveAt(i);
                }
            }
        }
    }
}
