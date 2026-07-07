namespace Barcade.Core.Board
{
    /// <summary>
    /// Builds the initial ring layout (GDD §5.1/§5.3): <paramref name="quota"/>
    /// copies of each <see cref="BoardTileType"/>, laid out in a uniformly random
    /// order via a seeded Fisher-Yates shuffle — deterministic given the same
    /// <see cref="SeededRandom"/> state (AC1).
    /// </summary>
    public static class BoardRing
    {
        public static BoardTileType[] Build(int[] quota, SeededRandom rng)
        {
            int total = 0;
            for (int i = 0; i < quota.Length; i++) total += quota[i];

            var ring = new BoardTileType[total];
            int w = 0;
            for (int type = 0; type < quota.Length; type++)
                for (int c = 0; c < quota[type]; c++)
                    ring[w++] = (BoardTileType)type;

            for (int i = ring.Length - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (ring[i], ring[j]) = (ring[j], ring[i]);
            }

            return ring;
        }
    }
}
