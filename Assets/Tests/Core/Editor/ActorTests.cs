using System.Collections.Generic;
using NUnit.Framework;
using Pragma.Lockstep.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pragma.Lockstep.Tests
{
    public class ActorTests
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
        public void ActorDefinition_InheritsTagsAndParts_OwnPartsReplaceTheParentsOfTheirType()
        {
            var parent = Definition("", null, new[] { "unit" }, new TransformPart(), Value(1), new ActorComponentPart<ChecksumTag>());
            var child = Definition("veteran", parent, new[] { "player", "unit" }, Value(2), new EntityIdPart());

            var tags = new List<string>();
            child.GetTags(tags);
            CollectionAssert.AreEqual(new[] { "unit", "player" }, tags, "the parent's tags first, each once");

            var parts = new List<ActorPart>();
            child.GetParts(parts);
            Assert.AreEqual(4, parts.Count);
            Assert.IsInstanceOf<TransformPart>(parts[0]);
            Assert.AreEqual((FixedPoint)2, ((ActorComponentPart<ChecksumValue>)parts[1]).Value.value, "the own part replaces the parent's in place");
            Assert.IsInstanceOf<ActorComponentPart<ChecksumTag>>(parts[2]);
            Assert.IsInstanceOf<EntityIdPart>(parts[3], "a part of a new type comes last");
        }

        [Test]
        public void ActorDefinition_StopsAtACycleOfParents()
        {
            var a = Definition("a", null, new[] { "x" });
            var b = Definition("b", a, new[] { "y" });
            a.Parent = b;

            var lineage = new List<ActorDefinition>();
            Assert.IsFalse(a.GetLineage(lineage));
            Assert.AreEqual(2, lineage.Count);
            var errors = new List<string>();
            Catalog(a).Validate(errors);
            Assert.That(errors, Has.Some.Contains("cycle"));
        }

        [Test]
        public void ActorCatalog_Validate_ReportsKeysAndTagsTheRegistryCannotHold()
        {
            var catalog = Catalog(
                Definition("rifleman", null, new[] { "unit" }),
                Definition("rifleman", null, new[] { "unit" }),
                Definition("", null, new string[0]),
                Definition("tank", null, new[] { new string('t', 30) }, Value(1), Value(2)));

            var errors = new List<string>();
            catalog.Validate(errors);
            Assert.That(errors, Has.Some.Contains("already the key of 0"));
            Assert.That(errors, Has.Some.Contains("the key '' must be"));
            Assert.That(errors, Has.Some.Contains("the tag"));
            Assert.That(errors, Has.Some.Contains("two parts of type"));
        }

        [Test]
        public void ActorRegistry_IsBuiltFromTheCatalog_AndCopiedIntoTheSimulation()
        {
            var catalog = Catalog(
                Definition("rifleman", null, new[] { "unit", "player" }, new TransformPart(), Value(3), new EntityIdPart()),
                null,
                Definition("tank", null, new[] { "vehicle", "unit" }, new TransformPart { Interpolate = false }, new ActorBufferPart<ChecksumElement>(new ChecksumElement { value = 7 })));

            using (var presentation = new World("Presentation"))
            {
                LockstepActorUtility.CreateRegistry(presentation.EntityManager, catalog);
                using (var simulation = new LockstepSimulation(TestUtility.Config(), Options(presentation)))
                {
                    var entityManager = simulation.World.EntityManager;
                    Assert.IsTrue(LockstepActors.TryGet(entityManager, out var actors));
                    Assert.AreEqual(3, actors.TypeCount, "the empty slot keeps its index");
                    Assert.AreEqual(3, actors.TagCount);
                    Assert.AreEqual(new FixedString32Bytes("player"), actors.GetTagName(new LockstepActorTag(0)), "tags in ordinal order");
                    Assert.AreEqual(Entity.Null, actors.GetPrefab(1));

                    Assert.IsTrue(actors.TryGetType("tank", out var tank));
                    Assert.AreEqual(2, tank);
                    Assert.IsTrue(actors.TryGetTag("unit", out var unit));
                    Assert.IsTrue(actors.TryGetTag("vehicle", out var vehicle));
                    Assert.IsTrue(actors.HasTag(0, unit));
                    Assert.IsFalse(actors.HasTag(0, vehicle));
                    Assert.IsTrue(actors.HasTag(tank, vehicle));
                    Assert.IsFalse(actors.TryGetTag("building", out _));

                    var rifleman = actors.Instantiate(entityManager, 0);
                    Assert.AreEqual(new LockstepActor { key = "rifleman", type = 0 }, entityManager.GetComponentData<LockstepActor>(rifleman));
                    Assert.AreEqual((FixedPoint)3, entityManager.GetComponentData<ChecksumValue>(rifleman).value);
                    Assert.IsTrue(entityManager.HasComponent<LockstepEntityId>(rifleman));
                    Assert.IsTrue(entityManager.HasComponent<LockstepTransformPrevious>(rifleman));

                    Assert.IsTrue(LockstepActors.TryGet(entityManager, out actors), "read again after the structural change");
                    var spawned = actors.Instantiate(entityManager, tank);
                    Assert.AreEqual(7, entityManager.GetBuffer<ChecksumElement>(spawned)[0].value);
                    Assert.IsFalse(entityManager.HasComponent<LockstepTransformPrevious>(spawned));
                }
            }
        }

        [Test]
        public void PlacedActors_GetTheTypeOfTheirKey_AndTheirOverrides()
        {
            var tower = Definition("tower", null, new[] { "building" }, new TransformPart(), Value(1));
            var catalog = Catalog(Definition("rifleman", null, new[] { "unit" }), tower);
            var orphan = Definition("orphan", null, new string[0], Value(5));

            using (var presentation = new World("Presentation"))
            {
                var entityManager = presentation.EntityManager;
                LockstepActorUtility.CreateRegistry(entityManager, catalog);
                var placement = LockstepTransform.FromPosition(new FixedVector3(4, 0, 2));
                LockstepActorUtility.CreatePlaced(entityManager, tower, placement, new FixedVector3(1, 1, 1), new ActorPart[] { Value(9) }, 2);
                LockstepActorUtility.CreatePlaced(entityManager, orphan, LockstepTransform.Identity, new FixedVector3(1, 1, 1), null, 1);

                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'orphan' has no type"));
                using (var simulation = new LockstepSimulation(TestUtility.Config(), Options(presentation)))
                {
                    var simulationManager = simulation.World.EntityManager;
                    using (var query = simulationManager.CreateEntityQuery(ComponentType.ReadOnly<LockstepSceneEntity>(), ComponentType.ReadOnly<LockstepActor>()))
                    {
                        var entities = query.ToEntityArray(Allocator.Temp);
                        Assert.AreEqual(2, entities.Length);
                        var placed = default(Entity);
                        foreach (var entity in entities)
                        {
                            var actor = simulationManager.GetComponentData<LockstepActor>(entity);
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
                        Assert.AreEqual(placement.position, simulationManager.GetComponentData<LockstepTransform>(placed).position);
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
                    LockstepActorUtility.CreateRegistry(entityManager, catalog);
                    for (var i = 0; i < 3; i++)
                    {
                        var order = w == 0 ? i : 2 - i;
                        var placement = LockstepTransform.FromPosition(new FixedVector3(order * 10, 0, 0));
                        LockstepActorUtility.CreatePlaced(entityManager, catalog.Actors[order], placement, new FixedVector3(1, 1, 1), null, (ulong)order);
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
                    Assert.AreEqual(5, TestUtility.Count<LockstepActor>(simulation), "three placed actors and two spawned ones");
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
            definition.name = string.IsNullOrEmpty(key) ? "base" : key;
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
