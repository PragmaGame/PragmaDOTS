#if PRAGMA_DOTS_LOCKSTEP
using Pragma.Dots.Lockstep;
#endif
using Pragma.Dots.StateMachines;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    // The tests of plain worlds create the group by hand; a lockstep session finds its place from the attribute.
#if PRAGMA_DOTS_LOCKSTEP
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
#endif
    public partial class TestMachineGroup : StateMachineSystemGroup<TestMachine>
    {
    }
}
