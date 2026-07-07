using System;
using V2 = Barcade.Core.Microgames.V2;

namespace Barcade.Core
{
    /// <summary>
    /// The full session FSM (GDD T-102, §2.1): Attract -> Join -> (BoardMove ->
    /// BoardResolve -> MgIntro -> MgPlay -> MgResult -> Intermission)* -> FinalWager
    /// -> FinalMg -> GameOver -> Attract.
    ///
    /// <para>
    /// <b>Extension approach (AC6: extend RoundPhaseMachine, don't rewrite it).</b>
    /// <see cref="RoundPhaseMachine"/> is untouched — not subclassed, not modified —
    /// so both of its existing consumers (<c>RoundPhaseMachineTests</c> and the
    /// Framework's <c>MicrogameLoopController</c>) keep compiling and passing
    /// unchanged. This class <b>composes</b> a private <see cref="RoundPhaseMachine"/>
    /// instance and reuses it for exactly the sub-loop it already models —
    /// <c>Intermission/CommandShow/Play/Result</c> map 1:1 onto GDD's
    /// <c>(throwaway)/MgIntro/MgPlay/MgResult</c> — rather than hand-rolling that
    /// same timing logic a second time. Two adaptations, both driven by how
    /// RoundPhaseMachine already behaves (see its own doc comments):
    /// <list type="bullet">
    /// <item><description>
    /// The inner instance is constructed with <c>intermissionDuration: 0</c>.
    /// <see cref="RoundPhaseMachine.StartRound"/> always resets to its own
    /// <c>Intermission</c> phase first; a real (2s) intermission belongs at the
    /// GDD's INTERMISSION state instead (after MG_RESULT, before the next
    /// BOARD_MOVE) — configuring the inner instance's Intermission to 0 makes it
    /// skip through instantly (RoundPhaseMachine's own documented zero-duration
    /// behavior) and lets <see cref="SessionStateMachine"/> own the real
    /// Intermission timing itself, once, at the correct point in the graph.
    /// </description></item>
    /// <item><description>
    /// RoundPhaseMachine's <c>Result</c> phase is intentionally terminal — by
    /// design, the caller decides how long to linger there (see
    /// <c>MicrogameLoopController</c>'s own <c>_resultElapsed</c> field, which does
    /// exactly this already). SessionStateMachine replicates that same pattern for
    /// MgResult's GDD-exact 1.5s window rather than inventing a different one.
    /// </description></item>
    /// </list>
    /// <c>FinalMg</c> is deliberately NOT routed through the inner RoundPhaseMachine:
    /// the GDD §2.1 diagram draws it as a single box (no separate intro/result
    /// sub-phases), unlike the repeating round loop, so it's handled as its own
    /// simple elapsed-time-or-IsFinished state.
    /// </para>
    ///
    /// <para>
    /// <b>Input boundary (AC3).</b> <see cref="Tick"/> takes the GDD-literal,
    /// session-level v2 <c>InputSnapshot</c> (matching how the v2 microgames
    /// already read input). It is forwarded verbatim to the active microgame's own
    /// <c>Tick</c> ONLY while <see cref="SessionPhase.MgPlay"/> (or
    /// <see cref="SessionPhase.FinalMg"/>, itself a Play-equivalent state) is
    /// current — the GDD §2.1 invariant "MG_PLAY es el único estado donde las
    /// mecánicas leen input de juego." Every other state reads input only through
    /// this machine's own private <see cref="InputInterpreter"/> (press edges for
    /// Join's color-claim; no other state currently needs "confirmation taps" —
    /// BoardMove/BoardResolve are timer-only stubs and FinalWager is a fixed
    /// window, see each state's own notes below). ATTRACT reads no player input at
    /// all — it advances only via <see cref="InsertCredit"/>, a control-plane call
    /// modeling the GDD's "moneda/crédito" trigger, not one of §3.1's four
    /// universal gestures.
    /// </para>
    ///
    /// <para>
    /// <b>InputInterpreter ownership (T-101 note applies here).</b> This machine
    /// owns one long-lived <see cref="InputInterpreter"/> for its own needs
    /// (currently just Join) and calls its <c>Reset()</c> exactly once, on entering
    /// MgIntro (and the FinalMg-equivalent entry) — the natural home T-101 always
    /// intended for that call, finally wired up for real. This is or independent of
    /// each v2 microgame's own private InputInterpreter (ReaccionaMicrogame /
    /// ApuntaMicrogame each still build their own, a decision from TASK-025/026 that
    /// is NOT revisited here) — resetting this machine's interpreter cannot affect
    /// theirs; it only prevents this machine's OWN accumulated state (e.g. a
    /// still-held Join button) from misreading as a confirmation tap in whatever
    /// state comes next. A future ticket that hoists ONE shared InputInterpreter
    /// across the whole session (handed to microgames instead of each building
    /// their own) would reset THIS SAME instance at THIS SAME point — the seam is
    /// already in the right place.
    /// </para>
    ///
    /// <para>
    /// <b>Active-microgame seam (deliberately minimal for this ticket).</b>
    /// <see cref="SetActiveMicrogame"/> injects whatever <c>IMicrogame</c> MgPlay
    /// (or FinalMg) should drive next — for this ticket, tests inject a fake; the
    /// real sequencer/pool (GDD T-108) is explicitly out of scope. If nothing is
    /// injected before a round begins, MgPlay is skipped in &lt;=1 tick (its
    /// duration is set to 0), consistent with the "no state can be blocked by an
    /// absent player [or absent microgame]" invariant — this is exactly what lets
    /// a fully zero-input, zero-injection session still reach GameOver (AC2).
    /// </para>
    ///
    /// <para>
    /// <b>Determinism (AC5).</b> <see cref="Snapshot"/> returns a
    /// <see cref="SessionStateSnapshot"/> — see that type's doc for why a plain
    /// struct trace (rather than MiniJson-encoding, though TASK-030 makes that
    /// available too) satisfies "serializable POCO" here.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] design gap: Join with zero ready players (AC2).</b> GDD §2.1
    /// states Join completes early once "&gt;=2 jugadores listos," with a 30s
    /// timeout, but doesn't say what happens if the timeout expires with FEWER than
    /// 2 ready (real bot-fill, GDD §1.3/D.3, is T-110 — out of scope). This machine
    /// advances to BoardMove on EITHER condition (ready count OR timeout), with
    /// however many seats actually claimed (down to zero) — never routing back to
    /// Attract. This is required for AC2's "a session driven with zero input still
    /// reaches GameOver" to be achievable at all: routing an under-joined session
    /// back to Attract would loop Attract/Join forever and never reach GameOver.
    /// Flagged for reviewer confirmation; pinned by
    /// <c>ZeroInputSession_StillReachesGameOver</c>.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] Join stays open past its own minimum (TASK-024 review fix
    /// round, MEDIUM-1, orchestrator ruling).</b> Reaching GDD §2.1's documented
    /// "&gt;=2 listos" minimum does NOT close Join — it stays open for the rest of
    /// its <see cref="SessionStateMachineConfig.JoinTimeoutSeconds"/> window so
    /// seats 3/4 can still claim, exiting early only once every seat (all 4) has
    /// claimed. GDD §2.1's ">=2 listos" label is ambiguous between "the minimum to
    /// proceed" and "closes the window right then"; GDD §9.3 budgets up to 30s for
    /// color choice; and closing the instant 2 claim would lock out seats 3/4
    /// mid-claim, the worst reading given the social-cabinet premise. Cost
    /// accepted: a 2-3-player group waits out the full window (a future "start
    /// now" confirmation gesture could shorten this — out of scope here). The
    /// zero-ready/zero-input timeout fallback above is unchanged by this — it
    /// still fires on its own, regardless of ready count. Pinned by
    /// <c>Join_TwoReadyPlayers_StaysOpenUntilAllFourOrTimeout</c>,
    /// <c>Join_ThirdAndFourthPlayerCanStillJoinAfterMinimumReached</c>, and
    /// <c>Join_AllFourClaimed_AdvancesBeforeTimeout</c>.
    /// </para>
    ///
    /// Pure C# — no UnityEngine dependency. C# 9 compatible. Zero heap allocation in
    /// steady-state <see cref="Tick"/> — the one exception is the per-seat roster
    /// array built exactly once, at the Join-&gt;BoardMove transition (a once-per-
    /// session event, not a per-tick cost — consistent with how <c>GetResult()</c>
    /// elsewhere in this codebase also allocates once per round, not per tick).
    /// </summary>
    public sealed class SessionStateMachine
    {
        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly SessionStateMachineConfig _config;
        private readonly SeededRandom _rng;
        private readonly InputInterpreter _interpreter;
        private readonly InputBridge _inputBridge = new InputBridge();
        private readonly RoundPhaseMachine _roundMachine;

