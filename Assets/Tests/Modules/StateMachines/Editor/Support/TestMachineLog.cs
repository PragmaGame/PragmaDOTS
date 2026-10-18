using Unity.Entities;

namespace Pragma.Dots.Tests
{
    /// <summary>What the states of a <see cref="TestMachine"/> did, and the seed that sets how long they last.</summary>
    public struct TestMachineLog : IComponentData
    {
        public int seed;
        public int idleEnters;
        public int idleExits;
        public int walkEnters;
        public int walkExits;
        public int walkTicks;
        public int restEnters;
        public int restExits;
        public int managedRuns;

        public int IdleTicks => 1 + seed % 3;
        public int WalkTicks => 2 + seed % 4;
        public int RestTicks => 1;
    }
}
