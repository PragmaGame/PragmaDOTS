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
        public void FixedDirection_ConvertsByItsConvention()
        {
            var angle = new FixedDirectionAttribute();
            var heading = new FixedDirectionAttribute(heading: true);
            var quarter = FixedMath.HalfPi;

            Assert.Less((double)FixedMath.Distance(new FixedVector2(0, 1), angle.ToDirection(quarter)), 1e-4, "90 degrees from +X is +Y");
            Assert.Less((double)FixedMath.Distance(new FixedVector2(1, 0), heading.ToDirection(quarter)), 1e-4, "a heading of 90 degrees is +X");
            Assert.AreEqual(0.0, (double)heading.ToAngle(new FixedVector2(0, 1)), 0.0, "+Z is heading 0");
            Assert.AreEqual((double)quarter, (double)heading.ToAngle(heading.ToDirection(quarter)), 2e-4);
            Assert.AreEqual((double)quarter, (double)angle.ToAngle(angle.ToDirection(quarter)), 2e-4);
            Assert.AreEqual(FixedMath.Yaw(FixedQuaternion.RotateY(quarter)).rawValue, heading.ToAngle(heading.ToDirection(quarter)).rawValue, 4);
        }

        [Test]
        public void FixedDegrees_HasLimitsOnlyWhenBothAreGiven()
        {
            Assert.IsFalse(new FixedDegreesAttribute().HasLimits);
            var limited = new FixedDegreesAttribute(-45, 45);
            Assert.IsTrue(limited.HasLimits);
            Assert.AreEqual(-45.0, limited.Min);
            Assert.AreEqual(45.0, limited.Max);
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