        private SessionPhase _phase;
        private int _tick;
        private float _phaseElapsed;

        private readonly bool[] _ready = new bool[4];
        private int _readyCount;
        private V2.PlayerRoster _roster;

        private int _roundIndex;

        private V2.IMicrogame _activeMicrogame;
        private string _activeVerb;
        private float _activePlayDurationSeconds;
        private float _activeDifficultyMult = 1f;
        private V2.MicrogameResult? _lastResult;

        public SessionStateMachine(SeededRandom rng, SessionStateMachineConfig? config = null)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _rng = rng;
            _config = config ?? SessionStateMachineConfig.GddDefaults;
            _interpreter = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            _roundMachine = new RoundPhaseMachine(
                intermissionDuration: 0f,
                commandShowDuration: _config.MgIntroSeconds,
                resultDuration: _config.MgResultSeconds);

            _phase = SessionPhase.Attract;
        }

        // ── Public read-only API ─────────────────────────────────────────────────

        public SessionPhase CurrentPhase => _phase;
        public int RoundIndex => _roundIndex;
        public int ReadyCount => _readyCount;

        /// <summary>Valid once Join has completed (i.e. once <see cref="CurrentPhase"/> has moved past <see cref="SessionPhase.Join"/> at least once this session).</summary>
        public V2.PlayerRoster Roster => _roster;

