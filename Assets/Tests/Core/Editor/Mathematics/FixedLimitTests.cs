using NUnit.Framework;
using Pragma.Dots.Mathematics;

namespace Pragma.Dots.Tests
{
    public class FixedLimitTests
    {
        [Test]
        public void FixedMin_RaisesValuesBelowTheLimit()
        {
            var min = new FixedMinAttribute(0.5);

            Assert.AreEqual(FixedPoint.Half, min.Clamp(FixedPoint.FromFraction(1, 4)));
            Assert.AreEqual(FixedPoint.Half, min.Clamp(FixedPoint.MinValue));
            Assert.AreEqual(FixedPoint.Half, min.Clamp(FixedPoint.Half));
            Assert.AreEqual((FixedPoint)3, min.Clamp(3));
        }

        [Test]
        public void FixedRange_MovesValuesOutsideToTheNearestLimit()
        {
            var range = new FixedRangeAttribute(-1, 2);

            Assert.AreEqual(FixedPoint.MinusOne, range.Clamp(-5));
            Assert.AreEqual(FixedPoint.Two, range.Clamp(FixedPoint.MaxValue));
            Assert.AreEqual(FixedPoint.Half, range.Clamp(FixedPoint.Half));
            Assert.AreEqual(FixedPoint.MinusOne, range.Clamp(FixedPoint.MinusOne));
        }

        [Test]
        public void Limits_AreRoundedToTheNearestStep()
        {
            Assert.AreEqual(6554, new FixedMinAttribute(0.1).Min.rawValue);

            var range = new FixedRangeAttribute(1.0 / 3, 2.0 / 3);
            Assert.AreEqual(21845, range.Min.rawValue);
            Assert.AreEqual(43691, range.Max.rawValue);
        }
    }
}
