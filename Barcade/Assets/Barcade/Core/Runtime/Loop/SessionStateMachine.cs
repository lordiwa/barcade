using System;
using System.Collections.Generic;
using Barcade.Core.Board;
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
    /// investment/robbery, GDD §5.3/§5.4) are BOARD-tile-driven. TASK-068 wires
    /// <see cref="BoardModel"/>'s BOARD_MOVE/BOARD_RESOLVE timing and its
    /// <see cref="BoardResolution.CoinFlows"/> into <see cref="Coins"/> (see
    /// "[ASSUMED] coin-pool reconciliation" below) but deliberately does NOT wire
    /// <see cref="BoardResolution.Stars"/> or <see cref="BoardResolution.Modifier"/>
    /// into <see cref="SessionCounters"/> or round difficulty — that is a separate,
    /// still-open follow-up (TASK-042 territory). So these three
    /// <see cref="SessionCounters"/> methods are still never called here; they
    /// stay correctly at their zero/tied default until that follow-up lands.
    /// <see cref="StarKind.Zen"/> stays unfed too, pending the human
    /// GDD fix for its truncated table row (see <c>SessionCounters.RecordZenMetric</c>'s
    /// own doc). Bonus-star baseStars (the per-seat count BEFORE the Game Over
    /// reveal) are likewise always 0 here for the same reason: GDD §5.3's only
    /// described pre-bonus star sources (Inversión tile ownership payouts,
    /// buying the Estrella tile) are both board-tile events not yet wired to
    /// <see cref="SessionCounters"/>. None of this weakens
    /// <see cref="BonusStarDraw"/>'s own exclusion-rule correctness (already
    /// covered by TASK-037's tests against varied baseStars) — it just means every
    /// session plays out with an honest, currently-true zero baseline until that
    /// follow-up wires <see cref="BoardResolution.Stars"/> through.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] Board instantiation (TASK-068).</b> A single <see cref="BoardModel"/>
    /// is constructed once per session, in <see cref="CompleteJoin"/> (the natural
    /// "session's board arc begins here" point — mirrors how <see cref="_roster"/>
    /// itself is only built there), using <see cref="BoardConfig.DefaultRingSize"/>
    /// (GDD §5.1's own default, N=20 — no config field threads a different ring
    /// size in today, so there is nothing else to choose from) at the session's own
    /// <see cref="SessionStateMachineConfig.TicksPerSecond"/> (a hard correctness
    /// tie, not a guess — <see cref="BoardModel"/>'s internal 8s/4s timeouts are
    /// tick counts derived from this rate, and <see cref="Tick"/> drives it exactly
    /// once per session tick, so the two rates must agree for "8 real seconds" to
    /// mean the same thing in both places). Its setup RNG derives from the session
    /// seed via <see cref="RngStream.Board"/> at round number -1
    /// (<see cref="BoardSetupRoundNumber"/>) — distinct from any real round's own
    /// 0-based <see cref="_roundIndex"/>, so the ring shuffle never shares a stream
    /// position with round 0's own move/resolve RNG. The instance is discarded (set
    /// to null) in <see cref="ResetToAttract"/> so a fresh session never reads back
    /// the previous one's ring/ownership/inventory state — mirrors the existing
    /// roster-clear precedent (LOW-2, TASK-024 review fix round).
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] Coin-pool reconciliation — single source of truth (TASK-068,
    /// GDD §5.6).</b> <see cref="Coins"/> is THE session's coin ledger; a coin is
    /// never live in two places at once. <see cref="BoardModel"/> owns its own
    /// per-seat balances internally (needed for its own tile-effect math — clamped
    /// non-negative purchases/transfers, Inversión deposits, etc.) but those
    /// balances are treated as a scratch working copy of <see cref="Coins"/> for
    /// exactly the span of one round's BOARD_MOVE/BOARD_RESOLVE: seeded FROM
    /// <see cref="Coins"/> via <c>BoardModel.SetBalances</c> the instant BOARD_MOVE
    /// begins (<see cref="EnterBoardMove"/>, which also picks up whatever the
    /// PRIOR round's microgame payout already added), then every
    /// <see cref="BoardResolution.CoinFlows"/> delta the round's <c>Resolve</c>
    /// call produced is replayed back onto <see cref="Coins"/> the instant
    /// BOARD_RESOLVE exits (<see cref="FinishBoardResolve"/> → <see cref="ApplyCoinFlows"/>).
    /// Between those two points only <see cref="BoardModel"/>'s own balances move;
    /// <see cref="Coins"/> is frozen at its pre-round value until the CoinFlows
    /// replay reconciles it in one shot — GDD §5.3's own "todo efecto que quita
    /// monedas... nunca desaparecen 'al banco' sin representación visual" invariant
    /// (<see cref="CoinDelta.Bank"/> is that visible pot) is exactly what makes the
    /// replay lossless: every delta names both an origin and a destination, so
    /// replaying the full set changes <see cref="Coins"/> by precisely what
    /// <see cref="BoardModel"/>'s own balances changed by. This was the DEFAULT
    /// reading flagged by both dev-t031 and the TASK-031 reviewer as needing human
    /// ratification (GDD §5.6: "~60% microgames / ~40% casillas" implies one shared
    /// pool); implemented here as the default per orchestrator instruction, pending
    /// that ratification.
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

        /// <summary>GDD §5.5: DobleVelocidad -> siguiente microjuego a 1.5x.</summary>
        private const float DobleVelocidadSpeedMult = 1.5f;

        /// <summary>GDD §5.5: LluviaDeMonedas -> +2 a todos (no "next microgame" qualifier -- applied immediately, see TASK-042 hand-off).</summary>
        private const int LluviaDeMonedasAmount = 2;

        /// <summary>
        /// TASK-068 [ASSUMED]: the round-number key used to derive the board's
        /// one-time ring-setup RNG (see class doc "[ASSUMED] Board instantiation").
        /// Deliberately outside the real, 0-based <see cref="_roundIndex"/> range so
        /// the setup shuffle never shares a <see cref="RngStream.Board"/> stream
        /// position with round 0's own per-round move/resolve RNG.
        /// </summary>
        private const int BoardSetupRoundNumber = -1;

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

        // TASK-068: the session's board meta-game (GDD T-113, §5). Constructed once
        // per session in CompleteJoin, discarded in ResetToAttract -- see class doc
        // "[ASSUMED] Board instantiation" and "[ASSUMED] Coin-pool reconciliation".
        private BoardModel _board;

        // TASK-042 (T-114): evento application (GDD §5.5) -- see FinishBoardResolve.
        // _lastRoundModifier is the modifier drawn on the round that just resolved
        // (None if no seat landed on Evento); BeginRound consumes it once for
        // DobleVelocidad's difficultyMult multiplier, but the field itself is left
        // for inspection (telemetry/tests/future HUD) rather than cleared right
        // after -- the NEXT FinishBoardResolve naturally overwrites it.
        private RoundModifier _lastRoundModifier;
        // Apagon (GDD §5.5): "el HUD de puntuaciones se oculta hasta el final" --
        // sticky for the whole rest of the session once drawn (unlike
        // _lastRoundModifier's one-round scope), since Core has no rendering of
        // its own this is exposed as a flag for a future HUD presenter to read.
        private bool _apagonActive;
        // The most recently completed BOARD_RESOLVE's full coin-flow picture --
        // BoardModel's own tile/weapon CoinDeltas plus this evento-application
        // step's own LluviaDeMonedas Bank flow, merged (see FinishBoardResolve).
        // Exposed the same way LastMicrogameResult/LastWagerResult/
        // LastBonusStarResult already are: "most recently completed X".
        private BoardResolution? _lastBoardResolution;

        // TASK-056 (GDD §3.2, line 224): dead-seat ("puesto muerto") detection. Per
        // claimed-human seat, ticks since its last input edge ("flanco") while in a
        // game state; a seat that reaches _idleThresholdTicks with no flanco is marked
        // HumanIdle and excluded from that round's payout (not eliminated — it resumes
        // on the next flanco). _prevRawDir remembers each seat's previous raw stick
        // direction so a direction change registers as a flanco alongside button edges.
        private readonly int[] _ticksSinceActivity = new int[4];
        private readonly Direction8[] _prevRawDir = new Direction8[4];
        private readonly int _idleThresholdTicks;

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
            // TASK-056: 45 s -> ticks (2700 @ 60 Hz). Rounded rather than truncated so a
            // fractional seconds*rate lands on the nearest whole tick, and clamped to at
            // least 1 so a (validated-positive) sub-tick window can still ever fire.
            _idleThresholdTicks = Math.Max(1, (int)Math.Round(_config.IdleTimeoutSeconds * _config.TicksPerSecond));
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

        /// <summary>Per-seat coins: payout accumulation through the round loop, then post-wager totals from FinalMg onward. See the class doc's "Regular-round payout application" note (per-definition payoutTable when set, else the GDD §6.1 fallback).</summary>
        public int[] Coins => _coins;

        /// <summary>
        /// TASK-068: read-only view of the board's own state (ring, positions,
        /// balances, ownership, weapons — see <see cref="BoardSnapshot"/>) once
        /// BOARD_MOVE has begun at least once this session; null before then
        /// (Attract/Join). Exposed for the same reason <see cref="Roster"/>/
        /// <see cref="Counters"/> are — HUD rendering (BOARD_MOVE's live stop-meter,
        /// GDD §8) and test/determinism verification. <see cref="Coins"/> remains
        /// the single source of truth for the coin economy (see class doc
        /// "[ASSUMED] Coin-pool reconciliation") — this snapshot's own
        /// <c>Balances</c> field mirrors <see cref="Coins"/> exactly the instant
        /// every BOARD_RESOLVE completes, by construction, never diverges.
        /// </summary>
        public BoardSnapshot BoardSnapshot => _board?.GetSnapshot();

        /// <summary>TASK-042: pass-through of the board's own weapon-window state (see <see cref="BoardModel.WeaponWindowActive"/>) -- for a HUD/bot to know which BOARD_RESOLVE stick semantics currently apply. False before BOARD_MOVE has begun.</summary>
        public bool BoardWeaponWindowActive => _board?.WeaponWindowActive ?? false;

        /// <summary>TASK-042 (GDD §5.5): the evento modifier drawn on the round that just resolved BOARD_RESOLVE (<see cref="RoundModifier.None"/> if nobody landed on an Evento square). Applies to that SAME round's own microgame (see <see cref="BeginRound"/>), not the following round's.</summary>
        public RoundModifier LastRoundModifier => _lastRoundModifier;

        /// <summary>TASK-042 (GDD §5.5): true from the round Apagon is drawn until GameOver ("hasta el final"); false before then and after a session reset. A future HUD presenter reads this to suppress score display -- Core does no rendering itself.</summary>
        public bool ApagonActive => _apagonActive;

        /// <summary>TASK-042: every coin flow from the most recently completed BOARD_RESOLVE -- BoardModel's own tile/weapon CoinDeltas plus this step's own evento-application flows (LluviaDeMonedas), merged. Null before BOARD_RESOLVE has completed at least once this session.</summary>
        public BoardResolution? LastBoardResolution => _lastBoardResolution;

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

            // TASK-056: dead-seat detection reads the edges the interpreter just
            // derived, against the phase as of the start of this tick. Runs before the
            // phase switch so the state entered on a previous tick is the one whose
            // "estados de juego" membership gates this tick's idle accounting.
            UpdateDeadSeatTracking();

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
                    TickBoardMove(input, dt);
                    break;

                case SessionPhase.BoardResolve:
                    TickBoardResolve(input, dt);
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
                            EnterBoardMove();
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

            // TASK-056: dead-seat tracking begins with the roster. Zero every seat's
            // idle counter and seed _prevRawDir to neutral so the first game-state tick
            // compares against None (a seat already holding a stick at Join-complete then
            // reads as one harmless flanco, which only resets an already-zero counter).
            for (int i = 0; i < 4; i++) { _ticksSinceActivity[i] = 0; _prevRawDir[i] = Direction8.None; }

            _roundIndex = 0;
            // TASK-068: a fresh BoardModel per session -- see class doc "[ASSUMED]
            // Board instantiation" for the ring-size/tick-rate/RNG-derivation choices.
            _board = new BoardModel(
                new BoardConfig(BoardConfig.DefaultRingSize, _config.TicksPerSecond),
                SeededRandom.Derive(_scoringSeed, BoardSetupRoundNumber, RngStream.Board));
            EnterBoardMove();
        }

        /// <summary>
        /// TASK-068: BOARD_MOVE entry -- the "seed FROM Coins" half of the coin-pool
        /// reconciliation contract (class doc), plus starting the board's own
        /// stop-meter phase. Called from both <see cref="CompleteJoin"/> (round 1)
        /// and the Intermission handler's loop-back (every subsequent round).
        /// </summary>
        private void EnterBoardMove()
        {
            EnterSimplePhase(SessionPhase.BoardMove);
            _board.SetBalances(_coins);
            // BeginMovePhase's rng is accepted but never consumed by BoardModel (see
            // its own class doc) -- still derived via RngStream.Board/_roundIndex,
            // never a bare `new Random()`, per the GDD §13 RNG-only-through-injection
            // rule the fast suite's analyzer test already enforces everywhere else.
            _board.BeginMovePhase(SeededRandom.Derive(_scoringSeed, _roundIndex, RngStream.Board));
        }

        private void TickBoardMove(in V2.InputSnapshot input, float dt)
        {
            _phaseElapsed += dt;
            _board.TickMove(input);
            if (_board.MoveFinished) EnterBoardResolve();
        }

        private void EnterBoardResolve()
        {
            EnterSimplePhase(SessionPhase.BoardResolve);
            _board.BeginResolvePhase();
        }

        private void TickBoardResolve(in V2.InputSnapshot input, float dt)
        {
            _phaseElapsed += dt;
            _board.TickResolve(input);
            if (_board.ResolveFinished) FinishBoardResolve();
        }

        /// <summary>
        /// TASK-068/042: BOARD_RESOLVE exit -- resolves this round's landed-square
        /// and weapon effects, replays the resulting <see cref="BoardResolution.CoinFlows"/>
        /// back onto <see cref="Coins"/> (the "apply BACK" half of the
        /// reconciliation contract, class doc), applies the drawn evento (GDD
        /// §5.5 -- see "evento application" below), then starts the round proper.
        /// <see cref="BoardResolution.Stars"/> is deliberately not consumed here —
        /// see class doc.
        ///
        /// <para>
        /// <b>Evento application (TASK-042, GDD §5.5).</b> <see cref="_lastRoundModifier"/>
        /// captures the draw for <see cref="BeginRound"/> to consume (DobleVelocidad's
        /// difficultyMult multiplier). LluviaDeMonedas is applied HERE, immediately
        /// (GDD names no "next microgame" qualifier for it, unlike the other four)
        /// as a Bank-sourced <see cref="CoinDelta"/> to every seat
        /// <see cref="ActiveSeatsMask"/> covers — [ASSUMED] "a todos" reads as every
        /// player still in the session, including a currently-Idle one (this is an
        /// ambient/luck event, not a skill-based round reparto, so TASK-056's idle
        /// exclusion does not apply here). Apagon sets the sticky
        /// <see cref="_apagonActive"/> flag. MuerteSubita/ModoPinata reach
        /// <see cref="_lastRoundModifier"/> too but have no mechanic today with a
        /// "lives"/"damage-spills-coins" concept to hook a behavioral effect onto
        /// (Esquiva is already unconditionally one-hit) -- an honest, flagged gap,
        /// not a faked one (see hand-off).
        /// </para>
        /// </summary>
        private void FinishBoardResolve()
        {
            BoardResolution resolution = _board.Resolve(SeededRandom.Derive(_scoringSeed, _roundIndex, RngStream.Board));
            ApplyCoinFlows(resolution.CoinFlows);

            _lastRoundModifier = resolution.Modifier;
            CoinDelta[] combinedFlows = resolution.CoinFlows;

            if (resolution.Modifier == RoundModifier.LluviaDeMonedas)
            {
                var eventoFlows = new List<CoinDelta>();
                int mask = ActiveSeatsMask();
                for (int i = 0; i < AllSlots.Length; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    eventoFlows.Add(new CoinDelta(CoinDelta.Bank, i, LluviaDeMonedasAmount));
                }
                ApplyCoinFlows(eventoFlows.ToArray());
                // Re-sync BoardModel's own balances to match -- LluviaDeMonedas
                // only touched _coins above, and the class doc's own reconciliation
                // invariant ("this snapshot's Balances mirrors Coins exactly the
                // instant every BOARD_RESOLVE completes") must still hold once this
                // method returns. Reuses the same SetBalances seam EnterBoardMove
                // already uses to seed FROM Coins.
                _board.SetBalances(_coins);

                var merged = new CoinDelta[resolution.CoinFlows.Length + eventoFlows.Count];
                Array.Copy(resolution.CoinFlows, merged, resolution.CoinFlows.Length);
                eventoFlows.CopyTo(merged, resolution.CoinFlows.Length);
                combinedFlows = merged;
            }
            else if (resolution.Modifier == RoundModifier.Apagon)
            {
                _apagonActive = true;
            }

            _lastBoardResolution = new BoardResolution(combinedFlows, resolution.Stars, resolution.Modifier);

            BeginRound();
        }

        /// <summary>
        /// GDD §5.3's economic invariant ("todo efecto que quita monedas... nunca
        /// desaparecen 'al banco' sin representación visual") is exactly what makes
        /// this replay lossless onto <see cref="Coins"/> — <see cref="CoinDelta.Bank"/>
        /// is the visible pot, never a seat, so only seat-indexed origins/destinations
        /// touch <see cref="_coins"/> here.
        /// </summary>
        private void ApplyCoinFlows(CoinDelta[] flows)
        {
            foreach (CoinDelta flow in flows)
            {
                if (flow.Origin != CoinDelta.Bank) _coins[flow.Origin] -= flow.Amount;
                if (flow.Destination != CoinDelta.Bank) _coins[flow.Destination] += flow.Amount;
            }
        }

        private void BeginRound()
        {
            _interpreter.Reset();
            // TASK-042 (GDD §5.5): DobleVelocidad multiplies the difficultyMult
            // this round's microgame is Initialize'd with -- the exact channel
            // SetActiveMicrogame's own doc already earmarked for a future D_final-
            // style multiplier (GDD §9.1). Applies to the round that just resolved
            // BOARD_RESOLVE (_lastRoundModifier), never the climax's own
            // BeginFinalMg (see class doc "evento application" for why: no fresh
            // BOARD_RESOLVE immediately precedes FinalMg).
            float effectiveDifficultyMult = _activeDifficultyMult
                * (_lastRoundModifier == RoundModifier.DobleVelocidad ? DobleVelocidadSpeedMult : 1f);
            _activeMicrogame?.Initialize(_rng, _roster, effectiveDifficultyMult);

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
                    // TASK-056 (GDD §3.2, line 224): an Idle (dead) seat is excluded from
                    // THIS round's reparto. Zeroing its place makes ApplyCompetitive skip
                    // it (place 0 == absent == no payout), so no coins are created or
                    // destroyed for the excluded seat and every other seat's per-place
                    // payout is untouched. Win-crediting above intentionally still uses
                    // rank.Place — the win count is a tiebreak, not part of the "reparto".
                    for (int i = 0; i < AllSlots.Length; i++)
                        if (_roster.Seats[i] == V2.SeatState.HumanIdle) places[i] = 0;
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
                    // TASK-056: pay only active, non-Idle seats — a dead seat is excluded
                    // from the coop reparto too (with no Idle seat this is identical to
                    // ActiveSeatsMask, so TASK-050/052 coop payouts are unaffected).
                    PayoutRules.ApplyCoop(_coins, success, payout, PayoutEligibleSeatsMask());
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

        /// <summary>
        /// TASK-056: seats eligible for a round's coin reparto — active (not
        /// <see cref="V2.SeatState.Empty"/>) AND not <see cref="V2.SeatState.HumanIdle"/>.
        /// A Bot counts as eligible; only a dead (Idle) human is excluded. Distinct from
        /// <see cref="ActiveSeatsMask"/> (which keeps Idle seats — they are still in the
        /// session for FINAL_WAGER, bonus stars and the final podium, just not paid the
        /// round they were dead).
        /// </summary>
        private int PayoutEligibleSeatsMask()
        {
            int mask = 0;
            for (int i = 0; i < AllSlots.Length; i++)
            {
                V2.SeatState seat = _roster.Seats[i];
                if (seat != V2.SeatState.Empty && seat != V2.SeatState.HumanIdle)
                    mask |= 1 << i;
            }
            return mask;
        }

        /// <summary>
        /// TASK-056 (GDD §3.2, line 224). Once the roster exists and we are in a game
        /// state, accumulate per claimed-human seat the ticks since its last input edge;
        /// a seat that reaches <see cref="_idleThresholdTicks"/> with no edge is marked
        /// <see cref="V2.SeatState.HumanIdle"/> (excluded from that round's reparto), and
        /// any edge resets its counter and resumes an Idle seat to
        /// <see cref="V2.SeatState.Human"/> ("puede volver pulsando"). A "flanco" is a
        /// debounced button press/release edge OR a change in the raw stick direction
        /// since the previous tick — [ASSUMED] the activity source, since the GDD names
        /// no specific stream (any button edge OR stick movement counts).
        /// </summary>
        private void UpdateDeadSeatTracking()
        {
            if (_roster.Seats == null) return;       // no roster yet (Attract/Join)
            if (!IsGameState(_phase)) return;        // only "estados de juego"

            for (int i = 0; i < AllSlots.Length; i++)
            {
                V2.SeatState seat = _roster.Seats[i];
                if (seat != V2.SeatState.Human && seat != V2.SeatState.HumanIdle)
                    continue; // Empty/Bot seats are not subject to dead-seat detection

                PlayerSlot slot = AllSlots[i];
                Direction8 raw = _interpreter.RawDirection(slot);
                bool flanco = _interpreter.ButtonPressedThisTick(slot)
                           || _interpreter.ButtonReleasedThisTick(slot)
                           || raw != _prevRawDir[i];
                _prevRawDir[i] = raw;

                if (flanco)
                {
                    _ticksSinceActivity[i] = 0;
                    if (seat == V2.SeatState.HumanIdle)
                        _roster.Seats[i] = V2.SeatState.Human; // resume — not eliminated
                }
                else if (seat == V2.SeatState.Human)
                {
                    _ticksSinceActivity[i]++;
                    if (_ticksSinceActivity[i] >= _idleThresholdTicks)
                        _roster.Seats[i] = V2.SeatState.HumanIdle;
                }
                // already HumanIdle with no flanco: stays Idle, counter frozen.
            }
        }

        /// <summary>
        /// TASK-056 [ASSUMED] "estados de juego": the round loop
        /// (BoardMove..Intermission) plus FinalWager/FinalMg — i.e. everything from the
        /// first board move through the climax. Attract/Join/GameOver are the
        /// lobby/game-over menus, where a claimed seat's idleness is meaningless, so the
        /// idle timer neither runs nor accumulates there.
        /// </summary>
        private static bool IsGameState(SessionPhase phase)
        {
            switch (phase)
            {
                case SessionPhase.BoardMove:
                case SessionPhase.BoardResolve:
                case SessionPhase.MgIntro:
                case SessionPhase.MgPlay:
                case SessionPhase.MgResult:
                case SessionPhase.Intermission:
                case SessionPhase.FinalWager:
                case SessionPhase.FinalMg:
                    return true;
                default: // Attract, Join, GameOver
                    return false;
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

            // TASK-068: discard the previous session's board too -- its ring/
            // ownership/inventory state must not leak into a fresh session (same
            // rationale as the roster clear above); CompleteJoin builds a new one.
            _board = null;

            // TASK-042: a fresh session must not read back the previous one's
            // evento state either (same rationale as every other clear here).
            _lastRoundModifier = RoundModifier.None;
            _apagonActive = false;
            _lastBoardResolution = null;

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
