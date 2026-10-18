namespace Pragma.Dots.Tests
{
    // A byte enum with a state of value 2: the case Burst once compiled wrong inside generic structs.
    public enum TestState : byte
    {
        None = 0,
        Idle = 1,
        Walk = 2,
        Rest = 3,
    }
}
