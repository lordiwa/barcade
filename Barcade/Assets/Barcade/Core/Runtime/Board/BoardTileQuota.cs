using System;

namespace Barcade.Core.Board
{
    /// <summary>
    /// GDD §5.3's ring composition quota table — exact for N=20 (the GDD's literal
    /// counts), scaled for any other N in [<see cref="BoardConfig.MinRingSize"/>,
    /// <see cref="BoardConfig.MaxRingSize"/>] (AC3).
    ///
    /// <para>
    /// <b>[ASSUMED] scaling rule.</b> GDD §5.3 gives exact counts only for N=20 and
    /// does not say how the table scales for other ring sizes. Estrella is pinned
    /// to exactly 1 for every N — it is the ring's single "móvil" tile by
    /// definition (§5.3: "Solo hay una a la vez"), not a count that makes sense to
    /// scale. The remaining N-1 squares are apportioned across the other 6 types by
    /// the Hamilton (largest-remainder) method, weighted by their §5.3 N=20 counts
    /// (6/2/3/3/3/2, summing to 19) — the standard, bias-free way to scale an
    /// integer quota table to a different total while staying as close as possible
    /// to the original ratios. At N=20 this reproduces the GDD table exactly (the
    /// scale factor is 1, so every floor is exact and no remainder is distributed).
    /// </para>
    /// </summary>
    public static class BoardTileQuota
    {
        // Declaration order doubles as the deterministic tie-break order for the
        // Hamilton remainder step (earlier in this array wins a tied fractional
        // remainder) — Estrella excluded, see class doc.
        private static readonly BoardTileType[] ScaledTypes =
        {
            BoardTileType.MonedaPositiva, BoardTileType.MonedaNegativa, BoardTileType.Inversion,
            BoardTileType.Propiedad, BoardTileType.Evento, BoardTileType.Cangrejo,
        };

        private static readonly int[] BaseWeights = { 6, 2, 3, 3, 3, 2 }; // GDD §5.3 @ N=20, sums to 19

        /// <summary>Per-type square count for a ring of <paramref name="ringSize"/> squares. Indexed by <see cref="BoardTileType"/>; sums to <paramref name="ringSize"/>.</summary>
        public static int[] Compute(int ringSize)
        {
            if (ringSize < BoardConfig.MinRingSize || ringSize > BoardConfig.MaxRingSize)
                throw new ArgumentOutOfRangeException(nameof(ringSize), $"ringSize must be in [{BoardConfig.MinRingSize},{BoardConfig.MaxRingSize}]; got {ringSize}");

            var quota = new int[7];
            quota[(int)BoardTileType.Estrella] = 1;

            int remaining = ringSize - 1;
            int weightSum = 0;
            for (int i = 0; i < BaseWeights.Length; i++) weightSum += BaseWeights[i];

            var floorCounts = new int[ScaledTypes.Length];
            var remainders = new double[ScaledTypes.Length];
            int floorSum = 0;
            for (int i = 0; i < ScaledTypes.Length; i++)
            {
                double exact = (double)BaseWeights[i] * remaining / weightSum;
                floorCounts[i] = (int)Math.Floor(exact);
                remainders[i] = exact - floorCounts[i];
                floorSum += floorCounts[i];
            }

            int leftover = remaining - floorSum;
            while (leftover > 0)
            {
                int best = 0;
                for (int i = 1; i < ScaledTypes.Length; i++)
                    if (remainders[i] > remainders[best]) best = i;
                floorCounts[best]++;
                remainders[best] = -1.0; // consumed -- never picked again this pass
                leftover--;
            }

            for (int i = 0; i < ScaledTypes.Length; i++)
                quota[(int)ScaledTypes[i]] = floorCounts[i];

            return quota;
        }
    }
}
