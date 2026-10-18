using Pragma.Dots.StateMachines;

namespace Pragma.Dots.Tests
{
    /// <summary>A machine component with data beside its machine, which the module refuses.</summary>
    public struct TestBadMachine : IStateMachine
    {
        public StateMachine machine;
        public int data;
    }
}
