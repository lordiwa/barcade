namespace Barcade.Core.Dodge
{
    /// <summary>
    /// Top-level play state for the Esquiva 3D dodge simulation.
    /// </summary>
    public enum DodgeState
    {
        Playing,
        Won,
        Lost,
    }

    /// <summary>
    /// Reason the simulation entered <see cref="DodgeState.Lost"/>.
    /// <see cref="None"/> when the game has not been lost.
    /// </summary>
    public enum LostReason
    {
        None,
        /// <summary>Player stepped onto a collapsed or out-of-bounds tile.</summary>
        Fell,
        /// <summary>An obstacle touched the player (distance &lt;= contactRadius).</summary>
        Caught,
    }
}
