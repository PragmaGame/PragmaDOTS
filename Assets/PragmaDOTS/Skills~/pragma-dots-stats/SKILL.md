---
name: pragma-dots-stats
description: Keep the numbers of units, buildings and players in a lockstep simulation with the Stats module of Pragma DOTS (package com.pragma.dots, define PRAGMA_DOTS_STATS, assembly Pragma.Dots.Stats) - attributes with flat, additive and multiplicative modifiers (LockstepStat, LockstepStatModifier), timed and non-stacking modifiers, resources with caps such as health and money (LockstepStatChange, TryGetPendingValue, regeneration, IsDepleted), grants that reach whole groups such as research and squad bonuses (LockstepStatGrant, LockstepStatGrantor, LockstepStatTarget), LockstepStatSystem and the order around it, the StatsPart and StatGrantsPart actor parts the game closes over its enums, and showing stats in views. Use it whenever the task touches health, damage, healing, armor, speed, range, sight, cooldown reduction, money or other resources, upgrades, research, buffs, debuffs, auras or level-ups - even if the user only says "add a health bar", "make units stronger" or "the player pays for a building".
---

# Stats in Pragma DOTS

Numbers that upgrades, research, abilities, auras or cover change (max health, speed, damage, range, sight) are
*attributes*; amounts that gameplay spends and refills (health, morale, energy, a player's money) are *resources*. Both
live in one buffer per entity, in `FixedPoint`, and one system applies the modifiers, the grants an entity receives and
the tick's changes, recalculating only what changed. Systems never patch these numbers by hand: two effects that each
"multiply speed by 1.5 and restore it later" break as soon as they overlap.

The Stats module is opt-in: define `PRAGMA_DOTS_STATS` (turn on *Stats* in *Project Settings > Pragma > DOTS*, which
also turns on Lockstep, which it needs), namespace and assembly `Pragma.Dots.Stats`. Stat code is simulation code: the
rules of the `pragma-lockstep-gameplay` skill apply.

Deeper reference:

- `references/api.md` - every type, member, formula and part field of the module.
- The package README, section *Stats* (`Assets/PragmaDOTS/README.md` in the package repository, or
  `Library/PackageCache/com.pragma.dots@*/README.md` in a project that installed it).
- `pragma-dots-actors` for parts and variants, `pragma-dots-views` to show stats.

## Declaring the stats of a game

```csharp
public enum StatType { None = 0, MaxHealth = 1, Speed = 2, Health = 3, Sight = 4, Money = 5 }
public enum StatTarget { Any = 0, Infantry = 1, Vehicle = 2 }   // or NoStatTarget when grants reach everyone

[Serializable, ActorPartMenu("Stats")]
public sealed class UnitStatsPart : StatsPart<StatType, StatTarget>
{
}

[Serializable, ActorPartMenu("Stat Grants")]
public sealed class UnitGrantsPart : StatGrantsPart<StatType, StatTarget>
{
}
```

- Value 0 is "none" for stats and "any" for targets. The buffers store the enum's value as an `int`: give every member
  an explicit value and never renumber one once content or replays exist (assets serialize enums by value too).
- The factories and helpers take the enum itself (`LockstepStat.Attribute(StatType.Speed, 5)`), in Burst too.
- Put the part classes in a runtime assembly that references `Pragma.Dots.Stats` and `Pragma.Dots.Actors`.

## Giving entities stats

- **Actors.** A *Stats* part: attributes (stat, base value), resources (stat, the attribute that caps it, cap policy,
  amount, the attribute it regenerates by), the targets grants reach it as, *Receives Grants* (on by default). A capped
  resource without an amount starts full; with one (a building under construction) it starts with that much; an
  uncapped one (money) starts with its amount. The part also adds the buffers the actor takes part in:
  `LockstepStatModifier` with any attribute, `LockstepStatChange` with any resource, `LockstepStatTarget` with any
  target, `LockstepStatGrantor` when it receives grants. It warns about a stat listed twice and a resource capped by, or
  regenerating by, something that is not an attribute of the actor.
- **Variants.** Both parts merge: the *Stats* of a veteran derived from the rifleman lists only *Max Health* 150; a
  placed building only its own amount. Later changes of the parent still reach the variant.
- **In code** (an entity that is not an actor, such as a player's economy created on the join tick):

```csharp
var economy = buffer.CreateEntity();
buffer.AddComponent(economy, new PlayerEconomy { slot = player.ValueRO.slot });
var stats = buffer.AddBuffer<LockstepStat>(economy);
stats.Add(LockstepStat.Resource(StatType.Money, 500));
buffer.AddBuffer<LockstepStatChange>(economy);
buffer.AddBuffer<LockstepStatGrant>(economy);   // its research reaches its units
```

Fill `LockstepStat` when you create it, then add the empty buffers: gameplay later changes stats without structural
changes.

## Order inside the tick

`LockstepStatSystem` is in the default slot of `LockstepSimulationSystemGroup`. **Change** stats in systems with
`[UpdateBefore(typeof(LockstepStatSystem))]` and **read** them in systems with `[UpdateAfter(typeof(LockstepStatSystem))]`:
both then happen on the same tick. A system that copies a stat into navigation (speed) also updates before
`LockstepNavSystemGroup`.

## Modifiers (attributes)

```csharp
[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
[UpdateBefore(typeof(LockstepStatSystem))]
public partial struct SprintSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var time = SystemAPI.GetSingleton<LockstepTime>();
        var source = new LockstepStatSource((uint)EffectKind.Sprint, 0);
        foreach (var modifiers in SystemAPI.Query<DynamicBuffer<LockstepStatModifier>>().WithAll<SprintRequest>())
        {
            // Applied again, the effect is refreshed instead of stacking.
            modifiers.RemoveModifiers(source);
            modifiers.Add(LockstepStatModifier.AdditivePercent(StatType.Speed, FixedPoint.Half, source, time.tick + 5 * time.tickRate));
        }
    }
}
```

- `Flat` adds to the base; `AdditivePercent` values are shares that add up (0.25 + 0.15 = +40 %);
  `MultiplicativePercent` ones multiply (0.5 and 0.5 make 2.25 times). The percentage factors stop at zero: -100 % or
  less zeroes an attribute and debuffs never flip its sign; the flat part may go below zero.
- `LockstepStatSource(kind, id)`: two numbers the game picks (the kind of effect, which one or whose, such as the
  caster's `LockstepEntityId`). `RemoveModifiers(source)` ends an effect; removing before adding refreshes it.
- `endTick`: the tick the modifier is removed on (`time.tick + durationTicks`; durations in ticks,
  `seconds * time.tickRate`), or `PERMANENT` (0) until removed.
- `stacking: LockstepStatStacking.Strongest`: of the `Strongest` modifiers of an attribute with the same type and source
  `kind`, only the largest applies. Two banners with the same aura give it once. An aura that lapses by itself: every
  tick, remove and add again a `Strongest` modifier with `endTick = time.tick + 1` on the units in range.
- A modifier of a resource, or of a stat the entity lacks, changes nothing.

## Changes (resources)

Damage, healing, income and payments are one-tick changes, never writes of `value`:

```csharp
changes.Add(LockstepStatChange.Create(StatType.Health, -damage, new LockstepStatSource((uint)EffectKind.Shot, attackerId)));
```

The tick's changes of a resource are summed, then the amount is kept within zero and its cap, so the order they were
added in does not matter. Until `LockstepStatSystem` runs, the buffer lists them with their sources: read "who dealt
the damage" before it, since it clears the buffer.

**Payments** check the amount the update will leave, not `value`, which does not see the payments already added on
this tick:

```csharp
// Before LockstepStatSystem, handling a BuyCommand of the player whose economy entity is `economy`.
var stats = SystemAPI.GetBufferLookup<LockstepStat>(true)[economy];
var changes = SystemAPI.GetBuffer<LockstepStatChange>(economy);
if (stats.TryGetPendingValue(changes, StatType.Money, out var money) && money >= price)
{
    changes.Add(LockstepStatChange.Create(StatType.Money, -price, new LockstepStatSource((uint)EffectKind.Purchase, 0)));
    // Spawn what was bought: two purchases on one tick cannot spend the same money.
}
```

**Caps.** A resource follows the attribute named in its `cap` on the same tick. `KeepRatio` keeps the share (80 of
100 becomes 120 of 150), so a bonus to max health neither heals nor wounds; `Clamp` keeps the amount and cuts it down to
a lower cap. `max` is the cap it was fitted to: the maximum a health bar shows (negative before the first update of a
resource that starts full).

**Regeneration.** Health that heals over time and morale that recovers are not a system of yours: name the attribute
the resource grows by per second in its `regen` (*Regen* in the part, or `LockstepStat.Resource(...).WithRegen(StatType.HealthRegen)`
in code). `LockstepStatSystem` adds `rate * deltaTime` on every tick while the resource is below its cap (an uncapped
one without end), before the tick's changes; a rate of zero or less does nothing, and the attribute takes modifiers
(slower healing in combat). `TryGetPendingValue` leaves the tick's regeneration out: it only adds.

**Pending and depleted.** A resource that starts full holds zero until the first `LockstepStatSystem` update fills it,
and an entity created after that system on a tick waits for the next tick (`IsPending`). Check whether a resource ran
out with `IsDepleted` (not pending, and zero), never with `value <= 0` or with `max > 0` workarounds.

## Grants (groups)

A grant is a modifier an entity gives to every entity whose `LockstepStatGrantor` buffer names it and whose
`LockstepStatTarget`s include the grant's target (`LockstepStatGrant.ANY` = 0 for every receiver):

```csharp
// Research: one grant on the player's economy reaches all of its infantry, also the units made later...
grants.Add(LockstepStatGrant.Create(LockstepStatModifier.AdditivePercent(StatType.MaxHealth, FixedPoint.Half, research), StatTarget.Infantry));
// ...because every unit of the player names that entity where the unit is created.
buffer.AppendToBuffer(unit, new LockstepStatGrantor(economy));
```

- Adding, removing or expiring a grant updates every receiver on the same tick; a receiver that starts inheriting later
  gets the grants in place. `RemoveGrants(source)` ends them.
- A grantor that no longer exists is dropped, and with it what it gave: a squad's banner dies, its bonus goes.
- `LockstepStatGrantor` holds an `Entity`: a reference inside the simulation world, which the checksum hashes by its
  target. Never sort by it, hash it or send it; commands name entities by `LockstepEntityId`.
- Permanent grants of an actor (a commander's aura over its squad) come from its *Stat Grants* part (a source and
  grants: stat, modifier type, value, stacking, target).

## Reading stats

```csharp
stats.TryGet(StatType.Health, out var health);       // health.value, health.max for a capped resource
stats.TryGetValue(StatType.Speed, out var speed);
stats.TryGetBase(StatType.MaxHealth, out var baseMax);
stats.TrySetBase(StatType.MaxHealth, baseMax + 20);  // a level up: the value follows in the next stat update
```

Copy a stat into another component (agent speed, vision radius, weapon range) in a job that runs only when stats
changed, and read the stats with `in`: `LockstepStatSystem` writes them only when they change, and a read-write access
marks them changed and makes it recalculate them every tick.

```csharp
[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
[UpdateAfter(typeof(LockstepStatSystem))]
[UpdateBefore(typeof(LockstepNavSystemGroup))]
public partial struct SpeedStatSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        new ApplyJob().ScheduleParallel();
    }

    [WithChangeFilter(typeof(LockstepStat))]
    private partial struct ApplyJob : IJobEntity
    {
        private void Execute(ref LockstepNavAgent agent, in DynamicBuffer<LockstepStat> stats)
        {
            if (stats.TryGetValue(StatType.Speed, out var speed))
            {
                agent.speed = speed;
            }
        }
    }
}
```

Death works the same way: a job after `LockstepStatSystem`, filtered on `LockstepStat`, that finds `Health`
`IsDepleted` and records the death through the end-of-tick command buffer (`[ChunkIndexInQuery]` sort key), or puts the unit into a
dying state its view can animate. `SystemAPI.Query<DynamicBuffer<LockstepStat>>()` is read-write: prefer the job, or a
read-only `SystemAPI.GetBufferLookup<LockstepStat>(true)`.

## Showing stats

A view part derived from `EntityBufferView<LockstepStat>` and one update system per buffer type,
`public partial class StatViewUpdateSystem : EntityBufferViewUpdateSystem<LockstepStat> { }` (`pragma-dots-views`). A
health bar is `value / max`, skipped while `max` is not above zero. Tooltips and previews compute what a modifier
would give with `LockstepStats.Calculate(type, baseValue, modifiers)`, without touching the simulation.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| A modifier or change has no effect | Added after `LockstepStatSystem` (it applies on the next tick), the entity lacks the buffer (the part adds `LockstepStatModifier` only with an attribute, `LockstepStatChange` only with a resource), the stat is of the other kind, or its stat is 0 |
| A buff stacks when cast twice | Its source is not removed before it is added again, or each cast picks another source |
| Two auras of the same kind add up | They are not `Strongest`, or their sources have different `kind`s |
| Health jumps when max health changes | The resource uses `Clamp`; `KeepRatio` keeps the share |
| Two purchases spend the same money | The check reads `TryGetValue`: use `TryGetPendingValue` with the changes buffer |
| A unit dies or a squad breaks on the tick it appears | The check reads `value <= 0` of a resource still `IsPending`: use `IsDepleted` |
| Health does not regenerate | The resource has no `regen`, the attribute it names is missing or not above zero, or it is already at its cap |
| A research bonus misses new units | The unit does not name the grantor (`LockstepStatGrantor`), lacks the target, or *Receives Grants* is off |
| Stats recalculate every tick | A system accesses `LockstepStat` read-write every tick: read with `in` or a read-only lookup |
| Values differ after renaming an enum member's value | Stats are stored by value: renumbering changes content, saves and replays |

## Before you finish

- [ ] Changes before `LockstepStatSystem`, reads after it; nothing writes a resource's `value` or an attribute's value.
- [ ] Every effect has a `LockstepStatSource`, removed before it is applied again; timed ones have an `endTick`.
- [ ] Prices are checked with `TryGetPendingValue`.
- [ ] Systems that follow stats read them with `in` and a change filter.
- [ ] The stat and target enums have explicit, stable values; 0 is none / any.
- [ ] A session test with latency and jitter (a late joiner too) shows no desync (`pragma-lockstep-desync`).
