using System;
using Pragma.Dots.Mathematics;
using Unity.Entities;

namespace Pragma.Dots.Stats
{
    /// <summary>
    /// One stat of an entity: an attribute (max health, speed, damage, sight) or a resource (health, morale, a player's
    /// money). The buffer holds each stat of the entity once.
    /// </summary>
    /// <remarks>
    /// The game names its stats: <see cref="type"/> is an id of its own, usually an enum value (the generic overloads take
    /// the enum itself), and 0 is <see cref="NONE"/>. <see cref="LockstepStatSystem"/> keeps <see cref="value"/> up to
    /// date: attributes from their base value and modifiers, resources from their changes and cap. Gameplay reads it with
    /// <see cref="LockstepStats.TryGet(DynamicBuffer{LockstepStat}, int, out LockstepStat)"/> after that system.
    /// </remarks>
    public struct LockstepStat : IBufferElementData
    {
        /// <summary>No stat: the <see cref="cap"/> of an uncapped resource. Never a <see cref="type"/>.</summary>
        public const int NONE = 0;

        /// <summary>The game's id of the stat.</summary>
        public int type;
        public LockstepStatKind kind;
        /// <summary>Resources: what the amount does when the cap changes.</summary>
        public LockstepStatCapPolicy capPolicy;
        /// <summary>Resources: the <see cref="type"/> of the attribute of the same entity that caps the amount, or <see cref="NONE"/>.</summary>
        public int cap;
        /// <summary>
        /// Resources: the <see cref="type"/> of the attribute of the same entity whose value the amount grows by every second
        /// while it is below its cap (health regeneration, morale recovery), or <see cref="NONE"/>; set it with
        /// <see cref="WithRegen(int)"/>. A rate of zero or less does nothing; an uncapped resource grows without end.
        /// </summary>
        public int regen;
        /// <summary>Attributes: the value without modifiers, authored or changed with <see cref="LockstepStats.TrySetBase(DynamicBuffer{LockstepStat}, int, FixedPoint)"/>.</summary>
        public FixedPoint baseValue;
        /// <summary>Attributes: the base value with the modifiers applied. Resources: the amount.</summary>
        public FixedPoint value;
        /// <summary>
        /// Capped resources: the cap the amount was last fitted to, as of the last <see cref="LockstepStatSystem"/> update,
        /// which is the maximum a bar shows. Negative for a resource that starts full, until the first update fills it
        /// (<see cref="IsPending"/>); zero for one that starts with an amount, which the first update keeps under the cap.
        /// </summary>
        public FixedPoint max;

        public bool IsAttribute => kind == LockstepStatKind.Attribute;

        public bool IsResource => kind == LockstepStatKind.Resource;

        /// <summary>
        /// A capped resource that starts full and has not been filled yet: its <see cref="value"/> is zero, not its amount,
        /// until the first <see cref="LockstepStatSystem"/> update after the entity was created. An entity created after that
        /// system on a tick stays pending until the next tick, so a check of the amount (is the unit dead, is the squad
        /// broken) skips it; <see cref="IsDepleted"/> does. A resource whose cap attribute the entity lacks is never filled.
        /// </summary>
        public bool IsPending => IsResource && max.rawValue < 0;

        /// <summary>
        /// A resource that has run out: its amount is zero. False for an attribute and for a resource still
        /// <see cref="IsPending"/>, which is full rather than empty.
        /// </summary>
        public bool IsDepleted => IsResource && max.rawValue >= 0 && value.rawValue <= 0;

        /// <summary>
        /// This resource with <see cref="regen"/> set to <paramref name="rate"/>: it grows by that attribute's value every
        /// second while it is below its cap.
        /// </summary>
        public LockstepStat WithRegen(int rate)
        {
            var stat = this;
            stat.regen = rate;
            return stat;
        }

        /// <inheritdoc cref="WithRegen(int)"/>
        public LockstepStat WithRegen<TType>(TType rate) where TType : unmanaged, Enum
        {
            return WithRegen(LockstepStats.Id(rate));
        }

        /// <summary>An attribute without modifiers yet: its value is the base value.</summary>
        public static LockstepStat Attribute(int type, FixedPoint baseValue)
        {
            return new LockstepStat { type = type, kind = LockstepStatKind.Attribute, baseValue = baseValue, value = baseValue };
        }

        /// <inheritdoc cref="Attribute(int, FixedPoint)"/>
        public static LockstepStat Attribute<TType>(TType type, FixedPoint baseValue) where TType : unmanaged, Enum
        {
            return Attribute(LockstepStats.Id(type), baseValue);
        }

        /// <summary>An uncapped resource holding <paramref name="amount"/>, like a player's money.</summary>
        public static LockstepStat Resource(int type, FixedPoint amount)
        {
            return new LockstepStat { type = type, kind = LockstepStatKind.Resource, value = amount };
        }

        /// <inheritdoc cref="Resource(int, FixedPoint)"/>
        public static LockstepStat Resource<TType>(TType type, FixedPoint amount) where TType : unmanaged, Enum
        {
            return Resource(LockstepStats.Id(type), amount);
        }

        /// <summary>
        /// A resource capped by the attribute <paramref name="cap"/> of the same entity, like health by max health. It
        /// starts full: the first <see cref="LockstepStatSystem"/> update fills it to the cap, modifiers included.
        /// </summary>
        public static LockstepStat Resource(int type, int cap, LockstepStatCapPolicy capPolicy)
        {
            return new LockstepStat { type = type, kind = LockstepStatKind.Resource, cap = cap, capPolicy = capPolicy, max = -FixedPoint.One };
        }

        /// <inheritdoc cref="Resource(int, int, LockstepStatCapPolicy)"/>
        public static LockstepStat Resource<TType>(TType type, TType cap, LockstepStatCapPolicy capPolicy) where TType : unmanaged, Enum
        {
            return Resource(LockstepStats.Id(type), LockstepStats.Id(cap), capPolicy);
        }

        /// <summary>
        /// A resource capped by the attribute <paramref name="cap"/> of the same entity that starts with
        /// <paramref name="amount"/>, at most the cap: a building under construction, energy that starts empty.
        /// </summary>
        public static LockstepStat Resource(int type, int cap, LockstepStatCapPolicy capPolicy, FixedPoint amount)
        {
            return new LockstepStat
            {
                type = type, kind = LockstepStatKind.Resource, cap = cap, capPolicy = capPolicy, value = FixedMath.Max(amount, FixedPoint.Zero),
            };
        }

        /// <inheritdoc cref="Resource(int, int, LockstepStatCapPolicy, FixedPoint)"/>
        public static LockstepStat Resource<TType>(TType type, TType cap, LockstepStatCapPolicy capPolicy, FixedPoint amount) where TType : unmanaged, Enum
        {
            return Resource(LockstepStats.Id(type), LockstepStats.Id(cap), capPolicy, amount);
        }
    }
}