        /// <summary>
        /// The most recently completed round's/climax's result, or null if that
        /// round's microgame never reported <see cref="V2.IMicrogame.IsFinished"/>
        /// (including "no microgame was injected"). Updated the tick MgResult (or
        /// the FinalMg-equivalent completion) is reached.
        /// </summary>
        public V2.MicrogameResult? LastMicrogameResult => _lastResult;

        /// <summary>A per-tick, serializable snapshot for replay/determinism comparisons — see <see cref="SessionStateSnapshot"/>.</summary>
        public SessionStateSnapshot Snapshot() => new SessionStateSnapshot(_tick, _phase, _roundIndex, _readyCount);

        // ── Control API ──────────────────────────────────────────────────────────

        /// <summary>
        /// GDD §2.1's "moneda/crédito" trigger: Attract -> Join. A no-op outside
        /// Attract. Not player input — a machine-level/control-plane event.
        /// </summary>
        public void InsertCredit()
        {
            if (_phase != SessionPhase.Attract) return;
            EnterSimplePhase(SessionPhase.Join);
        }

        /// <summary>
        /// Injects the microgame MgPlay (or FinalMg) should drive next. Persists
        /// until changed — a real per-microgame instance is safe to reuse round to
        /// round since <see cref="V2.IMicrogame.Initialize"/> fully resets its
        /// internal state every call (by design, see ReaccionaMicrogame/
        /// ApuntaMicrogame). If never called, the next MgPlay/FinalMg is skipped in
        /// &lt;=1 tick — see class doc.
        ///
        /// <para>
        /// <paramref name="difficultyMult"/> (TASK-024 review fix round, MEDIUM-3):
        /// forwarded verbatim to <see cref="V2.IMicrogame.Initialize"/>. Defaults
        /// to 1f (no difficulty scaling) — this ticket does not compute a real
        /// difficulty curve (GDD §9.1's D_final=1.5x for the climax round is T-108
        /// territory), it just stops hardcoding the value at the call site so
        /// T-108's selector can plug a real number in here without any further FSM
        /// change.
        /// </para>
        /// </summary>
        public void SetActiveMicrogame(V2.IMicrogame microgame, string verb, float playDurationSeconds = 5f, float difficultyMult = 1f)
        {
            _activeMicrogame = microgame;
            _activeVerb = verb ?? string.Empty;
            _activePlayDurationSeconds = playDurationSeconds;
            _activeDifficultyMult = difficultyMult;
        }

