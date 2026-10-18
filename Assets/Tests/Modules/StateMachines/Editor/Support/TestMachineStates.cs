using Pragma.Dots.StateMachines;
using Unity.Burst;

namespace Pragma.Dots.Tests
{
    /// <summary>
    /// The states of <see cref="TestMachine"/>, shared by the state systems (Burst jobs) and the managed model the tests
    /// compare them with.
    /// </summary>
    internal static class TestMachineStates
    {
        public static void Idle(ref StateMachine machine, ref TestMachineLog log)
        {
            CountManagedRun(ref log);
            if (machine.Exited(TestState.Idle))
            {
                log.idleExits++;
            }
            if (!machine.Is(TestState.Idle))
            {
                return;
            }
            if (machine.Entered(TestState.Idle))
            {
                log.idleEnters++;
            }
            if (machine.ticksInState >= log.IdleTicks)
            {
                machine.Request(TestState.Walk);
            }
        }

        public static void Walk(ref StateMachine machine, ref TestMachineLog log)
        {
            CountManagedRun(ref log);
            if (machine.Exited(TestState.Walk))
            {
                log.walkExits++;
            }
            if (!machine.Is(TestState.Walk))
            {
                return;
            }
            if (machine.Entered(TestState.Walk))
            {
                log.walkEnters++;
            }
            log.walkTicks++;
            if (machine.ticksInState >= log.WalkTicks)
            {
                machine.Request(TestState.Rest);
            }
        }

        public static void Rest(ref StateMachine machine, ref TestMachineLog log)
        {
            CountManagedRun(ref log);
            if (machine.Exited(TestState.Rest))
            {
                log.restExits++;
            }
            if (!machine.Is(TestState.Rest))
            {
                return;
            }
            if (machine.Entered(TestState.Rest))
            {
                log.restEnters++;
            }
            if (machine.ticksInState >= log.RestTicks)
            {
                machine.Request(TestState.Idle);
            }
        }

        // Burst drops the call: a job compiled with Burst leaves the count at zero.
        [BurstDiscard]
        private static void CountManagedRun(ref TestMachineLog log)
        {
            log.managedRuns++;
        }
    }
}
