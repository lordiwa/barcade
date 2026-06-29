using System;

namespace Barcade.Core.Dodge
{
    /// <summary>
    /// N×N grid of solid tiles that collapse ring-by-ring from the outside inward.
    ///
    /// Ring index = min(x, z, N-1-x, N-1-z).
    /// Ring 0 is the outermost border; ring (N-1)/2 is the centre.
    ///
    /// Schedule:
    ///   - 0 .. graceDelay          : all tiles solid.
    ///   - graceDelay               : ring 0 collapses.
    ///   - graceDelay + k * interval: ring k collapses (k = 0, 1, …, maxRing).
    ///
    /// Pure C# — no UnityEngine dependency.
    /// </summary>
    public sealed class GridArena
    {
        // ── Immutable config ─────────────────────────────────────────────────────

        private readonly float _graceDelay;
        private readonly float _collapseInterval;

        // ── Tile state ────────────────────────────────────────────────────────────

        private readonly bool[,] _solid; // [x, z]

        // ── Collapse progress ─────────────────────────────────────────────────────

        private float  _elapsed;
        private int    _fallenRingCount; // how many rings have been removed so far

        // ── Public readonly properties ────────────────────────────────────────────

        /// <summary>Grid dimension; the arena is N×N tiles.</summary>
        public int N { get; }

        /// <summary>Number of rings that have fully collapsed so far.</summary>
        public int FallenRingCount => _fallenRingCount;

        /// <summary>Total number of rings (= ceiling(N/2)).</summary>
        public int TotalRings { get; }

        // ── Construction ──────────────────────────────────────────────────────────

        /// <param name="n">Grid dimension (odd recommended, default 9).</param>
        /// <param name="graceDelay">Seconds before the first ring collapses.</param>
        /// <param name="collapseInterval">Seconds between successive ring collapses.</param>
        public GridArena(int n = 9, float graceDelay = 3f, float collapseInterval = 3f)
        {
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), "Grid size must be >= 1.");
            if (graceDelay < 0f)         throw new ArgumentOutOfRangeException(nameof(graceDelay));
            if (collapseInterval <= 0f)  throw new ArgumentOutOfRangeException(nameof(collapseInterval));

            N                  = n;
            _graceDelay        = graceDelay;
            _collapseInterval  = collapseInterval;
            TotalRings         = (n + 1) / 2;   // ceil(N/2)

            _solid = new bool[n, n];
            for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                    _solid[x, z] = true;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns <c>true</c> when tile (x, z) exists and has not yet fallen.
        /// Returns <c>false</c> for out-of-bounds coordinates.
        /// </summary>
        public bool IsSolid(int x, int z)
        {
            if (x < 0 || x >= N || z < 0 || z >= N) return false;
            return _solid[x, z];
        }

        /// <summary>
        /// Seconds remaining until tile (x, z) collapses.
        ///
        /// Returns <c>float.NegativeInfinity</c> (sentinel) when:
        ///   - (x, z) is out of bounds, or
        ///   - the tile has already fallen (not solid).
        ///
        /// Ring index = min(x, z, N-1-x, N-1-z), matching <see cref="CollapseRing"/>.
        /// Collapse time for ring k = graceDelay + k × collapseInterval.
        /// </summary>
        public float TimeUntilFall(int x, int z)
        {
            if (x < 0 || x >= N || z < 0 || z >= N) return float.NegativeInfinity;
            if (!_solid[x, z]) return float.NegativeInfinity;

            int ring = Math.Min(Math.Min(x, z), Math.Min(N - 1 - x, N - 1 - z));
            float collapseAt = _graceDelay + ring * _collapseInterval;
            return collapseAt - _elapsed;
        }

        /// <summary>Advance the collapse schedule by <paramref name="dt"/> seconds.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            _elapsed += dt;

            // Collapse every ring whose scheduled time has arrived.
            while (_fallenRingCount < TotalRings)
            {
                float collapseTime = _graceDelay + _fallenRingCount * _collapseInterval;
                if (_elapsed < collapseTime) break;

                CollapseRing(_fallenRingCount);
                _fallenRingCount++;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private void CollapseRing(int ring)
        {
            // Ring index = min(x, z, N-1-x, N-1-z).
            // Tiles in this ring: those for which that minimum equals `ring`.
            int lo = ring;
            int hi = N - 1 - ring;

            if (lo > hi) return; // degenerate (shouldn't happen for valid ring indices)

            for (int x = lo; x <= hi; x++)
            {
                for (int z = lo; z <= hi; z++)
                {
                    // Only the perimeter of [lo..hi, lo..hi] belongs to this ring.
                    if (x == lo || x == hi || z == lo || z == hi)
                        _solid[x, z] = false;
                }
            }
        }
    }
}
