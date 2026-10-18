using System;
using NUnit.Framework;
using Pragma.Dots.Actors;
using Pragma.Dots.Mathematics;
using Pragma.Dots.StateMachines;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
#if PRAGMA_DOTS_LOCKSTEP
using Pragma.Dots.Lockstep;
using Unity.Collections.LowLevel.Unsafe;
#endif
using Object = UnityEngine.Object;

namespace Pragma.Dots.Tests
{
    public class StateMachineTests
    {
        [Test]
        public void Machine_EntersItsInitialState_OnItsFirstUpdate()
        {
            var machine = StateMachine.Create(TestState.Walk);
            Assert.AreEqual(StateMachine.NONE, machine.current);
            Assert.AreEqual((int)TestState.Walk, machine.next);
            Assert.IsTrue(machine.HasRequest);

            machine.Advance();
            Assert.IsTrue(machine.Is(TestState.Walk));
            Assert.IsTrue(machine.Entered(TestState.Walk));
            Assert.AreEqual(StateMachine.NONE, machine.previous);
            Assert.AreEqual(0, machine.ticksInState);
            Assert.IsFalse(machine.HasRequest);

            machine.Advance();
            Assert.IsFalse(machine.Entered(TestState.Walk), "entered only on the update of the transition");
            Assert.AreEqual(1, machine.ticksInState);
        }

        [Test]
        public void Request_IsAppliedOnTheNextUpdate_AndTheLastOneWins()
        {
            var machine = StateMachine.Create(TestState.Idle);
            machine.Advance();
            machine.Request(TestState.Walk);
            machine.Request(TestState.Rest);
            Assert.IsTrue(machine.Is(TestState.Idle), "nothing changes before the update");

            machine.Advance();
            Assert.IsTrue(machine.Is(TestState.Rest));
            Assert.IsTrue(machine.Exited(TestState.Idle));
            Assert.IsFalse(machine.Exited(TestState.Walk));
            Assert.AreEqual((int)TestState.Idle, machine.previous);
        }

        [Test]
        public void Request_OfTheCurrentState_ChangesNothing()
        {
            var machine = StateMachine.Create(TestState.Idle);
            machine.Advance();
            machine.Advance();
            machine.Request(TestState.Idle);

            machine.Advance();
            Assert.IsTrue(machine.Is(TestState.Idle));
            Assert.AreEqual(2, machine.ticksInState, "the state goes on, it is not entered again");
            Assert.IsFalse(machine.Exited(TestState.Idle));
            Assert.IsFalse(machine.HasRequest);
        }

        [Test]
        public void Request_OfTheCurrentState_CancelsAPendingOne()
        {
            var machine = StateMachine.Create(TestState.Idle);
            machine.Advance();
            machine.Request(TestState.Walk);
            machine.Request(TestState.Idle);

            machine.Advance();
            Assert.IsTrue(machine.Is(TestState.Idle), "the last request wins");
            Assert.IsFalse(machine.Exited(TestState.Idle));
        }

        [Test]
        public void Id_IsTheValueOfAByteEnum_AlsoInBurst()
        {
            Assert.AreEqual(2, StateMachine.Id(TestState.Walk));
            using (var results = new NativeArray<StateMachine>(1, Allocator.TempJob))
            {
                new RequestJob { results = results }.Run();
                Assert.AreEqual(2, results[0].next, "Burst requests the same state");
                Assert.AreEqual(3, results[0].current);
            }
        }

