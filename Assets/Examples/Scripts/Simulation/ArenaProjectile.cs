using Pragma.Dots.Mathematics;
using Unity.Entities;

namespace Pragma.Dots.Examples
{
    public struct ArenaProjectile : IComponentData
    {
        public int ownerSlot;
        public FixedVector2 velocity;
        public int ticksLeft;
    }
}
