using Pragma.Dots.Actors;

namespace Pragma.Dots.Tests
{
    // Abstract, so that new definitions of the project never get it: only the selection of default parts is tested.
    [DefaultActorPart(Order = 5)]
    public abstract class TestDefaultPartLate : ActorPart
    {
    }
}
