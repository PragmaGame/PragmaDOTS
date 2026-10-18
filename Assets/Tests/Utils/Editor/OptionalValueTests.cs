using NUnit.Framework;

namespace Pragma.Lockstep.Utils.Tests
{
    public class OptionalValueTests
    {
        [Test]
        public void OptionalValue_IsSetOnlyWithAValue()
        {
            var empty = default(OptionalValue<int>);
            Assert.IsFalse(empty.IsSet);
            Assert.IsNull(empty.ToNullable());
            Assert.IsFalse(OptionalValue<int>.FromNullable(null).IsSet);

            var assigned = empty;
            assigned.Value = 0;
            Assert.IsTrue(assigned.IsSet);
            Assert.AreEqual(0, assigned.ToNullable());

            var created = OptionalValue<int>.FromNullable(7);
            Assert.IsTrue(created.IsSet);
            Assert.AreEqual(7, created.Value);
            Assert.AreEqual(7, new OptionalValue<int>(7).ToNullable());
        }
    }
}
