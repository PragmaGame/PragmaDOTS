using Pragma.Dots.Mathematics;
using Unity.Entities;

namespace Pragma.Dots.Examples
{
    public struct ArenaAvatar : IComponentData
    {
        public int slot;
        public FixedVector2 velocity;
        public FixedVector2 facing;
        public int score;
        public int colorIndex;
        public int fireCooldown;
        public int dashCooldown;
    }
}
