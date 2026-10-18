using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pragma.Dots.Actors;
using Pragma.Dots.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pragma.Dots.Tests
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
            var parent = Definition("", null, new[] { "unit" }, Value(1), Elements(4));
            var child = Definition("veteran", parent, new[] { "player", "unit" }, Value(2), new TestActorPart());

            var tags = new List<string>();
            child.GetTags(tags);
            CollectionAssert.AreEqual(new[] { "unit", "player" }, tags, "the parent's tags first, each once");

            var parts = new List<ActorPart>();
            child.GetParts(parts);
            Assert.AreEqual(3, parts.Count);
            Assert.AreEqual((FixedPoint)2, ((ActorComponentPart<TestActorValue>)parts[0]).Value.value, "the own part replaces the parent's in place");
            Assert.IsInstanceOf<ActorBufferPart<TestActorElement>>(parts[1]);
            Assert.IsInstanceOf<TestActorPart>(parts[2], "a part of a new type comes last");
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
        public void ActorRegistry_IsBuiltFromTheCatalog_WithTypesByKeyAndTagsAsBits()
        {
            var catalog = Catalog(
                Definition("rifleman", null, new[] { "unit", "player" }, Value(3), new TestActorPart()),
                null,
                Definition("tank", null, new[] { "vehicle", "unit" }, Elements(7)));

            using (var world = new World("Actors"))
            {
                var entityManager = world.EntityManager;
                Assert.IsFalse(ActorUtility.HasRegistry(entityManager));
                ActorUtility.CreateRegistry(entityManager, catalog);
                Assert.IsTrue(ActorUtility.HasRegistry(entityManager));

                Assert.IsTrue(ActorLookup.TryGet(entityManager, out var actors));
                Assert.AreEqual(3, actors.TypeCount, "the empty slot keeps its index");
                Assert.AreEqual(3, actors.TagCount);
                Assert.AreEqual(new FixedString32Bytes("player"), actors.GetTagName(new ActorTag(0)), "tags in ordinal order");
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
                Assert.AreEqual(new Actor { key = "rifleman", type = 0 }, entityManager.GetComponentData<Actor>(rifleman));
                Assert.AreEqual((FixedPoint)3, entityManager.GetComponentData<TestActorValue>(rifleman).value);
                var placement = entityManager.GetComponentData<TestActorPlacement>(rifleman);
                Assert.IsFalse(placement.isPlaced, "a prefab of the registry");
                Assert.AreEqual(FixedPose.Identity, placement.pose);
                Assert.AreEqual(new FixedVector3(1, 1, 1), placement.scale);

                Assert.IsTrue(ActorLookup.TryGet(entityManager, out actors), "read again after the structural change");
                var spawned = actors.Instantiate(entityManager, tank);
                Assert.AreEqual(7, entityManager.GetBuffer<TestActorElement>(spawned)[0].value);
            }
        }

        [Test]
        public void ActorRegistryQuery_RequiresTheRegistry_AndReadsItWithoutAQueryPerUpdate()
        {
            var catalog = Catalog(Definition("rifleman", null, new[] { "unit" }, Value(3)));

            using (var world = new World("Actors"))
            using (var spawned = world.EntityManager.CreateEntityQuery(typeof(Actor)))
            {
                var system = world.CreateSystem<TestRegistrySystem>();
                system.Update(world.Unmanaged);
                Assert.AreEqual(0, spawned.CalculateEntityCount(), "without a registry the system does not update");

                ActorUtility.CreateRegistry(world.EntityManager, catalog);
                system.Update(world.Unmanaged);
                system.Update(world.Unmanaged);
                Assert.AreEqual(2, spawned.CalculateEntityCount(), "a rifleman on every update");
            }

            // Entities warns about a query a system creates in OnUpdate.
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void PlacedActors_GetTheTypeOfTheirKey_AndTheirOverrides()
        {
            var tower = Definition("tower", null, new[] { "building" }, Value(1), new TestActorPart());
            var catalog = Catalog(Definition("rifleman", null, new[] { "unit" }), tower);
            var orphan = Definition("orphan", null, new string[0], Value(5));

            using (var world = new World("Actors"))
            {
                var entityManager = world.EntityManager;
                ActorUtility.CreateRegistry(entityManager, catalog);
                var pose = new FixedPose(new FixedVector3(4, 0, 2), FixedQuaternion.AxisAngle(FixedVector3.Up, FixedMath.HalfPi));
                var placed = ActorUtility.CreatePlaced(entityManager, tower, pose, new FixedVector3(2, 1, 3), new ActorPart[] { Value(9) }, 2);
                var stray = ActorUtility.CreatePlaced(entityManager, orphan, FixedPose.Identity, new FixedVector3(1, 1, 1), null, 1);
                Assert.AreEqual(-1, entityManager.GetComponentData<Actor>(placed).type, "placing does not know the catalog");
                Assert.AreEqual(2ul, entityManager.GetComponentData<PlacedActor>(placed).order);

                LogAssert.Expect(LogType.Warning, new Regex("'orphan' has no type"));
                ActorUtility.ResolveTypes(entityManager);
                Assert.AreEqual(1, entityManager.GetComponentData<Actor>(placed).type, "the type comes from the registry, by key");
                Assert.AreEqual(-1, entityManager.GetComponentData<Actor>(stray).type, "a key no type has");

                Assert.AreEqual((FixedPoint)9, entityManager.GetComponentData<TestActorValue>(placed).value, "the override replaces the definition's part");
                var placement = entityManager.GetComponentData<TestActorPlacement>(placed);
                Assert.IsTrue(placement.isPlaced);
                Assert.AreEqual(pose, placement.pose);
                Assert.AreEqual(new FixedVector3(2, 1, 3), placement.scale);
            }
        }

        [Test]
        public void TwoPartsAddingOneType_TheFirstOneWins_AsWhenBaked()
        {
            var tower = Definition("tower", null, new string[0], Value(4), new TestMergeablePart(8, (1, 1)));
            using (var world = new World("Actors"))
            {
                var entityManager = world.EntityManager;
                LogAssert.Expect(LogType.Error, new Regex("TestMergeablePart of the actor 'tower' failed to build.*TestActorValue already"));
                var placed = ActorUtility.CreatePlaced(entityManager, tower, FixedPose.Identity, new FixedVector3(1, 1, 1), null, 1);
                Assert.AreEqual((FixedPoint)4, entityManager.GetComponentData<TestActorValue>(placed).value, "the second part does not overwrite it");
            }
        }

        [Test]
        public void ActorAuthoring_PlacesItsDefinition_AtThePoseOfItsGameObject_WithItsOverrides()
        {
            var tower = Definition("tower", null, new[] { "building" }, Value(1), new TestActorPart());
            var gameObject = new GameObject("Tower");
            _assets.Add(gameObject);
            gameObject.transform.SetPositionAndRotation(new Vector3(1.5f, 2f, -3f), Quaternion.Euler(0f, 90f, 0f));
            gameObject.transform.localScale = new Vector3(2f, 1f, 0.5f);
            var authoring = gameObject.AddComponent<ActorAuthoring>();
            authoring.Definition = tower;
            authoring.SetOverrides(Value(4));

            var placement = authoring.GetPlacement();
            Assert.AreEqual(new FixedVector3((FixedPoint)1.5f, 2, -3), placement.position);
            Assert.AreEqual(FixedMath.NormalizeSafe((FixedQuaternion)gameObject.transform.rotation), placement.rotation);
            Assert.AreEqual(new FixedVector3(2, 1, (FixedPoint)0.5f), authoring.GetPlacementScale());

            using (var world = new World("Actors"))
            {
                var entityManager = world.EntityManager;
                var placed = ActorUtility.CreatePlaced(entityManager, authoring, 7);
                Assert.AreEqual(7ul, entityManager.GetComponentData<PlacedActor>(placed).order);
                Assert.AreEqual((FixedPoint)4, entityManager.GetComponentData<TestActorValue>(placed).value);
                var built = entityManager.GetComponentData<TestActorPlacement>(placed);
                Assert.AreEqual(placement, built.pose);
                Assert.AreEqual(authoring.GetPlacementScale(), built.scale);
            }
        }

        [Test]
        public void MergeablePart_Override_ChangesTheEntriesItLists_AndKeepsTheOthers()
        {
            var parentPart = new TestMergeablePart(1, (1, 10), (2, 20));
            var parent = Definition("", null, new string[0], parentPart);
            var child = Definition("veteran", parent, new string[0], new TestMergeablePart(2, (2, 25), (3, 30)));

            var parts = new List<ActorPart>();
            child.GetParts(parts);
            Assert.AreEqual(1, parts.Count);
            CollectionAssert.AreEqual(new[] { (1, 10), (2, 25), (3, 30) }, Entries((TestMergeablePart)parts[0]));
            CollectionAssert.AreEqual(new[] { (1, 10), (2, 20) }, Entries(parentPart), "the parent's part does not change");

            using (var world = new World("Actors"))
            {
                var entityManager = world.EntityManager;
                var placed = ActorUtility.CreatePlaced(entityManager, child, FixedPose.Identity, new FixedVector3(1, 1, 1),
                    new ActorPart[] { new TestMergeablePart(3, (1, 11)) }, 1);
                var entries = entityManager.GetBuffer<TestMergedEntry>(placed);
                CollectionAssert.AreEqual(new[] { (1, 11), (2, 25), (3, 30) }, new[] { Pair(entries[0]), Pair(entries[1]), Pair(entries[2]) });
                Assert.AreEqual((FixedPoint)3, entityManager.GetComponentData<TestActorValue>(placed).value, "the override's settings");
            }

            var start = (TestMergeablePart)((IMergeableActorPart)parts[0]).CreateOverride();
            Assert.AreEqual(0, start.Entries.Count, "an override starts with no entries");
        }

        private static (int, int)[] Entries(TestMergeablePart part)
        {
            var entries = new (int, int)[part.Entries.Count];
            for (var i = 0; i < entries.Length; i++)
            {
                entries[i] = Pair(part.Entries[i]);
            }
            return entries;
        }

        private static (int, int) Pair(TestMergedEntry entry) => (entry.key, entry.value);

        [Test]
        public void LocalTransformPart_PlacesTheActor_AndKeepsANonUniformScaleApart()
        {
            var wall = Definition("wall", null, new string[0], new LocalTransformPart());
            var rotation = FixedQuaternion.RotateY(FixedMath.HalfPi);
            using (var world = new World("Actors"))
            {
                var entityManager = world.EntityManager;
                var uniform = ActorUtility.CreatePlaced(entityManager, wall, new FixedPose(new FixedVector3(1, 2, 3), rotation), new FixedVector3(2, 2, 2), null, 1);
                var local = entityManager.GetComponentData<LocalTransform>(uniform);
                Assert.AreEqual(new float3(1, 2, 3), local.Position);
                Assert.AreEqual(((quaternion)rotation).value, local.Rotation.value);
                Assert.AreEqual(2f, local.Scale);
                Assert.IsFalse(entityManager.HasComponent<PostTransformMatrix>(uniform));
                Assert.AreEqual(local.ToMatrix(), entityManager.GetComponentData<LocalToWorld>(uniform).Value);

                var stretched = ActorUtility.CreatePlaced(entityManager, wall, FixedPose.Identity, new FixedVector3(4, 1, 1), null, 2);
                Assert.AreEqual(1f, entityManager.GetComponentData<LocalTransform>(stretched).Scale, "the scale goes to the matrix");
                Assert.AreEqual(float4x4.Scale(4, 1, 1), entityManager.GetComponentData<PostTransformMatrix>(stretched).Value);
                Assert.AreEqual(float4x4.Scale(4, 1, 1), entityManager.GetComponentData<LocalToWorld>(stretched).Value);
            }
        }

        [Test]
        public void DefaultParts_AreSortedByOrder_WithoutTheOnesAnotherDefaultReplaces()
        {
            var types = ActorDefinition.SelectDefaultParts(new[] { typeof(TestDefaultPartLate), typeof(TestDefaultPartReplaced), typeof(TestDefaultPartReplacing) });
            CollectionAssert.AreEqual(new[] { typeof(TestDefaultPartReplacing), typeof(TestDefaultPartLate) }, types);

            types = ActorDefinition.SelectDefaultParts(new[] { typeof(TestDefaultPartLate), typeof(TestDefaultPartReplaced) });
            CollectionAssert.AreEqual(new[] { typeof(TestDefaultPartReplaced), typeof(TestDefaultPartLate) }, types, "without its replacement it stays");
        }

        private static ActorComponentPart<TestActorValue> Value(int value) => new ActorComponentPart<TestActorValue>(new TestActorValue { value = value });

        private static ActorBufferPart<TestActorElement> Elements(int value) => new ActorBufferPart<TestActorElement>(new TestActorElement { value = value });

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
    }
}
