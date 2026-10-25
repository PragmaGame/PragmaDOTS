using NUnit.Framework;
using Pragma.Dots.Actors;
using Pragma.Dots.Actors.Editor;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Pragma.Dots.Tests
{
    public class ActorKeyTests
    {
        private const string FOLDER = "Assets/ActorKeyTests";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.CreateFolder("Assets", "ActorKeyTests");
            ActorKeyIndex.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(FOLDER);
            ActorKeyIndex.Reset();
        }

        [Test]
        public void Index_FindsTheDefinitionWithTheKey()
        {
            var scout = Create("Scout", "key_test_scout");

            Assert.AreSame(scout, ActorKeyIndex.Find("key_test_scout"));
            Assert.IsNull(ActorKeyIndex.Find("key_test_missing"));
            Assert.IsNull(ActorKeyIndex.Find(string.Empty));
        }

        [Test]
        public void Index_FollowsAKeyTheDefinitionWasGiven()
        {
            var scout = Create("Scout", "key_test_old");
            Assert.AreSame(scout, ActorKeyIndex.Find("key_test_old"));

            scout.Key = "key_test_new";

            Assert.IsNull(ActorKeyIndex.Find("key_test_old"), "the old key leads nowhere");
            Assert.AreSame(scout, ActorKeyIndex.Find("key_test_new"));
        }

        [Test]
        public void Drawer_StoresTheKeyOfThePickedDefinition()
        {
            var scout = Create("Scout", "key_test_scout");
            var parent = Create("Parent", string.Empty);
            var holder = ScriptableObject.CreateInstance<TestActorKeyHolder>();
            try
            {
                var serialized = new SerializedObject(holder);
                var unit = serialized.FindProperty(nameof(TestActorKeyHolder.unit));
                var element = serialized.FindProperty(nameof(TestActorKeyHolder.squad)).GetArrayElementAtIndex(0);
                Assert.IsTrue(ActorKeyDrawer.IsKey(unit));
                Assert.IsTrue(ActorKeyDrawer.IsKey(element), "an element of a key array is a key");
                Assert.IsFalse(ActorKeyDrawer.IsKey(serialized.FindProperty(nameof(TestActorKeyHolder.count))));

                Assert.IsTrue(ActorKeyDrawer.Assign(unit, scout));
                Assert.IsTrue(ActorKeyDrawer.Assign(element, scout));
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(new FixedString32Bytes("key_test_scout"), holder.unit);
                Assert.AreEqual(new FixedString32Bytes("key_test_scout"), holder.squad[0]);
                Assert.AreSame(scout, ActorKeyIndex.Find(unit.boxedValue.ToString()));

                Assert.IsFalse(ActorKeyDrawer.Assign(unit, parent), "a definition without a key is not stored");
                Assert.IsTrue(ActorKeyDrawer.Assign(unit, null));
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(default(FixedString32Bytes), holder.unit);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        private static ActorDefinition Create(string name, string key)
        {
            var definition = ScriptableObject.CreateInstance<ActorDefinition>();
            definition.Key = key;
            AssetDatabase.CreateAsset(definition, $"{FOLDER}/{name}.asset");
            return definition;
        }
    }
}
