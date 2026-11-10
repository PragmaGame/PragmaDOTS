using System;
using NUnit.Framework;
using Pragma.Dots.Mathematics;

namespace Pragma.Dots.Tests
{
    public class CheckedArithmeticTests
    {
        [Test]
        public void TryAdd_AndTrySubtract_ReportOverflowAndSaturate()
        {
            Assert.IsTrue(FixedPoint.TryAdd(FixedPoint.FromFraction(3, 2), FixedPoint.Two, out var sum));
            Assert.AreEqual(FixedPoint.FromFraction(7, 2), sum);

            Assert.IsFalse(FixedPoint.TryAdd(FixedPoint.MaxValue, FixedPoint.Epsilon, out sum));
            Assert.AreEqual(FixedPoint.MaxValue, sum);
            Assert.IsFalse(FixedPoint.TryAdd(FixedPoint.MinValue, -FixedPoint.Epsilon, out sum), "long.MinValue is outside the range");
            Assert.AreEqual(FixedPoint.MinValue, sum);

            Assert.IsTrue(FixedPoint.TrySubtract(FixedPoint.MaxValue, FixedPoint.One, out var difference));
            Assert.AreEqual(FixedPoint.MaxValue - FixedPoint.One, difference);
            Assert.IsFalse(FixedPoint.TrySubtract(FixedPoint.MinValue, FixedPoint.Epsilon, out difference));
            Assert.AreEqual(FixedPoint.MinValue, difference);
            Assert.IsFalse(FixedPoint.TrySubtract(FixedPoint.MaxValue, FixedPoint.MinusOne, out difference));
            Assert.AreEqual(FixedPoint.MaxValue, difference);
        }

        [Test]
        public void TryMultiply_KnowsTheLargestProduct()
        {
            Assert.IsTrue(FixedPoint.TryMultiply((FixedPoint)46340, (FixedPoint)46340, out var product));
            Assert.AreEqual((FixedPoint)46340 * (FixedPoint)46340, product);
            Assert.IsFalse(FixedPoint.TryMultiply((FixedPoint)46341, (FixedPoint)46341, out product), "46341² · 2³² does not fit in 64 bits");
            Assert.AreEqual(FixedPoint.MaxValue, product);
            Assert.IsFalse(FixedPoint.TryMultiply((FixedPoint)(-46341), (FixedPoint)46341, out product));
            Assert.AreEqual(FixedPoint.MinValue, product);

            Assert.IsTrue(FixedPoint.TryMultiply(FixedPoint.FromRaw(FixedPoint.MAX_RAW / 3), 3, out product));
            Assert.IsFalse(FixedPoint.TryMultiply(FixedPoint.FromRaw(FixedPoint.MAX_RAW / 3 + 1), 3, out product));
            Assert.AreEqual(FixedPoint.MaxValue, product);
            Assert.IsFalse(FixedPoint.TryMultiply(FixedPoint.MaxValue, -2, out product));
            Assert.AreEqual(FixedPoint.MinValue, product);
        }

        [Test]
        public void TryMultiply_MatchesADecimalReference()
        {
            var random = new System.Random(3);
            for (var i = 0; i < 20000; i++)
            {
                // Raw magnitudes up to 2^40 keep the exact product inside decimal and cross the overflow boundary.
                var a = NextLong(random, 1L << 40);
                var b = NextLong(random, 1L << 40);
                var exact = (decimal)a * b + FixedPoint.HALF_RAW;
                var fits = exact >= long.MinValue && exact <= long.MaxValue;

                var succeeded = FixedPoint.TryMultiply(FixedPoint.FromRaw(a), FixedPoint.FromRaw(b), out var product);
                Assert.AreEqual(fits, succeeded, $"{a} * {b}");
                if (fits)
                {
                    Assert.AreEqual((FixedPoint.FromRaw(a) * FixedPoint.FromRaw(b)).rawValue, product.rawValue, $"{a} * {b}");
                }
            }
        }

#if PRAGMA_DOTS_CHECKED_MATH
        [Test]
        public void WithTheDefine_OperatorsThrowOnOverflow()
        {
            Assert.Throws<OverflowException>(() => _ = FixedPoint.MaxValue + FixedPoint.Epsilon);
            Assert.Throws<OverflowException>(() => _ = FixedPoint.MinValue - FixedPoint.Epsilon);
            Assert.Throws<OverflowException>(() => _ = (FixedPoint)46341 * (FixedPoint)46341);
            Assert.Throws<OverflowException>(() => _ = FixedPoint.MaxValue * 2);
            Assert.Throws<OverflowException>(() => _ = (FixedPoint)(1L << 50));
            Assert.Throws<OverflowException>(() => _ = FixedMath.Dot(new FixedVector2(50000, 0), new FixedVector2(50000, 0)));
            Assert.AreEqual(FixedPoint.FromRaw(46340L * 46340 << 16), (FixedPoint)46340 * (FixedPoint)46340, "products that fit pass");
        }
#else
        [Test]
        public void WithoutTheDefine_OperatorsWrapLikeLong()
        {
            Assert.AreEqual(FixedPoint.MaxValue.rawValue + 1, (FixedPoint.MaxValue + FixedPoint.Epsilon).rawValue);
            Assert.AreEqual(unchecked(46341L * 65536 * (46341L * 65536) + FixedPoint.HALF_RAW) >> 16, ((FixedPoint)46341 * (FixedPoint)46341).rawValue);
        }
#endif

        private static long NextLong(System.Random random, long range)
        {
            var bytes = new byte[8];
            random.NextBytes(bytes);
            return BitConverter.ToInt64(bytes, 0) % range;
        }
    }
}
