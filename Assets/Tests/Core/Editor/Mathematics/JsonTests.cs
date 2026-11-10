using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Pragma.Dots.Mathematics;
using UnityEngine;

namespace Pragma.Dots.Tests
{
    public class JsonTests
    {
        [Test]
        public void Newtonsoft_RoundTripsEveryTypeExactly()
        {
            var point = FixedPoint.FromFraction(-7, 3);
            var vector2 = new FixedVector2(FixedPoint.MaxValue, FixedPoint.MinValue);
            var vector3 = new FixedVector3(FixedPoint.Epsilon, -FixedPoint.Epsilon, FixedPoint.FromFraction(355, 113));
            var rotation = FixedQuaternion.Euler(new FixedVector3(1, 2, 3));
            var pose = new FixedPose(vector3, rotation);
            var random = new FixedRandom(42);
            random.NextUInt();

            Assert.AreEqual(point, RoundTrip(point));
            Assert.AreEqual(vector2, RoundTrip(vector2));
            Assert.AreEqual(vector3, RoundTrip(vector3));
            Assert.AreEqual(rotation, RoundTrip(rotation));
            Assert.AreEqual(pose, RoundTrip(pose));

            var vector4 = new FixedVector4(FixedPoint.MinValue, 1, -2, FixedPoint.FromFraction(1, 3));
            var matrix3 = FixedMatrix3x3.FromRotation(rotation);
            var matrix4 = FixedMatrix4x4.TRS(vector3, rotation, new FixedVector3(2, 3, 4));
            Assert.AreEqual(vector4, RoundTrip(vector4));
            Assert.AreEqual(matrix3, RoundTrip(matrix3));
            Assert.AreEqual(matrix4, RoundTrip(matrix4), "Translation and Linear are not written");

            var bounds = FixedBounds2.FromCenterSize(new FixedVector2(1, -2), new FixedVector2(3, 4));
            var circle = new FixedCircle(new FixedVector2(-7, 2), FixedPoint.FromFraction(5, 2));
            var segment = new FixedSegment2(new FixedVector2(1, 2), new FixedVector2(-3, 4));
            var ray = new FixedRay2(new FixedVector2(1, 2), new FixedVector2(3, 4));
            var triangle = new FixedTriangle2(new FixedVector2(0, 0), new FixedVector2(5, 0), new FixedVector2(0, 5));
            Assert.AreEqual(bounds, RoundTrip(bounds));
            Assert.AreEqual(circle, RoundTrip(circle));
            Assert.AreEqual(segment, RoundTrip(segment));
            Assert.AreEqual(ray, RoundTrip(ray), "the direction is read back as stored, not normalized again");
            Assert.AreEqual(triangle, RoundTrip(triangle));

            var restored = RoundTrip(random);
            Assert.AreEqual(random, restored);
            Assert.AreEqual(random.NextUInt(), restored.NextUInt(), "the sequence goes on where it stopped");

            var hash = FixedHash.Empty;
            hash.Add(pose);
            Assert.AreEqual(hash, RoundTrip(hash), "all 64 bits are read back");
            Assert.AreEqual(FixedHash.Empty, RoundTrip(FixedHash.Empty), "a value above long.MaxValue too");
            Assert.AreEqual("{\"value\":" + hash.value + "}", JsonConvert.SerializeObject(hash));

            var grid = RoundTrip(new FixedGrid(new FixedVector2(-5, 7), FixedPoint.FromFraction(5, 2), 12, 9));
            Assert.AreEqual(new FixedVector2(-5, 7), grid.origin, "readonly fields are read back too");
            Assert.AreEqual(FixedPoint.FromFraction(5, 2), grid.cellSize);
            Assert.AreEqual(12, grid.width);
            Assert.AreEqual(9, grid.height);
        }

        // The JSON is part of every replay and save built on it: change it only on purpose.
        [Test]
        public void Newtonsoft_WritesFieldsOnly()
        {
            Assert.AreEqual("{\"rawValue\":-98304}", JsonConvert.SerializeObject(FixedPoint.FromFraction(-3, 2)));
            Assert.AreEqual("{\"x\":{\"rawValue\":65536},\"y\":{\"rawValue\":-32768}}", JsonConvert.SerializeObject(new FixedVector2(FixedPoint.One, -FixedPoint.Half)));
            Assert.AreEqual(
                "{\"origin\":{\"x\":{\"rawValue\":0},\"y\":{\"rawValue\":0}},\"cellSize\":{\"rawValue\":65536},\"width\":2,\"height\":3}",
                JsonConvert.SerializeObject(new FixedGrid(FixedVector2.Zero, FixedPoint.One, 2, 3)));
        }

        [Test]
        public void Newtonsoft_ReadsValuesOutOfAnUntypedDictionary()
        {
            var section = new Dictionary<string, object>
            {
                ["position"] = new FixedVector3(1, 2, 3),
                ["rotation"] = FixedQuaternion.RotateY(FixedMath.HalfPi),
                ["speed"] = FixedPoint.FromFraction(7, 2),
            };

            var json = JObject.FromObject(section).ToString(Formatting.None);
            var read = JObject.Parse(json);

            Assert.AreEqual(new FixedVector3(1, 2, 3), read["position"].ToObject<FixedVector3>());
            Assert.AreEqual(FixedQuaternion.RotateY(FixedMath.HalfPi), read["rotation"].ToObject<FixedQuaternion>());
            Assert.AreEqual(FixedPoint.FromFraction(7, 2), read["speed"].ToObject<FixedPoint>());
        }

        [Test]
        public void JsonUtility_RoundTripsExactly()
        {
            var state = new State
            {
                point = FixedPoint.MinValue,
                position = new FixedVector3(FixedPoint.Epsilon, 2, FixedPoint.MaxValue),
                rotation = FixedQuaternion.Euler(new FixedVector3(3, 2, 1)),
                random = new FixedRandom(7),
                hash = FixedHash.Empty,
            };

            var read = JsonUtility.FromJson<State>(JsonUtility.ToJson(state));

            Assert.AreEqual(state.point, read.point);
            Assert.AreEqual(state.position, read.position);
            Assert.AreEqual(state.rotation, read.rotation);
            Assert.AreEqual(state.random, read.random);
            Assert.AreEqual(state.hash, read.hash);
        }

        private static T RoundTrip<T>(T value)
        {
            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
        }

        [Serializable]
        private class State
        {
            public FixedPoint point;
            public FixedVector3 position;
            public FixedQuaternion rotation;
            public FixedRandom random;
            public FixedHash hash;
        }
    }
}
