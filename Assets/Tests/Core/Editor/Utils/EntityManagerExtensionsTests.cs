using NUnit.Framework;
using Pragma.Dots.Utils;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    public class EntityManagerExtensionsTests
    {
        [Test]
        public void TryGetComponent_FindsWhatTheEntityHas()
        {
            using (var world = new World("Extensions"))
            {
                var entityManager = world.EntityManager;
                var entity = entityManager.CreateEntity(typeof(TestTagSignal));
                entityManager.AddComponentData(entity, new TestSignal { value = 4 });

                Assert.IsTrue(entityManager.TryGetComponent<TestSignal>(entity, out var signal));
                Assert.AreEqual(4, signal.value);
                Assert.IsTrue(entityManager.TryGetComponent<TestTagSignal>(entity, out _), "a tag counts too");
                Assert.IsFalse(entityManager.TryGetComponent<TestSignal>(entityManager.CreateEntity(), out _));

                entityManager.DestroyEntity(entity);
                Assert.IsFalse(entityManager.TryGetComponent<TestSignal>(entity, out _), "a destroyed entity has nothing");
                Assert.IsFalse(entityManager.TryGetComponent<TestSignal>(Entity.Null, out _));
            }
        }

        [Test]
        public void TryGetBuffer_FindsWhatTheEntityHas()
        {
            using (var world = new World("Extensions"))
            {
                var entityManager = world.EntityManager;
                var entity = entityManager.CreateEntity();
                entityManager.AddBuffer<TestDictionaryEntry>(entity).Add(new TestDictionaryEntry { key = 2 });

                Assert.IsTrue(entityManager.TryGetBuffer<TestDictionaryEntry>(entity, true, out var buffer));
                Assert.AreEqual(2, buffer[0].key);
                Assert.IsFalse(entityManager.TryGetBuffer<TestDictionaryEntry>(entityManager.CreateEntity(), true, out _));
                Assert.IsFalse(entityManager.TryGetBuffer<TestDictionaryEntry>(Entity.Null, false, out _));
            }
        }
    }
}
