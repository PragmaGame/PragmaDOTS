using Unity.Entities;

namespace Pragma.Dots.Tests
{
    /// <summary>A signal every player publishes on every tick, with the tick it was published on.</summary>
    public struct TestTickSignal : IComponentData
    {
        public int tick;
        public int slot;
    }
}
