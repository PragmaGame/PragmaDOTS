using Pragma.Dots.Mathematics;

namespace Pragma.Dots.Navigation
{
    /// <summary>
    /// A place taken around a footprint (<see cref="LockstepNavGridView.TryFindPerimeterPlace"/>): its offset on the
    /// perimeter (<see cref="LockstepNavObstacleFootprint"/>) and the radius of the body that stands on it. Which unit holds
    /// which place, and for how long, is gameplay's to keep.
    /// </summary>
    public struct LockstepNavPerimeterPlace
    {
        public FixedPoint offset;
        public FixedPoint radius;

        public LockstepNavPerimeterPlace(FixedPoint offset, FixedPoint radius)
        {
            this.offset = offset;
            this.radius = radius;
        }
    }
}
