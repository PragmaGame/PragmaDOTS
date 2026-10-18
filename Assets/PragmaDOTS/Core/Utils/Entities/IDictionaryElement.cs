using System;

namespace Pragma.Dots.Utils
{
    /// <summary>
    /// An element of a buffer used as a dictionary (<see cref="BufferDictionary"/>): its key, by which the buffer keeps its
    /// elements sorted. The element type is the game's own, with any fields beside the key.
    /// </summary>
    /// <example>
    /// <code>
    /// [InternalBufferCapacity(0)]
    /// public struct Cooldown : IBufferElementData, IDictionaryElement&lt;int&gt;
    /// {
    ///     public int ability;
    ///     public int ticksLeft;
    ///
    ///     public int Key { get => ability; set => ability = value; }
    /// }
    /// </code>
    /// </example>
    public interface IDictionaryElement<TKey> where TKey : unmanaged, IComparable<TKey>
    {
        TKey Key { get; set; }
    }
}
