using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pragma.Dots.Actors;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using Pragma.Dots.Stats;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pragma.Dots.Tests
{
    public class StatPartTests
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
        public void StatsPart_BuildsTheStats_AndTheBuffersTheyTakePartIn()
        {
            var unit = Definition("rifleman", null, Infantry());
            var bank = Definition("bank", null, new TestStatsPart { ReceivesGrants = false, Resources = { new StatResourceEntry<TestStat>(TestStat.Gold, 50) } });
            using (var world = new World("Stat Parts"))
            {
                var entityManager = world.EntityManager;
                var rifleman = Place(entityManager, unit);
                var stats = entityManager.GetBuffer<LockstepStat>(rifleman);
                Assert.AreEqual(3, stats.Length);
                Assert.IsTrue(stats.TryGet(TestStat.MaxHealth, out var maxHealth) && maxHealth.IsAttribute);
                Assert.AreEqual((FixedPoint)100, maxHealth.baseValue);
                Assert.IsTrue(stats.TryGet(TestStat.Health, out var health) && health.IsResource);
                Assert.AreEqual(LockstepStats.Id(TestStat.MaxHealth), health.cap);
                Assert.AreEqual(LockstepStatCapPolicy.KeepRatio, health.capPolicy);
                Assert.IsTrue(entityManager.HasBuffer<LockstepStatModifier>(rifleman));
                Assert.IsTrue(entityManager.HasBuffer<LockstepStatChange>(rifleman));
                Assert.IsTrue(entityManager.HasBuffer<LockstepStatGrantor>(rifleman));
                var targets = entityManager.GetBuffer<LockstepStatTarget>(rifleman);
                Assert.AreEqual(1, targets.Length);
                Assert.AreEqual(LockstepStats.Id(TestStatTarget.Infantry), targets[0].value);

                var vault = Place(entityManager, bank);
                Assert.IsTrue(entityManager.GetBuffer<LockstepStat>(vault).TryGetValue(TestStat.Gold, out var gold));
                Assert.AreEqual((FixedPoint)50, gold);
                Assert.IsFalse(entityManager.HasBuffer<LockstepStatModifier>(vault), "no attribute, no modifiers");
                Assert.IsTrue(entityManager.HasBuffer<LockstepStatChange>(vault));
                Assert.IsFalse(entityManager.HasBuffer<LockstepStatGrantor>(vault));
                Assert.IsFalse(entityManager.HasBuffer<LockstepStatTarget>(vault));
            }
        }

        [Test]
        public void StatsPart_Override_ChangesOnlyTheStatsItLists()
        {
            var rifleman = Definition("", null, Infantry());
            var veteran = Definition("veteran", rifleman, new TestStatsPart
            {
                Attributes = { new StatAttributeEntry<TestStat>(TestStat.MaxHealth, 150) },
                Targets = { TestStatTarget.Veteran },
            });
            using (var world = new World("Stat Parts"))
            {
                var entityManager = world.EntityManager;
                var placed = ActorUtility.CreatePlaced(entityManager, veteran, FixedPose.Identity, new FixedVector3(1, 1, 1),
                    new ActorPart[] { new TestStatsPart { Attributes = { new StatAttributeEntry<TestStat>(TestStat.Speed, 7) } } }, 1);
                var stats = entityManager.GetBuffer<LockstepStat>(placed);
                Assert.AreEqual(3, stats.Length, "nothing inherited is lost");
                Assert.IsTrue(stats.TryGetBase(TestStat.MaxHealth, out var maxHealth));
                Assert.AreEqual((FixedPoint)150, maxHealth, "from the derived definition");
                Assert.IsTrue(stats.TryGetBase(TestStat.Speed, out var speed));
                Assert.AreEqual((FixedPoint)7, speed, "from the placement");
                Assert.IsTrue(stats.TryGet(TestStat.Health, out var health) && health.IsResource, "from the parent");
                var targets = entityManager.GetBuffer<LockstepStatTarget>(placed);
                Assert.AreEqual(2, targets.Length, "targets add up");
            }

            var parentPart = (TestStatsPart)rifleman.OwnParts[0];
            Assert.AreEqual(2, parentPart.Attributes.Count, "the parent's part does not change");
            Assert.AreEqual((FixedPoint)100, parentPart.Attributes[0].BaseValue);
        }

        [Test]
        public void StatsPart_Override_TurnsAnAttributeIntoAResource()
        {
            var parent = Definition("", null, new TestStatsPart { Attributes = { new StatAttributeEntry<TestStat>(TestStat.Gold, 3) } });
            var child = Definition("banker", parent, new TestStatsPart { Resources = { new StatResourceEntry<TestStat>(TestStat.Gold, 80) } });
            using (var world = new World("Stat Parts"))
            {
                var stats = world.EntityManager.GetBuffer<LockstepStat>(Place(world.EntityManager, child));
                Assert.AreEqual(1, stats.Length);
                Assert.IsTrue(stats[0].IsResource);
                Assert.AreEqual((FixedPoint)80, stats[0].value);
            }
        }

        [Test]
        public void StatsPart_Override_WinsOverAStatTheParentListsTwice()
        {
            var parent = Definition("", null, new TestStatsPart
            {
                Attributes = { new StatAttributeEntry<TestStat>(TestStat.Speed, 5), new StatAttributeEntry<TestStat>(TestStat.Speed, 6) },
            });
            var child = Definition("fast", parent, new TestStatsPart { Attributes = { new StatAttributeEntry<TestStat>(TestStat.Speed, 9) } });
            using (var world = new World("Stat Parts"))
            {
                var stats = world.EntityManager.GetBuffer<LockstepStat>(Place(world.EntityManager, child));
                Assert.AreEqual(1, stats.Length, "the override stands for both");
                Assert.IsTrue(stats.TryGetBase(TestStat.Speed, out var speed));
                Assert.AreEqual((FixedPoint)9, speed);
            }
        }

        [Test]
        public void StatsPart_CappedResource_StartsFullOrWithItsAmount()
        {
            var part = new TestStatsPart
            {
                Attributes = { new StatAttributeEntry<TestStat>(TestStat.MaxHealth, 100) },
                Resources = { new StatResourceEntry<TestStat>(TestStat.Health, TestStat.MaxHealth, LockstepStatCapPolicy.KeepRatio, 25) },
            };
            var site = Definition("site", null, part);
            var tower = Definition("tower", null, Infantry());
            using (var world = new World("Stat Parts"))
            {
                var entityManager = world.EntityManager;
                Assert.IsTrue(entityManager.GetBuffer<LockstepStat>(Place(entityManager, site)).TryGet(TestStat.Health, out var health));
                Assert.AreEqual((FixedPoint)25, health.value, "a construction site starts with its amount");
                Assert.AreEqual(FixedPoint.Zero, health.max);
                Assert.IsTrue(entityManager.GetBuffer<LockstepStat>(Place(entityManager, tower)).TryGet(TestStat.Health, out health));
                Assert.Less(health.max, FixedPoint.Zero, "without an amount it fills on the first update");
            }
        }

        [Test]
        public void StatsPart_ReportsDuplicateStatsAndMissingCaps()
        {
            var part = new TestStatsPart
            {
                Attributes = { new StatAttributeEntry<TestStat>(TestStat.Speed, 4), new StatAttributeEntry<TestStat>(TestStat.Speed, 6) },
                Resources = { new StatResourceEntry<TestStat>(TestStat.Health, TestStat.MaxHealth) },
            };
            var broken = Definition("broken", null, part);
            using (var world = new World("Stat Parts"))
            {
                LogAssert.Expect(LogType.Warning, new Regex("lists the stat Speed twice"));
                LogAssert.Expect(LogType.Warning, new Regex("Health .* capped by MaxHealth, which is not an attribute"));
                var stats = world.EntityManager.GetBuffer<LockstepStat>(Place(world.EntityManager, broken));
                Assert.IsTrue(stats.TryGetBase(TestStat.Speed, out var speed));
                Assert.AreEqual((FixedPoint)6, speed, "the last one counts");
            }
        }

        [Test]
        public void StatGrantsPart_GivesItsGrants_ToTheEntitiesThatNameIt()
        {
            var banner = new TestStatGrantsPart { Source = new LockstepStatSource(7, 1) };
            banner.Grants.Add(new StatGrantEntry<TestStat, TestStatTarget>(TestStat.Speed, LockstepStatModifierType.AdditivePercent, FixedPoint.Half,
                TestStatTarget.Infantry));
            banner.Grants.Add(new StatGrantEntry<TestStat, TestStatTarget>(TestStat.MaxHealth, LockstepStatModifierType.Flat, 10, TestStatTarget.Any));
            var squad = Definition("squad", null, banner);
            var grantsOverride = new TestStatGrantsPart { Source = new LockstepStatSource(7, 2) };
            grantsOverride.Grants.Add(new StatGrantEntry<TestStat, TestStatTarget>(TestStat.MaxHealth, LockstepStatModifierType.Flat, 20, TestStatTarget.Any));
            var eliteSquad = Definition("elite squad", squad, grantsOverride);
            var unit = Definition("rifleman", null, Infantry());

            using (var simulation = new LockstepSimulation(TestUtility.Config(), TestUtility.Options(typeof(LockstepStatSystem))))
            {
                var entityManager = simulation.World.EntityManager;
                var leader = Place(entityManager, eliteSquad);
                var grants = entityManager.GetBuffer<LockstepStatGrant>(leader);
                Assert.AreEqual(2, grants.Length, "the override changed one grant and kept the other");
                Assert.AreEqual(new LockstepStatSource(7, 2), grants[0].modifier.source, "every grant carries the override's source");

                var rifleman = Place(entityManager, unit);
                entityManager.GetBuffer<LockstepStatGrantor>(rifleman).Add(new LockstepStatGrantor(leader));
                TestUtility.Step(simulation);

                var stats = entityManager.GetBuffer<LockstepStat>(rifleman, true);
                Assert.IsTrue(stats.TryGetValue(TestStat.Speed, out var speed));
                Assert.AreEqual(FixedPoint.FromFraction(15, 2), speed, "+50 % to infantry");
                Assert.IsTrue(stats.TryGetValue(TestStat.MaxHealth, out var maxHealth));
                Assert.AreEqual((FixedPoint)120, maxHealth, "+20 to everyone, from the elite squad");
            }
        }

        private static TestStatsPart Infantry()
        {
            return new TestStatsPart
            {
                Attributes =
                {
                    new StatAttributeEntry<TestStat>(TestStat.MaxHealth, 100),
                    new StatAttributeEntry<TestStat>(TestStat.Speed, 5),
                },
                Resources = { new StatResourceEntry<TestStat>(TestStat.Health, TestStat.MaxHealth) },
                Targets = { TestStatTarget.Infantry },
            };
        }

        private static Entity Place(EntityManager entityManager, ActorDefinition definition)
        {
            return ActorUtility.CreatePlaced(entityManager, definition, FixedPose.Identity, new FixedVector3(1, 1, 1), null, 1);
        }

        private ActorDefinition Definition(string key, ActorDefinition parent, params ActorPart[] parts)
        {
            var definition = ScriptableObject.CreateInstance<ActorDefinition>();
            definition.name = string.IsNullOrEmpty(key) ? "base" : key;
            definition.Key = key;
            definition.Parent = parent;
            definition.SetParts(parts);
            _assets.Add(definition);
            return definition;
        }
    }
}
