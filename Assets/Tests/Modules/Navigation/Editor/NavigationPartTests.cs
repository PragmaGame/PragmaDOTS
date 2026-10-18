using NUnit.Framework;
using Pragma.Dots.Actors;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using Pragma.Dots.Navigation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Pragma.Dots.Tests
{
    public class NavigationPartTests
    {
        private static readonly FixedVector3 One = new FixedVector3(1, 1, 1);

        [Test]
        public void NavAgentPart_BuildsAnAgentThatWalks()
        {
            var agent = new NavAgentPart { Speed = 5, AngularSpeed = 90, TurnInPlaceAngle = 200, StoppingDistance = FixedPoint.FromFraction(1, 4), Radius = 1 };
            var definition = Definition("tank", new TransformPart(), agent);
            using (var world = new World("Parts"))
            {
                var entityManager = world.EntityManager;
                var entity = ActorUtility.CreatePlaced(entityManager, definition, new FixedPose(new FixedVector3(10, 0, 0), FixedQuaternion.Identity), One, null, 1);

                var built = entityManager.GetComponentData<LockstepNavAgent>(entity);
                Assert.AreEqual((FixedPoint)5, built.speed);
                Assert.AreEqual(FixedMath.ToRadians(90), built.angularSpeed, "degrees in the inspector, radians in the simulation");
                Assert.AreEqual(FixedMath.ToRadians(180), built.turnInPlaceAngle, "the angle is clamped to 180 degrees");
                Assert.AreEqual(FixedPoint.FromFraction(1, 4), built.stoppingDistance);
                Assert.AreEqual(FixedPoint.One, built.radius);
                Assert.IsTrue(entityManager.HasBuffer<LockstepNavWaypoint>(entity));
                Assert.IsTrue(entityManager.HasComponent<LockstepNavVelocity>(entity));
            }
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void NavObstaclePart_MovesWithATransform_OrKeepsThePlacementAndItsScale()
        {
            var obstacle = new NavObstaclePart { Center = new FixedVector2(1, 1), Size = new FixedVector2(2, 2) };
            var building = Definition("building", new TransformPart(), obstacle);
            var wall = Definition("wall", obstacle);
            using (var world = new World("Parts"))
            {
                var entityManager = world.EntityManager;
                var rotation = FixedQuaternion.AxisAngle(FixedVector3.Up, FixedMath.HalfPi);
                var placement = new FixedPose(new FixedVector3(5, 0, 5), rotation);

                var moving = ActorUtility.CreatePlaced(entityManager, building, placement, new FixedVector3(2, 1, 3), null, 1);
                var local = entityManager.GetComponentData<LockstepNavObstacle>(moving);
                Assert.AreEqual(new FixedVector2(1, 1), local.center, "the box of a moving obstacle stays local");
                Assert.AreEqual(new FixedVector2(2, 2), local.size);
                Assert.AreEqual((FixedPoint)2, entityManager.GetComponentData<LockstepTransform>(moving).scale, "it scales with the actor's transform, by X");

                var fixedWall = ActorUtility.CreatePlaced(entityManager, wall, placement, new FixedVector3(2, 1, 3), null, 2);
                var box = entityManager.GetComponentData<LockstepNavObstacle>(fixedWall);
                Assert.AreEqual(new FixedVector2(2, 3), box.center, "a static box takes the non-uniform scale of its placement");
                Assert.AreEqual(new FixedVector2(4, 6), box.size);
                var pose = entityManager.GetComponentData<LockstepTransform>(fixedWall);
                Assert.AreEqual(placement.position, pose.position);
                Assert.AreEqual(rotation, pose.rotation);
                Assert.AreEqual(FixedPoint.One, pose.scale);
            }
            Object.DestroyImmediate(building);
            Object.DestroyImmediate(wall);
        }

        [Test]
        public void NavGridPart_CentresTheGrid_AndSamplesTheTerrainInFixedPoint()
        {
            var terrain = new TerrainData { heightmapResolution = 33 };
            terrain.size = new Vector3(10, 4, 6);
            var samples = new float[33, 33];
            for (var z = 0; z < 33; z++)
            {
                for (var x = 0; x < 33; x++)
                {
                    samples[z, x] = x / 32f;
                }
            }
            terrain.SetHeights(0, 0, samples);
            var grid = new NavGridPart { Size = new FixedVector2(10, 6), CellSize = 1, AgentRadius = FixedPoint.FromFraction(1, 2), Terrain = terrain, TerrainHeight = 1, MaxSlope = 45 };
            var definition = Definition("map", grid);
            using (var world = new World("Parts"))
            {
                var entityManager = world.EntityManager;
                var entity = ActorUtility.CreatePlaced(entityManager, definition, new FixedPose(new FixedVector3(2, 0, 4), FixedQuaternion.Identity), One, null, 1);

                var built = entityManager.GetComponentData<LockstepNavGrid>(entity);
                Assert.AreEqual(10, built.width);
                Assert.AreEqual(6, built.height);
                Assert.AreEqual(new FixedVector2(-3, 1), built.origin, "centred on the placement");
                Assert.AreEqual(FixedMath.Tan(FixedMath.ToRadians(45)), built.maxSlope);
                Assert.IsTrue(entityManager.HasBuffer<LockstepNavCell>(entity));

                var heights = entityManager.GetBuffer<LockstepNavHeight>(entity);
                Assert.AreEqual(built.CornerCount, heights.Length);
                Assert.AreEqual((FixedPoint)1, heights[built.GetCornerIndex(new int2(0, 3))].value, "the terrain's Y at its low edge");
                Assert.AreEqual((FixedPoint)5, heights[built.GetCornerIndex(new int2(10, 3))].value, "and its full height at the high edge");
                for (var x = 1; x <= built.width; x++)
                {
                    Assert.Greater(heights[built.GetCornerIndex(new int2(x, 0))].value, heights[built.GetCornerIndex(new int2(x - 1, 0))].value);
                }
            }
            Object.DestroyImmediate(definition);
            Object.DestroyImmediate(terrain);
        }

        [Test]
        public void NavAvoidancePart_BuildsTheAvoidanceOfTheMap()
        {
            var definition = Definition("map", new NavAvoidancePart());
            using (var world = new World("Parts"))
            {
                var entity = ActorUtility.CreatePlaced(world.EntityManager, definition, FixedPose.Identity, One, null, 1);
                var avoidance = world.EntityManager.GetComponentData<LockstepNavAvoidance>(entity);
                Assert.AreEqual(FixedPoint.FromFraction(3, 2), avoidance.timeHorizon);
                Assert.AreEqual(8, avoidance.maxNeighbours);
            }
            Object.DestroyImmediate(definition);
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
