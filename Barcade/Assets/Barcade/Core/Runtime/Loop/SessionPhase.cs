namespace Barcade.Core
{
    /// <summary>
    /// The full session FSM (GDD §2.1). <see cref="SessionStateMachine"/> implements
    /// every one of these states and transitions.
    ///
    /// <c>MgIntro</c>/<c>MgPlay</c>/<c>MgResult</c>/<c>Intermission</c> are the GDD
    /// names for exactly the same sub-loop <see cref="RoundPhaseMachine"/> already
    /// models as <c>CommandShow</c>/<c>Play</c>/<c>Result</c>/<c>Intermission</c> —
    /// <see cref="SessionStateMachine"/> wraps a <see cref="RoundPhaseMachine"/>
    /// internally for that sub-loop rather than re-implementing its timing (see
    /// SessionStateMachine's class doc for exactly how).
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner. C# 9 compatible.
    /// </summary>
    public enum SessionPhase
    {
        /// <summary>Attract-loop demo. No timeout — loops until <see cref="SessionStateMachine.InsertCredit"/>.</summary>
        Attract,

        /// <summary>Color-claim: each seat presses once to join. GDD §2.1: &gt;=2 ready or 30s timeout, whichever first.</summary>
        Join,

        /// <summary>Board movement — GDD §5.2's medidor de parada. TASK-068 wires the real BoardModel here (up to the §2.2 8s timeout); see SessionStateMachine's class doc.</summary>
        BoardMove,

        /// <summary>Board tile resolution, including the &lt;=4s Inversión deposit-choice window (GDD §5.3). TASK-068 wires the real BoardModel here; see SessionStateMachine's class doc.</summary>
        BoardResolve,

        /// <summary>The giant imperative verb. GDD §2.2: 0.8s fixed.</summary>
        MgIntro,

        /// <summary>The active microgame runs. Only this state forwards gameplay input to it (GDD §2.1 invariant).</summary>
        MgPlay,

        /// <summary>Per-player result reveal. GDD §2.2: 1.5s fixed.</summary>
        MgResult,

        /// <summary>Rhythmic breather between rounds. GDD §2.2: 2s fixed.</summary>
        Intermission,

        /// <summary>Final pot wager (25/50/75%). GDD §6.2: 5s fixed.</summary>
        FinalWager,

        /// <summary>The climax microgame (GDD §9.1: D_final = 1.5x speed).</summary>
        FinalMg,

        /// <summary>Bonus stars + podium. GDD §2.1 diagram: 20s timeout back to Attract.</summary>
        GameOver,
    }
}
