using Pragma.Dots.Lockstep;
using Pragma.Dots.Views;

namespace Pragma.Dots.Tests
{
    /// <summary>Pushes <see cref="LockstepTime"/>, which the session writes outside systems before every tick.</summary>
    public partial class TestTimeViewUpdateSystem : EntityViewUpdateSystem<LockstepTime>
    {
    }
}
