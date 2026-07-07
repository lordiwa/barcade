namespace Barcade.Core
{
    /// <summary>
    /// The eight compass directions of a digital 8-way stick, plus a neutral
    /// (None) state. Matches the GDD §3.2 <c>PlayerInput.Stick</c> listing.
    ///
    /// Distinct from <see cref="CardinalDir"/>, which models the 4-way result
    /// consumers get after diagonal collapse (see
    /// <see cref="InputInterpreter.CollapseDiagonal"/>).
    /// </summary>
    public enum Direction8
    {
        None = 0,
        N    = 1,
        NE   = 2,
        E    = 3,
        SE   = 4,
        S    = 5,
        SW   = 6,
        W    = 7,
        NW   = 8,
    }
}
