namespace Barcade.Core.Board
{
    /// <summary>Annex D.2's literal <c>BoardResolution</c> — the outcome of one <see cref="IBoardModel.Resolve"/> call.</summary>
    public readonly struct BoardResolution
    {
        /// <summary>Every coin movement this resolution caused, origin→destination (GDD §5.3 invariant — always a visible pair).</summary>
        public readonly CoinDelta[] CoinFlows;

        /// <summary>Stars awarded this resolution (Estrella purchase, Inversión owner payout).</summary>
        public readonly StarEvent[] Stars;

        /// <summary>The round modifier drawn from an Evento square this round, or <see cref="RoundModifier.None"/>.</summary>
        public readonly RoundModifier Modifier;

        public BoardResolution(CoinDelta[] coinFlows, StarEvent[] stars, RoundModifier modifier)
        {
            CoinFlows = coinFlows;
            Stars = stars;
            Modifier = modifier;
        }
    }
}
