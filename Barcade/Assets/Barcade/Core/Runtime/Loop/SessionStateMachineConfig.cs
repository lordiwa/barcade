using System;

namespace Barcade.Core
{
    /// <summary>
    /// Constructor-injected timeouts for <see cref="SessionStateMachine"/> (GDD §2.1
    /// state graph, §2.2 timeout budget table). Where the GDD gives an exact number
    /// it is used exactly; where it is silent the default is tagged [ASSUMED] below
    /// and pinned by a dedicated test — see <c>SessionStateMachineTests</c>.
    /// </summary>
    public readonly struct SessionStateMachineConfig
    {
        /// <summary>Join's own timeout, in seconds (GDD §2.1: "timeout 30 s").</summary>
        public readonly float JoinTimeoutSeconds;

        /// <summary>
        /// GDD §2.1's own documented minimum ("&gt;=2 jugadores listos"). As of the
        /// TASK-024 review fix round (MEDIUM-1, [ASSUMED] orchestrator ruling) this
        /// no longer closes Join the instant it's reached — Join stays open for
        /// the rest of its <see cref="JoinTimeoutSeconds"/> window so seats 3/4 can
        /// still claim, exiting early only once every seat has claimed. Kept as a
        /// field for GDD traceability and potential future use (e.g. a presenter
        /// "ready to start" indicator); it no longer drives
        /// <see cref="SessionStateMachine"/>'s own exit condition, which now checks
        /// all-4-claimed OR <see cref="JoinTimeoutSeconds"/> only.
        /// </summary>
        public readonly int JoinMinReady;

        /// <summary>MgIntro's fixed duration, in seconds (GDD §2.2 table: 0.8s, "fijo").</summary>
        public readonly float MgIntroSeconds;

        /// <summary>MgResult's fixed duration, in seconds (GDD §2.2 table: 1.5s, "fijo").</summary>
        public readonly float MgResultSeconds;

        /// <summary>Intermission's fixed duration, in seconds (GDD §2.2 table: 2s, "fijo").</summary>
        public readonly float IntermissionSeconds;

        /// <summary>FinalWager's fixed duration, in seconds (GDD §6.2: "elección con palanca, 5 s").</summary>
        public readonly float FinalWagerSeconds;

        /// <summary>
        /// GameOver's fixed duration before looping back to Attract, in seconds
        /// (GDD §2.1 diagram: "timeout 20 s").
        /// </summary>
        public readonly float GameOverSeconds;

        /// <summary>
        /// [ASSUMED] Number of round-loop iterations (BoardMove..Intermission) before
        /// exiting to FinalWager. Not stated in §2.1/§2.2 (those describe a single
        /// round's sub-phases and the loop shape, not how many iterations); GDD
        /// Annex B's parameter table lists "session.rounds": 7 (range 5-8) for the
        /// session as a whole, which this reuses as the closest available number.
        /// </summary>
        public readonly int TotalRounds;

        /// <summary>Fixed simulation tick rate (GDD §3.2: 60 Hz), used to translate every *Seconds field into ticks.</summary>
        public readonly int TicksPerSecond;

        public SessionStateMachineConfig(
            float joinTimeoutSeconds,
            int joinMinReady,
            float mgIntroSeconds,
            float mgResultSeconds,
            float intermissionSeconds,
            float finalWagerSeconds,
            float gameOverSeconds,
            int totalRounds,
            int ticksPerSecond)
        {
            if (joinTimeoutSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(joinTimeoutSeconds), "must be positive.");
            if (joinMinReady < 1) throw new ArgumentOutOfRangeException(nameof(joinMinReady), "must be at least 1.");
            if (mgIntroSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(mgIntroSeconds), "must be non-negative.");
            if (mgResultSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(mgResultSeconds), "must be non-negative.");
            if (intermissionSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(intermissionSeconds), "must be non-negative.");
            if (finalWagerSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(finalWagerSeconds), "must be non-negative.");
            if (gameOverSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(gameOverSeconds), "must be positive.");
            if (totalRounds < 1) throw new ArgumentOutOfRangeException(nameof(totalRounds), "must be at least 1.");
            if (ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), "must be positive.");

            JoinTimeoutSeconds = joinTimeoutSeconds;
            JoinMinReady = joinMinReady;
            MgIntroSeconds = mgIntroSeconds;
            MgResultSeconds = mgResultSeconds;
            IntermissionSeconds = intermissionSeconds;
            FinalWagerSeconds = finalWagerSeconds;
            GameOverSeconds = gameOverSeconds;
            TotalRounds = totalRounds;
            TicksPerSecond = ticksPerSecond;
        }

        /// <summary>GDD §2.1/§2.2/§6.2 calibration — see each field's own doc comment for its exact source.</summary>
        public static SessionStateMachineConfig GddDefaults => new SessionStateMachineConfig(
            joinTimeoutSeconds: 30f,
            joinMinReady: 2,
            mgIntroSeconds: 0.8f,
            mgResultSeconds: 1.5f,
            intermissionSeconds: 2f,
            finalWagerSeconds: 5f,
            gameOverSeconds: 20f,
            totalRounds: 7,
            ticksPerSecond: 60);
    }
}
