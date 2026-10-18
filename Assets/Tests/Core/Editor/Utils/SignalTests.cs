using NUnit.Framework;
using Pragma.Dots.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Pragma.Dots.Tests
{
    public class SignalTests
    {
        [Test]
        public void Signal_PublishedAtOnce_LivesToTheEndOfTheUpdate()
        {
            using (var world = CreateWorld(out var group))
            {
                var entityManager = world.EntityManager;
                entityManager.Publish(new TestSignal { value = 3 });
                Assert.AreEqual(3, Signals(entityManager)[0].value);

                group.Update();
                Assert.AreEqual(0, Signals(entityManager).Length);
            }
        }

        [Test]
        public void Signal_FromTheEndOfUpdateBuffer_LivesThroughTheNextUpdate()
        {
            using (var world = CreateWorld(out var group))
            {
                var entityManager = world.EntityManager;
                world.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>().CreateCommandBuffer().Publish(new TestSignal { value = 5 });

                group.Update();
                var signals = Signals(entityManager);
                Assert.AreEqual(1, signals.Length, "created after the cleanup");
                Assert.AreEqual(5, signals[0].value);

                group.Update();
                Assert.AreEqual(0, Signals(entityManager).Length, "gone at the end of the next update");
            }
        }

        [Test]
        public void Signals_FromAParallelJob_AreCreatedInTheOrderOfTheirSortKeys()
        {
            const int COUNT = 300;
            using (var world = new World("Signals"))
            using (var commandBuffer = new EntityCommandBuffer(Allocator.TempJob))
            {
                new PublishJob { commandBuffer = commandBuffer.AsParallelWriter() }.Schedule(COUNT, 16).Complete();
                commandBuffer.Playback(world.EntityManager);

                var signals = Signals(world.EntityManager);
                Assert.AreEqual(COUNT, signals.Length);
                for (var i = 0; i < COUNT; i++)
                {
                    Assert.AreEqual(i, signals[i].value);
                }
            }
        }

        [Test]
        public void Signal_WithoutData_IsATagOfItsOwn()
        {
            using (var world = CreateWorld(out var group))
            using (var commandBuffer = new EntityCommandBuffer(Allocator.Temp))
            {
                var entityManager = world.EntityManager;
                entityManager.Publish(new TestTagSignal());
                commandBuffer.Publish(new TestTagSignal());
                commandBuffer.Playback(entityManager);

                using (var query = entityManager.CreateEntityQuery(typeof(TestTagSignal), typeof(SignalTag)))
                {
                    Assert.AreEqual(2, query.CalculateEntityCount());
                    group.Update();
                    Assert.AreEqual(0, query.CalculateEntityCount());
                }
            }
        }

        private static World CreateWorld(out SimulationSystemGroup group)
        {
            var world = new World("Signals");
            group = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            group.AddSystemToUpdateList(world.CreateSystem<SignalCleanupSystem>());
            group.AddSystemToUpdateList(world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>());
            return world;
        }

        private static NativeArray<TestSignal> Signals(EntityManager entityManager)
        {
            using (var query = entityManager.CreateEntityQuery(typeof(TestSignal), typeof(SignalTag)))
            {
                return query.ToComponentDataArray<TestSignal>(Allocator.Temp);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct PublishJob : IJobParallelFor
        {
            public EntityCommandBuffer.ParallelWriter commandBuffer;

            public void Execute(int index)
            {
                commandBuffer.Publish(index, new TestSignal { value = index });
            }
        }
    }
}
