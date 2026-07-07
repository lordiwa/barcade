namespace Barcade.Core
{
    /// <summary>
    /// The states of the full session FSM (GDD §2.1), in graph order:
    ///
    ///   Attract -> Join -> (BoardMove -> BoardResolve -> MgIntro -> MgPlay ->
    ///   MgResult -> Intermission)* -> FinalWager -> FinalMg -> GameOver -> Attract
    ///
    /// Driven by <see cref="SessionStateMachine"/>. The per-round MG sub-phases
    /// map onto the wrapped <see cref="RoundPhaseMachine"/>'s <see cref="PhaseKind"/>
    /// (MgIntro = CommandShow, MgPlay = Play) — see the machine's doc for the
    /// exact mapping.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public enum SessionState
    {
        /// <summary>Idle demo loop. Exits on a credit (or free-play button press). The rest state — no timeout.</summary>
        Attract,

        /// <summary>Color claim: each press joins a seat. 30 s window (GDD §2.1); needs >= 2 players to start.</summary>
        Join,

        /// <summary>Simultaneous board movement (GDD §5.2). Pass-through stub (&lt;= 1 tick) until BoardModel lands (Hito 4).</summary>
        BoardMove,

        /// <summary>Board tile effects (GDD §5.3). Pass-through stub (&lt;= 1 tick) until BoardModel lands (Hito 4).</summary>
        BoardResolve,

        /// <summary>Imperative verb display, 0.8 s fixed (GDD §2.2). Maps to <see cref="PhaseKind.CommandShow"/>.</summary>
        MgIntro,

        /// <summary>The microgame runs. The ONLY state (with <see cref="FinalMg"/>) that forwards gameplay input (GDD §2.1 invariant).</summary>
        MgPlay,

        /// <summary>Payout display, 1.5 s fixed (GDD §2.2).</summary>
        MgResult,

        /// <summary>Rhythmic breather, 2 s fixed (GDD §2.2). Round counter advances on exit.</summary>
        Intermission,

        /// <summary>Pot wager before the climax microgame (GDD §6.2). Wager input wiring lands with T-109.</summary>
        FinalWager,

        /// <summary>The climax microgame (GDD §2.1). A play state: forwards gameplay input like <see cref="MgPlay"/>.</summary>
        FinalMg,

        /// <summary>Bonus stars + podium (GDD §6.3). Returns to <see cref="Attract"/> after 20 s.</summary>
        GameOver,
    }
}
