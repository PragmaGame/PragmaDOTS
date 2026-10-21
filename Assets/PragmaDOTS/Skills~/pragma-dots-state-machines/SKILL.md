---
name: pragma-dots-state-machines
description: Give entities state machines whose states are systems with the State Machines module of Pragma DOTS (package com.pragma.dots, define PRAGMA_DOTS_STATE_MACHINES, assembly Pragma.Dots.StateMachines) - StateMachine (current, previous, requested state, ticksInState), machine components (IStateMachine), StateMachineSystemGroup<TMachine> that applies transitions in a parallel Burst job and runs its state systems, enter and exit logic, requests from states and from other systems, several machines per entity, the StateMachinePart actor part, in lockstep simulations and in ordinary Entities worlds. Use it whenever behaviour goes through phases - unit AI (idle, move, attack), abilities and spells with cooldowns, buildings under construction, match phases, doors, animations driven by state - or when flags and timers for such phases pile up in a component, even if the user only says "FSM", "AI states", "cooldown" or "phase".
---

# State machines in Pragma DOTS

A state machine is one small component per entity, `StateMachine` (`current`, `previous`, `next`, `ticksInState`),
updated by a system group of its own: on every update the group applies the requested transitions in a parallel Burst
job, then runs its systems, which are the states. Each state ticks the entities in it with a parallel job and runs its
enter and exit logic on the update of a transition. It works in any Entities world: in a lockstep simulation the
machines are deterministic state like any other, outside one they serve any game.

The State Machines module is opt-in and needs only the core: define `PRAGMA_DOTS_STATE_MACHINES` (*State Machines* in
*Project Settings > Pragma > DOTS*), namespace and assembly `Pragma.Dots.StateMachines`. Inside a lockstep simulation
the rules of the `pragma-lockstep-gameplay` skill apply to the states.

Deeper reference: the package README, section *State machines* (`Assets/PragmaDOTS/README.md` in the package
repository, or `Library/PackageCache/com.pragma.dots@*/README.md` in a project that installed it); `pragma-dots-actors`
for parts.

## API

| Type | Members |
|---|---|
| `StateMachine` | `int current` (`NONE` until the first update), `int previous`, `int next` (requested; `NONE` = 0 when none), `int ticksInState` (0 on the update of the transition); `Create(initial)`, `HasRequest`, `Is(state)`, `Entered(state)`, `Exited(state)`, `Request(state)`, `Id(enum)`; every method also takes the game's enum |
| `IStateMachine` | Marker of a machine component: a struct of the game whose only field is a `StateMachine`, checked when its group is created (an `InvalidOperationException` otherwise) |
| `StateMachineSystemGroup<TMachine>` | Abstract `ComponentSystemGroup`: applies the requests of `TMachine` in a parallel Burst job, finished before its systems, the states, run. One empty subclass per machine |
| `StateMachinePart<TMachine, TState>` | Abstract actor part: `Initial` (picked in the inspector); builds `StateMachine.Create(Initial)`. One `[Serializable]` subclass per machine |

## Declaring a machine

```csharp
public enum UnitState : byte { None = 0, Idle = 1, Move = 2, Attack = 3 }

public struct UnitBrain : IStateMachine
{
    public StateMachine machine;
}

[UpdateInGroup(typeof(LockstepSimulationSystemGroup))]   // SimulationSystemGroup in a game without lockstep
[UpdateBefore(typeof(LockstepNavSystemGroup))]           // its Move state sets destinations
public partial class UnitBrainGroup : StateMachineSystemGroup<UnitBrain>
{
}

[Serializable, ActorPartMenu("Unit Brain")]
public sealed class UnitBrainPart : StateMachinePart<UnitBrain, UnitState>
{
}
```

- 0 is no state: give the enum a `None = 0` and explicit values; the machine stores them as `int`s.
- The machine component holds the machine and nothing else. The data of the states (targets, timers beyond
  `ticksInState`, cooldown lengths) goes into components of their own.
- Give entities the machine with the part (the inspector picks the initial state) or with
  `new UnitBrain { machine = StateMachine.Create(UnitState.Idle) }` where they are created. A new machine enters its
  initial state on its first update.
- Several machines on one entity (its movement and its weapon) are several components, each with its own group.

## Writing a state

Every state is a system in the group, usually a parallel job over the machine's entities. Check the exit before
returning for entities in other states: on the update of a transition `current` is already the new state.

