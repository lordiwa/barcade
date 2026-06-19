namespace Barcade.Core
{
    /// <summary>
    /// The phases a single microgame round travels through, in order.
    ///
    /// Intermission -> CommandShow -> Play -> Result
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public enum PhaseKind
    {
        /// <summary>
        /// Buffer between microgames. Duration is configurable via
        /// <see cref="RoundPhaseMachine.IntermissionDuration"/>.
        /// The Framework's IntermissionView is visible during this phase.
        /// </summary>
        Intermission,

        /// <summary>
        /// The giant imperative verb (e.g. "¡ESQUIVA!") is shown to all players
        /// for a short fixed window (~1 s). Duration is configurable via
        /// <see cref="RoundPhaseMachine.CommandShowDuration"/>.
        /// The Framework's CommandDisplayView is visible during this phase.
        /// </summary>
        CommandShow,

        /// <summary>
        /// The microgame is actively running. Duration equals the effective play
        /// duration supplied by SequencerDirector to <see cref="RoundPhaseMachine.StartRound"/>.
        /// </summary>
        Play,

        /// <summary>
        /// The round has ended and per-player results are being shown.
        /// This is a terminal phase — the machine stays here until the next
        /// <see cref="RoundPhaseMachine.StartRound"/> call.
        /// Duration is configurable via <see cref="RoundPhaseMachine.ResultDuration"/>.
        /// </summary>
        Result
    }
}
