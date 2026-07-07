namespace Barcade.Core.Scoring
{
    /// <summary>
    /// The six hidden-objective bonus stars of GDD §6.3, revealed (2 of 6, seeded
    /// draw) at Game Over. Each is worth 1 normal star. The tracked metric lives
    /// in <see cref="SessionCounters"/>.
    ///
    /// Values are part of the replay/telemetry vocabulary — append, never renumber.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public enum StarKind
    {
        /// <summary>Most times eliminated in microgames (max wins).</summary>
        Kamikaze = 0,

        /// <summary>Most weapons used (max wins).</summary>
        Cangreja = 1,

        /// <summary>
        /// [ASSUMED] Min-direction metric. The GDD §6.3 table row is truncated at
        /// the source ("| Estrella Zen | Menor |") — the direction ("Menor")
        /// survived but the metric name did not; likely "least movement/input".
        /// The counter is generic (<see cref="SessionCounters.RecordZenMetric"/>)
        /// so gameplay decides what it accumulates. Needs human calibration.
        /// </summary>
        Zen = 2,

        /// <summary>Best (lowest) mean ¡REACCIONA! latency (min wins; requires at least one sample).</summary>
        Gatillo = 3,

        /// <summary>Most coins deposited into Inversión tiles (§5.3) (max wins).</summary>
        Inversora = 4,

        /// <summary>Fewest coins robbed by rivals (min wins).</summary>
        Fantasma = 5,
    }
}
