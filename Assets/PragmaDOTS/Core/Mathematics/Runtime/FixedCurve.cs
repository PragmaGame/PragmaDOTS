using System;
using System.Runtime.Serialization;
using UnityEngine;

namespace Pragma.Dots.Mathematics
{
    /// <summary>
    /// A deterministic curve of <see cref="FixedPoint"/> keys, evaluated the way <c>AnimationCurve</c> is: falloff by
    /// distance, speed over time, a stat by level. The inspector edits it with Unity's curve editor and stores the keys.
    /// </summary>
    /// <remarks>
    /// <para>Keys are sorted by time, which must strictly increase. Each segment is <see cref="FixedCurveInterpolation.Cubic"/>
    /// (Hermite through the tangents), <see cref="FixedCurveInterpolation.Linear"/> or <see cref="FixedCurveInterpolation.Constant"/>.
    /// An empty curve evaluates to zero, a single key to its value.</para>
    /// <para>Convert authoring curves once with <see cref="FromAnimationCurve"/> (key for key) or <see cref="Bake"/>
    /// (samples, for weighted keys); never evaluate a float curve in the simulation. Burst code evaluates the keys
    /// through <see cref="Evaluate(ReadOnlySpan{FixedCurveKey}, FixedCurveWrap, FixedCurveWrap, FixedPoint)"/>.</para>
    /// </remarks>
    [Serializable]
    [DataContract]
    public sealed class FixedCurve : IEquatable<FixedCurve>
    {
        [SerializeField, DataMember(Name = "keys")] private FixedCurveKey[] _keys;
        [SerializeField, DataMember(Name = "preWrap")] private FixedCurveWrap _preWrap;
        [SerializeField, DataMember(Name = "postWrap")] private FixedCurveWrap _postWrap;

        /// <summary>An empty curve: it evaluates to zero.</summary>
        public FixedCurve()
        {
            _keys = Array.Empty<FixedCurveKey>();
        }

        /// <exception cref="ArgumentException">The key times do not strictly increase.</exception>
        public FixedCurve(params FixedCurveKey[] keys) : this(FixedCurveWrap.Clamp, FixedCurveWrap.Clamp, keys)
        {
        }