        [Test]
        public void States_RunEnterTickAndExit_InTheOrderOfTheTransitions()
        {
            using (var world = CreateWorld(out var group))
            {
                var entityManager = world.EntityManager;
                var entity = CreateMachine(entityManager, 0); // Idle 1 tick, Walk 2 ticks, Rest 1 tick

                group.Update();
                AssertMachine(entityManager, entity, TestState.Idle, 0);
                Assert.AreEqual(1, Log(entityManager, entity).idleEnters);

                group.Update();
                AssertMachine(entityManager, entity, TestState.Idle, 1);

                group.Update();
                AssertMachine(entityManager, entity, TestState.Walk, 0);
                var log = Log(entityManager, entity);
                Assert.AreEqual(1, log.idleExits, "Idle exits on the update the machine leaves it");
                Assert.AreEqual(1, log.walkEnters);
                Assert.AreEqual(1, log.walkTicks, "a state also ticks on the update it is entered");

                group.Update();
                group.Update();
                AssertMachine(entityManager, entity, TestState.Walk, 2);
                Assert.AreEqual(3, Log(entityManager, entity).walkTicks);

                group.Update();
                AssertMachine(entityManager, entity, TestState.Rest, 0);
                group.Update();
                group.Update();
                AssertMachine(entityManager, entity, TestState.Idle, 0);
                log = Log(entityManager, entity);
                Assert.AreEqual(1, log.walkExits);
                Assert.AreEqual(1, log.restEnters);
                Assert.AreEqual(1, log.restExits);
                Assert.AreEqual(2, log.idleEnters);
            }
        }

        [Test]
        public void States_TickInParallelBurstJobs_LikeTheManagedModel()
        {
            const int COUNT = 10000;
            const int UPDATES = 40;
            using (var world = CreateWorld(out var group))
            {
                var entityManager = world.EntityManager;
                var entities = new Entity[COUNT];
                var machines = new StateMachine[COUNT];
                var logs = new TestMachineLog[COUNT];
                for (var i = 0; i < COUNT; i++)
                {
                    entities[i] = CreateMachine(entityManager, i);
                    machines[i] = StateMachine.Create(TestState.Idle);
                    logs[i] = new TestMachineLog { seed = i };
                }
                using (var query = entityManager.CreateEntityQuery(typeof(TestMachine)))
                {
                    Assert.Greater(query.CalculateChunkCount(), 4, "several chunks, so the jobs spread over threads");
                }

                for (var update = 0; update < UPDATES; update++)
                {
                    group.Update();
                    for (var i = 0; i < COUNT; i++)
                    {
                        machines[i].Advance();
                        TestMachineStates.Idle(ref machines[i], ref logs[i]);
                        TestMachineStates.Walk(ref machines[i], ref logs[i]);
                        TestMachineStates.Rest(ref machines[i], ref logs[i]);
                    }
                }

                var cycles = 0;
                for (var i = 0; i < COUNT; i++)
                {
                    var machine = entityManager.GetComponentData<TestMachine>(entities[i]).machine;
                    var log = Log(entityManager, entities[i]);
                    Assert.AreEqual(machines[i].current, machine.current, $"entity {i}");
                    Assert.AreEqual(machines[i].previous, machine.previous, $"entity {i}");
                    Assert.AreEqual(machines[i].next, machine.next, $"entity {i}");
                    Assert.AreEqual(machines[i].ticksInState, machine.ticksInState, $"entity {i}");
                    var expected = logs[i];
                    expected.managedRuns = log.managedRuns;
                    Assert.AreEqual(expected, log, $"entity {i}");
                    if (BurstCompiler.IsEnabled)
                    {
                        Assert.AreEqual(0, log.managedRuns, "the states ran in Burst");
                    }
                    cycles += log.restExits;
                }
                Assert.Greater(cycles, COUNT, "the machines went round their states several times");
            }
        }

        [Test]
        public void Machines_OfOneEntity_UpdateIndependently()
        {
            using (var world = CreateWorld(out var group))
            {
                var entityManager = world.EntityManager;
                var other = world.CreateSystemManaged<TestOtherMachineGroup>();
                var entity = CreateMachine(entityManager, 0);
                entityManager.AddComponentData(entity, new TestOtherMachine { machine = StateMachine.Create(TestState.Rest) });

                group.Update();
                other.Update();
                AssertMachine(entityManager, entity, TestState.Idle, 0);
                var second = entityManager.GetComponentData<TestOtherMachine>(entity).machine;
                Assert.IsTrue(second.Is(TestState.Rest));

                // Only the group of the first machine runs: the second one keeps its state and its count.
                group.Update();
                group.Update();
                AssertMachine(entityManager, entity, TestState.Walk, 0);
                Assert.AreEqual(second, entityManager.GetComponentData<TestOtherMachine>(entity).machine);
            }
        }

