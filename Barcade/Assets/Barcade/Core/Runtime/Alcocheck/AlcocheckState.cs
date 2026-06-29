namespace Barcade.Core.Alcocheck
{
    /// <summary>Top-level play state for the Alcocheck drunk-balance simulation.</summary>
    public enum AlcocheckState
    {
        Playing,
        Won,
        Lost,
    }

    /// <summary>
    /// Reason the simulation entered <see cref="AlcocheckState.Lost"/>.
    /// <see cref="None"/> when the game has not been lost.
    /// </summary>
    public enum AlcocheckLostReason
    {
        None,
        /// <summary>Lean magnitude reached or exceeded maxLean (avatar toppled over).</summary>
        ToppledOver,
    }
}