        /// <exception cref="ArgumentException">The key times do not strictly increase.</exception>
        public FixedCurve(FixedCurveWrap preWrap, FixedCurveWrap postWrap, params FixedCurveKey[] keys)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }
            for (var i = 1; i < keys.Length; i++)
            {
                if (keys[i].time <= keys[i - 1].time)
                {
                    throw new ArgumentException($"Key {i} at {keys[i].time} does not come after key {i - 1} at {keys[i - 1].time}.", nameof(keys));
                }
            }
            _keys = (FixedCurveKey[])keys.Clone();
            _preWrap = preWrap;
            _postWrap = postWrap;
        }

        /// <summary>The keys, sorted by time.</summary>
        public ReadOnlySpan<FixedCurveKey> Keys => _keys ?? Array.Empty<FixedCurveKey>();

        public int KeyCount => _keys?.Length ?? 0;

        public FixedCurveWrap PreWrap => _preWrap;

        public FixedCurveWrap PostWrap => _postWrap;

        /// <summary>A curve that is <paramref name="value"/> everywhere.</summary>
        public static FixedCurve Constant(FixedPoint value) => new FixedCurve(new FixedCurveKey(FixedPoint.Zero, value));

        /// <summary>A straight line through two keys, clamped outside them, like <c>AnimationCurve.Linear</c>.</summary>
        public static FixedCurve Linear(FixedPoint startTime, FixedPoint startValue, FixedPoint endTime, FixedPoint endValue)
        {
            return new FixedCurve(new FixedCurveKey(startTime, startValue), new FixedCurveKey(endTime, endValue));
        }

        /// <summary>An S-curve between two keys with flat ends, like <c>AnimationCurve.EaseInOut</c>.</summary>
        public static FixedCurve EaseInOut(FixedPoint startTime, FixedPoint startValue, FixedPoint endTime, FixedPoint endValue)
        {
            return new FixedCurve(
                new FixedCurveKey(startTime, startValue, FixedPoint.Zero, FixedPoint.Zero),
                new FixedCurveKey(endTime, endValue, FixedPoint.Zero, FixedPoint.Zero));
        }

        public FixedPoint Evaluate(FixedPoint time) => Evaluate(Keys, _preWrap, _postWrap, time);

        /// <summary>The value of a curve given by its keys, for Burst code that keeps the keys in a native container.</summary>
        public static FixedPoint Evaluate(ReadOnlySpan<FixedCurveKey> keys, FixedCurveWrap preWrap, FixedCurveWrap postWrap, FixedPoint time)
        {
            if (keys.Length == 0)
            {
                return FixedPoint.Zero;
            }
            var first = keys[0];
            var last = keys[keys.Length - 1];
            if (keys.Length == 1)
            {
                return first.value;
            }

            if (time < first.time)
            {
                if (preWrap == FixedCurveWrap.Clamp)
                {
                    return first.value;
                }
                time = WrapTime(time, first.time, last.time, preWrap);
            }
            else if (time > last.time)
            {
                if (postWrap == FixedCurveWrap.Clamp)
                {
                    return last.value;
                }
                time = WrapTime(time, first.time, last.time, postWrap);
            }
            if (time >= last.time)
            {
                return last.value;
            }

            // The last key at or before the time.
            int low = 0, high = keys.Length - 2;
            while (low < high)
            {
                var middle = (low + high + 1) >> 1;
                if (keys[middle].time <= time)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return EvaluateSegment(keys[low], keys[low + 1], time);
        }

        /// <summary>
        /// The curve with the same keys as <paramref name="curve"/>, each rounded to the nearest step: a constant segment
        /// stays a step, any other becomes <see cref="FixedCurveInterpolation.Cubic"/> through the key tangents.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// A key is weighted (use <see cref="Bake"/>), or two keys round to the same time.
        /// </exception>
        public static FixedCurve FromAnimationCurve(AnimationCurve curve)
        {
            if (!TryFromAnimationCurve(curve, out var result, out var problem))
            {
                throw new ArgumentException(problem, nameof(curve));
            }
            return result;
        }

        /// <summary><see cref="FromAnimationCurve"/> that returns <c>false</c> instead of throwing.</summary>
        public static bool TryFromAnimationCurve(AnimationCurve curve, out FixedCurve result) => TryFromAnimationCurve(curve, out result, out _);

        /// <summary>
        /// A <see cref="FixedCurveInterpolation.Linear"/> curve through <paramref name="samples"/> evenly spaced points of
        /// <paramref name="curve"/>, ends included: an approximation for curves <see cref="FromAnimationCurve"/> cannot
        /// take, such as weighted keys. Bake once, when authoring or loading, and keep the result.
        /// </summary>
        /// <remarks>A curve shorter than <paramref name="samples"/> steps gets one sample per step.</remarks>
        public static FixedCurve Bake(AnimationCurve curve, int samples)
        {
            if (curve == null)
            {
                throw new ArgumentNullException(nameof(curve));
            }
            if (samples < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(samples), samples, "At least two samples are needed.");
            }
            var unityKeys = curve.keys;
            var start = unityKeys.Length == 0 ? FixedPoint.Zero : (FixedPoint)unityKeys[0].time;
            var end = unityKeys.Length == 0 ? FixedPoint.Zero : (FixedPoint)unityKeys[unityKeys.Length - 1].time;
            if (end <= start)
            {
                return unityKeys.Length == 0 ? new FixedCurve() : Constant((FixedPoint)unityKeys[0].value);
            }

            // Sample times must strictly increase, so there is at most one sample per step.
            samples = (int)Math.Min(samples, (end - start).rawValue + 1);
            var keys = new FixedCurveKey[samples];
            for (var i = 0; i < samples; i++)
            {
                var time = i == samples - 1 ? end : start + FixedMath.MulDiv(end - start, i, samples - 1);
                keys[i] = new FixedCurveKey(time, (FixedPoint)curve.Evaluate((float)time));
            }
            return new FixedCurve(ToWrap(curve.preWrapMode), ToWrap(curve.postWrapMode), keys);
        }

        /// <summary>The curve as an <c>AnimationCurve</c>, for the inspector and for drawing; never for the simulation.</summary>
        public AnimationCurve ToAnimationCurve()
        {
            var keys = Keys;
            var unityKeys = new Keyframe[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                unityKeys[i] = new Keyframe((float)keys[i].time, (float)keys[i].value, (float)keys[i].inTangent, (float)keys[i].outTangent);
            }
            for (var i = 0; i < keys.Length - 1; i++)
            {
                switch (keys[i].interpolation)
                {
                    case FixedCurveInterpolation.Constant:
                        unityKeys[i].outTangent = float.PositiveInfinity;
                        unityKeys[i + 1].inTangent = float.PositiveInfinity;
                        break;
                    case FixedCurveInterpolation.Linear:
                        var slope = (float)((keys[i + 1].value - keys[i].value) / (keys[i + 1].time - keys[i].time));
                        unityKeys[i].outTangent = slope;
                        unityKeys[i + 1].inTangent = slope;
                        break;
                }
            }
            return new AnimationCurve(unityKeys) { preWrapMode = ToWrapMode(_preWrap), postWrapMode = ToWrapMode(_postWrap) };
        }

        public bool Equals(FixedCurve other)
        {
            return other != null && _preWrap == other._preWrap && _postWrap == other._postWrap && Keys.SequenceEqual(other.Keys);
        }

        public override bool Equals(object obj) => obj is FixedCurve other && Equals(other);

        public override int GetHashCode()
        {
            var hash = ((int)_preWrap * 397) ^ (int)_postWrap;
            foreach (var key in Keys)
            {
                hash = unchecked(hash * 397 ^ key.GetHashCode());
            }
            return hash;
        }

        private static FixedPoint EvaluateSegment(FixedCurveKey from, FixedCurveKey to, FixedPoint time)
        {
            switch (from.interpolation)
            {
                case FixedCurveInterpolation.Constant:
                    return from.value;
                case FixedCurveInterpolation.Linear:
                    return from.value + FixedMath.MulDiv(to.value - from.value, time - from.time, to.time - from.time);
                default:
                    // Hermite in Horner form: v0 + s (m0 + s (3d - 2m0 - m1 + s (m0 + m1 - 2d))), with s in [0, 1].
                    var duration = to.time - from.time;
                    var s = (time - from.time) / duration;
                    var m0 = from.outTangent * duration;
                    var m1 = to.inTangent * duration;
                    var d = to.value - from.value;
                    var cubic = m0 + m1 - d * 2;
                    var square = d * 3 - m0 * 2 - m1;
                    return from.value + s * (m0 + s * (square + s * cubic));
            }
        }

        private static FixedPoint WrapTime(FixedPoint time, FixedPoint start, FixedPoint end, FixedCurveWrap wrap)
        {
            var span = end - start;
            if (wrap == FixedCurveWrap.Loop)
            {
                return start + FixedMath.Mod(time - start, span);
            }
            var bounce = FixedMath.Mod(time - start, span * 2);
            return start + (bounce > span ? span * 2 - bounce : bounce);
        }

        private static bool TryFromAnimationCurve(AnimationCurve curve, out FixedCurve result, out string problem)
        {
            if (curve == null)
            {
                throw new ArgumentNullException(nameof(curve));
            }
            result = null;
            var unityKeys = curve.keys;
            var keys = new FixedCurveKey[unityKeys.Length];
            for (var i = 0; i < unityKeys.Length; i++)
            {
                var key = unityKeys[i];
                if (key.weightedMode != WeightedMode.None)
                {
                    problem = $"Key {i} is weighted; FixedCurve.Bake approximates such curves.";
                    return false;
                }
                keys[i] = new FixedCurveKey((FixedPoint)key.time, (FixedPoint)key.value, Finite(key.inTangent), Finite(key.outTangent));
                if (i > 0 && keys[i].time <= keys[i - 1].time)
                {
                    problem = $"Keys {i - 1} and {i} are closer than a fixed-point step in time.";
                    return false;
                }
            }
            for (var i = 0; i < unityKeys.Length - 1; i++)
            {
                if (float.IsInfinity(unityKeys[i].outTangent) || float.IsInfinity(unityKeys[i + 1].inTangent))
                {
                    keys[i].interpolation = FixedCurveInterpolation.Constant;
                }
            }
            result = new FixedCurve(ToWrap(curve.preWrapMode), ToWrap(curve.postWrapMode), keys);
            problem = null;
            return true;
        }

        private static FixedPoint Finite(float tangent) => float.IsInfinity(tangent) || float.IsNaN(tangent) ? FixedPoint.Zero : (FixedPoint)tangent;

        private static FixedCurveWrap ToWrap(WrapMode mode)
        {
            switch (mode)
            {
                case WrapMode.Loop: return FixedCurveWrap.Loop;
                case WrapMode.PingPong: return FixedCurveWrap.PingPong;
                default: return FixedCurveWrap.Clamp;
            }
        }

        private static WrapMode ToWrapMode(FixedCurveWrap wrap)
        {
            switch (wrap)
            {
                case FixedCurveWrap.Loop: return WrapMode.Loop;
                case FixedCurveWrap.PingPong: return WrapMode.PingPong;
                default: return WrapMode.ClampForever;
            }
        }
    }
}
