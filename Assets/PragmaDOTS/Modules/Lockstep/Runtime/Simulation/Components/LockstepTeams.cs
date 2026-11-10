using Unity.Collections;
using Unity.Entities;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// The team of every player slot: a singleton on the session entity of the simulation world. Slots of one team are
    /// allies: the game keeps their units from attacking each other, and the Vision module shares what they see.
    /// </summary>
    /// <remarks>
    /// It starts from <see cref="LockstepSessionConfig.Teams"/>, which the server takes from its settings and from
    /// <see cref="LockstepServer.SetTeam"/> in the lobby, and is simulation state from tick 0 on: gameplay may change it
    /// (an alliance, a player changing sides) from a command with <see cref="SetTeam"/>, and the change counts from that
    /// tick on every client. A team belongs to the slot, not to the player entity, so the units of a player who left keep
    /// their side. A slot outside the session (-1 for neutral units) has no team: it is the enemy of every slot and the ally
    /// of the same number only, so neutral units do not fight each other. Attacks, targets and damage are the game's: it
    /// asks <see cref="AreEnemies"/> before them.
    /// </remarks>
    public struct LockstepTeams : IComponentData
    {
        /// <summary>The team of a slot that has none: it is allied with no other slot.</summary>
        public const int NONE = 0;

        /// <summary>The largest team number.</summary>
        public const int MAX_TEAM = byte.MaxValue;

        // One entry per slot of the session, so every client hashes the same bytes.
        private FixedList128Bytes<byte> _teams;

        /// <summary>The teams of <paramref name="slotCount"/> slots, from <paramref name="teams"/> (slot <c>s</c> at index <c>s</c>; missing entries have none).</summary>
        public static LockstepTeams Create(int slotCount, in FixedList128Bytes<byte> teams)
        {
            var result = new LockstepTeams();
            var count = slotCount < 0 ? 0 : slotCount > LockstepProtocol.MAX_PLAYERS ? LockstepProtocol.MAX_PLAYERS : slotCount;
            for (var slot = 0; slot < count; slot++)
            {
                result._teams.Add(slot < teams.Length ? teams[slot] : (byte)NONE);
            }
            return result;
        }

        /// <summary>Number of slots: the session's <see cref="LockstepSessionInfo.maxPlayers"/>.</summary>
        public int SlotCount => _teams.Length;

        /// <summary>The team of <paramref name="slot"/>; <see cref="NONE"/> for a slot without one or outside the session.</summary>
        public int GetTeam(int slot) => IsSlot(slot) ? _teams[slot] : NONE;

        /// <summary>
        /// Puts <paramref name="slot"/> into <paramref name="team"/> (1 to <see cref="MAX_TEAM"/>), or takes it out of its
        /// team with <see cref="NONE"/>. A slot outside the session or a team outside 0 to 255 is ignored.
        /// </summary>
        public void SetTeam(int slot, int team)
        {
            if (IsSlot(slot) && team >= NONE && team <= MAX_TEAM)
            {
                _teams[slot] = (byte)team;
            }
        }

        /// <summary>
        /// The two slots are on one side: the same slot (also -1 with -1), or two slots of the session in the same team.
        /// </summary>
        public bool AreAllies(int a, int b)
        {
            if (a == b)
            {
                return true;
            }
            var team = GetTeam(a);
            return team != NONE && team == GetTeam(b);
        }

        /// <summary>The two slots are not <see cref="AreAllies">allies</see>: their units may attack each other.</summary>
        public bool AreEnemies(int a, int b) => !AreAllies(a, b);

        /// <summary>
        /// The slots allied with <paramref name="slot"/>, itself included, as a mask (bit <c>s</c> is slot <c>s</c>); zero for
        /// a slot outside the session.
        /// </summary>
        public ulong GetAllies(int slot)
        {
            if (!IsSlot(slot))
            {
                return 0;
            }
            var team = _teams[slot];
            if (team == NONE)
            {
                return 1ul << slot;
            }
            var mask = 0ul;
            for (var other = 0; other < _teams.Length; other++)
            {
                if (_teams[other] == team)
                {
                    mask |= 1ul << other;
                }
            }
            return mask;
        }

        private bool IsSlot(int slot) => slot >= 0 && slot < _teams.Length;
    }
}