        /// <summary>Advances the session by exactly one fixed 60 Hz tick.</summary>
        public void Tick(in V2.InputSnapshot input)
        {
            if (input.Players == null) throw new ArgumentException("InputSnapshot.Players must not be null.", nameof(input));

            _inputBridge.SetSource(input.Players);
            _interpreter.Tick(_inputBridge);

            float dt = 1f / _config.TicksPerSecond;

            switch (_phase)
            {
                case SessionPhase.Attract:
                    // No timeout, no player input read — only InsertCredit() moves it.
                    break;

                case SessionPhase.Join:
                    TickJoin(dt);
                    break;

                case SessionPhase.BoardMove:
                    _phaseElapsed += dt;
                    if (_phaseElapsed >= dt) EnterSimplePhase(SessionPhase.BoardResolve); // stub: exactly 1 tick
                    break;

                case SessionPhase.BoardResolve:
                    _phaseElapsed += dt;
                    if (_phaseElapsed >= dt) BeginRound(); // stub: exactly 1 tick, then the real round starts
                    break;

                case SessionPhase.MgIntro:
                case SessionPhase.MgPlay:
                case SessionPhase.MgResult:
                    TickRoundSubLoop(input, dt);
                    break;

                case SessionPhase.Intermission:
                    _phaseElapsed += dt;
                    if (_phaseElapsed >= _config.IntermissionSeconds)
                    {
                        _roundIndex++;
                        if (_roundIndex < _config.TotalRounds)
                            EnterSimplePhase(SessionPhase.BoardMove);
                        else
                            EnterSimplePhase(SessionPhase.FinalWager);
                    }
                    break;

                case SessionPhase.FinalWager:
                    _phaseElapsed += dt;
                    if (_phaseElapsed >= _config.FinalWagerSeconds)
                        BeginFinalMg();
                    break;

                case SessionPhase.FinalMg:
                    TickFinalMg(input, dt);
                    break;

                case SessionPhase.GameOver:
                    _phaseElapsed += dt;
                    if (_phaseElapsed >= _config.GameOverSeconds)
                        ResetToAttract();
                    break;
            }

            _tick++;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private void EnterSimplePhase(SessionPhase phase)
        {
            _phase = phase;
            _phaseElapsed = 0f;
        }

        private void TickJoin(float dt)
        {
            for (int i = 0; i < AllSlots.Length; i++)
            {
                if (_ready[i]) continue;
                if (_interpreter.ButtonPressedThisTick(AllSlots[i]))
                {
                    _ready[i] = true;
                    _readyCount++;
                }
            }

            _phaseElapsed += dt;

            // MEDIUM-1 (TASK-024 review fix round, [ASSUMED]): reaching the
            // documented minimum no longer closes the window by itself — see
            // class doc. The only early exit left is every seat having claimed;
            // the timeout is the sole other way out, unconditionally (0 ready
            // included — AC2).
            if (_readyCount >= AllSlots.Length || _phaseElapsed >= _config.JoinTimeoutSeconds)
                CompleteJoin();
        }

        private void CompleteJoin()
        {
            var seats = new V2.SeatState[4];
            for (int i = 0; i < 4; i++)
                seats[i] = _ready[i] ? V2.SeatState.Human : V2.SeatState.Empty;
            _roster = new V2.PlayerRoster(seats);

            _roundIndex = 0;
            EnterSimplePhase(SessionPhase.BoardMove);
        }

        private void BeginRound()
        {
            _interpreter.Reset();
            _activeMicrogame?.Initialize(_rng, _roster, _activeDifficultyMult);

            float effectivePlayDuration = _activeMicrogame != null ? _activePlayDurationSeconds : 0f;
            _roundMachine.StartRound(_activeVerb ?? string.Empty, effectivePlayDuration);
            _roundMachine.Tick(0f); // flush the inner machine's own zero-duration Intermission (see class doc)

            _phase = MapInnerPhase(_roundMachine.CurrentPhase);
            _phaseElapsed = 0f;

            // LOW-1 (TASK-024 review fix round): with mgIntroSeconds == 0 AND an
            // effective play duration of 0, the flush above can cascade straight
            // through CommandShow and Play into Result within this same call —
            // TickRoundSubLoop's own "just arrived at Result" capture never gets a
            // chance to run for this round, so without this check LastMicrogameResult
            // would silently keep whatever it was before this round.
            if (_roundMachine.CurrentPhase == PhaseKind.Result)
                CaptureResult();
        }

        /// <summary>
        /// Captures <see cref="_lastResult"/> from the active microgame if it
        /// genuinely finished, else leaves it null — shared by
        /// <see cref="TickRoundSubLoop"/>'s normal "just arrived at Result"
        /// transition and <see cref="BeginRound"/>'s same-call flush-to-Result
        /// edge case (LOW-1), so both paths capture identically.
        /// </summary>
        private void CaptureResult()
        {
            _lastResult = _activeMicrogame != null && _activeMicrogame.IsFinished
                ? _activeMicrogame.GetResult()
                : (V2.MicrogameResult?)null;
        }

        private void TickRoundSubLoop(in V2.InputSnapshot input, float dt)
        {
            _roundMachine.Tick(dt);

            if (_roundMachine.CurrentPhase == PhaseKind.Play && _activeMicrogame != null)
                _activeMicrogame.Tick(input);

            SessionPhase mapped = MapInnerPhase(_roundMachine.CurrentPhase);

            if (mapped == SessionPhase.MgResult && _phase != SessionPhase.MgResult)
            {
                // Just arrived at Result this tick — RoundPhaseMachine's Result is
                // intentionally terminal, so arm our own external result-window
                // timer and capture the outcome exactly once (see class doc).
                _phaseElapsed = 0f;
                CaptureResult();
            }

            _phase = mapped;

            if (_phase == SessionPhase.MgResult)
            {
                _phaseElapsed += dt;
                if (_phaseElapsed >= _config.MgResultSeconds)
                    EnterSimplePhase(SessionPhase.Intermission);
            }
        }

        private void BeginFinalMg()
        {
            _interpreter.Reset();
            _activeMicrogame?.Initialize(_rng, _roster, _activeDifficultyMult);
            EnterSimplePhase(SessionPhase.FinalMg);
        }

        private void TickFinalMg(in V2.InputSnapshot input, float dt)
        {
            if (_activeMicrogame != null)
                _activeMicrogame.Tick(input);

            _phaseElapsed += dt;

            bool microgameDone = _activeMicrogame != null && _activeMicrogame.IsFinished;
            bool ceilingHit = _activeMicrogame == null || _phaseElapsed >= _activePlayDurationSeconds;

            if (microgameDone || ceilingHit)
            {
                _lastResult = microgameDone ? _activeMicrogame.GetResult() : (V2.MicrogameResult?)null;
                EnterSimplePhase(SessionPhase.GameOver);
            }
        }

        private void ResetToAttract()
        {
            for (int i = 0; i < 4; i++) _ready[i] = false;
            _readyCount = 0;
            _roundIndex = 0;
            _lastResult = null;
            // LOW-2 (TASK-024 review fix round): clear the roster too, or the
            // previous session's seat claims would still read back via Roster
            // during the fresh Attract cycle, before Join runs again. SeatState.Empty
            // == 0, so a freshly allocated array is already all-Empty.
            _roster = new V2.PlayerRoster(new V2.SeatState[4]);
            EnterSimplePhase(SessionPhase.Attract);
        }

        private static SessionPhase MapInnerPhase(PhaseKind inner)
        {
            switch (inner)
            {
                case PhaseKind.CommandShow: return SessionPhase.MgIntro;
                case PhaseKind.Play: return SessionPhase.MgPlay;
                case PhaseKind.Result: return SessionPhase.MgResult;
                default: return SessionPhase.MgIntro; // Intermission — only ever seen transiently during BeginRound's flush-tick
            }
        }

        /// <summary>
        /// Adapts this tick's v2 <see cref="V2.InputSnapshot.Players"/> array into
        /// <see cref="IReadOnlyPlayerInputs"/> (v1, per-seat) so this machine's own
        /// internal <see cref="InputInterpreter"/> can be reused unchanged — same
        /// approach and rationale as ReaccionaMicrogame's/ApuntaMicrogame's private
        /// bridges (duplicated rather than shared/extracted, consistent with their
        /// own documented reasoning for not touching each other's files).
        /// </summary>
        private sealed class InputBridge : IReadOnlyPlayerInputs
        {
            private V2.PlayerInput[] _players;

            public void SetSource(V2.PlayerInput[] players) => _players = players;

            public InputSnapshot For(PlayerSlot slot)
            {
                V2.PlayerInput p = _players[(int)slot];
                ToStickXY(p.Stick, out float x, out float y);
                ButtonState state = p.Button ? ButtonState.Held : ButtonState.Released;
                return new InputSnapshot(x, y, state);
            }

            private static void ToStickXY(Direction8 d, out float x, out float y)
            {
                switch (d)
                {
                    case Direction8.N: x = 0f; y = 1f; break;
                    case Direction8.S: x = 0f; y = -1f; break;
                    case Direction8.E: x = 1f; y = 0f; break;
                    case Direction8.W: x = -1f; y = 0f; break;
                    case Direction8.NE: x = 1f; y = 1f; break;
                    case Direction8.SE: x = 1f; y = -1f; break;
                    case Direction8.NW: x = -1f; y = 1f; break;
                    case Direction8.SW: x = -1f; y = -1f; break;
                    default: x = 0f; y = 0f; break;
                }
            }
        }
    }
}
