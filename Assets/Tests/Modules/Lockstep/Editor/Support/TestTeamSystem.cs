using Pragma.Dots.Lockstep;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    /// <summary>Changes teams during the match, as a game's diplomacy would: a <see cref="TestTeamCommand"/> moves the sender's slot.</summary>
    [UpdateInGroup(typeof(LockstepSimulationSystemGroup))]
    public partial struct TestTeamSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<LockstepTeams>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var teams = SystemAPI.GetSingleton<LockstepTeams>();
            foreach (var (player, commands) in SystemAPI.Query<RefRO<LockstepPlayer>, DynamicBuffer<LockstepCommand>>())
            {
                for (var i = 0; i < commands.Length; i++)
                {
                    if (commands[i].TryGet<TestTeamCommand>(out var command))
                    {
                        teams.SetTeam(player.ValueRO.slot, command.team);
                    }
                }
            }
            SystemAPI.SetSingleton(teams);
        }
    }
}
