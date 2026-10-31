using System;
using Pragma.Dots.Mathematics;
using Unity.Collections;

namespace Pragma.Dots.Navigation
{
    /// <summary>
    /// Whether a point can be reached on the grid: the path to it ends at the point itself
    /// (<see cref="LockstepPathStatus.Complete"/>), not at the reachable point nearest to it. Check an order with it before
    /// accepting it: a target in an obstacle, beyond the edge of the map or on ground cut off from the unit would otherwise
    /// send the unit to the nearest edge. One search per question, in the integer math of <see cref="LockstepPathfinder"/>,
    /// for a body of the grid's agent radius.
    /// </summary>
    /// <remarks>
    /// It keeps the pathfinder and its waypoints between questions: create it once per update and dispose it. Without cells
    /// (<see cref="LockstepNavGridView.HasCells"/>) every point can be reached. Burst-compatible; not thread-safe.
    /// </remarks>
    public struct LockstepNavReach : IDisposable
    {
        private LockstepNavGridView _grid;
        private LockstepPathfinder _pathfinder;
        private NativeList<FixedVector3> _waypoints;

        public bool IsCreated => _waypoints.IsCreated;

        /// <param name="grid">The grid to search; it reads the cells in place, so do not keep the reach across structural changes.</param>
        /// <param name="allocator">Allocator of the scratch memory.</param>
        public static LockstepNavReach Create(in LockstepNavGridView grid, AllocatorManager.AllocatorHandle allocator)
        {
            var reach = new LockstepNavReach
            {
                _grid = grid,
                _waypoints = new NativeList<FixedVector3>(allocator),
            };
            if (grid.HasCells)
            {
                reach._pathfinder = new LockstepPathfinder(grid.grid.CellCount, allocator);
            }
            return reach;
        }

        /// <summary>
        /// A path from <paramref name="start"/> (from a blocked cell: from the nearest walkable one) reaches
        /// <paramref name="target"/>.
        /// </summary>
        public bool CanReach(FixedVector3 start, FixedVector3 target)
        {
            if (!_grid.HasCells)
            {
                return true;
            }
            return _pathfinder.FindPath(_grid.grid, _grid.cells, start, target, _waypoints) == LockstepPathStatus.Complete;
        }

        public void Dispose()
        {
            if (_pathfinder.IsCreated)
            {
                _pathfinder.Dispose();
            }
            if (_waypoints.IsCreated)
            {
                _waypoints.Dispose();
            }
        }
    }
}
