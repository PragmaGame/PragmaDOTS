using System;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// New <see cref="ActorDefinition"/> assets start with a part of this <see cref="ActorPart"/> class, such as the
    /// transform of the Lockstep module. The class needs a parameterless constructor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DefaultActorPartAttribute : Attribute
    {
        /// <summary>Where the part goes among the default parts, lowest first; ties go by class name.</summary>
        public int Order { get; set; }

        /// <summary>
        /// A default part this one stands in for: when both are compiled, new definitions get only this one (the transform
        /// of the Lockstep module replaces <see cref="LocalTransformPart"/>).
        /// </summary>
        public Type Replaces { get; set; }
    }
}