        [Test]
        public void MachineComponent_WithMoreThanAStateMachine_IsRefused()
        {
            Assert.DoesNotThrow(() => StateMachineLayout.Validate(typeof(TestMachine)));
            var exception = Assert.Throws<InvalidOperationException>(() => StateMachineLayout.Validate(typeof(TestBadMachine)));
            StringAssert.Contains(nameof(TestBadMachine), exception.Message);
        }

        [Test]
        public void Part_GivesTheActorItsMachine_InItsInitialState()
        {
            var definition = ScriptableObject.CreateInstance<ActorDefinition>();
            definition.Key = "walker";
            definition.SetParts(new TestMachinePart { Initial = TestState.Walk });
            try
            {
                using (var world = new World("State Machine Part"))
                {
                    var entity = ActorUtility.CreatePlaced(world.EntityManager, definition, FixedPose.Identity, new FixedVector3(1, 1, 1), null, 1);
                    var machine = world.EntityManager.GetComponentData<TestMachine>(entity).machine;
                    Assert.AreEqual(StateMachine.Create(TestState.Walk), machine);
                }
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

#if PRAGMA_DOTS_LOCKSTEP
        [Test]
        public void Session_MachinesMatchOnEveryClient_AlsoOnALateJoiner()
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.MaxPlayers = 2;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 5;
            settings.MinPlayersToStart = 1;
            settings.StartDelaySeconds = 0.3f;
            settings.ChecksumInterval = 5;

            using (var session = new SessionHarness(settings, latency: 0.05, jitter: 0.04))
            {
                var first = session.AddClient(SessionOptions());
                session.Run(4);
                // Replays the match from tick 0, many ticks per frame.
                var late = session.AddClient(SessionOptions());
                session.Run(4);

                Assert.AreEqual(LockstepClientState.Running, late.State);
                Assert.Greater(late.JoinTick, 60);
                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(first, late, 20);
                using (var query = first.Simulation.World.EntityManager.CreateEntityQuery(typeof(TestMachineLog)))
                {
                    var logs = query.ToComponentDataArray<TestMachineLog>(Allocator.Temp);
                    Assert.Greater(logs[0].restExits, 3, "the machines ran in the simulation");
                }
            }
        }

        private static LockstepSimulationOptions SessionOptions()
        {
            var options = TestUtility.Options(typeof(TestMachineGroup), typeof(TestIdleStateSystem), typeof(TestWalkStateSystem),
                typeof(TestRestStateSystem));
            options.Initialize = world =>
            {
                for (var i = 0; i < 2000; i++)
                {
                    CreateMachine(world.EntityManager, i);
                }
            };
            return options;
        }
#endif

        private static World CreateWorld(out TestMachineGroup group)
        {
            var world = new World("State Machines");
            group = world.CreateSystemManaged<TestMachineGroup>();
            group.AddSystemToUpdateList(world.CreateSystem<TestIdleStateSystem>());
            group.AddSystemToUpdateList(world.CreateSystem<TestWalkStateSystem>());
            group.AddSystemToUpdateList(world.CreateSystem<TestRestStateSystem>());
            return world;
        }

        private static Entity CreateMachine(EntityManager entityManager, int seed)
        {
            var entity = entityManager.CreateEntity(typeof(TestMachine), typeof(TestMachineLog));
            entityManager.SetComponentData(entity, new TestMachine { machine = StateMachine.Create(TestState.Idle) });
            entityManager.SetComponentData(entity, new TestMachineLog { seed = seed });
            return entity;
        }

        private static TestMachineLog Log(EntityManager entityManager, Entity entity)
        {
            return entityManager.GetComponentData<TestMachineLog>(entity);
        }

        private static void AssertMachine(EntityManager entityManager, Entity entity, TestState state, int ticksInState)
        {
            var machine = entityManager.GetComponentData<TestMachine>(entity).machine;
            Assert.AreEqual((int)state, machine.current, machine.ToString());
            Assert.AreEqual(ticksInState, machine.ticksInState, machine.ToString());
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct RequestJob : IJob
        {
            public NativeArray<StateMachine> results;

            public void Execute()
            {
                var machine = StateMachine.Create(TestState.Rest);
                machine.Advance();
                machine.Request(TestState.Walk);
                results[0] = machine;
            }
        }
    }
}
