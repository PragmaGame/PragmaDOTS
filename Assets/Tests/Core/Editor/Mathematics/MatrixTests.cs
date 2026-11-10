using System;
using NUnit.Framework;
using Pragma.Dots.Mathematics;
using Unity.Mathematics;

namespace Pragma.Dots.Tests
{
    public class MatrixTests
    {
        [Test]
        public void Constructors_PlaceElementsByRowsAndColumns()
        {
            var m = new FixedMatrix4x4(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);
            Assert.AreEqual(new FixedVector4(1, 5, 9, 13), m.c0, "row-major arguments, column storage");
            Assert.AreEqual(new FixedVector4(4, 8, 12, 16), m[3]);
            Assert.AreEqual(m, FixedMath.Transpose(FixedMath.Transpose(m)));
            Assert.AreEqual(new FixedVector4(13, 14, 15, 16), FixedMath.Transpose(m).c3);

            var n = new FixedMatrix3x3(1, 2, 3, 4, 5, 6, 7, 8, 9);
            Assert.AreEqual(new FixedVector3(1, 4, 7), n.c0);
            Assert.AreEqual(n, FixedMatrix3x3.Identity * n);
            Assert.AreEqual((FixedPoint)1, FixedMath.Determinant(FixedMatrix4x4.Identity));
            Assert.AreEqual(FixedPoint.Zero, FixedMath.Determinant(n), "linearly dependent rows");
        }

        [Test]
        public void RotationMatrix_RotatesLikeTheQuaternion()
        {
            var random = new FixedRandom(11);
            for (var i = 0; i < 200; i++)
            {
                var q = random.NextRotation();
                var v = random.NextVector3(new FixedVector3(-10), new FixedVector3(10));
                var rotated = FixedMatrix3x3.FromRotation(q) * v;
                Assert.Less((double)FixedMath.Distance(FixedMath.Rotate(q, v), rotated), 2e-3, $"{q} {v}");
                Assert.Less(RotationDistance(q, FixedQuaternion.FromMatrix(FixedMatrix3x3.FromRotation(q) * (FixedPoint)3)), 1e-3, "scale is dropped");
            }
        }

        [Test]
        public void Operations_MatchUnityMathematics()
        {
            var random = new FixedRandom(12);
            for (var i = 0; i < 200; i++)
            {
                var a = RandomTrs(ref random, 100);
                var b = RandomTrs(ref random, 100);
                var point = random.NextVector3(new FixedVector3(-50), new FixedVector3(50));
                var fa = (float4x4)a;
                var fb = (float4x4)b;

                AssertClose(math.mul(fa, fb), (float4x4)(a * b), 2e-2, "product");
                AssertClose(math.transform(fa, (float3)point), (float3)FixedMath.TransformPoint(a, point), 2e-2, "point");
                AssertClose(math.rotate(fa, (float3)point), (float3)FixedMath.TransformDirection(a, point), 2e-2, "direction");
                Assert.AreEqual(math.determinant(fa), (float)FixedMath.Determinant(a), 1e-3 * Math.Max(1f, Math.Abs(math.determinant(fa))), "determinant");
                AssertClose(math.inverse(fa), (float4x4)FixedMath.Inverse(a), 2e-3, "inverse");
            }
        }

        [Test]
        public void Inverses_UndoTheTransform()
        {
            var random = new FixedRandom(13);
            for (var i = 0; i < 200; i++)
            {
                var m = RandomTrs(ref random, 1000);
                Assert.IsTrue(FixedMath.TryInverse(m, out var inverse));
                Assert.IsTrue(FixedMath.TryInverseAffine(m, out var affine));
                // The translation column carries the rounding of products with the translation: about 1e-5 of it.
                AssertClose(float4x4.identity, (float4x4)(inverse * m), 3e-2, "general");
                AssertClose(float4x4.identity, (float4x4)(affine * m), 3e-2, "affine");
                AssertClose(float3x3.identity, (float3x3)(FixedMath.Inverse(m.Linear) * m.Linear), 1e-3, "3×3");
            }

            var flat = FixedMatrix4x4.TRS(FixedVector3.One, FixedQuaternion.Identity, new FixedVector3(1, 0, 1));
            Assert.IsFalse(FixedMath.TryInverse(flat, out var none));
            Assert.AreEqual(FixedMatrix4x4.Zero, none);
            Assert.IsFalse(FixedMath.TryInverseAffine(flat, out none));
            Assert.AreEqual(FixedMatrix3x3.Zero, FixedMath.Inverse(flat.Linear));
        }

