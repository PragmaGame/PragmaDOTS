using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Pragma.Lockstep.Utils.Tests
{
    public class FixedDictionaryTests
    {
        [Test]
        public void FixedDictionary_KeepsAddingAfterManyRemovals()
        {
            // Each removed key is followed by the live one, so it leaves a marker in its slot.
            var dictionary = new FixedDictionary4096Bytes<int, int>();
            for (var i = 0; i < 10000; i++)
            {
                dictionary.Add(i, i);
                if (i > 0)
                {
                    Assert.IsTrue(dictionary.Remove(i - 1), $"key {i - 1}");
                }
            }

            Assert.AreEqual(1, dictionary.Count);
            Assert.AreEqual(9999, dictionary[9999]);
            Assert.AreEqual(16, dictionary.Capacity, "the markers are dropped, not grown into");
        }

        [Test]
        public void FixedDictionary_GrowsUpToMaxCapacity()
        {
            var dictionary = new FixedDictionary4096Bytes<int, int>();
            var max = dictionary.MaxCapacity;
            for (var i = 0; i < max; i++)
            {
                dictionary.Add(i * 7919, i);
            }

            Assert.AreEqual(max, dictionary.Count);
            Assert.AreEqual(max, dictionary.Capacity);
            for (var i = 0; i < max; i++)
            {
                Assert.AreEqual(i, dictionary[i * 7919]);
            }
            Assert.IsFalse(dictionary.TryAdd(-1, 0));
            Assert.Throws<InvalidOperationException>(() => dictionary.Add(-1, 0));
            Assert.Throws<InvalidOperationException>(() => dictionary[-1] = 0);
        }

        [Test]
        public void FixedDictionary_FindsCollidingKeysAfterRemovals()
        {
            // 16 slots and integer keys hashed to themselves: 1, 17 and 33 share a probe chain.
            var dictionary = new FixedDictionary4096Bytes<int, int>(16);
            dictionary.Add(1, 10);
            dictionary.Add(17, 170);
            dictionary.Add(33, 330);

            Assert.IsTrue(dictionary.Remove(17));
            Assert.AreEqual(330, dictionary[33]);
            Assert.IsTrue(dictionary.Remove(1));
            Assert.AreEqual(330, dictionary[33]);
            Assert.IsFalse(dictionary.ContainsKey(17));

            dictionary.Add(49, 490);
            Assert.AreEqual(330, dictionary[33]);
            Assert.AreEqual(490, dictionary[49]);
            Assert.AreEqual(2, dictionary.Count);
        }

        [Test]
        public void FixedDictionary_MatchesDictionary()
        {
            var expected = new Dictionary<int, int>();
            var dictionary = new FixedDictionary4096Bytes<int, int>();
            var random = new Random(1);
            for (var i = 0; i < 20000; i++)
            {
                var key = random.Next(-150, 150);
                switch (random.Next(4))
                {
                    case 0:
                        Assert.AreEqual(expected.TryAdd(key, i), dictionary.TryAdd(key, i));
                        break;
                    case 1:
                        Assert.AreEqual(expected.Remove(key), dictionary.Remove(key));
                        break;
                    case 2:
                        expected[key] = i;
                        dictionary[key] = i;
                        break;
                    default:
                        Assert.AreEqual(expected.TryGetValue(key, out var value), dictionary.TryGetValue(key, out var actual));
                        Assert.AreEqual(value, actual);
                        break;
                }
                Assert.AreEqual(expected.Count, dictionary.Count);
            }

            var enumerated = new Dictionary<int, int>();
            foreach (var pair in dictionary)
            {
                enumerated.Add(pair.Key, pair.Value);
            }
            CollectionAssert.AreEquivalent(expected, enumerated);

            dictionary.Clear();
            Assert.AreEqual(0, dictionary.Count);
            Assert.IsFalse(dictionary.ContainsKey(0));
        }

        [Test]
        public void FixedDictionary_GetValueRefWritesInPlace()
        {
            var dictionary = new FixedDictionary4096Bytes<int, int>();
            dictionary.Add(5, 1);
            dictionary.GetValueRef(5) += 41;

            Assert.AreEqual(42, dictionary[5]);
            Assert.Throws<KeyNotFoundException>(() => dictionary.GetValueRef(6));
        }

        [Test]
        public void FixedDictionary_GivesTheSameResultsInBurst()
        {
            using (var managed = new NativeList<int>(Allocator.TempJob))
            using (var bursted = new NativeList<int>(Allocator.TempJob))
            {
                new ScriptJob { output = managed }.Execute();
                new ScriptJob { output = bursted }.Run();

                Assert.Greater(managed.Length, 0);
                CollectionAssert.AreEqual(managed.AsArray().ToArray(), bursted.AsArray().ToArray());
            }
        }

        // Adds, replaces and removes by a fixed script, writing what each step returned, then the entries in enumeration
        // order.
        [BurstCompile(CompileSynchronously = true)]
        private struct ScriptJob : IJob
        {
            public NativeList<int> output;

            public void Execute()
            {
                var dictionary = new FixedDictionary4096Bytes<int, int>();
                var state = 12345u;
                for (var i = 0; i < 5000; i++)
                {
                    state = state * 1664525u + 1013904223u;
                    var key = (int)(state >> 8) % 300;
                    var result = 0;
                    switch ((state >> 4) & 3)
                    {
                        case 0:
                            result = dictionary.TryAdd(key, i) ? 1 : 0;
                            break;
                        case 1:
                            result = dictionary.Remove(key) ? 1 : 0;
                            break;
                        case 2:
                            dictionary[key] = i;
                            break;
                        default:
                            if (dictionary.TryGetValue(key, out var value))
                            {
                                result = dictionary.GetValueRef(key) += value;
                            }
                            break;
                    }
                    output.Add(result);
                    output.Add(dictionary.Count);
                }

                foreach (var pair in dictionary)
                {
                    output.Add(pair.Key);
                    output.Add(pair.Value);
                }
            }
        }
    }
}
