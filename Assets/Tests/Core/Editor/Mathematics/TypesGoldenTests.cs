using NUnit.Framework;
using Pragma.Dots.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Pragma.Dots.Tests
{
    // The matrices, geometry, curves, hashes and checked products in Burst and in Mono, and pinned like the core math.
    public class TypesGoldenTests
    {
        [Test]
        public void Burst_ProducesBitIdenticalResults()
        {
            using (var data = new BatchData(Allocator.TempJob))
            using (var managed = new NativeArray<long>(data.inputs.Length * TypesJob.OUTPUTS_PER_INPUT, Allocator.TempJob))
            using (var bursted = new NativeArray<long>(data.inputs.Length * TypesJob.OUTPUTS_PER_INPUT, Allocator.TempJob))
            {
                data.Job(managed).Execute();
                data.Job(bursted).Run();
                for (var i = 0; i < managed.Length; i++)
                {
                    Assert.AreEqual(managed[i], bursted[i], $"output {i % TypesJob.OUTPUTS_PER_INPUT} of input {i / TypesJob.OUTPUTS_PER_INPUT}");
                }
            }
        }

        // A change here changes every simulation that uses these types: update the constant only on purpose.
        [Test]
        public void GoldenResults_DoNotChange()
        {
            using (var data = new BatchData(Allocator.TempJob))
            using (var outputs = new NativeArray<long>(data.inputs.Length * TypesJob.OUTPUTS_PER_INPUT, Allocator.TempJob))
            {
                data.Job(outputs).Execute();
                var hash = FixedHash.Empty;
                foreach (var output in outputs)
                {
                    hash.Add(output);
                }
                Assert.AreEqual(GOLDEN_HASH, hash.value, $"Results changed; new hash 0x{hash.value:X16}.");
            }
        }

        private const ulong GOLDEN_HASH = 0x9E267DBAADB6C202UL;

        private struct BatchData : System.IDisposable
        {
            public NativeArray<long> inputs;
            public NativeArray<FixedCurveKey> curve;
            public NativeArray<FixedVector2> polygon;

            public BatchData(Allocator allocator)
            {
                var random = new FixedRandom(4321);
                inputs = new NativeArray<long>(512, allocator);
                for (var i = 0; i < inputs.Length; i++)
                {
                    var magnitude = (i % 4) switch
                    {
                        0 => (FixedPoint)1,
                        1 => (FixedPoint)10,
                        2 => (FixedPoint)1000,
                        _ => (FixedPoint)100000,
                    };
                    inputs[i] = random.NextFixedPoint(-magnitude, magnitude).rawValue;
                }
                curve = new NativeArray<FixedCurveKey>(new[]
                {
                    new FixedCurveKey(-5, 2, FixedPoint.Zero, 3),
                    new FixedCurveKey(-1, -4, FixedCurveInterpolation.Constant),
                    new FixedCurveKey(2, 7, 1, -2),
                    new FixedCurveKey(6, FixedPoint.FromFraction(1, 3)),
                    new FixedCurveKey(9, -1, 0, 0),
                }, allocator);
                polygon = new NativeArray<FixedVector2>(new[]
                {
                    new FixedVector2(-400, -300), new FixedVector2(500, -350), new FixedVector2(100, 0), new FixedVector2(450, 420), new FixedVector2(-380, 300),
                }, allocator);
            }

            public TypesJob Job(NativeArray<long> outputs) => new TypesJob { inputs = inputs, curve = curve, polygon = polygon, outputs = outputs };

            public void Dispose()
            {
                inputs.Dispose();
                curve.Dispose();
                polygon.Dispose();
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct TypesJob : IJob
        {
            public const int OUTPUTS_PER_INPUT = 20;

            [ReadOnly] public NativeArray<long> inputs;
            [ReadOnly] public NativeArray<FixedCurveKey> curve;
            [ReadOnly] public NativeArray<FixedVector2> polygon;
            public NativeArray<long> outputs;

            public void Execute()
            {
                for (var i = 0; i < inputs.Length; i++)
                {
                    var x = FixedPoint.FromRaw(inputs[i]);
                    var y = FixedPoint.FromRaw(inputs[(i + 1) % inputs.Length]);
                    var z = FixedPoint.FromRaw(inputs[(i + 2) % inputs.Length]);
                    var o = i * OUTPUTS_PER_INPUT;

                    // Matrices of moderate size: translations within 100, scales from 1 to 4.
                    var translation = new FixedVector3(x % 100, y % 100, z % 100);
                    var rotation = FixedQuaternion.Euler(new FixedVector3(x % 4, y % 4, z % 4));
                    var scale = new FixedVector3(FixedPoint.One + FixedMath.Abs(x % 3), FixedPoint.One + FixedMath.Abs(y % 3), FixedPoint.One + FixedMath.Abs(z % 3));
                    var trs = FixedMatrix4x4.TRS(translation, rotation, scale);
                    outputs[o + 0] = FixedMath.Determinant(trs).rawValue;
                    outputs[o + 1] = FixedMath.TransformPoint(trs, new FixedVector3(z % 50, x % 50, y % 50)).x.rawValue;
                    outputs[o + 2] = FixedMath.InverseAffine(trs).c3.x.rawValue;
                    outputs[o + 3] = FixedMath.Inverse(trs).c0.y.rawValue;
                    FixedMath.TryDecompose(trs, out _, out var decomposed, out var scaleBack);
                    outputs[o + 4] = scaleBack.y.rawValue ^ decomposed.w.rawValue;
                    outputs[o + 5] = (trs * trs).c2.z.rawValue;
                    outputs[o + 6] = FixedMath.Length(new FixedVector4(x % 1000, y % 1000, z % 1000, x % 7)).rawValue;

                    // Geometry: the exact tests take the full range, the measured ones moderate radii.
                    var a = new FixedVector2(x, y);
                    var b = new FixedVector2(y, z);
                    var c = new FixedVector2(z, x);
                    outputs[o + 7] = FixedMath.Orientation(a, b, c);
                    var circle = new FixedCircle(b, FixedMath.Abs(z % 1000));
                    outputs[o + 8] = circle.Contains(a) ? 1 : 0;
                    var segment = new FixedSegment2(a, b);
                    outputs[o + 9] = segment.ClosestPoint(c).x.rawValue;
                    outputs[o + 10] = segment.TryIntersect(new FixedSegment2(c, -a), out var crossing) ? crossing.y.rawValue : -1;
                    var ray = new FixedRay2(c, b - c + new FixedVector2(x % 5, y % 5));
                    outputs[o + 11] = ray.TryIntersect(circle, out var distance) ? distance.rawValue : -1;
                    outputs[o + 12] = ray.TryIntersect(FixedBounds2.FromPoints(a, b), out distance) ? distance.rawValue : -1;
                    outputs[o + 13] = ray.TryIntersect(segment, out distance) ? distance.rawValue : -1;
                    outputs[o + 14] = new FixedCircle(c, FixedMath.Abs(y % 500)).TrySweep(b - c, circle, out var fraction) ? fraction.rawValue : -1;
                    outputs[o + 15] = new FixedTriangle2(a, b, c).Contains(new FixedVector2(x % 1000, z % 1000)) ? 1 : 0;
                    outputs[o + 16] = FixedMath.PolygonContains(polygon.AsReadOnlySpan(), new FixedVector2(x % 600, y % 600)) ? 1 : 0;

                    outputs[o + 17] = FixedCurve.Evaluate(curve.AsReadOnlySpan(), FixedCurveWrap.Loop, FixedCurveWrap.PingPong, x % 20).rawValue;
                    var hash = FixedHash.Empty;
                    hash.Add(a);
                    hash.Add(rotation);
                    outputs[o + 18] = (long)hash.value;
                    outputs[o + 19] = FixedPoint.TryMultiply(x, y, out var product) ? product.rawValue : -product.rawValue;
                }
            }
        }
    }
}
