using Unity.Entities;

namespace Pragma.Dots.Tests
{
    /// <summary>What <see cref="TestTickSignalSystem"/> sent and received; stale counts signals not from the previous tick.</summary>
    public struct TestTickSignalCounter : IComponentData
    {
        public int sent;
        public int received;
        public int stale;
    }
}
