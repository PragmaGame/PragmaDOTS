using NUnit.Framework;
using Pragma.Dots.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Pragma.Dots.Tests
{
    public class EnumIdTests
    {
        private enum ByteValue : byte
        {
            Value = 200,
        }

        private enum ShortValue : short
        {
            Value = 3000,
        }

        private enum IntValue
        {
            Value = 70000,
        }

        private enum LongValue : long
        {
            Value = 5,
        }

        [Test]
        public void Of_IsTheValueOfAnEnumOfAnySize_InBurstToo()
        {
            var expected = new[] { 200, 3000, 70000, 5 };
            CollectionAssert.AreEqual(expected, new[]
            {
                EnumId.Of(ByteValue.Value), EnumId.Of(ShortValue.Value), EnumId.Of(IntValue.Value), EnumId.Of(LongValue.Value),
            });
            using (var ids = new NativeArray<int>(4, Allocator.TempJob))
            {
                new IdJob { ids = ids }.Run();
                CollectionAssert.AreEqual(expected, ids.ToArray(), "Burst gives the same ids");
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct IdJob : IJob
        {
            public NativeArray<int> ids;

            public void Execute()
            {
                ids[0] = EnumId.Of(ByteValue.Value);
                ids[1] = EnumId.Of(ShortValue.Value);
                ids[2] = EnumId.Of(IntValue.Value);
                ids[3] = EnumId.Of(LongValue.Value);
            }
        }
    }
}
