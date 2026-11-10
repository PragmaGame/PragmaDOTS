using System;
using NUnit.Framework;
using Pragma.Dots.Lockstep;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    public class TeamTests
    {
        private static LockstepServerSettings Settings(int minPlayers)
        {
            var settings = LockstepServerSettings.Default;
            settings.TickRate = 30;
            settings.MaxPlayers = 4;
            settings.InputSize = UnsafeUtility.SizeOf<TestInput>();
            settings.Seed = 4321;
            settings.MinPlayersToStart = minPlayers;
            settings.StartDelaySeconds = 0.3f;
            settings.ChecksumInterval = 10;
            return settings;
        }

        private static void Input(LockstepClient client, int frame)
        {
            if (client.LocalSlot >= 0)
            {
                client.SetInput(new TestInput { moveX = (frame / (5 + client.LocalSlot)) % 3 - 1, moveY = client.LocalSlot + 1 });
            }
        }

        private static LockstepSimulationOptions TeamOptions() => TestUtility.Options(typeof(TestGameplaySystem), typeof(TestTeamSystem));

        private static LockstepTeams GetTeams(LockstepClient client)
        {
            using (var query = client.Simulation.World.EntityManager.CreateEntityQuery(typeof(LockstepTeams)))
            {
                return query.GetSingleton<LockstepTeams>();
            }
        }

        [Test]
        public void Teams_WithoutAnAssignment_AreEveryoneForThemselves()
        {
            var teams = LockstepTeams.Create(4, default);

            Assert.AreEqual(4, teams.SlotCount);
            for (var slot = 0; slot < 4; slot++)
            {
                Assert.AreEqual(LockstepTeams.NONE, teams.GetTeam(slot));
                Assert.AreEqual(1ul << slot, teams.GetAllies(slot), "a slot without a team is allied with itself only");
            }
            Assert.IsTrue(teams.AreAllies(2, 2));
            Assert.IsTrue(teams.AreEnemies(0, 1));
        }

        [Test]
        public void SlotsOfOneTeam_AreAllies_TheOthersEnemies()
        {
            var teams = LockstepTeams.Create(4, TestUtility.Bytes(1, 1, 2));

            Assert.IsTrue(teams.AreAllies(0, 1));
            Assert.IsFalse(teams.AreEnemies(1, 0));
            Assert.IsTrue(teams.AreEnemies(0, 2));
            Assert.IsTrue(teams.AreEnemies(2, 3), "a missing entry is no team");
            Assert.AreEqual(0b0011ul, teams.GetAllies(0));
            Assert.AreEqual(0b0011ul, teams.GetAllies(1));
            Assert.AreEqual(0b0100ul, teams.GetAllies(2));
            Assert.AreEqual(0b1000ul, teams.GetAllies(3));
        }

        [Test]
        public void Neutral_IsTheEnemyOfEverySlot_AndNotOfItself()
        {
            var teams = LockstepTeams.Create(2, TestUtility.Bytes(1, 1));

            Assert.AreEqual(LockstepTeams.NONE, teams.GetTeam(-1));
            Assert.AreEqual(0ul, teams.GetAllies(-1));
            Assert.IsTrue(teams.AreEnemies(-1, 0));
            Assert.IsTrue(teams.AreEnemies(1, -1));
            Assert.IsTrue(teams.AreAllies(-1, -1), "neutral units do not fight each other");
            Assert.IsTrue(teams.AreEnemies(5, 0), "a slot outside the session has no team");
        }

        [Test]
        public void SetTeam_ChangesSides_AndIgnoresWhatIsOutOfRange()
        {
            var teams = LockstepTeams.Create(3, TestUtility.Bytes(1, 1, 2));

            teams.SetTeam(1, 2);
            Assert.IsTrue(teams.AreAllies(1, 2));
            Assert.IsTrue(teams.AreEnemies(0, 1));

            teams.SetTeam(2, LockstepTeams.NONE);
            Assert.IsTrue(teams.AreEnemies(1, 2), "a slot taken out of its team has no allies");

            teams.SetTeam(0, LockstepTeams.MAX_TEAM);
            Assert.AreEqual(LockstepTeams.MAX_TEAM, teams.GetTeam(0));

            teams.SetTeam(-1, 1);
            teams.SetTeam(3, 1);
            teams.SetTeam(0, LockstepTeams.MAX_TEAM + 1);
            teams.SetTeam(1, -1);
            Assert.AreEqual(3, teams.SlotCount);
            Assert.AreEqual(LockstepTeams.MAX_TEAM, teams.GetTeam(0));
            Assert.AreEqual(2, teams.GetTeam(1));
        }

        [Test]
        public void Create_TakesOneEntryPerSlot()
        {
            var all = new FixedList128Bytes<byte>();
            for (var i = 0; i < LockstepProtocol.MAX_PLAYERS + 2; i++)
            {
                all.Add(1);
            }
            var teams = LockstepTeams.Create(LockstepProtocol.MAX_PLAYERS, all);
            Assert.AreEqual(LockstepProtocol.MAX_PLAYERS, teams.SlotCount);
            Assert.AreEqual(ulong.MaxValue, teams.GetAllies(LockstepProtocol.MAX_PLAYERS - 1), "every one of 64 slots fits the mask");

            Assert.AreEqual(2, LockstepTeams.Create(2, all).SlotCount, "entries past the slots are dropped");
        }

        [Test]
        public void Simulation_StartsTheTeamsFromTheConfig()
        {
            var config = TestUtility.Config(maxPlayers: 4);
            config.Teams = TestUtility.Bytes(3, 0, 3);
            using (var simulation = new LockstepSimulation(config, TestUtility.Options()))
            {
                using (var query = simulation.World.EntityManager.CreateEntityQuery(typeof(LockstepTeams), typeof(LockstepSessionInfo)))
                {
                    var teams = query.GetSingleton<LockstepTeams>();
                    Assert.AreEqual(4, teams.SlotCount);
                    Assert.IsTrue(teams.AreAllies(0, 2));
                    Assert.IsTrue(teams.AreEnemies(0, 1));
                    Assert.AreEqual(LockstepTeams.NONE, teams.GetTeam(3));
                }
            }
        }

        [Test]
        public void MoreTeamsThanSlots_AreRefused()
        {
            var config = TestUtility.Config(maxPlayers: 2);
            config.Teams = TestUtility.Bytes(1, 1, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());

            var settings = Settings(1);
            settings.MaxPlayers = 2;
            settings.Teams = config.Teams;
            Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
        }

        [Test]
        public void TeamsSetInTheLobby_ReachEveryClient_AndTheLateJoiner()
        {
            var settings = Settings(0);
            settings.Teams = TestUtility.Bytes(5);
            using (var session = new SessionHarness(settings, latency: 0.05, jitter: 0.04))
            {
                var a = session.AddClient();
                var b = session.AddClient();
                session.Run(0.5, Input);
                Assert.AreEqual(LockstepClientState.Lobby, a.State);
                Assert.AreEqual(5, session.Server.GetTeam(0), "the preset of the settings");

                // Slots 0 and 2 against slot 1; slot 2 is still empty and joins after the start.
                session.Server.SetTeam(0, 1);
                session.Server.SetTeam(1, 2);
                session.Server.SetTeam(2, 1);
                Assert.Throws<ArgumentOutOfRangeException>(() => session.Server.SetTeam(4, 1));
                Assert.Throws<ArgumentOutOfRangeException>(() => session.Server.SetTeam(0, 256));
                session.Server.StartGame(session.Time);
                Assert.Throws<InvalidOperationException>(() => session.Server.SetTeam(3, 1), "the match has started");

                session.Run(3, Input);
                var late = session.AddClient();
                session.Run(3, Input);

                Assert.AreEqual(LockstepClientState.Running, late.State);
                Assert.AreEqual(2, late.LocalSlot);
                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(a, late, 10);
                foreach (var client in new[] { a, b, late })
                {
                    Assert.AreEqual(session.Server.Config.Teams, client.Config.Teams);
                    var teams = GetTeams(client);
                    Assert.IsTrue(teams.AreAllies(0, 2));
                    Assert.IsTrue(teams.AreEnemies(0, 1));
                    Assert.IsTrue(teams.AreEnemies(1, 2));
                    Assert.IsTrue(teams.AreEnemies(3, 0), "a slot nobody put into a team");
                }
            }
        }

        [Test]
        public void TeamChange_FromACommand_CountsOnEveryClient()
        {
            var settings = Settings(1);
            settings.Teams = TestUtility.Bytes(1, 2);
            using (var session = new SessionHarness(settings, latency: 0.06, jitter: 0.05))
            {
                // One after the other: joins sent in one frame may arrive in either order through the jitter.
                var a = session.AddClient(TeamOptions());
                session.Run(0.5, Input);
                var b = session.AddClient(TeamOptions());
                session.Run(2, Input);
                Assert.AreEqual(0, a.LocalSlot);
                Assert.AreEqual(1, b.LocalSlot);
                Assert.IsTrue(GetTeams(a).AreEnemies(a.LocalSlot, b.LocalSlot));

                // The second player changes sides.
                b.AddCommand(new TestTeamCommand { team = 1 });
                session.Run(2, Input);
                var late = session.AddClient(TeamOptions());
                session.Run(3, Input);

                Assert.IsEmpty(session.Desyncs);
                TestUtility.AssertSameChecksums(a, b, 10);
                TestUtility.AssertSameChecksums(a, late, 10);
                foreach (var client in new[] { a, b, late })
                {
                    var teams = GetTeams(client);
                    Assert.AreEqual(1, teams.GetTeam(b.LocalSlot));
                    Assert.IsTrue(teams.AreAllies(a.LocalSlot, b.LocalSlot));
                    Assert.AreEqual(LockstepTeams.NONE, teams.GetTeam(late.LocalSlot), "the config gave the late joiner's slot no team");
                }
                Assert.AreEqual(2, session.Server.GetTeam(1), "the server keeps the start of the match only");
            }
        }

        [Test]
        public void Teams_SurviveTheLeaveOfTheirPlayer()
        {
            var settings = Settings(2);
            settings.Teams = TestUtility.Bytes(1, 1);
            using (var session = new SessionHarness(settings, latency: 0.02))
            {
                var stays = session.AddClient();
                var leaves = session.AddClient();
                session.Run(1, Input);
                var leftSlot = leaves.LocalSlot;
                session.Disconnect(leaves);
                session.Run(1, Input);

                Assert.AreEqual(Entity.Null, TestUtility.PlayerEntity(stays.Simulation, leftSlot));
                Assert.IsTrue(GetTeams(stays).AreAllies(stays.LocalSlot, leftSlot), "the units of a player who left keep their side");
            }
        }
    }
}
