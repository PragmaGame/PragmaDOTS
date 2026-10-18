using Pragma.Dots.StateMachines;

namespace Pragma.Dots.Tests
{
    /// <summary>A second machine of the same entities, with no states of its own.</summary>
    public struct TestOtherMachine : IStateMachine
    {
        public StateMachine machine;
    }
}
