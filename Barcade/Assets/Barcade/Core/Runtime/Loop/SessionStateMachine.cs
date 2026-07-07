using System;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.Core
{
    /// <summary>
    /// The full session FSM of GDD §2.1 (T-102):
    ///
    ///   ATTRACT -> JOIN -> (BOARD_MOVE -> BOARD_RESOLVE -> MG_INTRO -> MG_PLAY ->
    ///   MG_RESULT -> INTERMISSION)* -> FINAL_WAGER -> FINAL_MG -> GAME_OVER -> ATTRACT
    ///
    /// Ticked at the fixed 60 Hz simulation rate with the session-level v2
    /// <see cref="Barcade.Core.Microgames.V2.InputSnapshot"/>. Deterministic:
    /// identical (construction args, InsertCredit ticks, input sequence) produce
    /// an identical per-tick state trace (<see cref="Capture"/>), which is the
    /// §13 replay contract. <see cref="InsertCredit"/> is itself an input event —
    /// a replay must re-issue it on the same tick.
    ///
    /// <para>
    /// <b>Wraps <see cref="RoundPhaseMachine"/> (Annex D.1: extend, don't rewrite).</b>
    /// Each round's MG sub-phases are driven by an embedded RoundPhaseMachine:
    /// <see cref="SessionState.MgIntro"/> maps to its CommandShow phase and
    /// <see cref="SessionState.MgPlay"/> to its Play phase (entered when the
    /// wrapped machine's phase advances). It is constructed with a zero-width
    /// Intermission phase because in the §2.1 graph the intermission moved to the
    /// END of the round (after MG_RESULT), where this machine times it directly
    /// as <see cref="SessionState.Intermission"/>. Two deliberate divergence
    /// windows exist between session state and wrapped phase, both bounded: a
    /// microgame that finishes early moves the session to MG_RESULT while the
    /// wrapped machine is still in Play, and overtime (finished-late, e.g.
    /// APUNTA's in-flight shots) keeps the session in MG_PLAY up to the 8 s hard
    /// cap while the wrapped machine already sits in Result. The wrapped machine
    /// is re-armed by <c>StartRound</c> at every MG_INTRO entry, so drift never
    /// carries across rounds. Existing RoundPhaseMachine consumers are untouched.
    /// </para>
    ///
    /// <para>
    /// <b>FSM invariants (GDD §2.1):</b> every state except ATTRACT (the rest
    /// state, i.e. the timeout <i>target</i>) advances on a timeout, so a session
    /// driven with zero input always drains to GAME_OVER and back to ATTRACT.
    /// Gameplay input is forwarded to the active microgame ONLY during MG_PLAY
    /// and FINAL_MG (the climax microgame is the final round's play state; all
    /// other states read at most confirmation taps: credit in ATTRACT, color
    /// claims in JOIN, wager confirmation in FINAL_WAGER once T-109 lands).
    /// </para>
    ///
    /// <para>
    /// <b>Hosting contract:</b> the host (sequencer/session controller) calls
    /// <see cref="StageRound"/> with an already-Initialized v2 microgame, its verb
    /// and its play duration any time before MG_INTRO (for round microgames) or
    /// FINAL_MG (for the climax) — the board stub states and FINAL_WAGER exist as
    /// staging windows. Staged data is consumed on entry to those states. If
    /// nothing is staged the state still times out on
    /// <see cref="SessionConfig.DefaultMgPlaySeconds"/> (liveness over content).
    /// BOARD_MOVE / BOARD_RESOLVE are &lt;= 1 tick pass-through stubs until
    /// BoardModel (T-113) wires in, keeping the FSM shape fixed for Hito 4.
    /// [ASSUMED] JOIN also starts early when all 4 seats have claimed (nobody is
    /// left to wait for); the GDD only specifies the >= 2 + 30 s rule.
    /// </para>
    ///
    /// Zero heap allocation per <see cref="Tick"/> in steady state (GDD §14).
    /// Thread-safety: single-threaded simulation loop use only.
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class SessionStateMachine
    {
        /// <summary>Fixed simulation rate (GDD §3.2).</summary>
        public const int TicksPerSecond = 60;

        private const float TickDeltaSeconds = 1f / TicksPerSecond;

        // ── Configuration (ticks precomputed once — no per-tick float math on timeouts) ──

        private readonly SessionConfig _config;
        private readonly RoundPhaseMachine _roundMachine;
        private readonly int _joinTicks;
        private readonly int _mgPlayMaxTicks;
        private readonly int _mgResultTicks;
        private readonly int _intermissionTicks;
        private readonly int _finalWagerTicks;
        private readonly int _gameOverTicks;
        private readonly int _defaultPlayTicks;

        // ── Session state ────────────────────────────────────────────────────────

        private SessionState _state;
        private int _tickCount;
        private int _stateElapsedTicks;
        private int _roundIndex;
        private int _joinedMask;
        private bool _creditPending;
        private bool _stateChangedThisTick;
        private int _prevButtonsMask;

        // Staged round (host-supplied, consumed at MgIntro / FinalMg entry).
        private V2Microgame _stagedMicrogame;
        private string _stagedVerb;
        private float _stagedPlaySeconds;
        private bool _hasStagedRound;

        // Active round.
        private V2Microgame _activeMicrogame;
        private int _activePlayTicks;

        // ── Construction ─────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the machine in <see cref="SessionState.Attract"/>.
        /// </summary>
        /// <param name="config">Timing/shape config; null uses GDD defaults (<see cref="SessionConfig"/>).</param>
        /// <param name="sessionSeed">
        /// The session seed (GDD §13 hierarchy root). The FSM itself consumes no
        /// randomness yet — the seed is carried so hosts derive round seeds from
        /// one place and replays record it alongside the input sequence.
        /// </param>
        public SessionStateMachine(SessionConfig config = null, int sessionSeed = 0)
        {
            _config = config ?? new SessionConfig();
            SessionSeed = sessionSeed;

            // Zero-width Intermission: the §2.1 graph puts the intermission at the
            // END of the round, timed by this machine (see class doc).
            _roundMachine = new RoundPhaseMachine(
                intermissionDuration: 0f,
                commandShowDuration: _config.MgIntroSeconds,
                resultDuration: _config.MgResultSeconds);

            _joinTicks         = ToTicks(_config.JoinSeconds);
            _mgPlayMaxTicks    = ToTicks(_config.MgPlayMaxSeconds);
            _mgResultTicks     = ToTicks(_config.MgResultSeconds);
            _intermissionTicks = ToTicks(_config.IntermissionSeconds);
            _finalWagerTicks   = ToTicks(_config.FinalWagerSeconds);
            _gameOverTicks     = ToTicks(_config.GameOverSeconds);
            _defaultPlayTicks  = ToTicks(_config.DefaultMgPlaySeconds);

            _state = SessionState.Attract;
        }

        // ── Public read-only API ─────────────────────────────────────────────────

        /// <summary>The FSM state the session is currently in.</summary>
        public SessionState State => _state;

        /// <summary>Session seed recorded at construction (GDD §13). Not consumed by the FSM itself yet.</summary>
        public int SessionSeed { get; }

        /// <summary>Rounds before FINAL_WAGER (from config; GDD §9.3 default 7).</summary>
        public int RoundsTotal => _config.RoundsTotal;

        /// <summary>Completed-rounds counter (0-based index of the current round in the loop).</summary>
        public int RoundIndex => _roundIndex;

        /// <summary>Bit i set = seat i claimed its color this session (bit 0 = Rojo … bit 3 = Verde).</summary>
        public int JoinedSeatsMask => _joinedMask;

        /// <summary>Number of seats that have claimed a color this session.</summary>
        public int JoinedCount
        {
            get
            {
                int m = _joinedMask, n = 0;
                while (m != 0) { n += m & 1; m >>= 1; }
                return n;
            }
        }

        /// <summary>True if the given seat claimed its color this session.</summary>
        public bool IsSeatJoined(PlayerSlot slot) => (_joinedMask & (1 << (int)slot)) != 0;

        /// <summary>
        /// The wrapped per-round machine (Annex D.1). Views keep reading
        /// <see cref="RoundPhaseMachine.VerbText"/>/<see cref="RoundPhaseMachine.CurrentPhase"/>
        /// exactly as before.
        /// </summary>
        public RoundPhaseMachine RoundMachine => _roundMachine;

        /// <summary>True if a round has been staged and not yet consumed by MG_INTRO / FINAL_MG entry.</summary>
        public bool HasStagedRound => _hasStagedRound;

        /// <summary>True while gameplay input is being forwarded to the active microgame (GDD §2.1 invariant).</summary>
        public bool IsGameplayInputActive => _state == SessionState.MgPlay || _state == SessionState.FinalMg;

        /// <summary>True if the current <see cref="Tick"/> changed <see cref="State"/> (mirrors <see cref="RoundPhaseMachine.PhaseChangedThisTick"/>).</summary>
        public bool StateChangedThisTick => _stateChangedThisTick;

        // ── Control API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Registers a coin/credit ("moneda/crédito", GDD §2.1). Consumed on the
        /// next tick spent in ATTRACT; opens JOIN. An input event for replay
        /// purposes — re-issue on the same tick to reproduce a session.
        /// </summary>
        public void InsertCredit() => _creditPending = true;

        /// <summary>
        /// Stages the next round's content. Callable any time before the round's
        /// MG_INTRO entry (or FINAL_MG entry for the climax); staging during the
        /// board stub states / FINAL_WAGER is the intended host pattern. Replaces
        /// any previously staged, unconsumed round.
        /// </summary>
        /// <param name="microgame">
        /// The already-Initialized v2 microgame, or null to run a content-less
        /// timed round. The machine never calls Initialize — round setup
        /// (definition, RNG stream, roster) is the host's job.
        /// </param>
        /// <param name="verb">Imperative verb for MG_INTRO (null becomes empty).</param>
        /// <param name="playDurationSeconds">
        /// Play window from the definition (GDD §2.2 target 3–5 s); clamped to
        /// [0, <see cref="SessionConfig.MgPlayMaxSeconds"/>].
        /// </param>
        public void StageRound(V2Microgame microgame, string verb, float playDurationSeconds)
        {
            _stagedMicrogame = microgame;
            _stagedVerb = verb ?? string.Empty;
            _stagedPlaySeconds = Math.Min(Math.Max(0f, playDurationSeconds), _config.MgPlayMaxSeconds);
            _hasStagedRound = true;
        }

        /// <summary>
        /// Advances the session by exactly one 60 Hz simulation tick.
        /// At most one session-state transition happens per tick, so per-tick
        /// traces list every state the session passes through.
        /// </summary>
        public void Tick(in V2Snapshot snapshot)
        {
            _stateChangedThisTick = false;
            _tickCount++;
            _stateElapsedTicks++;

            // Rising button edges this tick, one bit per seat.
            int buttons = 0;
            Barcade.Core.Microgames.V2.PlayerInput[] players = snapshot.Players;
            for (int i = 0; i < 4; i++)
                if (players[i].Button) buttons |= 1 << i;
            int edges = buttons & ~_prevButtonsMask;
            _prevButtonsMask = buttons;

            switch (_state)
            {
                case SessionState.Attract:
                    // Any rising edge acts as a free-play credit; the coin path
                    // arrives via InsertCredit().
                    if (_creditPending || edges != 0)
                    {
                        _creditPending = false;
                        Enter(SessionState.Join);
                    }
                    break;

                case SessionState.Join:
                    // Start checks run BEFORE this tick's claims so the decision
                    // depends only on completed claims — a 4th claim starts the
                    // session on the following tick, deterministically.
                    if (JoinedCount == 4)
                    {
                        Enter(SessionState.BoardMove);
                    }
                    else if (_stateElapsedTicks >= _joinTicks)
                    {
                        Enter(JoinedCount >= 2 ? SessionState.BoardMove : SessionState.Attract);
                    }
                    else
                    {
                        _joinedMask |= edges & 0b1111;
                    }
                    break;

                case SessionState.BoardMove:
                    // Stub until BoardModel (T-113): pass through in one tick.
                    if (_stateElapsedTicks >= 1)
                        Enter(SessionState.BoardResolve);
                    break;

                case SessionState.BoardResolve:
                    if (_stateElapsedTicks >= 1)
                        Enter(SessionState.MgIntro);
                    break;

                case SessionState.MgIntro:
                    _roundMachine.Tick(TickDeltaSeconds);
                    if (_roundMachine.CurrentPhase != PhaseKind.CommandShow)
                        Enter(SessionState.MgPlay);
                    break;

                case SessionState.MgPlay:
                    if (_activeMicrogame != null)
                    {
                        // Exit checks run BEFORE forwarding so input is never
                        // forwarded on a tick that ends outside a play state.
                        // The mechanic owns its finish (early finish and bounded
                        // overtime both allowed); the 8 s cap is the liveness backstop.
                        if (_activeMicrogame.IsFinished || _stateElapsedTicks >= _mgPlayMaxTicks)
                        {
                            Enter(SessionState.MgResult);
                        }
                        else
                        {
                            _activeMicrogame.Tick(in snapshot);
                            _roundMachine.Tick(TickDeltaSeconds);
                        }
                    }
                    else
                    {
                        _roundMachine.Tick(TickDeltaSeconds);
                        if (_roundMachine.CurrentPhase == PhaseKind.Result)
                            Enter(SessionState.MgResult);
                    }
                    break;

                case SessionState.MgResult:
                    // The wrapped machine's Result phase is terminal/untimed, so the
                    // session times it (same configured duration).
                    if (_stateElapsedTicks >= _mgResultTicks)
                        Enter(SessionState.Intermission);
                    break;

                case SessionState.Intermission:
                    if (_stateElapsedTicks >= _intermissionTicks)
                    {
                        _roundIndex++;
                        Enter(_roundIndex < _config.RoundsTotal
                            ? SessionState.BoardMove
                            : SessionState.FinalWager);
                    }
                    break;

                case SessionState.FinalWager:
                    // Wager choices land with T-109; until then this is a pure
                    // timed window (and the climax staging window for hosts).
                    if (_stateElapsedTicks >= _finalWagerTicks)
                        Enter(SessionState.FinalMg);
                    break;

                case SessionState.FinalMg:
                    if (_activeMicrogame != null)
                    {
                        // Same exit-before-forward ordering as MG_PLAY.
                        if (_activeMicrogame.IsFinished || _stateElapsedTicks >= _mgPlayMaxTicks)
                            Enter(SessionState.GameOver);
                        else
                            _activeMicrogame.Tick(in snapshot);
                    }
                    else if (_stateElapsedTicks >= _activePlayTicks)
                    {
                        Enter(SessionState.GameOver);
                    }
                    break;

                case SessionState.GameOver:
                    if (_stateElapsedTicks >= _gameOverTicks)
                        Enter(SessionState.Attract);
                    break;

                default:
                    throw new InvalidOperationException("Unknown SessionState: " + _state);
            }
        }

        /// <summary>
        /// Captures the per-tick serializable FSM state (GDD §2.1 "el estado se
        /// serializa por tick" / §13 replay). Allocation-free.
        /// </summary>
        public SessionStateSnapshot Capture()
        {
            SessionStateSnapshot snap;
            snap.Tick = _tickCount;
            snap.State = _state;
            snap.StateElapsedTicks = _stateElapsedTicks;
            snap.RoundIndex = _roundIndex;
            snap.JoinedSeatsMask = (byte)_joinedMask;
            snap.RoundPhase = _roundMachine.CurrentPhase;
            return snap;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private void Enter(SessionState next)
        {
            _state = next;
            _stateElapsedTicks = 0;
            _stateChangedThisTick = true;

            switch (next)
            {
                case SessionState.Attract:
                    // Reset for the next group. A credit inserted during GAME_OVER
                    // stays pending (it means "play again").
                    _joinedMask = 0;
                    _roundIndex = 0;
                    _activeMicrogame = null;
                    _stagedMicrogame = null;
                    _stagedVerb = null;
                    _hasStagedRound = false;
                    break;

                case SessionState.Join:
                    _joinedMask = 0;
                    break;

                case SessionState.MgIntro:
                    ConsumeStagedRound();
                    _roundMachine.StartRound(_stagedVerb ?? string.Empty, _stagedPlaySeconds);
                    _roundMachine.Tick(0f); // consume the zero-width Intermission -> CommandShow
                    break;

                case SessionState.Intermission:
                    _activeMicrogame = null; // result was readable through MG_RESULT
                    break;

                case SessionState.FinalMg:
                    // §2.1 shows FINAL_MG as a single state — no MG_INTRO/MG_RESULT
                    // around the climax; its presentation is a later-hito concern.
                    ConsumeStagedRound();
                    break;

                case SessionState.GameOver:
                    _activeMicrogame = null;
                    break;
            }
        }

        private void ConsumeStagedRound()
        {
            if (_hasStagedRound)
            {
                _activeMicrogame = _stagedMicrogame;
                _activePlayTicks = ToTicks(_stagedPlaySeconds);
                _stagedMicrogame = null;
                _hasStagedRound = false;
            }
            else
            {
                _activeMicrogame = null;
                _activePlayTicks = _defaultPlayTicks;
                _stagedVerb = string.Empty;
                _stagedPlaySeconds = _config.DefaultMgPlaySeconds;
            }
        }

        private static int ToTicks(float seconds)
            => (int)Math.Round(Math.Max(0f, seconds) * TicksPerSecond);
    }
}
