using System.Text.RegularExpressions;
using NUnit.Framework;
using Pragma.Lockstep.Mathematics;
using Pragma.Lockstep.Views;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pragma.Lockstep.Tests
{
    public class ViewPartTests
    {
        [Test]
        public void ViewPart_KeysTheViewByTheActor_UnlessItNamesAnotherKey()
        {
            var plain = Definition("Arena Avatar", new ViewPart());
            var named = Definition("veteran", new ViewPart { Key = "Arena Avatar" });
            var invalid = Definition("broken", new ViewPart { Key = new string('k', 40) });
            using (var world = new World("Parts"))
            {
                var entityManager = world.EntityManager;
                Assert.AreEqual(new FixedString32Bytes("Arena Avatar"), Build(entityManager, plain).value, "the actor's own key");
                Assert.AreEqual(new FixedString32Bytes("Arena Avatar"), Build(entityManager, named).value);
                LogAssert.Expect(LogType.Error, new Regex("must be 1 to 29 bytes"));
                Assert.AreEqual(new FixedString32Bytes("broken"), Build(entityManager, invalid).value, "a key that does not fit falls back to the actor's");
            }
            Object.DestroyImmediate(plain);
            Object.DestroyImmediate(named);
            Object.DestroyImmediate(invalid);
        }

        private static EntityViewKey Build(EntityManager entityManager, ActorDefinition definition)
        {
            var entity = LockstepActorUtility.CreatePlaced(entityManager, definition, LockstepTransform.Identity, new FixedVector3(1, 1, 1), null, 1);
            return entityManager.GetComponentData<EntityViewKey>(entity);
        }

        private static ActorDefinition Definition(string key, params ActorPart[] parts)
        {
            var definition = ScriptableObject.CreateInstance<ActorDefinition>();
            definition.Key = key;
            definition.SetParts(parts);
            return definition;
        }
    }
}
