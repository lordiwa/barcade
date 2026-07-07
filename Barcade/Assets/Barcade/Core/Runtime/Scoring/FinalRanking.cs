using System;

namespace Barcade.Core.Scoring
{
    /// <summary>
    /// The GDD §6.3 final podium ordering: estrellas → monedas → victorias de
    /// microjuego → empate declarado. Competition ranking — seats that tie on the
    /// whole chain SHARE the place ("el empate compartido es socialmente
    /// productivo en bar"); place = 1 + number of strictly better seats. Absent
    /// seats get place 0. Pure function.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public static class FinalRanking
    {
        /// <summary>Places per seat (1..4, ties share; 0 = absent).</summary>
        public static int[] Rank(int[] stars, int[] coins, int[] microgameWins, int activeSeatsMask)
        {
            if (stars == null) throw new ArgumentNullException(nameof(stars));
            if (coins == null) throw new ArgumentNullException(nameof(coins));
            if (microgameWins == null) throw new ArgumentNullException(nameof(microgameWins));
            if (stars.Length != 4 || coins.Length != 4 || microgameWins.Length != 4)
                throw new ArgumentException("stars, coins and microgameWins must be length 4");

            var places = new int[4];
            for (int seat = 0; seat < 4; seat++)
            {
                if ((activeSeatsMask & (1 << seat)) == 0) continue;

                int better = 0;
                for (int other = 0; other < 4; other++)
                {
                    if (other == seat || (activeSeatsMask & (1 << other)) == 0) continue;
                    if (Compare(stars, coins, microgameWins, other, seat) > 0)
                        better++;
                }
                places[seat] = 1 + better;
            }
            return places;
        }

        /// <summary>Positive when <paramref name="a"/> ranks strictly better than <paramref name="b"/>.</summary>
        private static int Compare(int[] stars, int[] coins, int[] wins, int a, int b)
        {
            if (stars[a] != stars[b]) return stars[a] - stars[b];
            if (coins[a] != coins[b]) return coins[a] - coins[b];
            return wins[a] - wins[b];
        }
    }
}
