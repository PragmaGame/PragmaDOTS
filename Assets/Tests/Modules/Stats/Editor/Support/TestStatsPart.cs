using System;
using Pragma.Dots.Actors;
using Pragma.Dots.Stats;

namespace Pragma.Dots.Tests
{
    [Serializable, ActorPartMenu("Tests/Stats")]
    public sealed class TestStatsPart : StatsPart<TestStat, TestStatTarget>
    {
    }
}
