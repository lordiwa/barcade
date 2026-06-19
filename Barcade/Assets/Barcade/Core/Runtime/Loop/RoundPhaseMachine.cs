using System;

namespace Barcade.Core
{
    /// <summary>
    /// Pure deterministic state machine that drives per-round timing for the
    /// microgame loop.
    ///
    /// Phase order each round:
    ///   Intermission -> CommandShow -> Play -> Result
    ///
    /// Usage per round:
    /// <code>
    ///   // At the start of each round supply the verb and the effective play duration
    ///   // from SequencerDirector.CurrentRoundContext.EffectiveDuration:
    ///   machine.StartRound(descriptor.VerbText, ctx.EffectiveDuration);
    ///
    ///   // Every frame:
    ///   machine.Tick(Time.deltaTime);
    ///   if (machine.PhaseChangedThisTick)
    ///       UpdateViews(machine.CurrentPhase, machine.VerbText);
    ///
    ///   if (machine.IsRoundComplete)
    ///       // advance to the next round
    /// </code>
    ///
    /// Configurable durations:
    ///   <see cref="IntermissionDuration"/> — buffer between games (default: 2 s).
    ///   <see cref="CommandShowDuration"/>  — giant-verb display window (default: 1 s).
    ///   <see cref="ResultDuration"/>       — result flash window (default: 1.5 s).
    ///   Play duration is supplied per-round via <see cref="StartRound"/>.
    ///
    /// Zero / negative configured durations are clamped to 0 and the phase is
    /// skipped automatically on the next <see cref="Tick"/> call (even Tick(0)).
    ///
    /// Thread-safety: single-threaded game loop use only.
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class RoundPhaseMachine
    {
        // ── Configured durations ─────────────────────────────────────────────────

        /// <summary>Duration of the Intermission phase in seconds (clamped to >= 0).</summary>
        public float IntermissionDuration { get; }

        /// <summary>Duration of the CommandShow phase in seconds (clamped to >= 0).</summary>
        public float CommandShowDuration { get; }

        /// <summary>Duration of the Result phase in seconds (clamped to >= 0).</summary>
        public float ResultDuration { get; }

        // ── Per-round state ───────────────────────────────────────────────────────

        private PhaseKind _currentPhase;
        private float     _phaseElapsed;
        private float     _playDuration;
        private string    _verbText;
        private bool      _phaseChangedThisTick;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new <see cref="RoundPhaseMachine"/> with the specified durations.
        /// Negative durations are clamped to 0 and treated the same as zero-duration
        /// (the phase is skipped immediately).
        /// </summary>
        /// <param name="intermissionDuration">
        /// Duration of the Intermission phase in seconds. Default 2.0 s.
        /// </param>
        /// <param name="commandShowDuration">
        /// Duration of the CommandShow (giant verb) phase in seconds. Default 1.0 s.
        /// </param>
        /// <param name="resultDuration">
        /// Duration of the Result display phase in seconds. Default 1.5 s.
        /// </param>
        public RoundPhaseMachine(
            float intermissionDuration = 2.0f,
            float commandShowDuration  = 1.0f,
            float resultDuration       = 1.5f)
        {
            IntermissionDuration = Math.Max(0f, intermissionDuration);
            CommandShowDuration  = Math.Max(0f, commandShowDuration);
            ResultDuration       = Math.Max(0f, resultDuration);

            _currentPhase        = PhaseKind.Intermission;
            _phaseElapsed        = 0f;
            _playDuration        = 0f;
            _verbText            = string.Empty;
            _phaseChangedThisTick = false;
        }

        // ── Public read-only API ─────────────────────────────────────────────────

        /// <summary>The phase the machine is currently in.</summary>
        public PhaseKind CurrentPhase => _currentPhase;

        /// <summary>
        /// The imperative verb text set by the most recent <see cref="StartRound"/> call.
        /// Available throughout all phases so views can display it at any point.
        /// </summary>
        public string VerbText => _verbText;

        /// <summary>
        /// True if the current <see cref="Tick"/> caused at least one phase transition.
        /// Resets to false at the start of every <see cref="Tick"/> call, so callers
        /// must read it after each Tick while they care about the flag.
        /// </summary>
        public bool PhaseChangedThisTick => _phaseChangedThisTick;

        /// <summary>
        /// True when the machine is in the <see cref="PhaseKind.Result"/> phase
        /// (the terminal phase of a round). The caller should call
        /// <see cref="StartRound"/> to begin the next round.
        /// </summary>
        public bool IsRoundComplete => _currentPhase == PhaseKind.Result;

        // ── Control API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Begins a new round: resets elapsed time, stores the verb text and
        /// play duration, and sets the current phase to
        /// <see cref="PhaseKind.Intermission"/>.
        ///
        /// Call this once at the start of each round (after
        /// <see cref="SequencerDirector.PickNext"/> has been called so you have
        /// both the verb text and the effective play duration from
        /// <see cref="SequencerRoundContext.EffectiveDuration"/>).
        /// </summary>
        /// <param name="verbText">
        /// The imperative verb to show during CommandShow (e.g. "¡ESQUIVA!").
        /// Must not be null.
        /// </param>
        /// <param name="playDuration">
        /// The effective play duration in seconds for this round (from
        /// <see cref="SequencerRoundContext.EffectiveDuration"/>).
        /// Values &lt;= 0 are treated as 0.
        /// </param>
        public void StartRound(string verbText, float playDuration)
        {
            if (verbText == null) throw new ArgumentNullException("verbText");

            _verbText     = verbText;
            _playDuration = Math.Max(0f, playDuration);
            _phaseElapsed = 0f;
            _currentPhase = PhaseKind.Intermission;
            _phaseChangedThisTick = false;
        }

        /// <summary>
        /// Advances the phase machine by <paramref name="deltaTime"/> seconds.
        ///
        /// On each call the machine accumulates elapsed time for the current phase.
        /// When elapsed time meets or exceeds the current phase's duration the machine
        /// transitions to the next phase, carrying any remaining delta time forward
        /// so that a large delta can skip multiple phases in one call.
        ///
        /// The <see cref="PhaseChangedThisTick"/> flag is set to true whenever at
        /// least one transition occurred during this call; it is reset to false at
        /// the start of the call.
        ///
        /// <see cref="PhaseKind.Result"/> is a terminal phase: the machine stays
        /// there regardless of further Tick calls.
        /// </summary>
        /// <param name="deltaTime">
        /// Seconds elapsed since the previous Tick. 0 is valid (processes zero-duration
        /// phase transitions). Negative values are treated as 0.
        /// </param>
        public void Tick(float deltaTime)
        {
            _phaseChangedThisTick = false;

            float remaining = Math.Max(0f, deltaTime);

            // Drain remaining time, advancing phases until Result (terminal) or
            // until time is exhausted inside the current phase.
            while (true)
            {
                // Result is terminal — never advance further.
                if (_currentPhase == PhaseKind.Result)
                    break;

                float phaseDuration = DurationFor(_currentPhase);

                // Accumulate time into the current phase.
                _phaseElapsed += remaining;

                if (_phaseElapsed >= phaseDuration)
                {
                    // Time left after this phase elapses — carry forward.
                    remaining     = _phaseElapsed - phaseDuration;
                    _phaseElapsed = 0f;

                    // Advance to the next phase.
                    _currentPhase         = NextPhase(_currentPhase);
                    _phaseChangedThisTick = true;

                    // Continue the loop to consume any leftover time.
                }
                else
                {
                    // Still within the current phase — stop.
                    break;
                }
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private float DurationFor(PhaseKind phase)
        {
            switch (phase)
            {
                case PhaseKind.Intermission: return IntermissionDuration;
                case PhaseKind.CommandShow:  return CommandShowDuration;
                case PhaseKind.Play:         return _playDuration;
                case PhaseKind.Result:       return float.MaxValue; // terminal — never expires
                default:
                    throw new InvalidOperationException("Unknown PhaseKind: " + phase);
            }
        }

        private static PhaseKind NextPhase(PhaseKind phase)
        {
            switch (phase)
            {
                case PhaseKind.Intermission: return PhaseKind.CommandShow;
                case PhaseKind.CommandShow:  return PhaseKind.Play;
                case PhaseKind.Play:         return PhaseKind.Result;
                default:
                    return PhaseKind.Result;
            }
        }
    }
}
