namespace Barcade.Core.Board
{
    /// <summary>
    /// Annex D.2's literal <c>BoardSnapshot</c> — positions, saldos (balances),
    /// dueños (owners), armas (weapons). A reused mutable instance, same reference
    /// every <see cref="IBoardModel.GetSnapshot"/> call with fields overwritten in
    /// place — same "instancia doble-buffer, campos mutables reutilizados (cero
    /// alloc)" convention as <c>Microgames.V2.RenderState</c>, since this is read
    /// every simulation tick during BOARD_MOVE (to render the live stop-meters),
    /// not just once at the end of a phase.
    /// </summary>
    public sealed class BoardSnapshot
    {
        /// <summary>Length = ring size. A private copy, safe against external mutation of the model's own live ring.</summary>
        public readonly BoardTileType[] Ring;

        /// <summary>Per seat, current ring index (0..RingSize-1).</summary>
        public readonly int[] Positions;

        /// <summary>Per seat, current coin balance.</summary>
        public readonly int[] Balances;

        /// <summary>Per seat, the stop-meter's current value (1..6) — live/cycling while unfrozen, frozen once tapped or timed out.</summary>
        public readonly int[] MeterValues;

        /// <summary>Per seat, whether its stop-meter has frozen this move phase.</summary>
        public readonly bool[] MeterFrozen;

        /// <summary>Per ring index: owning seat (0..3), or -1 if unowned/not a Propiedad square.</summary>
        public readonly int[] PropertyOwner;

        /// <summary>Per ring index: current Inversión owner (highest cumulative depositor, ties by first-by-round), or -1 if none/not an Inversión square.</summary>
        public readonly int[] InversionOwner;

        /// <summary>Per seat, the single weapon currently held (GDD §5.4: max 1 in hand), or null.</summary>
        public readonly WeaponKind?[] Weapons;

        /// <summary>The ring index the (single, mobile) Estrella square currently occupies.</summary>
        public int StarSquareIndex;

        public BoardSnapshot(int ringSize)
        {
            Ring = new BoardTileType[ringSize];
            Positions = new int[4];
            Balances = new int[4];
            MeterValues = new int[4];
            MeterFrozen = new bool[4];
            PropertyOwner = new int[ringSize];
            InversionOwner = new int[ringSize];
            Weapons = new WeaponKind?[4];
        }
    }
}
