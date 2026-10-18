using Unity.Entities;

namespace Pragma.Dots.Lockstep
{
    /// <summary>Enabled only during the tick the player joined.</summary>
    public struct LockstepPlayerJoined : IComponentData, IEnableableComponent
    {
    }
}