        [Test]
        public void Decompose_ReturnsTheTrsParts()
        {
            var random = new FixedRandom(14);
            for (var i = 0; i < 200; i++)
            {
                var translation = random.NextVector3(new FixedVector3(-1000), new FixedVector3(1000));
                var rotation = random.NextRotation();
                var scale = random.NextVector3(new FixedVector3(FixedPoint.Half), new FixedVector3(4));
                if (i % 4 == 0)
                {
                    scale.x = -scale.x;
                }
                var m = FixedMatrix4x4.TRS(translation, rotation, scale);

                Assert.IsTrue(FixedMath.TryDecompose(m, out var t, out var r, out var s));
                Assert.AreEqual(translation, t);
                Assert.Less((double)FixedMath.Distance(scale, s), 2e-3, $"scale {scale} → {s}");
                Assert.Less(RotationDistance(rotation, r), 1e-3, $"rotation {rotation} → {r}");
                AssertClose((float4x4)m, (float4x4)FixedMatrix4x4.TRS(t, r, s), 2e-2, "rebuilt");
            }

            Assert.IsFalse(FixedMath.TryDecompose(FixedMatrix4x4.Scale(new FixedVector3(1, 0, 1)), out _, out var identity, out _));
            Assert.AreEqual(FixedQuaternion.Identity, identity);
        }

        // Against the exact inverse of the same fixed matrix. The determinant of a 0.02 scale is 8e-6, below a step: the
        // cofactors are taken on shifted columns, or the inverse would fail there and be off by percents at 0.1.
        [Test]
        public void Inverses_AreAccurateToAStepAtAnyScale()
        {
            var random = new FixedRandom(16);
            var cases = new[]
            {
                (scale: new FixedVector3(FixedPoint.FromFraction(1, 50)), reach: 100),
                (scale: new FixedVector3(FixedPoint.FromFraction(1, 10)), reach: 100),
                (scale: new FixedVector3(FixedPoint.FromFraction(1, 20), 1, 20), reach: 100),
                (scale: new FixedVector3(1, 2, 3), reach: 100000),
            };
            foreach (var (scale, reach) in cases)
            {
                for (var i = 0; i < 50; i++)
                {
                    var m = FixedMatrix4x4.TRS(random.NextVector3(new FixedVector3(-reach), new FixedVector3(reach)), random.NextRotation(), scale);
                    Assert.IsTrue(FixedMath.TryInverseAffine(m, out var affine), $"affine, scale {scale}");
                    AssertSteps(math.inverse(ToDouble(m)), affine, 4, $"affine, scale {scale}");
                    Assert.IsTrue(FixedMath.TryInverse(m, out var general));
                    Assert.AreEqual(affine, general, "an affine matrix takes the affine inverse");

                    var linear = new FixedMatrix4x4(m.Linear, FixedVector3.Zero);
                    AssertSteps(math.inverse(ToDouble(linear)), new FixedMatrix4x4(FixedMath.Inverse(m.Linear), FixedVector3.Zero), 4, $"3×3, scale {scale}");
                }
            }
        }

        [Test]
        public void Inverse_OfAProjection_MatchesTheExactInverse()
        {
            var random = new FixedRandom(18);
            var projection = (FixedMatrix4x4)float4x4.PerspectiveFov(math.radians(60f), 16f / 9f, 0.3f, 100f);
            for (var i = 0; i < 100; i++)
            {
                var view = FixedMatrix4x4.TRS(random.NextVector3(new FixedVector3(-50), new FixedVector3(50)), random.NextRotation(), FixedVector3.One);
                var m = projection * view;
                Assert.IsTrue(FixedMath.TryInverse(m, out var inverse));
                // The determinant multiplies rounded 2×2 minors: about 1e-5 of each element (2000 cases: 1.7e-5 at most).
                AssertSteps(math.inverse(ToDouble(m)), inverse, 4, 5e-5, $"projection · view {i}");
            }
        }

        // The mirror is read on the unit axes: the determinant of the matrix rounds to zero at 0.01 and overflows at 1500.
        [Test]
        public void Decompose_FindsMirrorsAtAnyScale()
        {
            var random = new FixedRandom(17);
            var scales = new[]
            {
                new FixedVector3(-FixedPoint.FromFraction(1, 100), FixedPoint.FromFraction(1, 100), FixedPoint.FromFraction(1, 100)),
                new FixedVector3(-1500, 1200, 1300),
            };
            foreach (var scale in scales)
            {
                for (var i = 0; i < 50; i++)
                {
                    var rotation = random.NextRotation();
                    Assert.IsTrue(FixedMath.TryDecompose(FixedMatrix4x4.TRS(FixedVector3.Zero, rotation, scale), out _, out var r, out var s));
                    Assert.Less((double)FixedMath.Distance(scale, s), 1e-3 * (double)FixedMath.Length(scale) + 1e-4, $"scale {scale} → {s}");
                    Assert.Less(RotationDistance(rotation, r), 1e-2, $"rotation {rotation} → {r}");
                }
            }
        }

