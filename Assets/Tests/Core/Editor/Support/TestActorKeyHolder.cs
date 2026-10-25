using Pragma.Dots.Actors;
using Unity.Collections;
using UnityEngine;

namespace Pragma.Dots.Tests
{
    /// <summary>Serialized fields with <see cref="ActorKeyAttribute"/>, for the tests of the key field drawer.</summary>
    public sealed class TestActorKeyHolder : ScriptableObject
    {
        [ActorKey] public FixedString32Bytes unit;
        [ActorKey] public FixedString32Bytes[] squad = new FixedString32Bytes[1];
        [ActorKey] public int count;
    }
}
