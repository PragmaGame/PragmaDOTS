using System;
using Newtonsoft.Json;
using NUnit.Framework;
using Pragma.Dots.Mathematics;
using Pragma.Dots.Mathematics.Editor;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Tests
{
    public class CurveTests
    {
        [Test]
        public void Segments_EvaluateByTheirInterpolation()
        {
            var curve = new FixedCurve(
                new FixedCurveKey(0, 0),
                new FixedCurveKey(10, 100, FixedCurveInterpolation.Constant),
                new FixedCurveKey(20, 50, FixedPoint.Zero, FixedPoint.Zero),
                new FixedCurveKey(30, 150, FixedPoint.Zero, FixedPoint.Zero));

            Assert.AreEqual((FixedPoint)25, curve.Evaluate(FixedPoint.FromFraction(5, 2)), "linear");
            Assert.AreEqual((FixedPoint)100, curve.Evaluate(FixedPoint.FromFraction(39, 2)), "constant holds until the next key");
            Assert.AreEqual((FixedPoint)50, curve.Evaluate(20), "a key's own value");
            Assert.AreEqual((FixedPoint)100, curve.Evaluate(25), "flat tangents meet halfway");
            Assert.AreEqual((FixedPoint)150, curve.Evaluate(30));
            Assert.AreEqual((FixedPoint)0, curve.Evaluate(-5), "clamped before");
            Assert.AreEqual((FixedPoint)150, curve.Evaluate(1000), "clamped after");

            Assert.AreEqual(FixedPoint.Zero, new FixedCurve().Evaluate(3), "an empty curve");
            Assert.AreEqual((FixedPoint)7, FixedCurve.Constant(7).Evaluate(-100));
            Assert.AreEqual((FixedPoint)15, FixedCurve.Linear(0, 10, 10, 20).Evaluate(5));
            Assert.AreEqual((FixedPoint)15, FixedCurve.EaseInOut(0, 10, 10, 20).Evaluate(5));
            Assert.Less((double)FixedCurve.EaseInOut(0, 10, 10, 20).Evaluate(1), 10.5, "eases in");

            Assert.Throws<ArgumentException>(() => new FixedCurve(new FixedCurveKey(1, 0), new FixedCurveKey(1, 5)), "times must increase");
        }

        [Test]
        public void Wraps_LoopAndPingPong()
        {
            var keys = new[] { new FixedCurveKey(0, 0), new FixedCurveKey(10, 10) };
            var loop = new FixedCurve(FixedCurveWrap.Loop, FixedCurveWrap.Loop, keys);
            var pingPong = new FixedCurve(FixedCurveWrap.PingPong, FixedCurveWrap.PingPong, keys);

            Assert.AreEqual((FixedPoint)3, loop.Evaluate(13));
            Assert.AreEqual((FixedPoint)7, loop.Evaluate(-3));
            Assert.AreEqual((FixedPoint)7, pingPong.Evaluate(13));
            Assert.AreEqual((FixedPoint)3, pingPong.Evaluate(-3));
            Assert.AreEqual((FixedPoint)10, pingPong.Evaluate(30), "the far end of a bounce");
        }

        [Test]
        public void ConvertedCurves_MatchAnimationCurve()
        {
            var random = new System.Random(31);
            var modes = new[] { WrapMode.ClampForever, WrapMode.Loop, WrapMode.PingPong };
            for (var c = 0; c < 200; c++)
            {
                var count = random.Next(2, 7);
                var keys = new Keyframe[count];
                var time = (float)(random.NextDouble() * 10 - 5);
                for (var i = 0; i < count; i++)
                {
                    keys[i] = new Keyframe(time, (float)(random.NextDouble() * 200 - 100), (float)(random.NextDouble() * 40 - 20), (float)(random.NextDouble() * 40 - 20));
                    time += (float)(0.5 + random.NextDouble() * 5);
                }
                if (random.Next(4) == 0)
                {
                    var step = random.Next(count - 1);
                    keys[step].outTangent = float.PositiveInfinity;
                }
                var unity = new AnimationCurve(keys) { preWrapMode = modes[random.Next(3)], postWrapMode = modes[random.Next(3)] };
                var converted = FixedCurve.FromAnimationCurve(unity);

                var start = keys[0].time;
                var span = keys[count - 1].time - start;
                for (var s = 0; s < 200; s++)
                {
                    var t = start - span + (float)random.NextDouble() * span * 3;
                    if (NearAStep(keys, t))
                    {
                        continue;
                    }
                    var expected = unity.Evaluate(t);
                    var actual = (double)converted.Evaluate((FixedPoint)t);
                    Assert.AreEqual(expected, actual, 2e-3 * Math.Max(1.0, Math.Abs(expected)) + 1e-2, $"curve {c} at {t}");
                }
            }
        }

        [Test]
        public void WeightedCurves_AreBakedInstead()
        {
            var weighted = new AnimationCurve(
                new Keyframe(0, 0, 0, 3, 0, 0.8f) { weightedMode = WeightedMode.Out },
                new Keyframe(2, 1, 0, 0));
            Assert.IsFalse(FixedCurve.TryFromAnimationCurve(weighted, out _));
            Assert.Throws<ArgumentException>(() => FixedCurve.FromAnimationCurve(weighted));

            var baked = FixedCurve.Bake(weighted, 129);
            Assert.AreEqual(129, baked.KeyCount);
            for (var t = 0f; t <= 2f; t += 0.0173f)
            {
                Assert.AreEqual(weighted.Evaluate(t), (double)baked.Evaluate((FixedPoint)t), 2e-3, $"at {t}");
            }
            Assert.AreEqual((FixedPoint)2, baked.Keys[128].time, "the last sample is the last key");
        }

        [Test]
        public void Bake_TakesAtMostOneSamplePerStep()
        {
            var baked = FixedCurve.Bake(AnimationCurve.Linear(0, 0, 0.0005f, 1), 64);
            Assert.AreEqual(34, baked.KeyCount, "0.0005 is 33 steps long");
            Assert.AreEqual(FixedPoint.One, baked.Evaluate(1));

            Assert.AreEqual(0, FixedCurve.Bake(new AnimationCurve(), 8).KeyCount);
            Assert.AreEqual((FixedPoint)3, FixedCurve.Bake(new AnimationCurve(new Keyframe(2, 3)), 8).Evaluate(0));
        }

        // The drawer shows linear and constant segments with those tangent modes and reads them back as they were.
        [Test]
        public void Inspector_KeepsLinearAndConstantSegments()
        {
            var curve = new FixedCurve(
                new FixedCurveKey(0, 0),
                new FixedCurveKey(2, 10, FixedCurveInterpolation.Constant),
                new FixedCurveKey(3, 4, FixedPoint.FromFraction(3, 2), -1),
                new FixedCurveKey(5, 6));

            var unity = FixedCurveDrawer.ToEditorCurve(curve);
            Assert.AreEqual(AnimationUtility.TangentMode.Linear, AnimationUtility.GetKeyRightTangentMode(unity, 0));
            Assert.AreEqual(AnimationUtility.TangentMode.Linear, AnimationUtility.GetKeyLeftTangentMode(unity, 1));
            Assert.AreEqual(AnimationUtility.TangentMode.Constant, AnimationUtility.GetKeyRightTangentMode(unity, 1));
            Assert.AreEqual(AnimationUtility.TangentMode.Free, AnimationUtility.GetKeyRightTangentMode(unity, 2));

            var back = FixedCurveDrawer.FromEditorCurve(unity);
            Assert.AreEqual(FixedCurveInterpolation.Linear, back.Keys[0].interpolation);
            Assert.AreEqual(FixedCurveInterpolation.Constant, back.Keys[1].interpolation);
            Assert.AreEqual(FixedCurveInterpolation.Cubic, back.Keys[2].interpolation);
            for (var raw = -FixedPoint.ONE_RAW; raw <= 6 * FixedPoint.ONE_RAW; raw += 997)
            {
                var t = FixedPoint.FromRaw(raw);
                Assert.AreEqual(curve.Evaluate(t), back.Evaluate(t), $"at {t}");
            }

            var weighted = new AnimationCurve(new Keyframe(0, 0, 0, 3, 0, 0.8f) { weightedMode = WeightedMode.Out }, new Keyframe(2, 1));
            Assert.AreEqual(64, FixedCurveDrawer.FromEditorCurve(weighted).KeyCount, "a weighted curve is baked");
        }

        [Test]
        public void ToAnimationCurve_RoundTrips()
        {
            var curve = new FixedCurve(FixedCurveWrap.Clamp, FixedCurveWrap.Loop,
                new FixedCurveKey(0, 0),
                new FixedCurveKey(1, 5, FixedCurveInterpolation.Constant),
                new FixedCurveKey(2, -3, FixedPoint.FromFraction(3, 2), -2),
                new FixedCurveKey(4, 8, 1, 1));
            var unity = curve.ToAnimationCurve();
            var back = FixedCurve.FromAnimationCurve(unity);

            Assert.AreEqual(WrapMode.Loop, unity.postWrapMode);
            Assert.AreEqual(FixedCurveWrap.Loop, back.PostWrap);
            for (var raw = -FixedPoint.ONE_RAW; raw <= 6 * FixedPoint.ONE_RAW; raw += 997)
            {
                var t = FixedPoint.FromRaw(raw);
                Assert.AreEqual((double)curve.Evaluate(t), (double)back.Evaluate(t), 2e-3, $"at {t}");
                Assert.AreEqual((double)curve.Evaluate(t), unity.Evaluate((float)t), 2e-3, $"Unity at {t}");
            }
        }

        [Test]
        public void Burst_EvaluatesTheSameBits()
        {
            var curve = FixedCurve.FromAnimationCurve(new AnimationCurve(
                new Keyframe(0, 0, 0, 2), new Keyframe(1, 3, -1, 4), new Keyframe(3, -2, 0, 0)) { postWrapMode = WrapMode.PingPong });
            using (var keys = new NativeArray<FixedCurveKey>(curve.Keys.ToArray(), Allocator.TempJob))
            using (var managed = new NativeArray<long>(1000, Allocator.TempJob))
            using (var bursted = new NativeArray<long>(1000, Allocator.TempJob))
            {
                new CurveJob { keys = keys, outputs = managed, wrap = curve.PostWrap }.Execute();
                new CurveJob { keys = keys, outputs = bursted, wrap = curve.PostWrap }.Run();
                for (var i = 0; i < managed.Length; i++)
                {
                    Assert.AreEqual(managed[i], bursted[i], $"sample {i}");
                    Assert.AreEqual(curve.Evaluate(CurveJob.Time(i)).rawValue, managed[i], $"sample {i}");
                }
            }
        }

        [Test]
        public void Json_RoundTripsTheKeys()
        {
            var curve = new FixedCurve(FixedCurveWrap.PingPong, FixedCurveWrap.Clamp, new FixedCurveKey(0, 1), new FixedCurveKey(2, 5, 1, -1));
            var json = JsonConvert.SerializeObject(curve);
            StringAssert.Contains("\"keys\":", json);
            Assert.AreEqual(curve, JsonConvert.DeserializeObject<FixedCurve>(json));

            var holder = new Holder { curve = curve };
            Assert.AreEqual(curve, JsonUtility.FromJson<Holder>(JsonUtility.ToJson(holder)).curve);
        }

        // Where a step jumps, a float and a fixed time may land on different sides of it.
        private static bool NearAStep(Keyframe[] keys, float time)
        {
            var span = keys[keys.Length - 1].time - keys[0].time;
            foreach (var key in keys)
            {
                for (var k = -3; k <= 3; k++)
                {
                    if (Math.Abs(time - (key.time + k * span)) < 1e-3)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        [Serializable]
        private class Holder
        {
            public FixedCurve curve;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct CurveJob : IJob
        {
            [ReadOnly] public NativeArray<FixedCurveKey> keys;
            public NativeArray<long> outputs;
            public FixedCurveWrap wrap;

            public static FixedPoint Time(int i) => FixedPoint.FromRaw((i - 200) * 997L);

            public void Execute()
            {
                for (var i = 0; i < outputs.Length; i++)
                {
                    outputs[i] = FixedCurve.Evaluate(keys.AsReadOnlySpan(), FixedCurveWrap.Clamp, wrap, Time(i)).rawValue;
                }
            }
        }
    }
}
