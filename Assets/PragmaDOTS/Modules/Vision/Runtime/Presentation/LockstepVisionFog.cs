using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pragma.Dots.Vision
{
    /// <summary>
    /// The fog of war of one slot as a texture, for the presentation: one texel per cell of the
    /// <see cref="LockstepVisionGrid"/>, 1 where the slot sees and 0 where it does not, with soft edges and a fade in time.
    /// The game draws it its own way (a full-screen pass, a decal, a terrain layer, the minimap) from <see cref="Texture"/>
    /// and <see cref="Area"/>; nothing here depends on a render pipeline.
    /// </summary>
    /// <remarks>
    /// <para>Call <see cref="TryUpdate"/> every frame the fog is shown. On a new tick each cell's target is the share of the
    /// visible cells around it (3 by 3), so the edge of the fog does not follow the steps of the cells; between ticks the
    /// shown visibility only moves toward the target, by <see cref="FadeSeconds"/>, and once it has caught up nothing is
    /// computed or uploaded again. The cells are read in Burst jobs by rows, finished within the call: the planes belong to
    /// the simulation world, which steps after the presentation. A grid with <see cref="LockstepVisionGrid.isRevealed"/>
    /// shows everything.</para>
    /// <para>The fog belongs to the caller, which disposes it (the texture is destroyed with it).</para>
    /// </remarks>
    public sealed class LockstepVisionFog : IDisposable
    {
        private readonly string _name;
        private Texture2D _texture;
        private NativeArray<float> _target;
        private NativeArray<float> _shown;
        private NativeArray<bool> _changedRows;
        private int _tick;
        private bool _isRestarted = true;
        private bool _isSettled;

        /// <param name="name">The name of the texture.</param>
        public LockstepVisionFog(string name = "Fog of War")
        {
            _name = name;
        }

        /// <summary>Seconds a cell takes to clear when it is seen and to fog over when it is not; zero changes at once.</summary>
        public float FadeSeconds { get; set; } = 0.3f;

        /// <summary>
        /// One texel per cell, row by row along X from the corner at <see cref="LockstepVisionGrid.origin"/>: R8, linear,
        /// bilinear, clamped. Null before the first update.
        /// </summary>
        public Texture2D Texture => _texture;

        /// <summary>Cells along world X.</summary>
        public int Width => _texture != null ? _texture.width : 0;

        /// <summary>Cells along world Z.</summary>
        public int Height => _texture != null ? _texture.height : 0;

        /// <summary>The world rectangle under the grid: X and Z of its corner, its size along X and along Z.</summary>
        public Rect Area { get; private set; }

        /// <summary>
        /// The visibility of the cells as drawn, 0 to 1, row by row along X: smoothed and fading. Valid until the next
        /// update; what a minimap reads to match the fog of the world.
        /// </summary>
        public NativeArray<float>.ReadOnly Visibility => _shown.AsReadOnly();

        /// <summary>Changes whenever the visibility of a cell does.</summary>
        public int Version { get; private set; }

        /// <summary>The visibility has caught up with the last tick: until a new tick nothing changes.</summary>
        public bool IsSettled => _isSettled;

        /// <summary>
        /// Draws what <paramref name="slot"/> sees on <paramref name="tick"/>: reads its plane when the tick is new and moves
        /// the shown visibility toward it by <paramref name="deltaTime"/> (the unscaled time of the frame).
        /// </summary>
        /// <param name="cells">The <see cref="LockstepVisionCell"/> buffer of the grid entity, read in place.</param>
        /// <returns>False when the grid has no cells or the slot no plane: there is no fog to draw.</returns>
        public bool TryUpdate(in LockstepVisionGrid grid, NativeArray<LockstepVisionCell> cells, int slot, int tick, float deltaTime)
        {
            if (!LockstepVision.HasCells(grid, cells) || slot < 0 || slot >= grid.slotCount)
            {
                return false;
            }

            var width = grid.width;
            var height = grid.height;
            var isNew = EnsureTexture(width, height) || _isRestarted;
            var isNewTick = isNew || tick != _tick;
            if (isNewTick || !_isSettled)
            {
                Draw(grid, LockstepVision.GetPlane(grid, cells, slot), isNew, isNewTick, deltaTime);
                _tick = tick;
            }
            _isRestarted = false;

            var cellSize = (float)grid.cellSize;
            Area = new Rect((float)grid.origin.x, (float)grid.origin.y, width * cellSize, height * cellSize);
            return true;
        }

        /// <summary>The next update shows the plane at once instead of fading into it: after showing another slot, or after frames the fog was not drawn in.</summary>
        public void Restart()
        {
            _isRestarted = true;
        }

        public void Dispose()
        {
            if (_texture != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_texture);
                }
                else
                {
                    Object.DestroyImmediate(_texture);
                }
                _texture = null;
            }
            DisposeCells();
        }

        // A new tick sets a new target; the shown visibility steps toward it, and the texture is uploaded if anything moved.
        private void Draw(in LockstepVisionGrid grid, NativeArray<LockstepVisionCell> plane, bool isNew, bool isNewTick, float deltaTime)
        {
            var handle = default(JobHandle);
            if (isNewTick)
            {
                handle = new SmoothJob
                {
                    width = grid.width,
                    height = grid.height,
                    isRevealed = grid.isRevealed,
                    plane = plane,
                    target = _target,
                }.Schedule(grid.height, 1);
            }

            // A fog shown anew stands at once instead of creeping in from the last slot or an empty texture.
            var step = isNew || FadeSeconds <= 0f ? 1f : deltaTime / FadeSeconds;
            new FadeJob
            {
                width = grid.width,
                step = step,
                target = _target,
                shown = _shown,
                pixels = _texture.GetPixelData<byte>(0),
                changedRows = _changedRows,
            }.Schedule(grid.height, 1, handle).Complete();

            var isChanged = isNew || _changedRows.Contains(true);
            _isSettled = !isChanged;
            if (isChanged)
            {
                Version++;
                _texture.Apply(false);
            }
        }

        // The texture and the cells match the size of the grid; true when they are new.
        private bool EnsureTexture(int width, int height)
        {
            if (_texture != null && _texture.width == width && _texture.height == height)
            {
                return false;
            }
            Dispose();
            _texture = new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                name = _name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _target = new NativeArray<float>(width * height, Allocator.Persistent);
            _shown = new NativeArray<float>(width * height, Allocator.Persistent);
            _changedRows = new NativeArray<bool>(height, Allocator.Persistent);
            return true;
        }

        private void DisposeCells()
        {
            if (_target.IsCreated)
            {
                _target.Dispose();
                _shown.Dispose();
                _changedRows.Dispose();
            }
        }

        // The visibility of the cells of a row, averaged with their neighbours (3 by 3): the edge of the fog is not a staircase of cells.
        [BurstCompile]
        private struct SmoothJob : IJobParallelFor
        {
            public int width;
            public int height;
            public bool isRevealed;

            [ReadOnly]
            public NativeArray<LockstepVisionCell> plane;

            // Each job writes only the cells of its row.
            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<float> target;

            public void Execute(int y)
            {
                if (isRevealed)
                {
                    for (var x = 0; x < width; x++)
                    {
                        target[y * width + x] = 1f;
                    }
                    return;
                }

                var top = math.max(y - 1, 0);
                var bottom = math.min(y + 1, height - 1);
                for (var x = 0; x < width; x++)
                {
                    var left = math.max(x - 1, 0);
                    var right = math.min(x + 1, width - 1);
                    var seen = 0;
                    for (var row = top; row <= bottom; row++)
                    {
                        for (var column = left; column <= right; column++)
                        {
                            if (plane[row * width + column].isVisible)
                            {
                                seen++;
                            }
                        }
                    }
                    target[y * width + x] = (float)seen / ((bottom - top + 1) * (right - left + 1));
                }
            }
        }

        // The shown visibility of the cells of a row moves toward the target by at most the step; the texels follow it.
        [BurstCompile]
        private struct FadeJob : IJobParallelFor
        {
            public int width;
            public float step;

            [ReadOnly]
            public NativeArray<float> target;

            // Each job writes only the cells of its row.
            [NativeDisableParallelForRestriction]
            public NativeArray<float> shown;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<byte> pixels;

            [WriteOnly]
            public NativeArray<bool> changedRows;

            public void Execute(int y)
            {
                var isChanged = false;
                for (var i = y * width; i < (y + 1) * width; i++)
                {
                    var current = shown[i];
                    var delta = target[i] - current;
                    var next = math.abs(delta) <= step ? target[i] : current + math.sign(delta) * step;
                    isChanged |= next != current;
                    shown[i] = next;
                    pixels[i] = (byte)math.round(next * 255f);
                }
                changedRows[y] = isChanged;
            }
        }
    }
}
