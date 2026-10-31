using NUnit.Framework;
using Pragma.Dots.Utils;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    public class EntityHierarchyTests
    {
        [Test]
        public void TryGetHiddenTag_FindsTheTagOfEntitiesInTheEditor()
        {
            // Entities keeps the tag internal and the lookup goes by name: a rename would quietly show hidden entities.
            using (var world = new World("Hierarchy"))
            {
                Assert.IsTrue(EntityHierarchy.TryGetHiddenTag(out var tag));
                Assert.AreEqual(EntityHierarchy.HIDDEN_TAG_NAME, tag.GetManagedType().FullName);
                Assert.IsTrue(tag.IsZeroSized, "a tag");

                var entity = world.EntityManager.CreateEntity(tag);
                Assert.IsTrue(world.EntityManager.HasComponent(entity, tag));
            }
        }
    }
}
