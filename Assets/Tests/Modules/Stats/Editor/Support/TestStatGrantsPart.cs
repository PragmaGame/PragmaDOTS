using System;
using Pragma.Dots.Actors;
using Pragma.Dots.Stats;

namespace Pragma.Dots.Tests
{
    [Serializable, ActorPartMenu("Tests/Stat Grants")]
    public sealed class TestStatGrantsPart : StatGrantsPart<TestStat, TestStatTarget>
    {
    }
}
