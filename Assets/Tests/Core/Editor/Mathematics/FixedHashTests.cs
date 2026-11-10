using System;
using NUnit.Framework;
using Pragma.Dots.Mathematics;

namespace Pragma.Dots.Tests
{
    public class FixedHashTests
    {
        [Test]
        public void Hash_IsFnv1aOverLittleEndianBytes()
        {
            var values = new[] { 0L, 1L, -1L, long.MaxValue, 65536L, -98304L };
            var hash = FixedHash.Empty;
            foreach (var value in values)
            {
                hash.Add(value);
            }

            // Reference FNV-1a written independently of the implementation under test.
            var reference = 14695981039346656037UL;
            foreach (var value in values)
            {
                var bytes = BitConverter.GetBytes(value);
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }
                foreach (var b in bytes)
                {
                    reference = unchecked((reference ^ b) * 1099511628211UL);
                }
            }

            Assert.AreEqual(reference, hash.value);
        }

        [Test]
        public void Hash_DependsOnValuesAndOrder()
        {
            var a = FixedHash.Empty;
            a.Add(new FixedVector3(1, 2, 3));
            var b = FixedHash.Empty;
            b.Add((FixedPoint)1);
            b.Add((FixedPoint)2);
            b.Add((FixedPoint)3);
            var c = FixedHash.Empty;
            c.Add(new FixedVector3(3, 2, 1));

            Assert.AreEqual(a, b, "a vector is its components in order");
            Assert.AreNotEqual(a, c);
            Assert.AreNotEqual(FixedHash.Empty, a);
        }

        [Test]
        public void Hash_OfARandomGenerator_FollowsItsState()
        {
            var random = new FixedRandom(5);
            var before = FixedHash.Empty;
            before.Add(random);
            random.NextUInt();
            var after = FixedHash.Empty;
            after.Add(random);

            Assert.AreNotEqual(before, after);
        }
    }
}
