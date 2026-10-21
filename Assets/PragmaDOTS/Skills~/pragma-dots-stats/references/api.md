# Stats API reference

Namespace and assembly `Pragma.Dots.Stats`: the Stats module (`PRAGMA_DOTS_STATS`, needs Lockstep). Fields of
components are public and camelCase. Every factory and helper that takes a stat, target or type as `int` also takes the
game's enum (stored as its value through `LockstepStats.Id`, Burst-compatible).

## Contents

- Components
- System
- Helpers
- Formulas
- Actor parts

## Components

| Type | Members |
|---|---|
| `LockstepStat` | Buffer: `int type` (the game's stat id; `NONE` = 0 is none), `LockstepStatKind kind` (`Attribute`, `Resource`), `FixedPoint value`; attributes: `FixedPoint baseValue`; resources: `int cap` (an attribute type of the same entity, or `NONE`), `LockstepStatCapPolicy capPolicy` (`KeepRatio`, `Clamp`), `FixedPoint max` (the cap fitted to; negative before the first update of a resource that starts full, 0 for one created with an amount). Factories `Attribute(type, baseValue)`, `Resource(type, amount)` (uncapped), `Resource(type, cap, capPolicy)` (starts full), `Resource(type, cap, capPolicy, amount)` (starts with the amount, at most the cap); `IsAttribute`, `IsResource`. Each stat once per entity |
| `LockstepStatModifier` | Buffer, attributes only: `int stat`, `LockstepStatModifierType type`, `LockstepStatStacking stacking`, `FixedPoint value`, `LockstepStatSource source`, `int endTick` (`PERMANENT` = 0: until removed). Factories `Flat`, `AdditivePercent`, `MultiplicativePercent` `(stat, value, source = default, endTick = PERMANENT, stacking = Stack)`; `IsExpired(tick)` |
| `LockstepStatModifierType` | `Flat` (adds), `AdditivePercent` (shares summed: 0.25 adds 25 %, a sum of -1 or less gives 0), `MultiplicativePercent` (times 1 + value, stacking; -1 or less gives 0) |
| `LockstepStatStacking` | `Stack`, `Strongest` (of the `Strongest` modifiers of an attribute with one type and source `kind`, only the largest absolute value applies, the first on a tie; own modifiers and grants alike) |
| `LockstepStatChange` | Buffer, resources only: `int stat`, `FixedPoint amount`, `LockstepStatSource source`; `Create(stat, amount, source = default)`. Applied and cleared by the next `LockstepStatSystem` update |
| `LockstepStatGrant` | Buffer of a granting entity: `int target` (`ANY` = 0: every receiver), `LockstepStatModifier modifier`; `Create(modifier, target = ANY)` |
| `LockstepStatGrantor` | Buffer of a receiver: `Entity entity`, an entity whose grants it receives; `new LockstepStatGrantor(entity)`. Dropped by the stat system when the entity is gone; an entity created on the same tick can be named at once, also through a command buffer |
| `LockstepStatTarget` | Buffer of a receiver: `int value`, a group grants can target (0 is not one); `new LockstepStatTarget(value)`, `Create(enum)` |
| `LockstepStatSource` | `[Serializable]`: `uint kind`, `uint id`, `new LockstepStatSource(kind, id)`, equality. What applied a modifier, grant or change; one source's modifiers are removed together |

## System

`LockstepStatSystem`, in the default slot of `LockstepSimulationSystemGroup`: removes expired modifiers and grants,
drops grantors that no longer exist, recalculates the chunks whose stats, modifiers, grantors or targets changed, whose
grantors' grants changed, which have pending changes or expiring modifiers, fits resources to their caps, applies and
clears the changes, and writes `LockstepStat` only then (so `[WithChangeFilter(typeof(LockstepStat))]` follows real
changes). Chunks run in parallel, each writing only its own entities. A recalculation gives the same values however
often it runs.

## Helpers

`LockstepStats`, extensions on the buffers:

| Member | Notes |
|---|---|
| `TryGet(type, out LockstepStat)` | The whole stat: `value`, `max`, `baseValue` |
| `TryGetValue(type, out FixedPoint)` | An attribute's value or a resource's amount, as of the last update |
| `TryGetPendingValue(changes, type, out FixedPoint)` | A resource after the changes not yet applied, under the cap as of the last update (an attribute: its value): for payments |
| `TryGetBase(type, out FixedPoint)`, `TrySetBase(type, FixedPoint)` | Attributes only; the value follows in the next update |
| `RemoveModifiers(modifiers, source)`, `RemoveGrants(grants, source)` | Remove everything of a source, keep the order of the rest, return the count |
| `Calculate(int type, baseValue, NativeArray<LockstepStatModifier>)` | The formula below, for tooltips and previews: modifiers of other stats skipped, end ticks not checked |
| `Id(enum)` | The value an enum member is stored as |

## Formulas

- Attribute, over own modifiers and received grants:
  `value = (base + sum of Flat) * max(0, 1 + sum of AdditivePercent) * product of max(0, 1 + MultiplicativePercent)`.
- Resource: the tick's changes are summed, then the amount is kept within 0 and the cap. `KeepRatio` keeps the share
  when the cap changes (shares of large amounts computed without overflow), `Clamp` keeps the amount and cuts it to a
  lower cap. One whose cap went to zero stays empty when the cap comes back. Without its cap attribute a resource is
  uncapped.
- Timing: a modifier or grant added on tick T before `LockstepStatSystem` with `endTick` T + D applies on ticks T to
  T + D - 1.

## Actor parts

Generic bases the game closes over its enums, one line each; both implement `IMergeableActorPart`.

| Part | Fields | Merging |
|---|---|---|
| `StatsPart<TStat, TTarget>` | `Attributes` (`StatAttributeEntry<TStat>`: `Stat`, `BaseValue`), `Resources` (`StatResourceEntry<TStat>`: `Stat`, `Cap` (none: uncapped), `CapPolicy`, `Amount` (0 for a capped one: starts full)), `Targets` (`TTarget` list; 0 ignored), `ReceivesGrants` (true) | By stat: an override's entry replaces the inherited one, and a stat listed as the other kind changes kind; targets add up; `ReceivesGrants` is the override's |
| `StatGrantsPart<TStat, TTarget>` | `Source` (`LockstepStatSource`), `Grants` (`StatGrantEntry<TStat, TTarget>`: `Stat`, `Type`, `Value`, `Stacking`, `Target`; permanent) | By stat, modifier type and target; `Source` is the override's |

`StatsPart` adds `LockstepStat` (each stat once: a duplicate warns and the last one counts; a cap that is not an
attribute of the actor warns and leaves the resource uncapped), `LockstepStatModifier` with any attribute,
`LockstepStatChange` with any resource, `LockstepStatGrantor` with *Receives Grants*, `LockstepStatTarget` with any
target. `StatGrantsPart` adds `LockstepStatGrant` (grants without a stat are left out with a warning).
`NoStatTarget` (`Any` = 0) closes the parts of a game without targets.
