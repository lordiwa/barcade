namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// The occupancy state of one physical seat for a round (GDD Annex D.2).
    /// Distinct from <see cref="Barcade.Core.ButtonState"/> — this describes who
    /// (if anyone) is sitting at the seat, not button/press state.
    /// </summary>
    public enum SeatState
    {
        Empty = 0,
        Human = 1,
        HumanIdle = 2,
        Bot = 3,
    }
}
