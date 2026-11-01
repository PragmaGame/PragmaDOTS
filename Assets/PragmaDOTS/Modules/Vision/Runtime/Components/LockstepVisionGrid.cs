using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

namespace Pragma.Dots.Vision
{
    /// <summary>
    /// What every player slot sees, as a grid of cells on the XZ plane: a singleton in the simulation world. Its cells are
    /// the <see cref="LockstepVisionCell"/> buffer of the same entity, one plane of <see cref="CellCount"/> cells per slot.
    /// </summary>
    /// <remarks>
    /// <see cref="LockstepVisionSystem"/> fills the planes again on every tick from the <see cref="LockstepVisionSource"/>s:
    /// vision is state of the simulation, the same on every client, so gameplay may depend on it (a player orders attacks
    /// only on what it sees) and the presentation draws the fog of war from it. The cells are laid out as a
    /// <see cref="FixedGrid"/> (<see cref="Layout"/>). Give the map's actor a <see cref="VisionGridPart"/> (placed in the
    /// subscene of the map), or create the entity in code before tick 0. Without a grid nothing is hidden: there is no fog.
    /// </remarks>
    public struct LockstepVisionGrid : IComponentData
    {
        /// <summary>World X and Z of the outer corner of cell (0, 0).</summary>
        public FixedVector2 origin;
        public FixedPoint cellSize;
        /// <summary>Number of cells along world X.</summary>
        public int width;
        /// <summary>Number of cells along world Z.</summary>
        public int height;
        /// <summary>Number of planes: the player slots of the session (<see cref="LockstepSessionInfo.maxPlayers"/>), set by <see cref="LockstepVisionSystem"/>.</summary>
        public int slotCount;
        /// <summary>
        /// The slots that see the whole map, inside the grid and outside — the fog is off for them: bit <c>s</c> is slot
        /// <c>s</c>, every slot of <see cref="LockstepProtocol.MAX_PLAYERS"/> fits. Gameplay sets it: a match option or the
        /// end of the match reveals the map to every slot (<see cref="ALL_SLOTS"/>), a cheat or a spectator to one
        /// (<see cref="SetRevealed"/>). The planes are still filled.
        /// </summary>
        public ulong revealedSlots;

        /// <summary><see cref="revealedSlots"/> of a map revealed to every slot.</summary>
        public const ulong ALL_SLOTS = ulong.MaxValue;

        /// <summary>The cells of one plane: the cell math lives there.</summary>
        public FixedGrid Layout => new FixedGrid(origin, cellSize, width, height);

        /// <summary><paramref name="slot"/> sees the whole map; false for a slot outside 0..63.</summary>
        public bool IsRevealed(int slot) => IsSlotBit(slot) && (revealedSlots & (1ul << slot)) != 0;

        /// <summary>Reveals the whole map to <paramref name="slot"/> or gives it the fog back; a slot outside 0..63 is ignored.</summary>
        public void SetRevealed(int slot, bool isRevealed)
        {
            if (!IsSlotBit(slot))
            {
                return;
            }
            revealedSlots = isRevealed ? revealedSlots | (1ul << slot) : revealedSlots & ~(1ul << slot);
        }

        /// <summary>Number of cells of one plane.</summary>
        public int CellCount => Layout.CellCount;

        public bool IsValid => Layout.IsValid;

        public bool Contains(int2 cell) => Layout.Contains(cell);

        /// <summary>The cell that holds a world position (X and Z); it may lie outside the grid.</summary>
        public int2 WorldToCell(FixedVector2 position) => Layout.WorldToCell(position);

        private static bool IsSlotBit(int slot) => slot >= 0 && slot < LockstepProtocol.MAX_PLAYERS;
    }
}
