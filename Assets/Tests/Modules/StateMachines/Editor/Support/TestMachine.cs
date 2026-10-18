using Pragma.Dots.StateMachines;

namespace Pragma.Dots.Tests
{
    /// <summary>A machine that cycles Idle, Walk and Rest; how long each lasts depends on its log's seed.</summary>
    public struct TestMachine : IStateMachine
    {
        public StateMachine machine;
    }
}
