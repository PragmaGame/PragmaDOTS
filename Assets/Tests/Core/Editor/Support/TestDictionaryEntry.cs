using Pragma.Dots.Utils;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    [InternalBufferCapacity(0)]
    public struct TestDictionaryEntry : IBufferElementData, IDictionaryElement<int>
    {
        public int key;
        public int value;

        public int Key
        {
            get => key;
            set => key = value;
        }
    }
}
