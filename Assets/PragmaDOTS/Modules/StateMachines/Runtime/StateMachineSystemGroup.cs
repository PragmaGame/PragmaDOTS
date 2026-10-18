using Unity.Entities;
using Unity.Jobs;

namespace Pragma.Dots.StateMachines
{
    /// <summary>
    /// Updates the machines of the component <typeparamref name="TMachine"/>: applies the transitions requested since the
    /// previous update, then runs its systems, the states of the machine. Declare one empty subclass per machine where the
    /// machine should update, and put the state systems into it:
    /// <c>[UpdateInGroup(typeof(LockstepSimulationSystemGroup))] public partial class SpellMachineGroup : StateMachineSystemGroup&lt;SpellMachine&gt; { }</c>,
    /// then <c>[UpdateInGroup(typeof(SpellMachineGroup))]</c> on each state's system.
    /// </summary>
    /// <remarks>
    /// The transitions run as a parallel Burst job and are finished before the states run: the states are systems inside
    /// this update, so they could not wait for a job of the group otherwise. A state's system usually schedules a parallel
    /// job over the machine's entities that writes only the entity's own machine, so its result does not depend on the
    /// threads, also in a lockstep simulation.
    /// </remarks>
    public abstract partial class StateMachineSystemGroup<TMachine> : ComponentSystemGroup where TMachine : unmanaged, IStateMachine
    {
        // Created with the EntityManager, not GetEntityQuery: a query of the group would register the group as a writer of
        // the machine after its states ran and hide their jobs from the systems that follow.
        private EntityQuery _machines;

        protected override void OnCreate()
        {
            base.OnCreate();
            StateMachineLayout.Validate(typeof(TMachine));
            _machines = EntityManager.CreateEntityQuery(ComponentType.ReadWrite<TMachine>());
        }

        protected override void OnDestroy()
        {
            _machines.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            EntityManager.CompleteDependencyBeforeRW<TMachine>();
            var job = new StateTransitionJob { machines = EntityManager.GetDynamicComponentTypeHandle(ComponentType.ReadWrite<TMachine>()) };
            job.ScheduleParallel(_machines, default).Complete();
            base.OnUpdate();
        }
    }
}
