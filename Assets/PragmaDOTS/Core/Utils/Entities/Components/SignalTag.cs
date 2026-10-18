using Unity.Entities;

namespace Pragma.Dots.Utils
{
    /// <summary>
    /// Marks a signal entity, so that the cleanup destroys the signals of every type with one query. <see cref="Signals"/>
    /// adds it; there is no need to add it by hand.
    /// </summary>
    public struct SignalTag : IComponentData
    {
    }
}
