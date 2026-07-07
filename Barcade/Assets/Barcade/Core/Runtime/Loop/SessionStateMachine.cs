using System;
using Barcade.Core.Scoring;
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
    /// Join's color-claim; stick direction for FinalWager's stake choice — see
    /// below). ATTRACT reads no player input at all — it advances only via
    /// <see cref="InsertCredit"/>, a control-plane call modeling the GDD's
    /// "moneda/crédito" trigger, not one of §3.1's four universal gestures.
    /// </para>
    ///
    /// <para>
    /// <b>InputInterpreter ownership (T-101 note applies here).</b> This machine
    /// owns one long-lived <see cref="InputInterpreter"/> for its own needs (Join,
    /// FinalWager) and calls its <c>Reset()</c> exactly once, on entering MgIntro
    /// (and the FinalMg-equivalent entry) — the natural home T-101 always intended
    /// for that call, finally wired up for real. This is independent of each v2
    /// microgame's own private InputInterpreter (ReaccionaMicrogame/ApuntaMicrogame
    /// each still build their own, a decision from TASK-025/026 that is NOT
    /// revisited here) — resetting this machine's interpreter cannot affect
    /// theirs; it only prevents this machine's OWN accumulated state (e.g. a
    /// still-held Join button) from misreading as a confirmation gesture in
    /// whatever state comes next. FinalWager (TASK-051) is exactly the SECOND
    /// interpreter-reading state this seam was built for.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] FinalWager gesture mapping (TASK-051, GDD §6.2 "elección con
    /// palanca" — no directions specified).</b> Left = 25% (<see cref="WagerChoice.Quarter"/>),
    /// Up = 50% (<see cref="WagerChoice.Half"/>), Right = 75%
    /// (<see cref="WagerChoice.ThreeQuarters"/>) — a left-to-right low-to-high
    /// reading, Up as the natural "default/neutral middle" position. Down is a
    /// no-op (only 3 choices exist for 4 cardinal directions; forcing a double
    /// mapping felt less honest than leaving one direction unused). Whichever
    /// direction a seat last held (collapsed-cardinal, sticky across neutral
    /// ticks — same "last confirmed gesture" idea as <c>InputInterpreter</c>'s own
    /// diagonal-collapse hysteresis, tracked here per seat since the interpreter
    /// itself only reports the CURRENT tick's collapsed direction) is that seat's
    /// choice when the 5s window closes; a seat that never picks one resolves to
    /// <see cref="FinalWager.DefaultChoice"/> (its own [ASSUMED] timeout default,
    /// Half — <see cref="FinalWager.Resolve"/>'s null-choice path).
    /// </para>
    ///
    /// <para>
    /// <b>Regular-round payout application (TASK-051, per-definition table via
    /// TASK-052).</b> Every completed regular round (not the climax — see below)
    /// applies a payout to <see cref="Coins"/> based on the round's
    /// <see cref="V2.MicrogameResult.Kind"/>: the active microgame's own
    /// per-definition <c>payoutTable</c> (GDD §11 schema / Annex D.2
    /// <c>MicrogameDefinitionV2.PayoutTable</c>, passed in via
    /// <see cref="SetActiveMicrogame"/>'s <c>payoutTable</c> parameter) when one is
    /// set for that round, otherwise <see cref="PayoutRules.DefaultCompetitive"/>/
    /// <see cref="PayoutRules.DefaultCoopSuccess"/>/<see cref="PayoutRules.DefaultCoopFail"/>
    /// as a FALLBACK. GDD §6.1's defaults are the design's baseline tuning values,
    /// not a hardcoded requirement — TASK-052 delivered the real per-definition
    /// path this paragraph used to call unowned (see <see cref="SetActiveMicrogame"/>'s
    /// own doc for the payoutTable channel's design rationale). A coop table is 2
    /// entries [success, fail] (TASK-050's already-validated shape convention);
    /// a mismatched length fails loudly rather than mis-indexing. Coop's payout
    /// counts as a win for every active seat when it succeeds (no individual
    /// place makes sense for a shared outcome); competitive places 1 count as a
    /// win for whoever holds them (ties share the credit, matching every other
    /// competition-ranking convention in this codebase). The CLIMAX round is
    /// exempt from this per-round payout (its outcome feeds
    /// <see cref="FinalWager"/> instead, GDD §6.2) — including any payoutTable set
    /// on the climax microgame itself — but still counts toward
    /// <see cref="V2.MicrogameId.Reacciona"/> latency samples and win counts — a
    /// microgame win is a microgame win regardless of which round it happened in.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] SessionCounters real feeds vs stubs (TASK-051, AC2).</b> Only
    /// <see cref="StarKind.Gatillo"/> (REACCIONA mean latency) and the
    /// <see cref="FinalRanking"/> <c>microgameWins</c> tiebreak have a real,
    /// currently-implemented source system to feed from — every v2 mechanic today
    /// (ReaccionaMicrogame, ApuntaMicrogame) is a scored, ranked minigame with no
    /// "elimination," "weapon," "investment," or "robbed" concept of its own.
    /// <see cref="StarKind.Kamikaze"/> (eliminations) and <see cref="StarKind.Cangreja"/>/
    /// <see cref="StarKind.Inversora"/>/<see cref="StarKind.Fantasma"/> (arsenal/
    /// investment/robbery, GDD §5.3/§5.4) are BOARD-tile-driven — BoardModel is
    /// still a pass-through stub (Hito 4, T-113/TASK-042 territory) — so those
    /// three <see cref="SessionCounters"/> methods are never called here; they
    /// stay correctly at their zero/tied default until a future ticket wires real
    /// board events. <see cref="StarKind.Zen"/> stays unfed too, pending the human
    /// GDD fix for its truncated table row (see <c>SessionCounters.RecordZenMetric</c>'s
    /// own doc). Bonus-star baseStars (the per-seat count BEFORE the Game Over
    /// reveal) are likewise always 0 here for the same reason: GDD §5.3's only
    /// described pre-bonus star sources (Inversión tile ownership payouts,
    /// buying the Estrella tile) are both board-tile events. None of this weakens
    /// <see cref="BonusStarDraw"/>'s own exclusion-rule correctness (already
    /// covered by TASK-037's tests against varied baseStars) — it just means every
    /// session plays out with an honest, currently-true zero baseline until Hito
    /// 4 wires the board.
    /// </para>
    ///
    /// <para>
    /// <b>Scoring seed derivation.</b> <see cref="BonusStarDraw.Draw"/> (and, via
    /// GDD §13 stream derivation, everything else that reads from
    /// <see cref="RngStream.Draws"/>) takes an explicit <c>int sessionSeed</c> —
    /// the same shape <see cref="Sequencer.V2.MicrogameSelector"/> already uses,
    /// deliberately NOT the <see cref="SeededRandom"/> object handed to
    /// microgames' own <c>Initialize</c> calls (whose internal state keeps
    /// advancing round to round). The two-argument
    /// <see cref="SessionStateMachine(SeededRandom, SessionStateMachineConfig?)"/>
    /// constructor derives one, once, at construction —
    /// <c>rng.NextInt(int.MinValue, int.MaxValue)</c>, the very FIRST draw ever
    /// taken from that <paramref name="rng"/>, before any microgame ever touches
    /// it — so it's fully deterministic from the same construction seed. [ASSUMED]
    /// (TASK-051 review fix round, M3): this first-draw derivation is a THIRD,
    /// unnamed seed idiom alongside the codebase's two established ones
    /// (<see cref="SeededRandom.Derive"/>'s GDD §13 stream hierarchy, and plain
    /// constructor seeds) — acceptable only as a stand-in until a real, explicit
    /// <c>int sessionSeed</c> is threaded through the rest of the session (a
    /// replay-format-breaking change, not yet done). The
    /// <see cref="SessionStateMachine(SeededRandom, int, SessionStateMachineConfig?)"/>
    /// overload below takes that explicit seed directly and is the migration
    /// target for whenever that threading happens — at that point every caller
    /// should move to it instead of relying on this implicit first-draw idiom.
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
    /// steady-state <see cref="Tick"/> — the exceptions are all once-per-round-or-
    /// rarer events, never a per-tick cost (L2, TASK-051 review fix round — the
    /// prior wording only cited the once-per-session cases and undersold this):
    /// the per-seat roster array built once at the Join-&gt;BoardMove transition;
    /// <see cref="CaptureResult"/>'s per-seat places array, allocated once at the
    /// end of EVERY regular ranked round (not just once per session); and the
    /// small fixed-size arrays <see cref="FinalWager.Resolve"/>/
    /// <see cref="BonusStarDraw.Draw"/>/<see cref="FinalRanking.Rank"/> allocate
    /// once at FinalMg completion.
    /// </summary>
    public sealed class SessionStateMachine
    {
        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        /// <summary>
        /// GDD §6.1 / TASK-050 shape convention: a coop payoutTable is exactly 2
        /// entries, [success, fail]. Matches
        /// <c>Barcade.Core.Content.MicrogameDefinitionValidator</c>'s own (private)
        /// <c>CoopPayoutTableLength</c> constant by value -- cited here rather than
        /// referenced directly since that constant isn't public, but the two must
        /// always agree: this is the same contract TASK-050 already validates a
        /// definition's payoutTable against before it ever reaches this class.
        /// </summary>
        private const int CoopPayoutTableLength = 2;

        private readonly SessionStateMachineConfig _config;
        private readonly SeededRandom _rng;
        private readonly int _scoringSeed;
        private readonly InputInterpreter _interpreter;
        // TASK-053: was a private nested InputBridge (sign-pure ±1/±1 diagonals);
        // now the shared, canonical-normalized V2.InputBridge (TASK-046) — see its
        // own class doc for the convention and why it was made shared in the
        // first place.
        private readonly V2.InputBridge _inputBridge = new V2.InputBridge();
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
        // TASK-052: null means "no per-definition table for this round" -- the GDD
        // §6.1 defaults apply, exactly as before this ticket. See SetActiveMicrogame
        // and CaptureResult.
        private int[] _activePayoutTable;
        private V2.MicrogameResult? _lastResult;

        private SessionCounters _counters = new SessionCounters();
        private int[] _coins = new int[4];
        private readonly int[] _microgameWins = new int[4];
        private readonly WagerChoice?[] _wagerChoice = new WagerChoice?[4];
        private WagerResult _lastWagerResult;
        private BonusStarResult _lastBonusStars;
        private int[] _finalPlaces;

        public SessionStateMachine(SeededRandom rng, SessionStateMachineConfig? config = null)
            : this(rng, DeriveScoringSeed(rng), config)
        {
        }

        /// <summary>
        /// Overload taking an explicit scoring seed directly, bypassing the
        /// two-argument constructor's own first-draw derivation from
        /// <paramref name="rng"/> — see class doc "Scoring seed derivation" (M3,
        /// TASK-051 review fix round). This is the migration target for whenever
        /// a real, explicit <c>int sessionSeed</c> is threaded through the rest
        /// of the session.
        /// </summary>
        public SessionStateMachine(SeededRandom rng, int sessionSeed, SessionStateMachineConfig? config = null)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _rng = rng;
            _scoringSeed = sessionSeed;
            _config = config ?? SessionStateMachineConfig.GddDefaults;
            _interpreter = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            _roundMachine = new RoundPhaseMachine(
                intermissionDuration: 0f,
                commandShowDuration: _config.MgIntroSeconds,
                resultDuration: _config.MgResultSeconds);

            _phase = SessionPhase.Attract;
        }

        /// <summary>
        /// See class doc "Scoring seed derivation": the very first draw ever
        /// taken from <paramref name="rng"/>, deterministic, before any microgame
        /// touches it. Kept as a separate static method (rather than inline in
        /// the two-argument constructor) so the constructor-initializer call to
        /// <c>this(rng, ..., config)</c> can compute it before any instance field
        /// exists yet — a constructor initializer's arguments cannot reference
        /// <c>this</c>.
        /// </summary>
        private static int DeriveScoringSeed(SeededRandom rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            return rng.NextInt(int.MinValue, int.MaxValue);
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

        /// <summary>Per-seat coins: payout accumulation through the round loop, then post-wager totals from FinalMg onward. See class doc for the [ASSUMED] default-payout note.</summary>
        public int[] Coins => _coins;

        /// <summary>Per-seat completed-microgame win counts this session (competitive place 1, or every active seat on a coop success) — the <see cref="FinalRanking"/> tiebreak source.</summary>
        public int[] MicrogameWins => _microgameWins;

        /// <summary>The tracked hidden-objective counters behind the GDD §6.3 bonus stars — see class doc for which are real feeds vs stubs today.</summary>
        public SessionCounters Counters => _counters;

        /// <summary>Set once FinalMg completes (GDD §6.2 pot resolution); null before then.</summary>
        public WagerResult LastWagerResult => _lastWagerResult;

        /// <summary>Set once FinalMg completes (GDD §6.3 bonus-star reveal); null before then.</summary>
        public BonusStarResult LastBonusStarResult => _lastBonusStars;

        /// <summary>The GDD §6.3 final podium (estrellas -> monedas -> victorias -> shared podium), 1..4 per seat / 0 if absent. Set once FinalMg completes; null before then.</summary>
        public int[] FinalPlaces => _finalPlaces;

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
        ///
        /// <para>
        /// <b>[DESIGN] <paramref name="payoutTable"/> channel (TASK-052).</b> An
        /// additive optional parameter, defaulting to null (every existing call
        /// site keeps compiling and behaves exactly as before this ticket — GDD
        /// §6.1 session defaults). When set, it is this round's per-definition
        /// <c>MicrogameDefinitionV2.PayoutTable</c> (GDD §11 schema, Annex D.2) and
        /// overrides the default at <see cref="CaptureResult"/> time: 4 entries
        /// (one per place) for a competitive round, or 2 entries [success, fail]
        /// for a coop round (TASK-050's already-validated shape convention — see
        /// <see cref="CoopPayoutTableLength"/>). Two other channels were
        /// considered and rejected as heavier than this ticket needs: extending
        /// <see cref="V2.IMicrogame"/> itself (would touch every v2 mechanic's
        /// contract for one field) and threading the whole
        /// <c>MicrogameDefinitionV2</c> object through (couples this class to
        /// <c>Barcade.Core.Content</c> for data it doesn't otherwise use — verb,
        /// duration and difficulty already flow through this same method's
        /// existing parameters, not a definition reference). A raw <c>int[]</c> is
        /// the minimal shape that satisfies this ticket; a future ticket can widen
        /// the channel if more definition data needs to reach the FSM.
        /// </para>
        /// </summary>
        public void SetActiveMicrogame(
            V2.IMicrogame microgame, string verb, float playDurationSeconds = 5f, float difficultyMult = 1f,
            int[] payoutTable = null)
        {
            _activeMicrogame = microgame;
            _activeVerb = verb ?? string.Empty;
            _activePlayDurationSeconds = playDurationSeconds;
            _activeDifficultyMult = difficultyMult;
            _activePayoutTable = payoutTable;
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
                            BeginFinalWager();
                    }
                    break;

                case SessionPhase.FinalWager:
                    TickFinalWager(dt);
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
                CaptureResult(isClimax: false);
        }

        /// <summary>
        /// Captures <see cref="_lastResult"/> from the active microgame if it
        /// genuinely finished, else leaves it null — shared by
        /// <see cref="TickRoundSubLoop"/>'s normal "just arrived at Result"
        /// transition, <see cref="BeginRound"/>'s same-call flush-to-Result edge
        /// case (LOW-1), and <see cref="TickFinalMg"/>'s climax completion, so
        /// every path captures identically.
        ///
        /// Also (TASK-051): feeds <see cref="_microgameWins"/> and whichever
        /// <see cref="SessionCounters"/> have a real source today (currently just
        /// Gatillo's REACCIONA latency — see class doc), and, for regular rounds
        /// only (<paramref name="isClimax"/> false), applies a payout to
        /// <see cref="Coins"/> — the active round's per-definition payoutTable
        /// (TASK-052) when set, else the GDD §6.1 default (see class doc).
        /// </summary>
        private void CaptureResult(bool isClimax)
        {
            _lastResult = _activeMicrogame != null && _activeMicrogame.IsFinished
                ? _activeMicrogame.GetResult()
                : (V2.MicrogameResult?)null;

            if (!_lastResult.HasValue) return; // ceiling cutoff without a legitimate finish: no counters, no payout, no wins credit

            V2.MicrogameResult result = _lastResult.Value;

            if (result.Kind == V2.ResultKind.Ranked)
            {
                for (int i = 0; i < result.Ranks.Length; i++)
                {
                    V2.PlayerRank rank = result.Ranks[i];
                    if (rank.Place == 1) _microgameWins[rank.Seat]++;

                    // Only REACCIONA's Metric is a documented latency-shaped value
                    // today (cumulative tick-latency, see ReaccionaMicrogame.GetResult) —
                    // no other v2 mechanic's Metric has a counters mapping yet.
                    //
                    // L1 (TASK-051 review fix round): rank.Metric is CUMULATIVE across
                    // every tanda this round (including the large FailurePenaltyTicks
                    // sentinel for any false start/DNF) -- converting it to
                    // "milliseconds" and feeding it as one latency sample is
                    // ORDERING-correct (WinnerOf's lower-is-better comparison still
                    // picks the genuinely best seat), but the stored value itself is
                    // NOT a meaningful per-tanda mean latency for GDD §15
                    // telemetry/HUD display. A true mean would need the tanda count
                    // (ReaccionaParams.Rounds), which isn't exposed through the
                    // IMicrogame/MicrogameResult contract -- extending that is a
                    // Microgames/V2 change, outside this fix round's Loop/Scoring
                    // surface. Documented as a known limitation rather than silently
                    // presented as a correct mean.
                    if (_activeMicrogame.Id == V2.MicrogameId.Reacciona)
                    {
                        float milliseconds = rank.Metric * (1000f / _config.TicksPerSecond);
                        _counters.RecordReaccionaLatency(rank.Seat, milliseconds);
                    }
                }

                if (!isClimax)
                {
                    var places = new int[4];
                    for (int i = 0; i < result.Ranks.Length; i++)
                        places[result.Ranks[i].Seat] = result.Ranks[i].Place;
                    // TASK-052: per-definition table overrides the GDD §6.1 default
                    // when set; ApplyCompetitive's own ValidateTable already throws
                    // if a non-null table isn't exactly 4 entries, so no separate
                    // length guard is needed on this branch.
                    int[] payoutTable = _activePayoutTable ?? PayoutRules.DefaultCompetitive;
                    PayoutRules.ApplyCompetitive(_coins, places, payoutTable);
                }
            }
            else
            {
                bool success = result.Kind == V2.ResultKind.CoopSuccess;
                if (success)
                    for (int i = 0; i < AllSlots.Length; i++)
                        if (_roster.IsActive(AllSlots[i])) _microgameWins[i]++;

                if (!isClimax)
                {
                    int payout;
                    if (_activePayoutTable != null)
                    {
                        // TASK-052: unlike ApplyCompetitive, ApplyCoop takes a single
                        // int (not a table), so this class owns the [success, fail]
                        // index lookup and must guard the shape itself.
                        if (_activePayoutTable.Length != CoopPayoutTableLength)
                        {
                            throw new ArgumentException(
                                $"coop payoutTable must have exactly {CoopPayoutTableLength} entries " +
                                $"[success, fail] (GDD §6.1 / TASK-050 shape); got {_activePayoutTable.Length}",
                                nameof(_activePayoutTable));
                        }
                        payout = success ? _activePayoutTable[0] : _activePayoutTable[1];
                    }
                    else
                    {
                        payout = success ? PayoutRules.DefaultCoopSuccess : PayoutRules.DefaultCoopFail;
                    }
                    PayoutRules.ApplyCoop(_coins, success, payout, ActiveSeatsMask());
                }
            }
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
                CaptureResult(isClimax: false);
            }

            _phase = mapped;

            if (_phase == SessionPhase.MgResult)
            {
                _phaseElapsed += dt;
                if (_phaseElapsed >= _config.MgResultSeconds)
                    EnterSimplePhase(SessionPhase.Intermission);
            }
        }

        private void BeginFinalWager()
        {
            for (int i = 0; i < 4; i++) _wagerChoice[i] = null;
            EnterSimplePhase(SessionPhase.FinalWager);
        }

        /// <summary>
        /// GDD §6.2's 5s stake-choice window — see class doc for the [ASSUMED]
        /// gesture mapping. Reads this machine's own InputInterpreter (T-101's
        /// forward-looking seam), same as Join's color-claim.
        /// </summary>
        private void TickFinalWager(float dt)
        {
            for (int i = 0; i < AllSlots.Length; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                WagerChoice? mapped = MapCardinalToWagerChoice(_interpreter.CollapsedCardinal(AllSlots[i]));
                if (mapped.HasValue) _wagerChoice[i] = mapped;
            }

            _phaseElapsed += dt;
            if (_phaseElapsed >= _config.FinalWagerSeconds)
                BeginFinalMg();
        }

        private static WagerChoice? MapCardinalToWagerChoice(CardinalDir dir)
        {
            switch (dir)
            {
                case CardinalDir.Left: return WagerChoice.Quarter;
                case CardinalDir.Up: return WagerChoice.Half;
                case CardinalDir.Right: return WagerChoice.ThreeQuarters;
                default: return null; // Down and None/neutral are no-ops (see class doc)
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
                CaptureResult(isClimax: true);
                FinishSessionScoring();
                EnterSimplePhase(SessionPhase.GameOver);
            }
        }

        /// <summary>
        /// GDD §6.2 pot resolution (AC1) then §6.3 bonus-star reveal + final
        /// ranking (AC3), run once, right after the climax's result is captured.
        /// </summary>
        private void FinishSessionScoring()
        {
            var climaxPlaces = new int[4];
            if (_lastResult.HasValue && _lastResult.Value.Kind == V2.ResultKind.Ranked)
            {
                V2.PlayerRank[] ranks = _lastResult.Value.Ranks;
                for (int i = 0; i < ranks.Length; i++)
                    climaxPlaces[ranks[i].Seat] = ranks[i].Place;
            }
            // else: the climax never finished legitimately, or (shouldn't happen —
            // SelectFinalRound is competitive by design) reported a non-ranked
            // result. [ASSUMED] leave every seat unplaced (0): the wager becomes a
            // no-op (nobody stakes or receives a share) — the safest fallback for
            // a degenerate case, consistent with this FSM's general "never block
            // or corrupt state on an absent/incomplete result" stance.

            _lastWagerResult = FinalWager.Resolve(_coins, _wagerChoice, climaxPlaces);
            _coins = _lastWagerResult.CoinsAfter;

            int mask = ActiveSeatsMask();
            var baseStars = new int[4]; // [ASSUMED] always 0 today — see class doc
            _lastBonusStars = BonusStarDraw.Draw(_scoringSeed, _counters, baseStars, _coins, _microgameWins, mask);
            _finalPlaces = FinalRanking.Rank(_lastBonusStars.StarsAfter, _coins, _microgameWins, mask);
        }

        private int ActiveSeatsMask()
        {
            int mask = 0;
            for (int i = 0; i < AllSlots.Length; i++)
                if (_roster.IsActive(AllSlots[i])) mask |= 1 << i;
            return mask;
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

            // TASK-051: clear every scoring/wager artifact too, or a fresh
            // session's Attract/Join cycle would still read back the PREVIOUS
            // session's coins/wins/counters/wager/podium before its own Join
            // even completes.
            _counters = new SessionCounters();
            _coins = new int[4];
            for (int i = 0; i < 4; i++) { _microgameWins[i] = 0; _wagerChoice[i] = null; }
            _lastWagerResult = null;
            _lastBonusStars = null;
            _finalPlaces = null;

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

    }
}
