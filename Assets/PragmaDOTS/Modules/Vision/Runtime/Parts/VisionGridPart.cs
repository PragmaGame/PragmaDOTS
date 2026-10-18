using System;
using Pragma.Dots.Actors;
using Pragma.Dots.Mathematics;
using Unity.Mathematics;
using UnityEngine;

namespace Pragma.Dots.Vision
{
    /// <summary>
    /// The <see cref="LockstepVisionGrid"/> of a map: a rectangle of square cells on the XZ plane, centred on the placed
    /// actor, that holds what every player slot sees. Place it once per map, usually over the navigation grid.
    /// </summary>
    /// <remarks>
    /// Vision is stamped in whole cells, so the cell size is how fine the edge of the fog is: a few world units are plenty.
    /// The planes are sized at run time, one per slot of the session.
    /// </remarks>
    [Serializable]
    [ActorPartMenu("Vision/Grid")]
    public sealed class VisionGridPart : ActorPart
    {
        [SerializeField, Tooltip("World size of the grid along X and Z, centred on the actor. Outside it nothing is visible.")]
        private FixedVector2 _size = new FixedVector2(100, 100);

        [SerializeField, Tooltip("Side of a cell in world units, at least 0.05: how fine the edge of the fog is. Smaller cells cost more per tick.")]
        private FixedPoint _cellSize = 2;

        public FixedVector2 Size
        {
            get => _size;
            set => _size = value;
        }

        public FixedPoint CellSize
        {
            get => _cellSize;
            set => _cellSize = value;
        }

        public override void Build(IActorBuilder builder)
        {
            builder.AddComponent(CreateGrid(builder.Placement.position));
            // Sized by LockstepVisionSystem on the first tick: an empty buffer keeps the subscene small.
            builder.AddBuffer<LockstepVisionCell>();
        }

        public LockstepVisionGrid CreateGrid(FixedVector3 center)
        {
            var cellSize = FixedMath.Max(_cellSize, FixedPoint.FromFraction(1, 20));
            var width = math.max(1, FixedMath.CeilToInt(_size.x / cellSize));
            var height = math.max(1, FixedMath.CeilToInt(_size.y / cellSize));
            return new LockstepVisionGrid
            {
                origin = new FixedVector2(center.x - cellSize * width / 2, center.z - cellSize * height / 2),
                cellSize = cellSize,
                width = width,
                height = height,
            };
        }

        public override void DrawGizmos(in ActorGizmoContext context)
        {
            var position = context.Transform.position;
            var grid = CreateGrid((FixedVector3)position);
            var corner = new Vector3((float)grid.origin.x, position.y, (float)grid.origin.y);
            ActorGizmos.DrawRectangle(corner, new Vector2((float)(grid.cellSize * grid.width), (float)(grid.cellSize * grid.height)), new Color(0.9f, 0.85f, 0.3f, 0.8f));
        }
    }
}
