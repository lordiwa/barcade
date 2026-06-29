namespace Barcade.Core.Runner
{
    /// <summary>Top-level play state for the Endless Run simulation.</summary>
    public enum RunnerState
    {
        Playing,
        Lost,
    }

    /// <summary>
    /// Reason the simulation entered <see cref="RunnerState.Lost"/>.
    /// <see cref="None"/> while the game is in progress.
    /// </summary>
    public enum RunnerLostReason
    {
        None,
        /// <summary>Runner walked into an obstacle without the correct evasion action.</summary>
        HitObstacle,
    }

    /// <summary>What a given track spawn represents.</summary>
    public enum SpawnKind
    {
        /// <summary>Low obstacle on the ground — runner must be airborne (jumping) to pass.</summary>
        LowObstacle,
        /// <summary>High overhead obstacle — runner must be sliding (ducking) to pass.</summary>
        HighObstacle,
        /// <summary>Collectible coin — grabbed by passing through the same lane.</summary>
        Coin,
    }

    /// <summary>
    /// A single upcoming spawn (obstacle or coin) on the track.
    /// Exposed via <see cref="RunnerSim.ActiveSpawns"/> so the view can build and
    /// cull visual representations each frame without owning the game rules.
    /// </summary>
    public readonly struct SpawnInfo
    {
        /// <summary>Forward track distance at which this spawn sits.</summary>
        public readonly float     DistancePos;
        /// <summary>Lane index in [0, laneCount).</summary>
        public readonly int       Lane;
        /// <summary>What this spawn is.</summary>
        public readonly SpawnKind Kind;

        public SpawnInfo(float distancePos, int lane, SpawnKind kind)
        {
            DistancePos = distancePos;
            Lane        = lane;
            Kind        = kind;
        }
    }
}
