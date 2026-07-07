using System;

namespace Barcade.Core.Scoring
{
    /// <summary>
    /// The FINAL_WAGER resolution (GDD §6.2): every player compulsorily stakes
    /// 25/50/75% of their coins (stick choice within 5 s; a timeout falls back to
    /// <see cref="DefaultChoice"/>), and the pot redistributes by final-microgame
    /// place at 50/30/15/5%.
    ///
    /// Pot math contract: stakes floor (a player never stakes more than held);
    /// tied places pool the percentages of the positions they span and split
    /// evenly; with fewer than 4 active players the occupied positions'
    /// percentages renormalize to 100% so the whole pot is always redistributed
    /// (§5.3 invariant: coins never vanish "al banco"); integer rounding
    /// remainders are dealt one coin at a time from the WORST place upward (the
    /// anti-zero spirit of §6.1). Pure function — deterministic, no RNG.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public static class FinalWager
    {
        /// <summary>
        /// Stake used when the 5 s choice window times out. [ASSUMED] Half — the
        /// GDD names no default; the middle option is the least punitive neutral.
        /// Design-calibration candidate.
        /// </summary>
        public const WagerChoice DefaultChoice = WagerChoice.Half;

        /// <summary>Pot split by finishing position (GDD §6.2), best place first.</summary>
        public static readonly int[] PotSplitPercent = { 50, 30, 15, 5 };

        /// <summary>
        /// Resolves the wager.
        /// </summary>
        /// <param name="coins">Pre-wager coins per seat.</param>
        /// <param name="choices">Per-seat stake choice; null = timed out = <see cref="DefaultChoice"/>.</param>
        /// <param name="finalPlaces">Final-microgame places per seat (1..4, ties share; 0 = absent, does not wager).</param>
        public static WagerResult Resolve(int[] coins, WagerChoice?[] choices, int[] finalPlaces)
        {
            if (coins == null) throw new ArgumentNullException(nameof(coins));
            if (choices == null) throw new ArgumentNullException(nameof(choices));
            if (finalPlaces == null) throw new ArgumentNullException(nameof(finalPlaces));
            if (coins.Length != 4 || choices.Length != 4 || finalPlaces.Length != 4)
                throw new ArgumentException("coins, choices and finalPlaces must be length 4");

            var stakes = new int[4];
            var shares = new int[4];
            var after = new int[4];

            int pot = 0;
            for (int seat = 0; seat < 4; seat++)
            {
                int place = finalPlaces[seat];
                if (place == 0)
                {
                    after[seat] = coins[seat];
                    continue;
                }
                // LOW-1 (TASK-037 review fix round): mirrors PayoutRules.ApplyCompetitive's
                // guard. Without it, a malformed place (e.g. 5) silently stakes this
                // seat's coins into the pot but never gives it a share back, since it
                // can never match a real place 1..4 in the ordering pass below --
                // a silent value transfer to the other seats instead of a loud error.
                if (place < 1 || place > 4)
                    throw new ArgumentException($"finalPlaces[{seat}]={place} out of range; expected 0 (absent) or 1..4", nameof(finalPlaces));

                int percent = (int)(choices[seat] ?? DefaultChoice);
                stakes[seat] = coins[seat] * percent / 100;
                pot += stakes[seat];
            }

            // Active seats ordered by place (stable by seat index) → table positions.
            Span<int> order = stackalloc int[4];
            int activeCount = 0;
            for (int place = 1; place <= 4; place++)
                for (int seat = 0; seat < 4; seat++)
                    if (finalPlaces[seat] == place)
                        order[activeCount++] = seat;

            if (activeCount > 0)
            {
                int totalPercent = 0;
                for (int pos = 0; pos < activeCount; pos++)
                    totalPercent += PotSplitPercent[pos];

                // Ideal share per seat: tied seats pool the percentages of the
                // positions they span and split evenly.
                Span<double> ideal = stackalloc double[4];
                int start = 0;
                while (start < activeCount)
                {
                    int place = finalPlaces[order[start]];
                    int end = start;
                    int groupPercent = 0;
                    while (end < activeCount && finalPlaces[order[end]] == place)
                    {
                        groupPercent += PotSplitPercent[end];
                        end++;
                    }
                    int groupSize = end - start;
                    for (int pos = start; pos < end; pos++)
                        ideal[order[pos]] = (double)pot * groupPercent / (totalPercent * groupSize);
                    start = end;
                }

                int distributed = 0;
                for (int seat = 0; seat < 4; seat++)
                {
                    shares[seat] = (int)ideal[seat]; // floor (ideal >= 0)
                    distributed += shares[seat];
                }

                // Remainders from the bottom up until the whole pot is out.
                int remainder = pot - distributed;
                while (remainder > 0)
                    for (int pos = activeCount - 1; pos >= 0 && remainder > 0; pos--)
                    {
                        shares[order[pos]]++;
                        remainder--;
                    }
            }

            for (int seat = 0; seat < 4; seat++)
                if (finalPlaces[seat] >= 1)
                    after[seat] = coins[seat] - stakes[seat] + shares[seat];

            return new WagerResult(pot, stakes, shares, after);
        }
    }
}
