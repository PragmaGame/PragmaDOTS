using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using Unity.Entities;

namespace Pragma.Dots.Navigation
{
    /// <summary>
    /// The world rectangle an obstacle has stamped into the grid. Written by <see cref="LockstepNavObstacleSystem"/>; a
    /// cleanup component, so a destroyed obstacle lingers until its cells are released.
    /// </summary>
    /// <remarks>
    /// <para>Build one for any obstacle with <see cref="Create(in LockstepNavObstacle, in LockstepTransform)"/>: its
    /// geometry answers where a unit reaches a building and where units stand around it, in fixed point only.</para>
    /// <para>The perimeter is walked from the corner at local (-X, -Z) along the sides -Z, +X, +Z and -X; a place on it is
    /// an offset along that walk in world units, from 0 up to <see cref="Perimeter"/>, and offsets wrap around.</para>
    /// </remarks>
    public struct LockstepNavObstacleFootprint : ICleanupComponentData
    {
        public FixedVector2 center;
        /// <summary>Unit direction of the rectangle's local X axis on the XZ plane.</summary>
        public FixedVector2 right;
        public FixedVector2 halfSize;

        /// <summary>Unit direction of the rectangle's local Z axis on the XZ plane.</summary>
        public FixedVector2 Forward => FixedMath.Perpendicular(right);

        /// <summary>The length of the walk around the rectangle: its perimeter.</summary>
        public FixedPoint Perimeter => (halfSize.x + halfSize.y) * 4;

        /// <summary>The point of the rectangle (on its edge or inside) nearest to a world position (X and Z).</summary>
        public FixedVector2 ClosestPoint(FixedVector2 position)
        {
            var offset = position - center;
            var forward = Forward;
            var x = FixedMath.Clamp(FixedMath.Dot(offset, right), -halfSize.x, halfSize.x);
            var z = FixedMath.Clamp(FixedMath.Dot(offset, forward), -halfSize.y, halfSize.y);
            return center + right * x + forward * z;
        }

        /// <summary><see cref="ClosestPoint(FixedVector2)"/> of a position on the ground; its Y is kept.</summary>
        public FixedVector3 ClosestPoint(FixedVector3 position)
        {
            var point = ClosestPoint(position.Xz);
            return new FixedVector3(point.x, position.y, point.y);
        }

        /// <summary>The squared distance on the ground from a world position (X and Z) to the rectangle; zero inside it.</summary>
        public FixedPoint DistanceSquared(FixedVector2 position)
        {
            var offset = position - center;
            var outsideX = FixedMath.Max(FixedMath.Abs(FixedMath.Dot(offset, right)) - halfSize.x, FixedPoint.Zero);
            var outsideZ = FixedMath.Max(FixedMath.Abs(FixedMath.Dot(offset, Forward)) - halfSize.y, FixedPoint.Zero);
            return outsideX * outsideX + outsideZ * outsideZ;
        }

        /// <summary><see cref="DistanceSquared(FixedVector2)"/> of a position on the ground; Y is ignored.</summary>
        public FixedPoint DistanceSquared(FixedVector3 position) => DistanceSquared(position.Xz);

        /// <summary>
        /// The offset on the perimeter of the edge point nearest to a world position (X and Z): on the side the position lies
        /// farthest beyond, or for a position inside, on the side it is nearest to.
        /// </summary>
        public FixedPoint GetPerimeterOffset(FixedVector2 position)
        {
            var offset = position - center;
            var x = FixedMath.Dot(offset, right);
            var z = FixedMath.Dot(offset, Forward);
            var alongX = FixedMath.Clamp(x, -halfSize.x, halfSize.x);
            var alongZ = FixedMath.Clamp(z, -halfSize.y, halfSize.y);
            if (FixedMath.Abs(z) - halfSize.y >= FixedMath.Abs(x) - halfSize.x)
            {
                return z < FixedPoint.Zero ? halfSize.x + alongX : halfSize.x * 3 + halfSize.y * 2 - alongX;
            }
            return x > FixedPoint.Zero ? halfSize.x * 2 + halfSize.y + alongZ : halfSize.x * 4 + halfSize.y * 3 - alongZ;
        }

        /// <summary>
        /// The world point (X and Z) <paramref name="distance"/> outside the edge point at <paramref name="offset"/> on the
        /// perimeter, straight out from its side; the offset wraps around the perimeter.
        /// </summary>
        public FixedVector2 GetPerimeterPoint(FixedPoint offset, FixedPoint distance)
        {
            var perimeter = Perimeter;
            var along = perimeter.rawValue > 0 ? FixedMath.Mod(offset, perimeter) : FixedPoint.Zero;
            FixedVector2 local;
            if (along < halfSize.x * 2)
            {
                local = new FixedVector2(along - halfSize.x, -halfSize.y - distance);
            }
            else if (along < halfSize.x * 2 + halfSize.y * 2)
            {
                local = new FixedVector2(halfSize.x + distance, along - halfSize.x * 2 - halfSize.y);
            }
            else if (along < halfSize.x * 4 + halfSize.y * 2)
            {
                local = new FixedVector2(halfSize.x * 3 + halfSize.y * 2 - along, halfSize.y + distance);
            }
            else
            {
                local = new FixedVector2(-halfSize.x - distance, halfSize.x * 4 + halfSize.y * 3 - along);
            }
            return center + right * local.x + Forward * local.y;
        }

        /// <summary>The distance along the perimeter between two offsets on it, the shorter way round.</summary>
        public FixedPoint GetPerimeterDistance(FixedPoint a, FixedPoint b)
        {
            var perimeter = Perimeter;
            if (perimeter.rawValue <= 0)
            {
                return FixedPoint.Zero;
            }
            var distance = FixedMath.Mod(a - b, perimeter);
            return FixedMath.Min(distance, perimeter - distance);
        }

        /// <summary>The footprint of an obstacle placed by a transform: its yaw, uniform scale and position.</summary>
        public static LockstepNavObstacleFootprint Create(in LockstepNavObstacle obstacle, in LockstepTransform transform)
        {
            // A yaw of a turns +X to (cos a, -sin a) on (X, Z) and +Z to (sin a, cos a), its perpendicular.
            var right = FixedMath.NormalizeSafe(FixedMath.Rotate(transform.rotation, FixedVector3.Right).Xz, FixedVector2.Right);
            var forward = FixedMath.Perpendicular(right);
            var center = obstacle.center * transform.scale;
            return new LockstepNavObstacleFootprint
            {
                center = transform.position.Xz + right * center.x + forward * center.y,
                right = right,
                halfSize = FixedMath.Abs(obstacle.size * transform.scale) / 2,
            };
        }

        /// <summary>The footprint of an obstacle without a transform: its rectangle in world space, axis-aligned.</summary>
        public static LockstepNavObstacleFootprint Create(in LockstepNavObstacle obstacle)
        {
            return new LockstepNavObstacleFootprint
            {
                center = obstacle.center,
                right = FixedVector2.Right,
                halfSize = FixedMath.Abs(obstacle.size) / 2,
            };
        }
    }
}
