namespace Barcade.Core.Scoring
{
    /// <summary>
    /// The outcome of the Game Over bonus-star reveal (GDD §6.3): which two
    /// distinct stars were drawn, who holds each (-1 = tied/no holder), and the
    /// resulting star totals. Plain data for the podium reveal sequence.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class BonusStarResult
    {
        public readonly StarKind First;
        public readonly StarKind Second;

        /// <summary>Seat holding <see cref="First"/>, or -1 when tied/no holder.</summary>
        public readonly int FirstWinner;

        /// <summary>Seat holding <see cref="Second"/>, or -1 when tied/no holder.</summary>
        public readonly int SecondWinner;

        /// <summary>Base stars plus the awarded bonus stars, per seat.</summary>
        public readonly int[] StarsAfter;

        public BonusStarResult(StarKind first, StarKind second, int firstWinner, int secondWinner, int[] starsAfter)
        {
            First = first;
            Second = second;
            FirstWinner = firstWinner;
            SecondWinner = secondWinner;
            StarsAfter = starsAfter;
        }
    }
}
