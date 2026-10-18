using System;
using Pragma.Dots.Actors;
using Pragma.Dots.StateMachines;

namespace Pragma.Dots.Tests
{
    [Serializable, ActorPartMenu("Tests/Test Machine")]
    public sealed class TestMachinePart : StateMachinePart<TestMachine, TestState>
    {
    }
}
