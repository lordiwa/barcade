namespace Barcade.Core
{
    /// <summary>
    /// Read-only context passed to a microgame at Prepare time.
    /// Exposes the seeded RNG and the set of participating player slots.
    /// No UnityEngine dependency — safe for dotnet fast-test runner.
    /// </summary>
    public interface IMicrogameContext
    {
        /// <summary>Deterministic RNG seeded per-round by the sequencer.</summary>
        ISeededRandom Rng { get; }

        /// <summary>
        /// The set of player slots participating in this round.
        /// For a standard 4-player barcade session this is all four slots.
        /// </summary>
        PlayerSlot[] Players { get; }
    }
}
