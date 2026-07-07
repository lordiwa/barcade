namespace Barcade.Core.Scoring
{
    /// <summary>
    /// The resolved FINAL_WAGER (GDD §6.2): what everyone staked, how the pot
    /// split 50/30/15/5 by final-microgame place, and the resulting coin totals.
    /// Plain data for the podium presentation and telemetry.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class WagerResult
    {
        /// <summary>Total coins staked by all players.</summary>
        public readonly int Pot;

        /// <summary>Coins each seat put in (0 for absent seats).</summary>
        public readonly int[] Stakes;

        /// <summary>Coins each seat took back out of the pot. Sums exactly to <see cref="Pot"/>.</summary>
        public readonly int[] Shares;

        /// <summary>Post-wager coin totals per seat.</summary>
        public readonly int[] CoinsAfter;

        public WagerResult(int pot, int[] stakes, int[] shares, int[] coinsAfter)
        {
            Pot = pot;
            Stakes = stakes;
            Shares = shares;
            CoinsAfter = coinsAfter;
        }
    }
}
