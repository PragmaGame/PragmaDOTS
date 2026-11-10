using NUnit.Framework;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using Pragma.Dots.Navigation;
using Pragma.Dots.Vision;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Pragma.Dots.Tests
{
    public class VisionTests
    {
        // 20 x 20 cells of one unit: X and Z from -10 to 10, two slots.
        private static LockstepVisionGrid CreateGrid(int slotCount = 2)
        {
            return new LockstepVisionGrid
            {
                origin = new FixedVector2(-10, -10),
                cellSize = FixedPoint.One,
                width = 20,
                height = 20,
                slotCount = slotCount,
            };
        }

        private static NativeArray<LockstepVisionCell> CreateCells(in LockstepVisionGrid grid)
        {
            return new NativeArray<LockstepVisionCell>(grid.CellCount * grid.slotCount, Allocator.Temp);
        }

        private static Entity CreateGridEntity(EntityManager entityManager)
        {
            var grid = CreateGrid(0);
            var entity = entityManager.CreateEntity(typeof(LockstepVisionGrid));
            entityManager.SetComponentData(entity, grid);
            return entity;
        }

        private static Entity CreateSource(EntityManager entityManager, FixedVector3 position, FixedPoint radius, int slot)
        {
            var source = entityManager.CreateEntity(typeof(LockstepTransform), typeof(LockstepVisionSource));
            entityManager.SetComponentData(source, LockstepTransform.FromPosition(position));
            entityManager.SetComponentData(source, new LockstepVisionSource { radius = radius, slot = slot });
            return source;
        }

        private static bool IsVisible(EntityManager entityManager, Entity gridEntity, int slot, FixedVector2 position)
        {
            var grid = entityManager.GetComponentData<LockstepVisionGrid>(gridEntity);
            var cells = entityManager.GetBuffer<LockstepVisionCell>(gridEntity).AsNativeArray();
            return LockstepVision.IsVisible(grid, cells, slot, position);
        }

        [Test]
        public void Stamp_MarksEveryCellTheCircleReachesInto()
        {
            var grid = CreateGrid();
            using (var cells = CreateCells(grid))
            {
                LockstepVision.Stamp(grid, cells, 0, FixedVector2.Zero, 2);

                // The circle touches the cells beside it at their nearest edge, not only those whose centre it covers.
                Assert.IsTrue(LockstepVision.IsCellVisible(grid, cells, 0, grid.WorldToCell(new FixedVector2(FixedPoint.Half, FixedPoint.Half))));
                Assert.IsTrue(LockstepVision.IsCellVisible(grid, cells, 0, grid.WorldToCell(new FixedVector2(FixedPoint.FromFraction(5, 2), FixedPoint.Half))));
                Assert.IsTrue(LockstepVision.IsCellVisible(grid, cells, 0, grid.WorldToCell(new FixedVector2(FixedPoint.FromFraction(-5, 2), FixedPoint.FromFraction(-1, 2)))));
                Assert.IsFalse(LockstepVision.IsCellVisible(grid, cells, 0, grid.WorldToCell(new FixedVector2(FixedPoint.FromFraction(5, 2), FixedPoint.FromFraction(5, 2)))),
                               "the corner of that cell is 2.83 away");
                Assert.IsFalse(LockstepVision.IsCellVisible(grid, cells, 0, grid.WorldToCell(new FixedVector2(FixedPoint.FromFraction(7, 2), 0))));

                // Whatever lies within the radius lies in a visible cell.
                for (var angle = 0; angle < 360; angle += 15)
                {
                    var radians = FixedMath.ToRadians(angle);
                    var point = new FixedVector2(FixedMath.Cos(radians), FixedMath.Sin(radians)) * FixedPoint.FromFraction(199, 100);
                    Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 0, point), $"a point {angle} degrees round, inside the radius");
                }

                for (var i = grid.CellCount; i < cells.Length; i++)
                {
                    Assert.IsFalse(cells[i].isVisible, "the other slot's plane stays dark");
                }
            }
        }

        [Test]
        public void Bodies_AreSeenByTheirEdge()
        {
            var grid = CreateGrid();
            using (var cells = CreateCells(grid))
            {
                LockstepVision.Stamp(grid, cells, 1, new FixedVector2(-5, 0), 3);
                var body = new FixedVector2(FixedPoint.FromFraction(-1, 2), 0);

                Assert.IsFalse(LockstepVision.IsVisible(grid, cells, 1, body), "its centre is out of sight");
                Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 1, body, FixedPoint.FromFraction(3, 2)), "its edge is in sight");
                Assert.IsFalse(LockstepVision.IsVisible(grid, cells, 0, body, FixedPoint.FromFraction(3, 2)), "only the slot of the source sees it");
                Assert.IsFalse(LockstepVision.IsVisible(grid, cells, 1, new FixedVector2(-30, 0)), "nothing outside the grid is visible");
            }
        }

        [Test]
        public void RevealedGrid_ShowsEverythingToEverySlot()
        {
            var grid = CreateGrid();
            grid.revealedSlots = LockstepVisionGrid.ALL_SLOTS;
            using (var cells = CreateCells(grid))
            {
                Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 0, new FixedVector2(4, 4)));
                Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 1, new FixedVector2(-40, 7)), "outside the grid too");
                Assert.IsTrue(LockstepVision.IsCellVisible(grid, cells, 1, new int2(3, 3)));
            }
        }

        [Test]
        public void RevealedSlot_SeesEverything_OthersKeepTheFog()
        {
            var grid = CreateGrid();
            grid.SetRevealed(1, true);
            using (var cells = CreateCells(grid))
            {
                Assert.IsTrue(grid.IsRevealed(1));
                Assert.IsFalse(grid.IsRevealed(0));
                Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 1, new FixedVector2(4, 4)));
                Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 1, new FixedVector2(-40, 7)), "outside the grid too");
                Assert.IsTrue(LockstepVision.IsCellVisible(grid, cells, 1, new int2(3, 3)));
                Assert.IsFalse(LockstepVision.IsVisible(grid, cells, 0, new FixedVector2(4, 4)), "the other slot sees nothing it has no source for");
                Assert.IsFalse(LockstepVision.IsCellVisible(grid, cells, 0, new int2(3, 3)));

                grid.SetRevealed(1, false);
                Assert.IsFalse(LockstepVision.IsVisible(grid, cells, 1, new FixedVector2(4, 4)), "the fog is back");
                Assert.AreEqual(0ul, grid.revealedSlots);
            }
        }

        [Test]
        public void SetRevealed_IgnoresSlotsOutsideTheMask()
        {
            var grid = CreateGrid();
            grid.SetRevealed(-1, true);
            grid.SetRevealed(LockstepProtocol.MAX_PLAYERS, true);
            grid.SetRevealed(LockstepProtocol.MAX_PLAYERS - 1, true);

            Assert.AreEqual(1ul << (LockstepProtocol.MAX_PLAYERS - 1), grid.revealedSlots);
            Assert.IsFalse(grid.IsRevealed(-1));
            Assert.IsFalse(grid.IsRevealed(LockstepProtocol.MAX_PLAYERS));
            Assert.IsTrue(grid.IsRevealed(LockstepProtocol.MAX_PLAYERS - 1));
        }

        [Test]
        public void Stamp_IgnoresSlotsWithoutAPlaneAndNegativeRadii()
        {
            var grid = CreateGrid();
            using (var cells = CreateCells(grid))
            {
                LockstepVision.Stamp(grid, cells, -1, FixedVector2.Zero, 5);
                LockstepVision.Stamp(grid, cells, 2, FixedVector2.Zero, 5);
                LockstepVision.Stamp(grid, cells, 0, FixedVector2.Zero, -1);

                for (var i = 0; i < cells.Length; i++)
                {
                    Assert.IsFalse(cells[i].isVisible);
                }
                Assert.IsFalse(LockstepVision.IsVisible(grid, cells, 2, FixedVector2.Zero));
                Assert.IsFalse(LockstepVision.GetPlane(grid, cells, 2).IsCreated);
                Assert.AreEqual(grid.CellCount, LockstepVision.GetPlane(grid, cells, 1).Length);
            }
        }

        [Test]
        public void ShareWithAllies_GivesEverySlotWhatItsTeamSees()
        {
            var grid = CreateGrid(4);
            var teams = LockstepTeams.Create(4, TestUtility.Bytes(1, 1, 0, 1));
            using (var cells = CreateCells(grid))
            {
                LockstepVision.Stamp(grid, cells, 0, new FixedVector2(-5, 0), 2);
                LockstepVision.Stamp(grid, cells, 1, new FixedVector2(5, 0), 2);
                LockstepVision.Stamp(grid, cells, 2, new FixedVector2(0, 5), 2);
                LockstepVision.ShareWithAllies(grid, cells, teams);

                foreach (var slot in new[] { 0, 1, 3 })
                {
                    Assert.IsTrue(LockstepVision.IsVisible(grid, cells, slot, new FixedVector2(-5, 0)), $"slot {slot} sees through slot 0");
                    Assert.IsTrue(LockstepVision.IsVisible(grid, cells, slot, new FixedVector2(5, 0)), $"slot {slot} sees through slot 1");
                    Assert.IsFalse(LockstepVision.IsVisible(grid, cells, slot, new FixedVector2(0, 5)), $"slot {slot} does not see through an enemy");
                }
                Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 2, new FixedVector2(0, 5)));
                Assert.IsFalse(LockstepVision.IsVisible(grid, cells, 2, new FixedVector2(-5, 0)), "a slot without a team sees what it sees");
                Assert.AreEqual(LockstepVision.GetPlane(grid, cells, 0).ToArray(), LockstepVision.GetPlane(grid, cells, 3).ToArray(),
                                "the fog of every ally is the same");
            }
        }

        [Test]
        public void SlotsThatSeeOnlyTheirOwn_KeepTheirPlane_TheirAlliesStillSeeThroughThem()
        {
            var grid = CreateGrid(3);
            grid.SetSeesAllies(1, false);
            var teams = LockstepTeams.Create(3, TestUtility.Bytes(1, 1, 1));
            using (var cells = CreateCells(grid))
            {
                LockstepVision.Stamp(grid, cells, 0, new FixedVector2(-5, 0), 2);
                LockstepVision.Stamp(grid, cells, 1, new FixedVector2(5, 0), 2);
                LockstepVision.ShareWithAllies(grid, cells, teams);

                Assert.IsFalse(grid.SeesAllies(1));
                Assert.IsTrue(grid.SeesAllies(0));
                Assert.IsFalse(LockstepVision.IsVisible(grid, cells, 1, new FixedVector2(-5, 0)), "slot 1 sees only through its own sources");
                Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 1, new FixedVector2(5, 0)));
                Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 0, new FixedVector2(5, 0)), "its allies still see through it");
                Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 2, new FixedVector2(5, 0)));
                Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 2, new FixedVector2(-5, 0)), "a slot without sources sees what its team sees");
            }

            grid.ownVisionOnlySlots = LockstepVisionGrid.ALL_SLOTS;
            using (var cells = CreateCells(grid))
            {
                LockstepVision.Stamp(grid, cells, 0, new FixedVector2(-5, 0), 2);
                LockstepVision.ShareWithAllies(grid, cells, teams);
                Assert.IsFalse(LockstepVision.IsVisible(grid, cells, 2, new FixedVector2(-5, 0)), "a match without shared vision");
            }

            grid.SetSeesAllies(-1, true);
            grid.SetSeesAllies(LockstepProtocol.MAX_PLAYERS, true);
            Assert.AreEqual(LockstepVisionGrid.ALL_SLOTS, grid.ownVisionOnlySlots, "slots outside the mask are ignored");
            Assert.IsFalse(grid.SeesAllies(-1));
        }

        [Test]
        public void System_SharesWhatAlliesSee_AndFollowsTeamChanges()
        {
            var config = TestUtility.Config(maxPlayers: 3);
            config.Teams = TestUtility.Bytes(1, 1, 2);
            using (var simulation = new LockstepSimulation(config, TestUtility.Options(typeof(LockstepVisionSystem))))
            {
                var entityManager = simulation.World.EntityManager;
                var gridEntity = CreateGridEntity(entityManager);
                CreateSource(entityManager, new FixedVector3(-5, 0, 0), 2, 0);
                CreateSource(entityManager, new FixedVector3(5, 0, 0), 2, 1);
                CreateSource(entityManager, new FixedVector3(0, 0, 5), 2, 2);

                TestUtility.Step(simulation);
                Assert.IsTrue(IsVisible(entityManager, gridEntity, 0, new FixedVector2(5, 0)), "slot 0 sees through its ally");
                Assert.IsTrue(IsVisible(entityManager, gridEntity, 1, new FixedVector2(-5, 0)));
                Assert.IsFalse(IsVisible(entityManager, gridEntity, 0, new FixedVector2(0, 5)), "not through its enemy");
                Assert.IsFalse(IsVisible(entityManager, gridEntity, 2, new FixedVector2(5, 0)));

                // Slot 1 changes sides.
                using (var query = entityManager.CreateEntityQuery(typeof(LockstepTeams)))
                {
                    var teams = query.GetSingleton<LockstepTeams>();
                    teams.SetTeam(1, 2);
                    query.SetSingleton(teams);
                }
                TestUtility.Step(simulation);
                Assert.IsFalse(IsVisible(entityManager, gridEntity, 0, new FixedVector2(5, 0)), "a former ally");
                Assert.IsTrue(IsVisible(entityManager, gridEntity, 2, new FixedVector2(5, 0)), "a new ally");
                Assert.IsTrue(IsVisible(entityManager, gridEntity, 1, new FixedVector2(0, 5)));
            }
        }

        [Test]
        public void System_FillsOnePlanePerSlotOnEveryTick()
        {
            using (var simulation = new LockstepSimulation(TestUtility.Config(maxPlayers: 3), TestUtility.Options(typeof(LockstepVisionSystem))))
            {
                var entityManager = simulation.World.EntityManager;
                var gridEntity = CreateGridEntity(entityManager);
                var first = CreateSource(entityManager, new FixedVector3(-5, 0, 0), 2, 0);
                CreateSource(entityManager, new FixedVector3(5, 0, 0), 2, 2);
                CreateSource(entityManager, new FixedVector3(0, 0, 5), 2, -1);

                TestUtility.Step(simulation);
                var grid = entityManager.GetComponentData<LockstepVisionGrid>(gridEntity);
                Assert.AreEqual(3, grid.slotCount, "one plane per slot of the session");
                Assert.AreEqual(grid.CellCount * 3, entityManager.GetBuffer<LockstepVisionCell>(gridEntity).Length);
                Assert.IsTrue(IsVisible(entityManager, gridEntity, 0, new FixedVector2(-5, 1)));
                Assert.IsFalse(IsVisible(entityManager, gridEntity, 0, new FixedVector2(5, 1)), "slot 0 does not see what slot 2 sees");
                Assert.IsTrue(IsVisible(entityManager, gridEntity, 2, new FixedVector2(5, 1)));
                Assert.IsFalse(IsVisible(entityManager, gridEntity, 1, new FixedVector2(0, 5)), "a neutral source sees for nobody");

                // A source that moved sees from its new place only; a destroyed one sees nothing.
                entityManager.SetComponentData(first, LockstepTransform.FromPosition(new FixedVector3(-5, 0, -6)));
                TestUtility.Step(simulation);
                Assert.IsFalse(IsVisible(entityManager, gridEntity, 0, new FixedVector2(-5, 1)));
                Assert.IsTrue(IsVisible(entityManager, gridEntity, 0, new FixedVector2(-5, -6)));

                entityManager.DestroyEntity(first);
                TestUtility.Step(simulation);
                Assert.IsFalse(IsVisible(entityManager, gridEntity, 0, new FixedVector2(-5, -6)));
            }
        }

        [Test]
        public void Session_EveryClientSeesTheSameFog()
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.MaxPlayers = 2;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 78;
            settings.MinPlayersToStart = 2;
            settings.StartDelaySeconds = 0.3f;
            settings.ChecksumInterval = 10;

            using (var session = new SessionHarness(settings, latency: 0.05, jitter: 0.04))
            {
                var first = session.AddClient(SessionOptions());
                var second = session.AddClient(SessionOptions());
                session.Run(8);

                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(first, second, 15);
                var planes = new LockstepVisionCell[2][];
                var clients = new[] { first, second };
                for (var c = 0; c < clients.Length; c++)
                {
                    Assert.Greater(clients[c].SimulatedTicks, TestNavigationSystem.SEND_TICK + 120);
                    var entityManager = clients[c].Simulation.World.EntityManager;
                    using (var grids = entityManager.CreateEntityQuery(typeof(LockstepVisionGrid), typeof(LockstepVisionCell)))
                    using (var query = entityManager.CreateEntityQuery(typeof(LockstepVisionSource), typeof(LockstepTransform), typeof(TestNavigationTarget)))
                    {
                        var gridEntity = grids.GetSingletonEntity();
                        planes[c] = entityManager.GetBuffer<LockstepVisionCell>(gridEntity).AsNativeArray().ToArray();
                        var transforms = query.ToComponentDataArray<LockstepTransform>(Allocator.Temp);
                        var sources = query.ToComponentDataArray<LockstepVisionSource>(Allocator.Temp);
                        Assert.AreEqual(6, sources.Length);
                        for (var i = 0; i < transforms.Length; i++)
                        {
                            Assert.IsTrue(IsVisible(entityManager, gridEntity, sources[i].slot, transforms[i].position.Xz), "a source sees where it stands");
                        }
                    }
                }
                Assert.AreEqual(planes[0], planes[1], "both clients see the same fog");
            }
        }

        [Test]
        public void Session_AlliesSeeTheSameFog()
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.MaxPlayers = 2;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 79;
            settings.MinPlayersToStart = 2;
            settings.StartDelaySeconds = 0.3f;
            settings.ChecksumInterval = 10;
            settings.Teams = TestUtility.Bytes(1, 1);

            using (var session = new SessionHarness(settings, latency: 0.05, jitter: 0.04))
            {
                var first = session.AddClient(SessionOptions());
                var second = session.AddClient(SessionOptions());
                session.Run(6);

                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(first, second, 15);
                foreach (var client in new[] { first, second })
                {
                    var entityManager = client.Simulation.World.EntityManager;
                    using (var grids = entityManager.CreateEntityQuery(typeof(LockstepVisionGrid), typeof(LockstepVisionCell)))
                    using (var query = entityManager.CreateEntityQuery(typeof(LockstepVisionSource), typeof(LockstepTransform)))
                    {
                        var gridEntity = grids.GetSingletonEntity();
                        var grid = entityManager.GetComponentData<LockstepVisionGrid>(gridEntity);
                        var cells = entityManager.GetBuffer<LockstepVisionCell>(gridEntity).AsNativeArray();
                        Assert.AreEqual(LockstepVision.GetPlane(grid, cells, 0).ToArray(), LockstepVision.GetPlane(grid, cells, 1).ToArray(),
                                        "both allies see the same fog");
                        var transforms = query.ToComponentDataArray<LockstepTransform>(Allocator.Temp);
                        for (var i = 0; i < transforms.Length; i++)
                        {
                            Assert.IsTrue(LockstepVision.IsVisible(grid, cells, 0, transforms[i].position.Xz), "slot 0 sees every source of its team");
                        }
                    }
                }
            }
        }

        // The agents of the navigation session, half of them seeing for slot 0 and half for slot 1.
        private static LockstepSimulationOptions SessionOptions()
        {
            var options = TestUtility.Options(typeof(LockstepNavSystemGroup), typeof(LockstepNavObstacleSystem), typeof(LockstepNavPathSystem),
                typeof(LockstepNavMoveSystem), typeof(LockstepNavSeparationSystem), typeof(TestNavigationSystem), typeof(LockstepVisionSystem));
            options.Initialize = world =>
            {
                var entityManager = world.EntityManager;
                var navigation = entityManager.CreateEntity(typeof(LockstepNavGrid));
                entityManager.SetComponentData(navigation, new LockstepNavGrid
                {
                    origin = new FixedVector2(-10, -10),
                    cellSize = FixedPoint.Half,
                    width = 40,
                    height = 40,
                    agentRadius = FixedPoint.FromFraction(1, 4),
                });
                CreateGridEntity(entityManager);
                for (var i = 0; i < 6; i++)
                {
                    var z = (FixedPoint)(i * 3 - 7);
                    var agent = CreateSource(entityManager, new FixedVector3(-8, 0, z), 3 + i % 3, i % 2);
                    entityManager.AddComponentData(agent, new LockstepNavAgent { speed = 3 + i, angularSpeed = FixedMath.Pi, radius = FixedPoint.FromFraction(2, 5) });
                    entityManager.AddBuffer<LockstepNavWaypoint>(agent);
                    entityManager.AddComponentData(agent, new TestNavigationTarget { there = new FixedVector3(8, 0, -z), back = new FixedVector3(-8, 0, z) });
                }
            };
            return options;
        }
    }
}
