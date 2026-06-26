using NUnit.Framework;
using Barcade.Core.Dodge;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="GridArena"/>: ring collapse ordering, timing, IsSolid boundary,
    /// and centre edge case.
    /// </summary>
    [TestFixture]
    public class GridArenaTests
    {
        // ── IsSolid: out-of-bounds ────────────────────────────────────────────────

        [Test]
        public void IsSolid_OutOfBounds_ReturnsFalse()
        {
            var arena = new GridArena(n: 3);
            Assert.That(arena.IsSolid(-1,  0), Is.False, "x < 0");
            Assert.That(arena.IsSolid( 0, -1), Is.False, "z < 0");
            Assert.That(arena.IsSolid( 3,  0), Is.False, "x >= N");
            Assert.That(arena.IsSolid( 0,  3), Is.False, "z >= N");
        }

        // ── IsSolid: all tiles solid before grace delay ───────────────────────────

        [Test]
        public void IsSolid_BeforeGraceDelay_AllTilesSolid()
        {
            var arena = new GridArena(n: 5, graceDelay: 3f, collapseInterval: 2f);

            // No tick yet — every tile should be solid.
            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                    Assert.That(arena.IsSolid(x, z), Is.True, $"Tile ({x},{z}) should be solid before grace");

            // Tick just under the grace delay.
            arena.Tick(2.99f);
            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                    Assert.That(arena.IsSolid(x, z), Is.True, $"Tile ({x},{z}) should still be solid");
        }

        // ── Ring 0 collapses at graceDelay ────────────────────────────────────────

        [Test]
        public void Tick_AtGraceDelay_Ring0Collapses()
        {
            var arena = new GridArena(n: 5, graceDelay: 3f, collapseInterval: 2f);

            arena.Tick(3f); // exactly grace delay

            // Ring 0 of a 5×5 grid is the border (x==0 or x==4 or z==0 or z==4).
            Assert.That(arena.IsSolid(0, 0), Is.False, "corner (0,0) — ring 0");
            Assert.That(arena.IsSolid(4, 4), Is.False, "corner (4,4) — ring 0");
            Assert.That(arena.IsSolid(2, 0), Is.False, "edge midpoint — ring 0");
            Assert.That(arena.IsSolid(0, 2), Is.False, "edge midpoint — ring 0");

            // Inner tiles still solid.
            Assert.That(arena.IsSolid(1, 1), Is.True, "ring 1 inner — still solid");
            Assert.That(arena.IsSolid(2, 2), Is.True, "centre — still solid");

            Assert.That(arena.FallenRingCount, Is.EqualTo(1));
        }

        // ── Ring 1 collapses after first interval ─────────────────────────────────

        [Test]
        public void Tick_AfterFirstInterval_Ring1Collapses()
        {
            var arena = new GridArena(n: 5, graceDelay: 3f, collapseInterval: 2f);

            arena.Tick(5f); // 3 + 2

            // Ring 0 and ring 1 should be fallen.
            Assert.That(arena.IsSolid(0, 0), Is.False, "ring 0 — corner");
            Assert.That(arena.IsSolid(1, 1), Is.False, "ring 1 — inner corner");
            Assert.That(arena.IsSolid(1, 2), Is.False, "ring 1 — edge of inner ring");

            // Centre tile (ring 2 for N=5) still solid.
            Assert.That(arena.IsSolid(2, 2), Is.True, "centre — still solid");

            Assert.That(arena.FallenRingCount, Is.EqualTo(2));
        }

        // ── Ring collapse order: outermost first, inward last ─────────────────────

        [Test]
        public void CollapseOrder_OutermostFirst_InnermostLast()
        {
            // 3×3 grid: ring 0 = border (8 tiles), ring 1 = centre (1 tile).
            // TotalRings = ceil(3/2) = 2.
            var arena = new GridArena(n: 3, graceDelay: 1f, collapseInterval: 1f);

            arena.Tick(1f); // ring 0 falls
            Assert.That(arena.IsSolid(0, 0), Is.False, "border tile after ring-0 collapse");
            Assert.That(arena.IsSolid(1, 1), Is.True,  "centre still solid after ring-0");

            arena.Tick(1f); // ring 1 (centre) falls
            Assert.That(arena.IsSolid(1, 1), Is.False, "centre tile fallen");
            Assert.That(arena.FallenRingCount, Is.EqualTo(2));
        }

        // ── Centre edge case: single-tile centre for odd N ────────────────────────

        [Test]
        public void Centre_N9_CollapsesLast()
        {
            // 9×9 grid: 5 rings (0..4). Centre tile is (4,4).
            var arena = new GridArena(n: 9, graceDelay: 1f, collapseInterval: 1f);

            // Collapse all rings: need 1 + 4*1 = 5 s total.
            arena.Tick(4.99f); // rings 0-3 fallen, ring 4 not yet
            Assert.That(arena.IsSolid(4, 4), Is.True, "centre should not have fallen at 4.99 s");

            arena.Tick(0.02f); // crosses 5s — ring 4 falls
            Assert.That(arena.IsSolid(4, 4), Is.False, "centre tile must fall last");
            Assert.That(arena.FallenRingCount, Is.EqualTo(5));
        }

        // ── FallenRingCount never exceeds TotalRings ─────────────────────────────

        [Test]
        public void FallenRingCount_NeverExceedsTotalRings()
        {
            var arena = new GridArena(n: 3, graceDelay: 0.1f, collapseInterval: 0.1f);
            arena.Tick(100f); // way past all collapses
            Assert.That(arena.FallenRingCount, Is.EqualTo(arena.TotalRings));
        }

        // ── Tick with zero or negative dt has no effect ───────────────────────────

        [Test]
        public void Tick_ZeroDt_NoChange()
        {
            var arena = new GridArena(n: 3, graceDelay: 1f, collapseInterval: 1f);
            arena.Tick(0f);
            Assert.That(arena.FallenRingCount, Is.EqualTo(0));
        }

        // ── N=1 edge case: single tile collapses at grace delay ───────────────────

        [Test]
        public void N1_SingleTile_CollapseAtGraceDelay()
        {
            var arena = new GridArena(n: 1, graceDelay: 1f, collapseInterval: 1f);
            Assert.That(arena.IsSolid(0, 0), Is.True,  "before collapse");
            arena.Tick(1f);
            Assert.That(arena.IsSolid(0, 0), Is.False, "after collapse");
        }

        // ── Multiple Tick calls accumulate correctly ──────────────────────────────

        [Test]
        public void Tick_MultipleSmallSteps_SameResultAsOneStep()
        {
            var arenaA = new GridArena(n: 5, graceDelay: 3f, collapseInterval: 2f);
            var arenaB = new GridArena(n: 5, graceDelay: 3f, collapseInterval: 2f);

            arenaA.Tick(5f);                              // one step
            arenaB.Tick(1f); arenaB.Tick(2f); arenaB.Tick(2f); // three steps

            Assert.That(arenaA.FallenRingCount, Is.EqualTo(arenaB.FallenRingCount));

            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                    Assert.That(arenaA.IsSolid(x, z), Is.EqualTo(arenaB.IsSolid(x, z)),
                        $"Tile ({x},{z}) must match between one-step and multi-step ticking");
        }

        // ── TotalRings formula for even and odd N ─────────────────────────────────

        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        [TestCase(9, 5)]
        public void TotalRings_Formula(int n, int expectedRings)
        {
            var arena = new GridArena(n: n);
            Assert.That(arena.TotalRings, Is.EqualTo(expectedRings));
        }
    }
}
