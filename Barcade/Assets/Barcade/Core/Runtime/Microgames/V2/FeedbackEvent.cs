namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// One "juice" event raised on a single tick (GDD Annex D.2 / §8.2). <see cref="Cue"/>
    /// is a mechanic-defined code (analogous to <see cref="RenderEntity.VisualVariant"/>) —
    /// each <see cref="Barcade.Core.Microgames.V2.IMicrogame"/> implementation documents
    /// the meaning of its own cue values.
    /// </summary>
    public readonly struct FeedbackEvent
    {
        /// <summary>Seat index 0..3, or -1 if the event is global (not owned by one seat).</summary>
        public readonly int Seat;

        public readonly FeedbackLevel Level;

        /// <summary>Mechanic-defined event code.</summary>
        public readonly byte Cue;

        public FeedbackEvent(int seat, FeedbackLevel level, byte cue)
        {
            Seat = seat;
            Level = level;
            Cue = cue;
        }
    }
}