        [Test]
        public void Conversions_MatchUnityMatrices()
        {
            var position = new UnityEngine.Vector3(3, -2, 7);
            var rotation = UnityEngine.Quaternion.Euler(30, 60, 10);
            var scale = new UnityEngine.Vector3(2, 1, 0.5f);
            var unity = UnityEngine.Matrix4x4.TRS(position, rotation, scale);
            var fixedTrs = FixedMatrix4x4.TRS((FixedVector3)position, (FixedQuaternion)rotation, (FixedVector3)scale);

            AssertClose((float4x4)unity, (float4x4)fixedTrs, 1e-3, "TRS");
            AssertClose((float4x4)unity, (float4x4)(FixedMatrix4x4)unity, 1e-4, "round trip");
            var back = (UnityEngine.Matrix4x4)fixedTrs;
            Assert.Less(UnityEngine.Vector3.Distance(unity.MultiplyPoint3x4(new UnityEngine.Vector3(1, 2, 3)), back.MultiplyPoint3x4(new UnityEngine.Vector3(1, 2, 3))), 1e-3f);

            var pose = new FixedPose(new FixedVector3(1, 2, 3), FixedQuaternion.RotateY(FixedMath.HalfPi));
            Assert.AreEqual(FixedMatrix4x4.TRS(pose.position, pose.rotation, FixedVector3.One), FixedMatrix4x4.FromPose(pose));
        }

        [Test]
        public void Vector4_HasLengthAndNormalize()
        {
            Assert.AreEqual((FixedPoint)2, FixedMath.Length(new FixedVector4(1, 1, 1, 1)));
            Assert.AreEqual(1.0, (double)FixedMath.Length(FixedMath.Normalize(new FixedVector4(3, -4, 12, 84))), 4e-5);
            Assert.AreEqual(FixedVector4.Zero, FixedMath.Normalize(FixedVector4.Zero));
            Assert.AreEqual(new FixedVector3(1, 2, 3), new FixedVector4(new FixedVector3(1, 2, 3), 9).Xyz);
            Assert.AreEqual((FixedPoint)30, FixedMath.Dot(new FixedVector4(1, 2, 3, 4), new FixedVector4(1, 2, 3, 4)));
        }

        // Component distance of two unit quaternions, either sign: FixedMath.Angle is too coarse near zero for this.
        private static double RotationDistance(FixedQuaternion a, FixedQuaternion b)
        {
            var same = (float4)new FixedVector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
            var opposite = (float4)new FixedVector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
            return Math.Min(math.length(same), math.length(opposite));
        }

        private static FixedMatrix4x4 RandomTrs(ref FixedRandom random, int reach)
        {
            return FixedMatrix4x4.TRS(
                random.NextVector3(new FixedVector3(-reach), new FixedVector3(reach)),
                random.NextRotation(),
                random.NextVector3(new FixedVector3(FixedPoint.Half), new FixedVector3(4)));
        }

        private static double4x4 ToDouble(FixedMatrix4x4 m) => new double4x4(ToDouble(m.c0), ToDouble(m.c1), ToDouble(m.c2), ToDouble(m.c3));

        private static double4 ToDouble(FixedVector4 v) => new double4((double)v.x, (double)v.y, (double)v.z, (double)v.w);

        // Every element within `steps` steps plus `relative` of its size.
        private static void AssertSteps(double4x4 expected, FixedMatrix4x4 actual, int steps, string what) => AssertSteps(expected, actual, steps, 1e-9, what);

        private static void AssertSteps(double4x4 expected, FixedMatrix4x4 actual, int steps, double relative, string what)
        {
            for (var c = 0; c < 4; c++)
            {
                for (var r = 0; r < 4; r++)
                {
                    var e = expected[c][r];
                    Assert.AreEqual(e, (double)actual[c][r], steps / (double)FixedPoint.ONE_RAW + relative * Math.Abs(e), $"{what}: element ({r}, {c})");
                }
            }
        }

        private static void AssertClose(float4x4 expected, float4x4 actual, double tolerance, string what)
        {
            for (var c = 0; c < 4; c++)
            {
                for (var r = 0; r < 4; r++)
                {
                    var e = expected[c][r];
                    var a = actual[c][r];
                    Assert.AreEqual(e, a, tolerance * Math.Max(1.0, Math.Abs(e)), $"{what}: element ({r}, {c})");
                }
            }
        }

        private static void AssertClose(float3x3 expected, float3x3 actual, double tolerance, string what)
        {
            for (var c = 0; c < 3; c++)
            {
                for (var r = 0; r < 3; r++)
                {
                    Assert.AreEqual(expected[c][r], actual[c][r], tolerance, $"{what}: element ({r}, {c})");
                }
            }
        }

        private static void AssertClose(float3 expected, float3 actual, double tolerance, string what)
        {
            Assert.Less(math.distance(expected, actual), tolerance * Math.Max(1.0, math.length(expected)), what);
        }
    }
}