```csharp
[UpdateInGroup(typeof(UnitBrainGroup))]
[BurstCompile]
public partial struct UnitMoveState : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new Job { tick = SystemAPI.GetSingleton<LockstepTime>().tick }.ScheduleParallel();
    }

    [BurstCompile]
    private partial struct Job : IJobEntity
    {
        public int tick;

        private void Execute(ref UnitBrain brain, ref LockstepNavAgent agent, in MoveOrder order)
        {
            ref var machine = ref brain.machine;
            if (machine.Exited(UnitState.Move))
            {
                agent.Stop();
            }
            if (!machine.Is(UnitState.Move))
            {
                return;
            }
            // A new order while moving is not a transition: the state sees it by its tick.
            if (machine.Entered(UnitState.Move) || order.issuedTick == tick)
            {
                agent.SetDestination(order.target);
            }
            else if (agent.status == LockstepNavStatus.Arrived)
            {
                machine.Request(UnitState.Idle);
            }
        }
    }
}
```

- `ticksInState` counts the updates since the state was entered (0 on the transition): a timer needs no field of its
  own (`machine.ticksInState >= effect.durationTicks`). In a lockstep simulation the group updates once per tick, so
  durations are `seconds * time.tickRate`.
- A machine with many states over many entities pays one pass per state system: one job with a `switch` on `current`
  visits each entity once.
- When the exit of one state and the enter of the next touch the same data, order their systems (`[UpdateBefore]`) or
  handle both in one job: both run on the update of the transition, in the order of the state systems.

## Requests and timing

- The group applies the requests at the start of its update, then runs its states. A request made by a state is applied
  on the **next** update (the requesting state finishes its own update, the new state is entered and ticked next time);
  a request from a system that updates **before** the group is applied in the same update.
- The last request of an update wins: `Request` replaces an earlier one, and a request of the current state (or of
  `NONE`) cancels a pending one. The current state is never entered again: to restart it (a second attack), reset its
  data in the state, or pass through another state.
- Orders from players are requests from the system that reads the commands, before the group. It writes the order's
  data with the tick it was given on, so that a state already running sees a new order too:

```csharp
public struct MoveOrder : IComponentData
{
    public FixedVector3 target;
    public int issuedTick;
}

// In the command system ([UpdateBefore(typeof(UnitBrainGroup))]), for every unit of a valid move command:
SystemAPI.SetComponent(unit, new MoveOrder { target = place, issuedTick = time.tick });
SystemAPI.GetComponentRW<UnitBrain>(unit).ValueRW.machine.Request(UnitState.Move);
```

## Determinism and threads

- The transitions and the usual state jobs write only the entity's own machine, so any number of threads gives the same
  result, in Burst and Mono alike.
- Changing other entities' machines (a stun that hits everything in a blast) runs single-threaded in a system before the
  group, or leaves a component on the target that the target's own state reads. Never write another entity's machine
  from a parallel job.
- Outside lockstep, `ticksInState` counts updates of the group, which follow the frame rate: use elapsed time for
  timers there.

## Presentation

A view part shows the state like any component: `EntityComponentViewUnmanaged<UnitBrain>` with an update system
`public partial class UnitBrainViewUpdateSystem : EntityViewUpdateSystem<UnitBrain> { }` (`pragma-dots-views`), picking
the animation from `machine.current`. The part sees changes, not every tick: `ticksInState` changes on every update, so
the part is called on every tick while the machine runs.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `InvalidOperationException` "must have one field, a StateMachine" | The machine component has other fields: move them into components of their own |
| A state never runs | Its system lacks `[UpdateInGroup(typeof(TheMachineGroup))]`, or the group sits outside the world's update (outside `LockstepSimulationSystemGroup` in a simulation) |
| Enter logic never runs | The state checked `Entered` after returning for other states, or the machine was created with `current` set by hand instead of `StateMachine.Create` |
| Exit logic never runs | `Exited` is checked after `if (!machine.Is(state)) return;` |
| A transition takes one tick longer than expected | The request came from a state (applied next update) or from a system after the group |
| A second order of the same kind is ignored | Requesting the current state is no transition (it cancels a pending request): the state must notice the new data, such as the order's `issuedTick` |
| Results differ between clients | A parallel job wrote another entity's machine, or a state reads something outside the simulation |

## Before you finish

- [ ] The machine component has only its `StateMachine`; state data lives in components of its own.
- [ ] The group is in the right parent group and ordered against the systems it feeds (navigation, stats).
- [ ] Each state checks `Exited` before returning, then `Entered`, then ticks.
- [ ] Parallel jobs write only their own entity's machine.
- [ ] In lockstep, a session test with a late joiner shows no desync (`pragma-lockstep-desync`).
