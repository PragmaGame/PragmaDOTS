using System;

namespace Pragma.Lockstep
{
    /// <summary>The path of an <see cref="ActorPart"/> class in the part menu, such as "Navigation/Agent".</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ActorPartMenuAttribute : Attribute
    {
        public ActorPartMenuAttribute(string menu)
        {
            Menu = menu;
        }

        public string Menu { get; }
    }
}
