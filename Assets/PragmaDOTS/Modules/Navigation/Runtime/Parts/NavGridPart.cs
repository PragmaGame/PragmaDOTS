using System;
using System.Collections.Generic;
using Pragma.Dots.Actors;
using Pragma.Dots.Lockstep;
using Pragma.Dots.Mathematics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Pragma.Dots.Navigation
{
    /// <summary>
    /// The <see cref="LockstepNavGrid"/> of a map: a rectangle of square cells on the XZ plane, centred on the placed actor,
    /// that agents walk on. Place it once per map; obstacles (<see cref="NavObstaclePart"/>) block its cells at run time.
    /// </summary>
    /// <remarks>
    /// The ground is a <see cref="TerrainData"/> sampled at every cell corner into <see cref="LockstepNavHeight"/>, in
    /// fixed point, so a grid built at run time is the same on every machine. Place its terrain so that it covers the grid
    /// exactly (its corner at the grid's corner, its size the grid's size) at world Y <see cref="TerrainHeight"/>.
    /// Selecting the placed actor previews the cells the obstacles of the open scenes and the steep ground block.
    /// </remarks>
    [Serializable]
    [ActorPartMenu("Navigation/Grid")]
    public sealed class NavGridPart : ActorPart
    {
        private const int MAX_PREVIEW_CELLS = 1 << 20;

        [SerializeField, Tooltip("World size of the grid along X and Z, centred on the actor.")]
        private FixedVector2 _size = new FixedVector2(100, 100);

        [SerializeField, Tooltip("Side of a cell in world units, at least 0.05. Smaller cells follow obstacles closer and take longer to search.")]
        private FixedPoint _cellSize = FixedPoint.FromFraction(1, 2);

        [SerializeField, Tooltip("Radius of the common agents: paths keep their centres this far from obstacles. Larger bodies (vehicles) keep to cells with as much more room as they are larger.")]
        private FixedPoint _agentRadius = FixedPoint.FromFraction(1, 2);

        [SerializeField, Tooltip("Ground agents walk on: a terrain covering the grid exactly. Empty: flat ground, agents keep their Y.")]
        private TerrainData _terrain;

        [SerializeField, Tooltip("World Y of the terrain object.")]
        private FixedPoint _terrainHeight;

        [SerializeField, Tooltip("The steepest ground agents walk on, in degrees (up to 89): steeper cells are blocked. Zero: every slope is walkable.")]
        private FixedPoint _maxSlope = 35;

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

        public FixedPoint AgentRadius
        {
            get => _agentRadius;
            set => _agentRadius = value;
        }

        public TerrainData Terrain
        {
            get => _terrain;
            set => _terrain = value;
        }

        public FixedPoint TerrainHeight
        {
            get => _terrainHeight;
            set => _terrainHeight = value;
        }

        public FixedPoint MaxSlope
        {
            get => _maxSlope;
            set => _maxSlope = value;
        }

        public override void Build(IActorBuilder builder)
        {
            var grid = CreateGrid(builder.Placement.position);
            builder.AddComponent(grid);
            // Sized by LockstepNavObstacleSystem on the first tick: an empty buffer keeps the subscene small.
            builder.AddBuffer<LockstepNavCell>();
            if (_terrain == null)
            {
                return;
            }
            builder.DependsOn(_terrain);
            WarnIfTerrainMismatches(grid);
            var heights = builder.AddBuffer<LockstepNavHeight>();
            heights.ResizeUninitialized(grid.CornerCount);
            SampleTerrain(grid, heights.AsNativeArray());
        }

        public LockstepNavGrid CreateGrid(FixedVector3 center)
        {
            var cellSize = FixedMath.Max(_cellSize, FixedPoint.FromFraction(1, 20));
            var width = math.max(1, FixedMath.CeilToInt(_size.x / cellSize));
            var height = math.max(1, FixedMath.CeilToInt(_size.y / cellSize));
            var maxSlope = FixedMath.Clamp(_maxSlope, FixedPoint.Zero, (FixedPoint)89);
            return new LockstepNavGrid
            {
                origin = new FixedVector2(center.x - cellSize * width / 2, center.z - cellSize * height / 2),
                cellSize = cellSize,
                width = width,
                height = height,
                agentRadius = _agentRadius,
                maxSlope = _terrain != null && maxSlope > FixedPoint.Zero ? FixedMath.Tan(FixedMath.ToRadians(maxSlope)) : FixedPoint.Zero,
            };
        }

        // World Y at every corner of the grid, the terrain stretched over the grid: the raw heightmap samples interpolated
        // in fixed point, so every machine gets the same values.
        private void SampleTerrain(in LockstepNavGrid grid, NativeArray<LockstepNavHeight> heights)
        {
            var resolution = _terrain.heightmapResolution;
            var samples = _terrain.GetHeights(0, 0, resolution, resolution);
            var top = (FixedPoint)_terrain.size.y;
            var last = resolution - 1;
            for (var z = 0; z <= grid.height; z++)
            {
                var v = FixedPoint.FromFraction((long)z * last, grid.height);
                var row = math.min(FixedMath.FloorToInt(v), last - 1);
                var fz = v - row;
                for (var x = 0; x <= grid.width; x++)
                {
                    var u = FixedPoint.FromFraction((long)x * last, grid.width);
                    var column = math.min(FixedMath.FloorToInt(u), last - 1);
                    var fx = u - column;
                    var near = FixedMath.Lerp((FixedPoint)samples[row, column], (FixedPoint)samples[row, column + 1], fx);
                    var far = FixedMath.Lerp((FixedPoint)samples[row + 1, column], (FixedPoint)samples[row + 1, column + 1], fx);
                    var height = _terrainHeight + FixedMath.Lerp(near, far, fz) * top;
                    heights[grid.GetCornerIndex(new int2(x, z))] = new LockstepNavHeight { value = height };
                }
            }
        }

        // The same heights in floats, without reading the whole heightmap on every repaint: only for the preview.
        private void SampleTerrainPreview(in LockstepNavGrid grid, NativeArray<LockstepNavHeight> heights)
        {
            var baseHeight = (float)_terrainHeight;
            for (var z = 0; z <= grid.height; z++)
            {
                for (var x = 0; x <= grid.width; x++)
                {
                    var height = baseHeight + _terrain.GetInterpolatedHeight((float)x / grid.width, (float)z / grid.height);
                    heights[grid.GetCornerIndex(new int2(x, z))] = new LockstepNavHeight { value = (FixedPoint)height };
                }
            }
        }

        private void WarnIfTerrainMismatches(in LockstepNavGrid grid)
        {
            var width = (float)(grid.cellSize * grid.width);
            var length = (float)(grid.cellSize * grid.height);
            if (Mathf.Abs(_terrain.size.x - width) > 0.01f || Mathf.Abs(_terrain.size.z - length) > 0.01f)
            {
                Debug.LogWarning($"[Lockstep] The terrain '{_terrain.name}' is {_terrain.size.x} x {_terrain.size.z} but the navigation grid is {width} x {length}: " +
                                 "its heights are stretched over the grid.", _terrain);
            }
        }

        public override void DrawGizmos(in ActorGizmoContext context)
        {
            var position = context.Transform.position;
            var grid = CreateGrid((FixedVector3)position);
            var corner = new Vector3((float)grid.origin.x, position.y, (float)grid.origin.y);
            ActorGizmos.DrawRectangle(corner, new Vector2((float)(grid.cellSize * grid.width), (float)(grid.cellSize * grid.height)), new Color(0.3f, 0.8f, 1f, 0.8f));
            if (context.IsSelected && grid.CellCount <= MAX_PREVIEW_CELLS)
            {
                DrawBlockedCells(grid, position.y);
            }
        }

        private void DrawBlockedCells(in LockstepNavGrid grid, float flatY)
        {
            using (var cells = new NativeArray<LockstepNavCell>(grid.CellCount, Allocator.Temp))
            using (var heights = new NativeArray<LockstepNavHeight>(_terrain != null ? grid.CornerCount : 0, Allocator.Temp))
            {
                if (_terrain != null)
                {
                    SampleTerrainPreview(grid, heights);
                }
                var parts = new List<ActorPart>();
                foreach (var actor in UnityEngine.Object.FindObjectsByType<ActorAuthoring>(FindObjectsInactive.Exclude))
                {
                    parts.Clear();
                    actor.GetParts(parts);
                    if (ActorParts.TryGet(parts, out NavObstaclePart obstacle))
                    {
                        LockstepNavigation.Stamp(grid, cells, obstacle.GetFootprint(actor.transform, ActorParts.TryGet<TransformPart>(parts, out _)), 1);
                    }
                }

                var hasHeights = LockstepNavigation.HasHeights(grid, heights);
                var cellSize = (float)grid.cellSize;
                var obstacleColor = new Color(1f, 0.3f, 0.2f, 0.4f);
                var steepColor = new Color(1f, 0.75f, 0.1f, 0.4f);
                for (var i = 0; i < cells.Length; i++)
                {
                    var cell = grid.GetCell(i);
                    var isSteep = LockstepNavigation.IsSteep(grid, heights, cell);
                    if (cells[i].IsWalkable && !isSteep)
                    {
                        continue;
                    }
                    var center = grid.GetCellCenter(cell);
                    var y = (hasHeights ? (float)LockstepNavigation.GetHeight(grid, heights, center) : flatY) + 0.02f;
                    Gizmos.color = cells[i].IsWalkable ? steepColor : obstacleColor;
                    Gizmos.DrawCube(new Vector3((float)center.x, y, (float)center.y), new Vector3(cellSize, 0.01f, cellSize));
                }
            }
        }
    }
}
