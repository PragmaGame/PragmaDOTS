using Pragma.Dots.Views;

namespace Pragma.Dots.Tests
{
    /// <summary>Counts the pushes of the tag <see cref="TestViewHidden"/>.</summary>
    public sealed class TestViewHiddenView : EntityComponentView<TestViewHidden>
    {
        public int pushCount;

        public override void UpdateData(TestViewHidden data)
        {
            pushCount++;
        }
    }
}
