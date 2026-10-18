using System;
using Pragma.Dots.Mathematics;
using Unity.Collections;
using Unity.Mathematics;

namespace Pragma.Dots.Lockstep
{
    /// <summary>
    /// Points on the XZ plane sorted into square cells, for "what lies near here" queries: build it from the positions of
    /// a tick (the units, the targets, the sources of an effect) and ask it many times. A query visits only the points in
    /// the cells a square around it touches, not every point.
    /// </summary>
    /// <remarks>
    /// <para>The cells cover the bounding box of the points (<see cref="Layout"/>), so no point falls outside them. Points
    /// so far apart that the box would need more than the given number of cells get larger cells: the index never takes
    /// more memory than that, whatever the positions.</para>
    /// <para>Cells come from integer math on raw fixed-point values, the points of a cell keep the order of their indices,
    /// and a query walks the cells row by row along X: every client visits the same points in the same order. A query that
    /// picks one point (the nearest) should still break ties by the index, so that its answer does not depend on the cell
    /// size.</para>
    /// <para>Built in O(points + cells) (a counting sort); Burst-compatible. Dispose it, or allocate it with an allocator
    /// that frees itself (<c>Allocator.Temp</c>, the world update allocator).</para>
    /// </remarks>
    public struct LockstepSpatialIndex : IDisposable
    {
        // Larger radii cover the whole box: fixed-point positions stay far below this, so adding it never overflows.
        private const long MAX_RADIUS_RAW = long.MaxValue >> 2;

        private FixedGrid _layout;
        // Points of cell c are _points[_starts[c] .. _starts[c + 1]).
        private NativeArray<int> _starts;
        private NativeArray<int> _points;

        /// <summary>
        /// Sorts the points <paramref name="positions"/> (index = position in the array) into cells of
        /// <paramref name="cellSize"/>, doubled as long as the bounding box would need more than <paramref name="maxCells"/>
        /// cells.
        /// </summary>
        public LockstepSpatialIndex(NativeArray<FixedVector2> positions, FixedPoint cellSize, int maxCells, AllocatorManager.AllocatorHandle allocator)
        {
            _layout = CreateLayout(positions, cellSize.rawValue > 0 ? cellSize.rawValue : FixedPoint.ONE_RAW, math.max(maxCells, 1));
            _starts = CollectionHelper.CreateNativeArray<int>(_layout.CellCount + 1, allocator);
            _points = CollectionHelper.CreateNativeArray<int>(positions.Length, allocator, NativeArrayOptions.UninitializedMemory);

            for (var i = 0; i < positions.Length; i++)
            {
                _starts[GetCellIndex(positions[i]) + 1]++;
            }
            for (var cell = 0; cell < _layout.CellCount; cell++)
            {
                _starts[cell + 1] += _starts[cell];
            }

            // Each cell fills from its start in index order; the next start doubles as the cursor and is set back after.
            var cursors = new NativeArray<int>(_layout.CellCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int>.Copy(_starts, cursors, _layout.CellCount);
            for (var i = 0; i < positions.Length; i++)
            {
                var cell = GetCellIndex(positions[i]);
                _points[cursors[cell]++] = i;
            }
            cursors.Dispose();
        }

        /// <summary>The cells: they cover the bounding box of the points. Empty without points.</summary>
        public FixedGrid Layout => _layout;

        /// <summary>Number of points.</summary>
        public int Count => _points.IsCreated ? _points.Length : 0;

        public bool IsCreated => _points.IsCreated;

        /// <summary>
        /// The points in the cells that the square of half side <paramref name="radius"/> around <paramref name="center"/>
        /// touches: every point within <paramref name="radius"/> of it, and some further ones — check the distance.
        /// </summary>
        public Enumerator Query(FixedVector2 center, FixedPoint radius)
        {
            if (Count == 0 || radius.rawValue < 0)
            {
                return default;
            }

            var reach = math.min(radius.rawValue, MAX_RADIUS_RAW);
            var min = _layout.WorldToCell(new FixedVector2(FixedPoint.FromRaw(center.x.rawValue - reach), FixedPoint.FromRaw(center.y.rawValue - reach)));
            var max = _layout.WorldToCell(new FixedVector2(FixedPoint.FromRaw(center.x.rawValue + reach), FixedPoint.FromRaw(center.y.rawValue + reach)));
            min = math.max(min, int2.zero);
            max = math.min(max, new int2(_layout.width - 1, _layout.height - 1));
            return new Enumerator(this, min, max);
        }

        public void Dispose()
        {
            if (_starts.IsCreated)
            {
                CollectionHelper.Dispose(_starts);
            }
            if (_points.IsCreated)
            {
                CollectionHelper.Dispose(_points);
            }
        }

        private int GetCellIndex(FixedVector2 position)
        {
            return _layout.GetIndex(_layout.WorldToCell(position));
        }

        private static FixedGrid CreateLayout(NativeArray<FixedVector2> positions, long cellSize, int maxCells)
        {
            if (positions.Length == 0)
            {
                return default;
            }

            long minX = long.MaxValue, minZ = long.MaxValue, maxX = long.MinValue, maxZ = long.MinValue;
            for (var i = 0; i < positions.Length; i++)
            {
                minX = math.min(minX, positions[i].x.rawValue);
                minZ = math.min(minZ, positions[i].y.rawValue);
                maxX = math.max(maxX, positions[i].x.rawValue);
                maxZ = math.max(maxZ, positions[i].y.rawValue);
            }

            var width = (maxX - minX) / cellSize + 1;
            var height = (maxZ - minZ) / cellSize + 1;
            while (width > maxCells || height > maxCells || width * height > maxCells)
            {
                cellSize *= 2;
                width = (maxX - minX) / cellSize + 1;
                height = (maxZ - minZ) / cellSize + 1;
            }
            return new FixedGrid(new FixedVector2(FixedPoint.FromRaw(minX), FixedPoint.FromRaw(minZ)), FixedPoint.FromRaw(cellSize), (int)width, (int)height);
        }

        /// <summary>The indices of the points of a query, cell by cell (rows along X), in index order inside a cell.</summary>
        public struct Enumerator
        {
            private readonly NativeArray<int> _starts;
            private readonly NativeArray<int> _points;
            private readonly int _width;
            private readonly int2 _min;
            private readonly int2 _max;
            private int2 _cell;
            private int _entry;
            private int _end;
            private bool _isStarted;

            internal Enumerator(in LockstepSpatialIndex index, int2 min, int2 max)
            {
                _starts = index._starts;
                _points = index._points;
                _width = index._layout.width;
                _min = min;
                _max = max;
                _cell = min;
                _entry = 0;
                _end = 0;
                _isStarted = false;
                Current = -1;
            }

            /// <summary>The index of the point: its position in the array the index was built from.</summary>
            public int Current { get; private set; }

            public bool MoveNext()
            {
                if (!_points.IsCreated || _min.x > _max.x || _min.y > _max.y)
                {
                    return false;
                }
                if (!_isStarted)
                {
                    _isStarted = true;
                    StartCell();
                }
                while (_entry >= _end)
                {
                    if (++_cell.x > _max.x)
                    {
                        _cell.x = _min.x;
                        if (++_cell.y > _max.y)
                        {
                            return false;
                        }
                    }
                    StartCell();
                }
                Current = _points[_entry++];
                return true;
            }

            public Enumerator GetEnumerator() => this;

            private void StartCell()
            {
                var cell = _cell.y * _width + _cell.x;
                _entry = _starts[cell];
                _end = _starts[cell + 1];
            }
        }
    }
}
