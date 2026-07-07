namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// One seat's placement in a <see cref="ResultKind.Ranked"/> <see cref="MicrogameResult"/>
    /// (GDD Annex D.2). Exact ties share the same <see cref="Place"/> — no sudden death
    /// inside a microjuego (the board absorbs ties, GDD §2.1).
    /// </summary>
    public readonly struct PlayerRank
    {
        /// <summary>Seat index, 0..3 — same convention as <c>(int)PlayerSlot</c>.</summary>
        public readonly int Seat;

        /// <summary>
        /// Finishing position, 1..(active seat count). Ties share a place and the
        /// next distinct group skips ahead (e.g. two seats tied for 1st means the
        /// next seat is placed 3rd, not 2nd) — standard competition ranking.
        /// </summary>
        public readonly int Place;

        /// <summary>
        /// Mechanic-defined tiebreak/telemetry value (survival ticks, hits, latency…).
        /// For ¡REACCIONA! this is the cumulative latency-or-penalty tick sum used to
        /// compute <see cref="Place"/> — see <c>ReaccionaMicrogame</c>.
        /// </summary>
        public readonly int Metric;

        public PlayerRank(int seat, int place, int metric)
        {
            Seat = seat;
            Place = place;
            Metric = metric;
        }
    }
}
