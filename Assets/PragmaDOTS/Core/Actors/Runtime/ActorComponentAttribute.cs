using System;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// Offers a component or buffer element type as an actor part without code: the part menu lists it, and the part
    /// (<see cref="ActorComponentPart{T}"/> or <see cref="ActorBufferPart{T}"/>) shows its fields for editing.
    /// </summary>
    /// <remarks>The type must be <c>[Serializable]</c> with public fields, or Unity keeps none of its values.</remarks>
    [AttributeUsage(AttributeTargets.Struct, Inherited = false)]
    public sealed class ActorComponentAttribute : Attribute
    {
        /// <param name="menu">Path in the part menu, such as "Combat/Health"; the type name when empty.</param>
        public ActorComponentAttribute(string menu = null)
        {
            Menu = menu;
        }

        public string Menu { get; }
    }
}
