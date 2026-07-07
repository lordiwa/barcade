namespace Barcade.Core
{
    /// <summary>
    /// Timing/shape configuration for <see cref="SessionStateMachine"/>. Defaults
    /// come straight from the GDD: the §2.2 budget table, the §2.1 graph
    /// annotations (JOIN 30 s, GAME_OVER 20 s), and the §9.3 reference timeline
    /// (7 rounds). Eventually these become <c>GameTuning</c> data (GDD §11.3);
    /// until then this POCO is the single place they live in code.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class SessionConfig
    {
        /// <summary>Number of board+microgame rounds before FINAL_WAGER (GDD §9.3 reference timeline).</summary>
        public int RoundsTotal = 7;

        /// <summary>JOIN color-claim window in seconds (GDD §2.1: "timeout 30 s").</summary>
        public float JoinSeconds = 30f;

        /// <summary>MG_INTRO verb display in seconds (GDD §2.2: 0.8 s fixed).</summary>
        public float MgIntroSeconds = 0.8f;

        /// <summary>
        /// MG_PLAY duration in seconds when no round has been staged
        /// (GDD §2.2 target window is 3–5 s; the staged definition normally supplies this).
        /// </summary>
        public float DefaultMgPlaySeconds = 5f;

        /// <summary>MG_PLAY hard maximum in seconds (GDD §2.2: 8 s, survival mechanics). Liveness backstop.</summary>
        public float MgPlayMaxSeconds = 8f;

        /// <summary>MG_RESULT payout display in seconds (GDD §2.2: 1.5 s fixed).</summary>
        public float MgResultSeconds = 1.5f;

        /// <summary>INTERMISSION breather in seconds (GDD §2.2: 2 s fixed).</summary>
        public float IntermissionSeconds = 2f;

        /// <summary>
        /// FINAL_WAGER window in seconds. [ASSUMED] — the GDD §2.2 budget table has
        /// no FINAL_WAGER row, but the §2.1 invariant requires every state to time
        /// out; 10 s comfortably fits a 4-way bet confirmation. Recalibrate with
        /// T-109 (wager mechanics) / pilot telemetry.
        /// </summary>
        public float FinalWagerSeconds = 10f;

        /// <summary>GAME_OVER podium display in seconds before returning to ATTRACT (GDD §2.1: 20 s).</summary>
        public float GameOverSeconds = 20f;
    }
}
