namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// The final outcome of a v2 microgame round (GDD Annex D.2). Distinct from
    /// <see cref="Barcade.Core.MicrogameResult"/> (the v1 win/lose class) — see the
    /// v2 <see cref="IMicrogame"/> XML doc for why the two contracts coexist.
    /// </summary>
    public readonly struct MicrogameResult
    {
        public readonly ResultKind Kind;

        /// <summary>Empty for coop results.</summary>
        public readonly PlayerRank[] Ranks;

        /// <summary>0 for ranked results.</summary>
        public readonly int CoopScore;

        public MicrogameResult(ResultKind kind, PlayerRank[] ranks, int coopScore)
        {
            Kind = kind;
            Ranks = ranks;
            CoopScore = coopScore;
        }
    }
}
