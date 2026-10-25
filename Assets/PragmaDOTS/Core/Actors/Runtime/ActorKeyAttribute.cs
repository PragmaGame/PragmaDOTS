using System;
using UnityEngine;

namespace Pragma.Dots.Actors
{
    /// <summary>
    /// Shows a <c>FixedString32Bytes</c> field that names an actor type as a reference to its <see cref="ActorDefinition"/>:
    /// the inspector picks the definition and stores its key. A serialized component (an <see cref="ActorComponentAttribute"/>
    /// part, a config) then names another type by key without a part that converts a reference.
    /// </summary>
    /// <remarks>
    /// The field keeps the key, not the asset: a definition whose key changes leaves the field with the old key, and the
    /// inspector marks a key no definition has. On an array the attribute applies to every element.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ActorKeyAttribute : PropertyAttribute
    {
    }
}
