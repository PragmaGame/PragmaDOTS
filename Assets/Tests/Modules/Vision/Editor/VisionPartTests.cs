using NUnit.Framework;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using Pragma.Dots.Vision;
using Unity.Entities;
using UnityEngine;

namespace Pragma.Dots.Tests
{
    public class VisionPartTests
    {
        [Test]
        public void VisionParts_BuildTheGridAroundThePlacement_AndASourceWithoutOwner()
        {
            var definition = ScriptableObject.CreateInstance<ActorDefinition>();
            definition.Key = "watchtower";
            definition.SetParts(new TransformPart(), new VisionGridPart { Size = new FixedVector2(20, 10), CellSize = 4 }, new VisionSourcePart { Radius = 7 });
            using (var world = new World("Parts"))
            {
                var entityManager = world.EntityManager;
                var entity = LockstepActorUtility.CreatePlaced(entityManager, definition, LockstepTransform.FromPosition(new FixedVector3(2, 0, 4)), new FixedVector3(1, 1, 1), null, 1);

                var grid = entityManager.GetComponentData<LockstepVisionGrid>(entity);
                Assert.AreEqual(5, grid.width);
                Assert.AreEqual(3, grid.height, "the size is rounded up to whole cells");
                Assert.AreEqual(new FixedVector2(-8, -2), grid.origin, "centred on the placement");
                Assert.IsTrue(entityManager.HasBuffer<LockstepVisionCell>(entity));

                var source = entityManager.GetComponentData<LockstepVisionSource>(entity);
                Assert.AreEqual((FixedPoint)7, source.radius);
                Assert.AreEqual(-1, source.slot, "gameplay gives the source its owner");
            }
            Object.DestroyImmediate(definition);
        }
    }
}
