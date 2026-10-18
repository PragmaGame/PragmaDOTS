using System.Collections.Generic;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Views;

namespace Pragma.Dots.Tests
{
    /// <summary>Records every tick of <see cref="LockstepTime"/> pushed to it.</summary>
    public sealed class TestTimeView : EntityComponentView<LockstepTime>
    {
        public readonly List<int> ticks = new List<int>();

        public override void UpdateData(LockstepTime data)
        {
            ticks.Add(data.tick);
        }
    }
}
