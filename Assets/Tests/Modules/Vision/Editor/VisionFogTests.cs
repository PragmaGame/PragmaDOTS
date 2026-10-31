using NUnit.Framework;
using Pragma.Dots.Mathematics;
using Pragma.Dots.Vision;
using Unity.Collections;
using UnityEngine;

namespace Pragma.Dots.Tests
{
    /// <summary>The fog of war texture of a slot (<see cref="LockstepVisionFog"/>).</summary>
    public class VisionFogTests
    {
        private const float TOLERANCE = 1e-5f;

        // 5 x 4 cells of two units from (-4, -3), two slots.
        private static LockstepVisionGrid CreateGrid()
        {
            return new LockstepVisionGrid
            {
                origin = new FixedVector2(-4, -3),
                cellSize = 2,
                width = 5,
                height = 4,
                slotCount = 2,
            };
        }

        // Slot 1 sees the 3 x 3 block around cell (2, 1) when it is seen.
        private static void SetSeen(in LockstepVisionGrid grid, NativeArray<LockstepVisionCell> cells, bool isSeen)
        {
            for (var y = 0; y <= 2; y++)
            {
                for (var x = 1; x <= 3; x++)
                {
                    cells[grid.CellCount + y * grid.width + x] = new LockstepVisionCell { isVisible = isSeen };
                }
            }
        }

        private static float At(LockstepVisionFog fog, int x, int y) => fog.Visibility[y * fog.Width + x];

        [Test]
        public void Fog_ShowsTheSmoothedPlaneOfItsSlotAtOnce()
        {
            var grid = CreateGrid();
            using (var cells = new NativeArray<LockstepVisionCell>(grid.CellCount * grid.slotCount, Allocator.TempJob))
            using (var fog = new LockstepVisionFog())
            {
                SetSeen(grid, cells, true);
                Assert.IsTrue(fog.TryUpdate(grid, cells, 1, 0, 0.01f));
                Assert.AreEqual(5, fog.Width);
                Assert.AreEqual(4, fog.Height);
                Assert.AreEqual(new Rect(-4, -3, 10, 8), fog.Area);
                Assert.AreEqual(1f, At(fog, 2, 1), TOLERANCE, "every neighbour is seen");
                Assert.AreEqual(0.5f, At(fog, 0, 0), TOLERANCE, "a corner averages the four cells it has");
                Assert.AreEqual(0.25f, At(fog, 0, 3), TOLERANCE);
                Assert.AreEqual(255, fog.Texture.GetPixelData<byte>(0)[1 * 5 + 2]);
                Assert.AreEqual(1, fog.Version);

                fog.TryUpdate(grid, cells, 1, 0, 0.01f);
                Assert.IsTrue(fog.IsSettled);
                Assert.AreEqual(1, fog.Version, "nothing moves until a new tick");

                Assert.IsTrue(fog.TryUpdate(grid, cells, 0, 0, 0.01f), "slot 0 sees nothing");
                Assert.AreEqual(1f, At(fog, 2, 1), TOLERANCE, "the same tick of another slot is not read again: restart the fog first");
                fog.Restart();
                fog.TryUpdate(grid, cells, 0, 0, 0.01f);
                Assert.AreEqual(0f, At(fog, 2, 1), TOLERANCE);

                Assert.IsFalse(fog.TryUpdate(grid, cells, 2, 1, 0.01f), "no plane for the slot");
                Assert.IsFalse(fog.TryUpdate(grid, default, 0, 1, 0.01f), "no cells");
            }
        }

        [Test]
        public void Fog_FadesTowardTheNewTickAndSettles()
        {
            var grid = CreateGrid();
            using (var cells = new NativeArray<LockstepVisionCell>(grid.CellCount * grid.slotCount, Allocator.TempJob))
            using (var fog = new LockstepVisionFog { FadeSeconds = 1f })
            {
                SetSeen(grid, cells, true);
                fog.TryUpdate(grid, cells, 1, 0, 0.25f);
                SetSeen(grid, cells, false);

                fog.TryUpdate(grid, cells, 1, 1, 0.25f);
                Assert.AreEqual(0.75f, At(fog, 2, 1), TOLERANCE);
                Assert.AreEqual(191, fog.Texture.GetPixelData<byte>(0)[1 * 5 + 2]);
                Assert.IsFalse(fog.IsSettled);
                fog.TryUpdate(grid, cells, 1, 1, 0.25f);
                Assert.AreEqual(0.5f, At(fog, 2, 1), TOLERANCE, "between ticks it keeps fading");
                for (var i = 0; i < 4; i++)
                {
                    fog.TryUpdate(grid, cells, 1, 1, 0.25f);
                }
                Assert.AreEqual(0f, At(fog, 2, 1), TOLERANCE);
                Assert.IsTrue(fog.IsSettled);
                var version = fog.Version;
                fog.TryUpdate(grid, cells, 1, 1, 0.25f);
                Assert.AreEqual(version, fog.Version);
            }
        }

        [Test]
        public void Fog_OfARevealedGrid_ShowsEverything()
        {
            var grid = CreateGrid();
            grid.isRevealed = true;
            using (var cells = new NativeArray<LockstepVisionCell>(grid.CellCount * grid.slotCount, Allocator.TempJob))
            using (var fog = new LockstepVisionFog())
            {
                Assert.IsTrue(fog.TryUpdate(grid, cells, 0, 0, 0.01f));
                for (var i = 0; i < fog.Visibility.Length; i++)
                {
                    Assert.AreEqual(1f, fog.Visibility[i], TOLERANCE);
                }
            }
        }
    }
}
