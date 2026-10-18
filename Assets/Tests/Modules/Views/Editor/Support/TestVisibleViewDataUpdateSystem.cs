using Pragma.Dots.Views;
using Unity.Entities;

namespace Pragma.Dots.Tests
{
    /// <summary>Pushes <see cref="TestViewData"/> only for entities whose <see cref="TestViewHidden"/> is off.</summary>
    public partial class TestVisibleViewDataUpdateSystem : EntityViewUpdateSystem<TestViewData>
    {
        protected override EntityQueryBuilder ConfigureQuery(EntityQueryBuilder builder)
        {
            return builder.WithNone<TestViewHidden>();
        }
    }
}
