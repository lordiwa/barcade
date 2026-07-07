namespace Barcade.Core.Board
{
    /// <summary>
    /// A single coin movement, always with a visible origin AND destination (GDD
    /// §5.3 economic invariant: "todo efecto que quita monedas a un jugador se las
    /// da a otro jugador o a un pozo visible — nunca desaparecen 'al banco' sin
    /// representación visual"). <see cref="Bank"/> is that visible pot: the source
    /// of square income (Moneda+) and the sink for purchases/penalties (§5.6
    /// "sumideros": estrella, propiedad, inversión) — never a silent void. Every
    /// amount is strictly positive; a zero-amount effect (e.g. choosing not to
    /// deposit) emits no <see cref="CoinDelta"/> at all.
    /// </summary>
    public readonly struct CoinDelta
    {
        /// <summary>The visible pot/sink — never a player seat (0..3 are reserved for those).</summary>
        public const int Bank = -1;

        public readonly int Origin;
        public readonly int Destination;
        public readonly int Amount;

        public CoinDelta(int origin, int destination, int amount)
        {
            Origin = origin;
            Destination = destination;
            Amount = amount;
        }
    }
}
