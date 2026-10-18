using Pragma.Dots.Utils;

#if PRAGMA_DOTS_UNITY_PHYSICS
using NUnit.Framework;

namespace Pragma.Dots.Tests
{
    public class CollisionMatrixToFilterTests
    {
        [Test]
        public void CollisionMatrixToFilter_KeepsTheGroupIndexOfEachCall()
        {
            var first = CollisionMatrixToFilter.GetCollisionFilter(0, 1);
            var second = CollisionMatrixToFilter.GetCollisionFilter(0, 2);

            Assert.AreEqual(1, first.GroupIndex);
            Assert.AreEqual(2, second.GroupIndex);
            Assert.AreEqual(1u, second.BelongsTo);
            Assert.AreEqual(first.CollidesWith, second.CollidesWith);
        }

        [Test]
        public void CollisionMatrixToFilter_FollowsTheLayerMatrix()
        {
            var filter = CollisionMatrixToFilter.GenerateFilter(31);
            Assert.AreEqual(1u << 31, filter.BelongsTo);
            for (var layer = 0; layer < 32; layer++)
            {
                var collides = (filter.CollidesWith & (1u << layer)) != 0;
                Assert.AreEqual(!UnityEngine.Physics.GetIgnoreLayerCollision(31, layer), collides, $"layer {layer}");
            }
        }
    }
}
#endif
