using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pragma.Dots.Actors;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pragma.Dots.Tests
{
    public class LockstepActorTests
    {
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _assets)
            {
                Object.DestroyImmediate(asset);
            }
            _assets.Clear();
        }

        [Test]
        public void NewActorDefinitions_StartWithATransformAndAnEntityId()
        {
            var definition = Definition("unit", null, new string[0]);
            typeof(ActorDefinition).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(definition, null);

            Assert.AreEqual(2, definition.OwnParts.Count);
            Assert.IsInstanceOf<TransformPart>(definition.OwnParts[0], "the default parts in their order");
            Assert.IsInstanceOf<EntityIdPart>(definition.OwnParts[1]);
        }

        [Test]
        public void ActorRegistry_IsCopiedIntoTheSimulation()
        {
            var catalog = Catalog(
                Definition("rifleman", null, new[] { "unit", "player" }, new TransformPart(), Value(3), new EntityIdPart()),
                null,
                Definition("tank", null, new[] { "vehicle", "unit" }, new TransformPart { Interpolate = false }, new ActorBufferPart<ChecksumElement>(new ChecksumElement { value = 7 })));

            using (var presentation = new World("Presentation"))
            {
                ActorUtility.CreateRegistry(presentation.EntityManager, catalog);
                using (var simulation = new LockstepSimulation(TestUtility.Config(), Options(presentation)))
                {
                    var entityManager = simulation.World.EntityManager;
                    Assert.IsTrue(ActorLookup.TryGet(entityManager, out var actors));
                    Assert.AreEqual(3, actors.TypeCount, "the empty slot keeps its index");
                    Assert.AreEqual(3, actors.TagCount);
                    Assert.AreEqual(Entity.Null, actors.GetPrefab(1));
                    Assert.IsTrue(actors.TryGetType("tank", out var tank));
                    Assert.IsTrue(actors.TryGetTag("vehicle", out var vehicle));
                    Assert.IsTrue(actors.HasTag(tank, vehicle));

                    var rifleman = actors.Instantiate(entityManager, 0);
                    Assert.AreEqual(new Actor { key = "rifleman", type = 0 }, entityManager.GetComponentData<Actor>(rifleman));
                    Assert.AreEqual((FixedPoint)3, entityManager.GetComponentData<ChecksumValue>(rifleman).value);
                    Assert.IsTrue(entityManager.HasComponent<LockstepEntityId>(rifleman));
                    Assert.IsTrue(entityManager.HasComponent<LockstepTransformPrevious>(rifleman));
                    Assert.AreEqual(FixedPoint.One, entityManager.GetComponentData<LockstepTransform>(rifleman).scale);

                    Assert.IsTrue(ActorLookup.TryGet(entityManager, out actors), "read again after the structural change");
                    var spawned = actors.Instantiate(entityManager, tank);
                    Assert.AreEqual(7, entityManager.GetBuffer<ChecksumElement>(spawned)[0].value);
                    Assert.IsFalse(entityManager.HasComponent<LockstepTransformPrevious>(spawned));
                }
            }
        }

        [Test]
        public void PlacedActors_AreCopiedIntoTheSimulation_WithTheTypeOfTheirKey()
        {
            var tower = Definition("tower", null, new[] { "building" }, new TransformPart(), Value(1));
            var catalog = Catalog(Definition("rifleman", null, new[] { "unit" }), tower);
            var orphan = Definition("orphan", null, new string[0], Value(5));

            using (var presentation = new World("Presentation"))
            {
                var entityManager = presentation.EntityManager;
                ActorUtility.CreateRegistry(entityManager, catalog);
                var placement = new FixedPose(new FixedVector3(4, 0, 2), FixedQuaternion.Identity);
                ActorUtility.CreatePlaced(entityManager, tower, placement, new FixedVector3(2, 1, 3), new ActorPart[] { Value(9) }, 2);
                ActorUtility.CreatePlaced(entityManager, orphan, FixedPose.Identity, new FixedVector3(1, 1, 1), null, 1);

                LogAssert.Expect(LogType.Warning, new Regex("'orphan' has no type"));
                using (var simulation = new LockstepSimulation(TestUtility.Config(), Options(presentation)))
                {
                    var simulationManager = simulation.World.EntityManager;
                    using (var query = simulationManager.CreateEntityQuery(ComponentType.ReadOnly<PlacedActor>(), ComponentType.ReadOnly<Actor>()))
                    {
                        var entities = query.ToEntityArray(Allocator.Temp);
                        Assert.AreEqual(2, entities.Length);
                        var placed = default(Entity);
                        foreach (var entity in entities)
                        {
                            var actor = simulationManager.GetComponentData<Actor>(entity);
                            if (actor.key == "tower")
                            {
                                placed = entity;
                                Assert.AreEqual(1, actor.type, "the type comes from the registry, by key");
                            }
                            else
                            {
                                Assert.AreEqual(-1, actor.type, "a key no type has");
                            }
                        }
                        Assert.AreEqual((FixedPoint)9, simulationManager.GetComponentData<ChecksumValue>(placed).value, "the override replaces the definition's part");
                        var transform = simulationManager.GetComponentData<LockstepTransform>(placed);
                        Assert.AreEqual(placement.position, transform.position);
                        Assert.AreEqual((FixedPoint)2, transform.scale, "the uniform scale is the X of the placement scale");
                    }
                }
            }
        }

        [Test]
        public void Actors_SpawnedAndTaggedFromTheRegistry_StayInSyncUnderLatencyAndJitter()
        {
            var catalog = Catalog(
                Definition("rifleman", null, new[] { "unit", "player" }, new TransformPart(), new EntityIdPart()),
                Definition("tank", null, new[] { "unit", "vehicle" }, new TransformPart(), new EntityIdPart()),
                Definition("tower", null, new[] { "building" }, new TransformPart()));
            var settings = LockstepServerSettings.Default;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.MinPlayersToStart = 2;
            settings.StartDelaySeconds = 0.3f;
            settings.ChecksumInterval = 10;

            var worlds = new[] { new World("Presentation A"), new World("Presentation B") };
            try
            {
                // The same map content on both clients, placed in different orders.
                for (var w = 0; w < worlds.Length; w++)
                {
                    var entityManager = worlds[w].EntityManager;
                    ActorUtility.CreateRegistry(entityManager, catalog);
                    for (var i = 0; i < 3; i++)
                    {
                        var order = w == 0 ? i : 2 - i;
                        var placement = new FixedPose(new FixedVector3(order * 10, 0, 0), FixedQuaternion.Identity);
                        ActorUtility.CreatePlaced(entityManager, catalog.Actors[order], placement, new FixedVector3(1, 1, 1), null, (ulong)order);
                    }
                }

                using (var harness = new SessionHarness(settings, latency: 0.06, jitter: 0.03))
                {
                    foreach (var world in worlds)
                    {
                        var options = Options(world);
                        options.AdditionalSystems = new[] { typeof(TestActorSystem) };
                        harness.AddClient(options);
                    }
                    harness.Run(6);

                    TestUtility.AssertSameChecksums(harness.Clients[0], harness.Clients[1], 10);
                    Assert.Greater(harness.Server.VerifiedChecksumCount, 10);
                    Assert.IsEmpty(harness.Desyncs);
                    var simulation = harness.Clients[0].Simulation.World.EntityManager;
                    Assert.AreEqual(5, TestUtility.Count<Actor>(simulation), "three placed actors and two spawned ones");
                }
            }
            finally
            {
                foreach (var world in worlds)
                {
                    world.Dispose();
                }
            }
        }

        private static ActorComponentPart<ChecksumValue> Value(int value) => new ActorComponentPart<ChecksumValue>(new ChecksumValue { value = value });

        private ActorDefinition Definition(string key, ActorDefinition parent, string[] tags, params ActorPart[] parts)
        {
            var definition = ScriptableObject.CreateInstance<ActorDefinition>();
            definition.name = key;
            definition.Key = key;
            definition.Parent = parent;
            definition.SetTags(tags);
            definition.SetParts(parts);
            _assets.Add(definition);
            return definition;
        }

        private ActorCatalog Catalog(params ActorDefinition[] definitions)
        {
            var catalog = ScriptableObject.CreateInstance<ActorCatalog>();
            catalog.SetActors(definitions);
            _assets.Add(catalog);
            return catalog;
        }

        private static LockstepSimulationOptions Options(World presentationWorld)
        {
            var options = LockstepClientWorldUtility.CreateSimulationOptions(presentationWorld, false);
            options.AutoDiscoverSystems = false;
            return options;
        }
    }
}
