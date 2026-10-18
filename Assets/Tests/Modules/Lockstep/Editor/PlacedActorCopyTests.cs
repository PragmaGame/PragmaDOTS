using NUnit.Framework;
using Pragma.Dots.Actors;
using Pragma.Dots.Lockstep;
using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    public class PlacedActorCopyTests
    {
        [Test]
        public void PlacedActors_AreCopiedInBakedOrder_WhateverOrderTheyLoadedIn()
        {
            using (var first = new World("Presentation A"))
            using (var second = new World("Presentation B"))
            {
                // The same scene content, loaded in different orders on two clients.
                CreatePlacedEntity(first.EntityManager, 30, 3);
                CreatePlacedEntity(first.EntityManager, 10, 1);
                CreatePlacedEntity(first.EntityManager, 20, 2);
                CreatePlacedEntity(second.EntityManager, 20, 2);
                CreatePlacedEntity(second.EntityManager, 30, 3);
                CreatePlacedEntity(second.EntityManager, 10, 1);
                // A prefab carrying the marker is not scene content.
                var prefab = first.EntityManager.CreateEntity(typeof(Prefab), typeof(PlacedActor), typeof(ChecksumPadded));
                first.EntityManager.SetComponentData(prefab, new ChecksumPadded { large = 99 });

                using (var a = new LockstepSimulation(TestUtility.Config(), Options(first)))
                using (var b = new LockstepSimulation(TestUtility.Config(), Options(second)))
                {
                    var entityManager = a.World.EntityManager;
                    using (var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlacedActor>(), ComponentType.ReadOnly<ChecksumPadded>()))
                    {
                        var values = query.ToComponentDataArray<ChecksumPadded>(Allocator.Temp);
                        Assert.AreEqual(3, values.Length, "every placed actor is copied, the prefab is not");
                        for (var i = 0; i < values.Length; i++)
                        {
                            Assert.AreEqual(i + 1, values[i].large, "the copies follow the baked order");
                        }
                    }
                    Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), "both clients start from the same state");

                    TestUtility.Step(a);
                    TestUtility.Step(b);
                    Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum());
                    Assert.AreEqual(1, TestUtility.Count<ActorRegistry>(entityManager), "the registry is still created");
                }
            }
        }

        [Test]
        public void PlacedActors_OfManyArchetypes_StartTheSameStateWhateverOrderTheyLoadedIn()
        {
            // A map: rules, a grid, two castles, two spawn points, two walls, three cover zones, each kind its own archetype.
            var items = new (ulong Order, int Kind)[]
            {
                (1, 0), (2, 1), (3, 2), (5, 3), (11, 4), (12, 4), (21, 0), (22, 0), (31, 1), (32, 1), (41, 2), (42, 2), (43, 2),
            };
            using (var first = new World("Presentation A"))
            using (var second = new World("Presentation B"))
            {
                for (var i = 0; i < items.Length; i++)
                {
                    CreateMapItem(first.EntityManager, items[i].Order, items[i].Kind);
                    var reversed = items[items.Length - 1 - i];
                    CreateMapItem(second.EntityManager, reversed.Order, reversed.Kind);
                }

                using (var a = new LockstepSimulation(TestUtility.Config(), Options(first)))
                using (var b = new LockstepSimulation(TestUtility.Config(), Options(second)))
                {
                    var hashesA = LockstepChecksum.ComputePerType(a.World.EntityManager);
                    var hashesB = LockstepChecksum.ComputePerType(b.World.EntityManager);
                    foreach (var pair in hashesA)
                    {
                        Assert.AreEqual(pair.Value, hashesB[pair.Key], $"{pair.Key} differs between the clients");
                    }
                    Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), "both clients start from the same state");
                }
            }
        }

        [Test]
        public void PlacedActors_OfOneArchetypeInSeveralChunks_StartTheSameStateWhateverOrderTheyLoadedIn()
        {
            // Two subscenes with actors of one archetype: their scene sections (a shared component) keep them in chunks
            // of their own, and the clients load the scenes in different orders.
            using (var first = new World("Presentation A"))
            using (var second = new World("Presentation B"))
            {
                CreateSection(first.EntityManager, 1, 1);
                CreateSection(first.EntityManager, 2, 7);
                CreateSection(second.EntityManager, 2, 7);
                CreateSection(second.EntityManager, 1, 1);

                using (var a = new LockstepSimulation(TestUtility.Config(), Options(first)))
                using (var b = new LockstepSimulation(TestUtility.Config(), Options(second)))
                {
                    foreach (var simulation in new[] { a, b })
                    {
                        using (var query = simulation.World.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ChecksumPadded>()))
                        {
                            var values = query.ToComponentDataArray<ChecksumPadded>(Allocator.Temp);
                            Assert.AreEqual(12, values.Length);
                            for (var i = 0; i < values.Length; i++)
                            {
                                Assert.AreEqual(i + 1, values[i].large, $"{simulation.World.Name}: the copies follow the baked order");
                            }
                        }
                    }
                    Assert.AreEqual(a.ComputeChecksum(), b.ComputeChecksum(), "both clients start from the same state");
                }
            }
        }

        [Test]
        public void PlacedActors_KeepLinkedEntitiesAndTheirReferences()
        {
            using (var presentation = new World("Presentation"))
            {
                var source = presentation.EntityManager;
                var outside = source.CreateEntity(typeof(ChecksumValue));
                var root = CreatePlacedEntity(source, 1, 1);
                var child = source.CreateEntity(typeof(ChecksumLink));
                source.SetComponentData(child, new ChecksumLink { target = root });
                var group = source.AddBuffer<LinkedEntityGroup>(root);
                group.Add(root);
                group.Add(child);
                var stray = CreatePlacedEntity(source, 2, 2);
                source.AddComponentData(stray, new ChecksumLink { target = outside });

                using (var simulation = new LockstepSimulation(TestUtility.Config(), Options(presentation)))
                {
                    var entityManager = simulation.World.EntityManager;
                    Assert.AreEqual(2, TestUtility.Count<PlacedActor>(entityManager), "both placed actors are copied");
                    Assert.AreEqual(2, TestUtility.Count<ChecksumLink>(entityManager), "the linked child comes along");
                    Assert.AreEqual(0, TestUtility.Count<ChecksumValue>(entityManager), "entities outside the scene content stay behind");

                    var rootCopy = Entity.Null;
                    var childLink = default(ChecksumLink);
                    var strayLink = default(ChecksumLink);
                    using (var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ChecksumLink>()))
                    {
                        var entities = query.ToEntityArray(Allocator.Temp);
                        for (var i = 0; i < entities.Length; i++)
                        {
                            var link = entityManager.GetComponentData<ChecksumLink>(entities[i]);
                            if (entityManager.HasComponent<PlacedActor>(entities[i]))
                            {
                                strayLink = link;
                            }
                            else
                            {
                                childLink = link;
                            }
                        }
                    }
                    using (var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<LinkedEntityGroup>()))
                    {
                        rootCopy = query.GetSingletonEntity();
                        Assert.AreEqual(2, entityManager.GetBuffer<LinkedEntityGroup>(rootCopy).Length);
                    }
                    Assert.AreEqual(rootCopy, childLink.target, "references inside the scene content point at the copies");
                    Assert.AreEqual(Entity.Null, strayLink.target, "references to anything else are cleared");
                }
            }
        }

        private static Entity CreatePlacedEntity(EntityManager entityManager, ulong order, int value)
        {
            var entity = entityManager.CreateEntity(typeof(PlacedActor), typeof(ChecksumPadded));
            entityManager.SetComponentData(entity, new PlacedActor { order = order });
            entityManager.SetComponentData(entity, new ChecksumPadded { large = value });
            return entity;
        }

        // Six placed actors of one archetype, in a chunk of their own: a subscene section.
        private static void CreateSection(EntityManager entityManager, ulong section, int firstOrder)
        {
            for (var i = 0; i < 6; i++)
            {
                var entity = CreatePlacedEntity(entityManager, (ulong)(firstOrder + i), firstOrder + i);
                entityManager.AddSharedComponent(entity, new ChecksumSceneData { mask = section });
            }
        }

        // A placed actor of one of five archetypes, made of the test components; its value is its order.
        private static void CreateMapItem(EntityManager entityManager, ulong order, int kind)
        {
            Entity entity;
            switch (kind)
            {
                case 0:
                    entity = entityManager.CreateEntity(typeof(PlacedActor), typeof(ChecksumPadded));
                    break;
                case 1:
                    entity = entityManager.CreateEntity(typeof(PlacedActor), typeof(ChecksumValue));
                    break;
                case 2:
                    entity = entityManager.CreateEntity(typeof(PlacedActor), typeof(ChecksumPadded), typeof(ChecksumValue));
                    break;
                case 3:
                    entity = entityManager.CreateEntity(typeof(PlacedActor), typeof(ChecksumValue), typeof(TestSpawnedTag));
                    break;
                default:
                    entity = entityManager.CreateEntity(typeof(PlacedActor), typeof(ChecksumPadded), typeof(TestSpawnedTag));
                    break;
            }
            entityManager.SetComponentData(entity, new PlacedActor { order = order });
            if (entityManager.HasComponent<ChecksumPadded>(entity))
            {
                entityManager.SetComponentData(entity, new ChecksumPadded { large = (int)order });
            }
        }

        private static LockstepSimulationOptions Options(World presentationWorld)
        {
            var options = LockstepClientWorldUtility.CreateSimulationOptions(presentationWorld, false);
            options.AutoDiscoverSystems = false;
            return options;
        }
    }
}
