using System.Collections.Generic;
using NUnit.Framework;
using Pragma.Dots.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Pragma.Dots.Tests
{
    public class BufferDictionaryTests
    {
        private const int ADD = 0;
        private const int REMOVE = 1;

        [Test]
        public void GetOrAdd_KeepsTheEntriesSortedByKey()
        {
            using (var world = new World("Buffer Dictionary"))
            {
                var buffer = world.EntityManager.AddBuffer<TestDictionaryEntry>(world.EntityManager.CreateEntity());
                foreach (var key in new[] { 5, 1, 3 })
                {
                    buffer.GetOrAdd(key, out var added).value = key * 10;
                    Assert.IsTrue(added);
                }
                buffer.GetOrAdd(3, out var addedAgain).value += 1;
                Assert.IsFalse(addedAgain, "an existing key is not added twice");

                CollectionAssert.AreEqual(new[] { 1, 3, 5 }, Keys(buffer));
                Assert.IsTrue(buffer.TryGet(3, out var entry));
                Assert.AreEqual(31, entry.value);
                Assert.IsFalse(buffer.TryGet(4, out _));
                Assert.AreEqual(~2, buffer.IndexOf(4), "where 4 would go");

                Assert.IsTrue(buffer.Remove(3));
                Assert.IsFalse(buffer.Remove(3));
                Assert.IsFalse(buffer.ContainsKey(3));
                CollectionAssert.AreEqual(new[] { 1, 5 }, Keys(buffer));
            }
        }

        [Test]
        public void Lookups_WorkOnReadOnlyBuffers()
        {
            using (var world = new World("Buffer Dictionary"))
            {
                var entityManager = world.EntityManager;
                var entity = entityManager.CreateEntity();
                var buffer = entityManager.AddBuffer<TestDictionaryEntry>(entity);
                buffer.GetOrAdd(2).value = 20;
                buffer.GetOrAdd(7).value = 70;

                var readOnly = entityManager.GetBuffer<TestDictionaryEntry>(entity, true);
                Assert.IsTrue(readOnly.TryGet(7, out var entry));
                Assert.AreEqual(70, entry.value);
                Assert.IsTrue(readOnly.ContainsKey(2));
                Assert.AreEqual(~1, readOnly.IndexOf(5));
            }
        }

        [Test]
        public void Operations_MatchASortedDictionary_InBurstToo()
        {
            var random = new System.Random(11);
            using (var operations = new NativeArray<int3>(3000, Allocator.TempJob))
            using (var world = new World("Buffer Dictionary"))
            {
                var expected = new SortedDictionary<int, int>();
                var array = operations;
                for (var i = 0; i < array.Length; i++)
                {
                    var operation = new int3(random.Next(3) == 0 ? REMOVE : ADD, random.Next(400), i);
                    array[i] = operation;
                    if (operation.x == ADD)
                    {
                        expected[operation.y] = operation.z;
                    }
                    else
                    {
                        expected.Remove(operation.y);
                    }
                }

                var entityManager = world.EntityManager;
                var managed = entityManager.CreateEntity(typeof(TestDictionaryEntry));
                var bursted = entityManager.CreateEntity(typeof(TestDictionaryEntry));
                Apply(entityManager.GetBuffer<TestDictionaryEntry>(managed), operations);
                new OperationsJob { buffer = entityManager.GetBuffer<TestDictionaryEntry>(bursted), operations = operations }.Run();

                foreach (var entity in new[] { managed, bursted })
                {
                    var buffer = entityManager.GetBuffer<TestDictionaryEntry>(entity);
                    CollectionAssert.AreEqual(expected.Keys, Keys(buffer));
                    var i = 0;
                    foreach (var pair in expected)
                    {
                        Assert.AreEqual(pair.Value, buffer[i++].value);
                    }
                }
            }
        }

        private static void Apply(DynamicBuffer<TestDictionaryEntry> buffer, NativeArray<int3> operations)
        {
            for (var i = 0; i < operations.Length; i++)
            {
                var operation = operations[i];
                if (operation.x == ADD)
                {
                    buffer.GetOrAdd(operation.y).value = operation.z;
                }
                else
                {
                    buffer.Remove(operation.y);
                }
            }
        }

        private static int[] Keys(DynamicBuffer<TestDictionaryEntry> buffer)
        {
            var keys = new int[buffer.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i] = buffer[i].key;
            }
            return keys;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct OperationsJob : IJob
        {
            public DynamicBuffer<TestDictionaryEntry> buffer;
            [ReadOnly] public NativeArray<int3> operations;

            public void Execute()
            {
                Apply(buffer, operations);
            }
        }
    }
}
