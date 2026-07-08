using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Board;
using Barcade.Core.Microgames.V2;
using Barcade.Core.Scoring;
// Same collision as ReaccionaMicrogameTests/ApuntaMicrogameV2Tests: this file's
// namespace (Barcade.Core.Tests) is lexically nested inside Barcade.Core, so an
// unqualified name resolves against Barcade.Core's own members before any
// "using"-imported namespace. Renamed aliases sidestep it.
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-102 — coverage for <see cref="SessionStateMachine"/> (§2.1 session FSM,
    /// §2.2 timeout budget table): full state graph traversal, the zero-input/zero-
    /// injection resilience invariant (AC2), the MgPlay/FinalMg-only gameplay-input
    /// boundary (AC3), board pass-through stubs (AC4), determinism (AC5), the exact
    /// GDD-sourced timeouts, and the [ASSUMED] decisions flagged in the class doc.
    ///
    /// AC6 (existing RoundPhaseMachine consumers keep compiling/passing) is verified
    /// by the full suite run, not a test in this file — RoundPhaseMachine.cs and
    /// RoundPhaseMachineTests.cs are untouched.
    ///
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class SessionStateMachineTests
    {
        // ── Test doubles ─────────────────────────────────────────────────────────

        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(PlayerSlot slot, bool pressed, Direction8 stick = Direction8.None)
                => _players[(int)slot] = new PlayerInput(stick, pressed);

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        /// <summary>
        /// A minimal injectable IMicrogame (see SessionStateMachine's class doc:
        /// "a minimal injectable IMicrogame fake in tests is fine — do NOT wire the
        /// real sequencer/pool"). Finishes after a configured tick count (or never,
        /// for int.MaxValue), and throws from GetResult() if called before
        /// IsFinished — matching the real mechanics' documented contract, so a
        /// SessionStateMachine bug that calls GetResult() too early surfaces as a
        /// loud exception rather than a silently-wrong value.
        /// </summary>
        private sealed class FakeMicrogame : V2Microgame
        {
            private readonly int _finishAfterTicks;
            private readonly V2Result _result;
            private int _ticksSeen;

            public int TickCount { get; private set; }
            public bool InitializeCalled { get; private set; }
            public float LastDifficultyMult { get; private set; } = float.NaN;

            public FakeMicrogame(int finishAfterTicks, V2Result? result = null, MicrogameId id = MicrogameId.Reacciona)
            {
                _finishAfterTicks = finishAfterTicks;
                _result = result ?? new V2Result(ResultKind.CoopSuccess, Array.Empty<PlayerRank>(), 0);
                Id = id;
            }

            public MicrogameId Id { get; }

            public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult)
            {
                InitializeCalled = true;
                LastDifficultyMult = difficultyMult;
                _ticksSeen = 0;
            }

            public void Tick(in V2Snapshot input)
            {
                _ticksSeen++;
                TickCount++;
            }

            public bool IsFinished => _ticksSeen >= _finishAfterTicks;

            public V2Result GetResult()
            {
                if (!IsFinished) throw new InvalidOperationException("GetResult() called before IsFinished.");
                return _result;
            }

            public RenderState GetRenderState() => new RenderState(0, 0);
        }

        // ── Config helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Short-but-nonzero durations for every state so a full round trip
        /// completes in well under a second of simulated time, without needing to
        /// simulate 30s/20s real GDD timeouts in every test. Determinism/graph-shape
        /// tests use this; timeout-pinning tests use <see cref="SessionStateMachineConfig.GddDefaults"/>.
        /// </summary>
        private static SessionStateMachineConfig FastConfig(int totalRounds) => new SessionStateMachineConfig(
            joinTimeoutSeconds: 30f,
            joinMinReady: 2,
            mgIntroSeconds: 0.05f,
            mgResultSeconds: 0.05f,
            intermissionSeconds: 0.05f,
            finalWagerSeconds: 0.05f,
            gameOverSeconds: 0.05f,
            totalRounds: totalRounds,
            ticksPerSecond: 60);

        /// <summary>
        /// Claims all 4 seats so Join exits within its debounce-confirm window
        /// regardless of the exact exit rule -- used by every test that just wants
        /// to get PAST Join quickly to exercise some other state, not to test
        /// Join's own mechanics (those use a bespoke <see cref="FakeInputs"/>
        /// instance with precise per-seat control instead; see the "AC1: Join
        /// semantics" tests below). Post-MEDIUM-1 (TASK-024 review fix round),
        /// claiming only 2 seats would no longer exit Join quickly -- Join now
        /// stays open until all 4 claim or the timeout elapses.
        /// </summary>
        private static FakeInputs JoinReadyInputs()
        {
            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, true);
            inputs.Set(PlayerSlot.Azul, true);
            inputs.Set(PlayerSlot.Amarillo, true);
            inputs.Set(PlayerSlot.Verde, true);
            return inputs;
        }

        // TASK-068 note (applies to every widened tick-loop bound below): BOARD_MOVE/
        // BOARD_RESOLVE now tick a REAL BoardModel instead of an exactly-1-tick
        // pass-through stub. With JoinReadyInputs (button held from tick 0),
        // BOARD_MOVE still resolves in exactly 1 tick -- the stop-meter freezes the
        // instant it goes live, since the button is already down (see
        // BoardModel.TickMove: level-triggered, not edge-triggered). BOARD_RESOLVE is
        // the real variable: its <=4s Inversión deposit-choice window (240 ticks @
        // 60Hz) genuinely elapses whenever a seat's board position lands on an
        // Inversión square that round -- which depends on the session-seeded ring
        // layout, not something a probe budget can pin or avoid per test. Rather than
        // hand-verify which of this file's ~40 seeds happen to dodge Inversión
        // (fragile against any future ring/seed/shuffle change), every probe loop
        // that must survive at least one full BOARD_MOVE+BOARD_RESOLVE pass gets a
        // flat, generous margin added to its pre-existing bound below -- a probe-
        // budget widening only, never a loosened assertion: every widened loop still
        // exits the moment its target phase is actually reached, and still fails if
        // that phase is never reached. See the TASK-068 hand-off for the itemized
        // list of which loops were widened and why.

        // ── AC1: full graph shape ────────────────────────────────────────────────

        [Test]
        public void FullGraph_WalksEveryStateInOrder()
        {
            var fsm = new SessionStateMachine(new SeededRandom(1), FastConfig(totalRounds: 2));
            var fake = new FakeMicrogame(finishAfterTicks: 2);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.05f);

            var inputs = JoinReadyInputs();
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Attract));

            var seq = new List<SessionPhase> { fsm.CurrentPhase };
            fsm.InsertCredit();
            if (fsm.CurrentPhase != seq[seq.Count - 1]) seq.Add(fsm.CurrentPhase);

            bool looped = false;
            for (int t = 0; t < 20000 && !looped; t++)
            {
                fsm.Tick(inputs.Build(t));
                if (fsm.CurrentPhase != seq[seq.Count - 1])
                {
                    seq.Add(fsm.CurrentPhase);
                    if (fsm.CurrentPhase == SessionPhase.Attract) looped = true;
                }
            }

            Assert.That(looped, Is.True, "session must loop back to Attract within the probe bound");
            CollectionAssert.AreEqual(new[]
            {
                SessionPhase.Attract,
                SessionPhase.Join,
                SessionPhase.BoardMove, SessionPhase.BoardResolve, SessionPhase.MgIntro, SessionPhase.MgPlay, SessionPhase.MgResult, SessionPhase.Intermission,
                SessionPhase.BoardMove, SessionPhase.BoardResolve, SessionPhase.MgIntro, SessionPhase.MgPlay, SessionPhase.MgResult, SessionPhase.Intermission,
                SessionPhase.FinalWager,
                SessionPhase.FinalMg,
                SessionPhase.GameOver,
                SessionPhase.Attract,
            }, seq);
        }

        [Test]
        public void Join_TwoReadyPlayers_StaysOpenUntilAllFourOrTimeout()
        {
            // MEDIUM-1 (TASK-024 review fix round, [ASSUMED] orchestrator ruling):
            // reaching GDD §2.1's documented ">=2 listos" minimum used to close
            // Join the instant it was reached, locking out seats 3/4 mid-claim.
            // GDD §2.1's ">=2 listos" label is ambiguous between "the minimum to
            // proceed" and "closes the window right then"; GDD §9.3 budgets up to
            // 30s for color choice; and the social-cabinet premise makes the
            // lockout the worst reading. Join now stays open with only 2 ready
            // until either all 4 claim or the 30s timeout elapses (unchanged).
            var fsm = new SessionStateMachine(new SeededRandom(2), FastConfig(totalRounds: 1));
            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, true);
            inputs.Set(PlayerSlot.Azul, true);
            fsm.InsertCredit();

            for (int t = 0; t < 100; t++)
                fsm.Tick(inputs.Build(t));

            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Join), "2 ready seats must not close Join early under the new semantics");
            Assert.That(fsm.ReadyCount, Is.EqualTo(2));
        }

        [Test]
        public void Join_ThirdAndFourthPlayerCanStillJoinAfterMinimumReached()
        {
            // MEDIUM-1 pin: a seat claiming AFTER the >=2 minimum is already
            // reached must still register -- proves Join is genuinely still
            // reading input, not just idling until the timeout.
            var fsm = new SessionStateMachine(new SeededRandom(3), FastConfig(totalRounds: 1));
            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, true);
            inputs.Set(PlayerSlot.Azul, true);
            fsm.InsertCredit();

            for (int t = 0; t < 10; t++) fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Join));
            Assert.That(fsm.ReadyCount, Is.EqualTo(2));

            // Amarillo claims after the minimum is already reached; held long
            // enough to clear the 2-tick-confirm debounce.
            inputs.Set(PlayerSlot.Amarillo, true);
            for (int t = 10; t < 20; t++) fsm.Tick(inputs.Build(t));

            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Join), "must still be open -- only 3 of 4 seats have claimed");
            Assert.That(fsm.ReadyCount, Is.EqualTo(3), "the 3rd seat's claim after the minimum was reached must still register");
        }

        [Test]
        public void Join_AllFourClaimed_AdvancesBeforeTimeout()
        {
            // MEDIUM-1 pin: the new early-exit condition (all 4 claimed).
            var fsm = new SessionStateMachine(new SeededRandom(4), FastConfig(totalRounds: 1));
            var inputs = JoinReadyInputs(); // claims all 4

            fsm.InsertCredit();
            for (int t = 0; t < 10 && fsm.CurrentPhase == SessionPhase.Join; t++)
                fsm.Tick(inputs.Build(t));

            Assert.That(fsm.CurrentPhase, Is.Not.EqualTo(SessionPhase.Join), "all 4 seats claimed must advance well before the 30s timeout");
            Assert.That(fsm.ReadyCount, Is.EqualTo(4));
        }

        // ── AC2: zero input, zero injection — still reaches GameOver ────────────

        [Test]
        public void ZeroInputSession_StillReachesGameOver()
        {
            // "Zero input" = zero PLAYER input for the life of an already-started
            // session; InsertCredit() is the control-plane coin-insert, not one of
            // §3.1's four universal gestures — see class doc's [ASSUMED] note. No
            // microgame is ever injected either, so MgPlay/FinalMg auto-skip.
            var fsm = new SessionStateMachine(new SeededRandom(3), SessionStateMachineConfig.GddDefaults);
            var inputs = new FakeInputs(); // all-neutral, never touched again
            fsm.InsertCredit();

            bool sawGameOver = false;
            bool loopedToAttract = false;
            for (int t = 0; t < 20000 && !loopedToAttract; t++)
            {
                fsm.Tick(inputs.Build(t));
                if (fsm.CurrentPhase == SessionPhase.GameOver) sawGameOver = true;
                if (sawGameOver && fsm.CurrentPhase == SessionPhase.Attract) loopedToAttract = true;
            }

            Assert.That(sawGameOver, Is.True, "a fully zero-input, zero-injection session must still reach GameOver — no state may block on an absent player");
            Assert.That(loopedToAttract, Is.True, "GameOver's own 20s timeout must still return the session to Attract");
        }

        [Test]
        public void Join_ZeroReadyPlayers_StillAdvancesAfterTimeout()
        {
            // [ASSUMED]: Join with < JoinMinReady ready seats still advances once
            // the 30s timeout elapses (never routes back to Attract) — required for
            // AC2 to be achievable at all. See class doc.
            var config = new SessionStateMachineConfig(
                joinTimeoutSeconds: 0.05f, joinMinReady: 2, mgIntroSeconds: 0.05f, mgResultSeconds: 0.05f,
                intermissionSeconds: 0.05f, finalWagerSeconds: 0.05f, gameOverSeconds: 0.05f,
                totalRounds: 1, ticksPerSecond: 60);
            var fsm = new SessionStateMachine(new SeededRandom(4), config);
            var inputs = new FakeInputs(); // nobody ever claims a seat
            fsm.InsertCredit();

            for (int t = 0; t < 20 && fsm.CurrentPhase == SessionPhase.Join; t++)
                fsm.Tick(inputs.Build(t));

            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardMove), "Join's timeout must force progress even with 0 ready seats");
            Assert.That(fsm.ReadyCount, Is.EqualTo(0));
        }

        // ── AC3: only MgPlay/FinalMg forward gameplay input ──────────────────────

        [Test]
        public void GameplayInput_OnlyForwardedDuringMgPlayOrFinalMg()
        {
            // GDD §2.1's literal invariant names MG_PLAY; FINAL_MG is this
            // machine's Play-equivalent state for the climax microgame (the GDD
            // diagram gives FINAL_MG no separate intro/result sub-phases of its
            // own) — see SessionStateMachine's class doc for the [ASSUMED] reading.
            // This test pins the interpretation as implemented: input reaches the
            // active microgame in exactly these two states and nowhere else.
            //
            // LOW-3 (TASK-024 review fix round): checks every single tick's
            // TickCount delta, not just a total-count match over the whole
            // session -- a total-count match can hold even under a boundary
            // off-by-one (forwarding one tick too early and missing one tick too
            // late) as long as the over/under-count happens to cancel out.
            //
            // The exact per-tick predicate the implementation satisfies at both
            // boundaries (verified by reading TickRoundSubLoop/TickFinalMg):
            // MgPlay forwards on its entry tick -- the same tick CommandShow's
            // duration elapses, since TickRoundSubLoop checks the inner phase
            // *after* ticking the inner machine -- but NOT on its exit tick (the
            // inner phase has already advanced to Result by the time that same
            // check runs that tick). FinalMg forwards on both its entry AND exit
            // ticks, since TickFinalMg always ticks the microgame unconditionally
            // before checking whether this same tick also ends the round.
            // postPhase==MgPlay (catches MgPlay's entry, misses its exit) OR
            // prePhase==FinalMg (catches FinalMg's entry once dispatch has begun,
            // and its exit) together match exactly.
            var fsm = new SessionStateMachine(new SeededRandom(5), FastConfig(totalRounds: 1));
            var fake = new FakeMicrogame(finishAfterTicks: int.MaxValue); // ceiling drives every transition, never self-finishes
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.05f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            int forwardedTickCount = 0;
            bool sawForwarding = false;
            bool loopedToAttract = false;
            int previousTickCount = fake.TickCount;
            for (int t = 0; t < 5000 && !loopedToAttract; t++)
            {
                SessionPhase prePhase = fsm.CurrentPhase;
                fsm.Tick(inputs.Build(t));
                SessionPhase postPhase = fsm.CurrentPhase;

                bool shouldForward = postPhase == SessionPhase.MgPlay || prePhase == SessionPhase.FinalMg;
                int delta = fake.TickCount - previousTickCount;
                Assert.That(delta, Is.EqualTo(shouldForward ? 1 : 0),
                    $"tick {t}: prePhase={prePhase}, postPhase={postPhase}, shouldForward={shouldForward}, but the microgame's TickCount changed by {delta}");
                previousTickCount = fake.TickCount;

                if (shouldForward) { sawForwarding = true; forwardedTickCount++; }
                if (t > 50 && postPhase == SessionPhase.Attract) loopedToAttract = true;
            }

            Assert.That(loopedToAttract, Is.True, "test setup sanity: the probe must complete a full session");
            Assert.That(sawForwarding, Is.True, "test setup sanity: MgPlay/FinalMg forwarding must actually have been observed");
            Assert.That(fake.TickCount, Is.EqualTo(forwardedTickCount), "running total must match too -- the per-tick checks above already prove it tick-by-tick");
        }

        [Test]
        public void JoinButtonHeldThroughRestOfSession_NeverMisreadAsGameplayInput()
        {
            // Rojo/Azul stay held the whole session (as JoinReadyInputs leaves
            // them) — proves the FSM's own InputInterpreter reading those presses
            // for Join's color-claim never leaks into forwarding to the microgame
            // outside MgPlay/FinalMg.
            var fsm = new SessionStateMachine(new SeededRandom(6), FastConfig(totalRounds: 1));
            var fake = new FakeMicrogame(finishAfterTicks: int.MaxValue);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.05f);
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 30 && fsm.CurrentPhase != SessionPhase.MgIntro; t++)
                fsm.Tick(inputs.Build(t));

            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro));
            Assert.That(fake.TickCount, Is.EqualTo(0), "no gameplay input may reach the microgame before MgPlay, even with buttons already held from Join");
        }

        // ── AC1/AC4 (TASK-068): BOARD_MOVE/BOARD_RESOLVE now drive a real
        //    BoardModel instead of an exactly-1-tick pass-through stub. No new
        //    SessionPhase and no transition-graph change (verified indirectly:
        //    FullGraph_WalksEveryStateInOrder above still asserts the exact same
        //    ordered list of DISTINCT phases -- if a phase had been added/removed
        //    or an edge changed, that sequence assertion would fail). ─────────────

        /// <summary>
        /// Drives a fresh session to BOARD_MOVE (buttons already held from Join,
        /// so the stop-meter is still frozen-eligible at tick 0) and searches a
        /// small, deterministic range of construction seeds for one whose ring
        /// reaches a square of the requested <see cref="BoardTileType"/> within a
        /// single tap (offsets 1..6 -- GDD §5.2's own medidor range, reachable from
        /// every seat's shared starting position 0). This sidesteps hand-computing
        /// the seeded ring shuffle while keeping every test fully deterministic
        /// (same seed each run -> same ring -> same outcome).
        /// </summary>
        private static (SessionStateMachine fsm, FakeInputs inputs, int tapOffset) DriveToBoardMove_RingReaches(BoardTileType wanted)
        {
            for (int seed = 1; seed < 300; seed++)
            {
                var fsm = new SessionStateMachine(new SeededRandom(seed), FastConfig(totalRounds: 1));
                var inputs = JoinReadyInputs();
                fsm.InsertCredit();
                for (int t = 0; t < 40 && fsm.CurrentPhase != SessionPhase.BoardMove; t++)
                    fsm.Tick(inputs.Build(t));
                Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardMove), "test setup sanity");

                BoardTileType[] ring = fsm.BoardSnapshot.Ring;
                for (int offset = 1; offset <= 6; offset++)
                    if (ring[offset] == wanted)
                        return (fsm, inputs, offset);
            }
            Assert.Fail($"no seed in [1,300) reaches a {wanted} square within one tap of BOARD_MOVE -- widen the search range");
            throw new InvalidOperationException("unreachable");
        }

        [Test]
        public void BoardMove_StaysLive_UntilTapOrTimeout_NotOneTickStub()
        {
            // AC1: BOARD_MOVE now ticks the REAL BoardModel (TASK-031) -- a seat's
            // move distance depends on WHEN it taps (GDD §5.2's medidor de parada),
            // not a hardcoded <=1-tick advance.
            var fsm = new SessionStateMachine(new SeededRandom(700), FastConfig(totalRounds: 1));
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();
            for (int t = 0; t < 40 && fsm.CurrentPhase != SessionPhase.BoardMove; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardMove), "test setup sanity");
            Assert.That(fsm.BoardSnapshot, Is.Not.Null, "a real BoardModel must exist once BOARD_MOVE has begun");

            // Release every button so the stop-meter genuinely stays live/cycling --
            // meaningless under the old 1-tick stub; load-bearing now.
            inputs.Set(PlayerSlot.Rojo, false);
            inputs.Set(PlayerSlot.Azul, false);
            inputs.Set(PlayerSlot.Amarillo, false);
            inputs.Set(PlayerSlot.Verde, false);
            for (int t = 100; t < 130; t++) fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardMove),
                "with nobody tapping, BOARD_MOVE must stay live well past 1 tick -- the old 1-tick stub is gone");

            // Everyone taps -- the meter freezes and BOARD_MOVE must exit promptly.
            inputs.Set(PlayerSlot.Rojo, true);
            inputs.Set(PlayerSlot.Azul, true);
            inputs.Set(PlayerSlot.Amarillo, true);
            inputs.Set(PlayerSlot.Verde, true);
            fsm.Tick(inputs.Build(200));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardResolve),
                "a tap freezes the stop-meter and BOARD_MOVE must exit into BOARD_RESOLVE");
        }

        [Test]
        public void BoardMove_Timeout_ForcesStopAtEightSeconds()
        {
            // AC1/§2.2/§5.2: nobody taps at all -- BOARD_MOVE's own 8s (480-tick @
            // 60Hz) auto-stop timeout must still force it to exit ("Sin tap antes
            // del timeout (8 s) -> se detiene solo en el valor del tick de timeout").
            var fsm = new SessionStateMachine(new SeededRandom(701), FastConfig(totalRounds: 1));
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();
            for (int t = 0; t < 40 && fsm.CurrentPhase != SessionPhase.BoardMove; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardMove), "test setup sanity");

            inputs.Set(PlayerSlot.Rojo, false);
            inputs.Set(PlayerSlot.Azul, false);
            inputs.Set(PlayerSlot.Amarillo, false);
            inputs.Set(PlayerSlot.Verde, false);

            int t2 = 100;
            for (int i = 0; i < 480; i++) fsm.Tick(inputs.Build(t2++));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardMove), "must not time out before the 8s (480-tick) timeout elapses");

            fsm.Tick(inputs.Build(t2));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardResolve), "must force-stop once the 8s (480-tick) timeout elapses");
        }

        [Test]
        public void BoardResolve_ResolvesWithinOneTick_WhenNoSeatNeedsAnInversionChoice()
        {
            // AC1: the "cero tiempo muerto" fast path -- when nobody landed on
            // Inversión this round, BOARD_RESOLVE must not wait out any window.
            (SessionStateMachine fsm, FakeInputs inputs, int tapOffset) = DriveToBoardMove_RingReaches(BoardTileType.MonedaPositiva);

            // DriveToBoardMove_RingReaches leaves buttons held from Join -- release
            // them first, or the very first BOARD_MOVE tick freezes everyone at
            // meterValue==1 regardless of tapOffset (level-triggered tap detection).
            inputs.Set(PlayerSlot.Rojo, false);
            inputs.Set(PlayerSlot.Azul, false);
            inputs.Set(PlayerSlot.Amarillo, false);
            inputs.Set(PlayerSlot.Verde, false);

            int t = 100;
            for (int i = 0; i < 15 * (tapOffset - 1); i++) fsm.Tick(inputs.Build(t++)); // stay neutral through the target bracket
            inputs.Set(PlayerSlot.Rojo, true);
            inputs.Set(PlayerSlot.Azul, true);
            inputs.Set(PlayerSlot.Amarillo, true);
            inputs.Set(PlayerSlot.Verde, true);
            fsm.Tick(inputs.Build(t++)); // the tap tick -- freezes all 4 at meterValue==tapOffset
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardResolve), "test setup sanity");
            Assert.That(fsm.BoardSnapshot.Ring[tapOffset], Is.EqualTo(BoardTileType.MonedaPositiva), "test setup sanity");

            fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro),
                "no seat landed on Inversión -- BOARD_RESOLVE must resolve within 1 tick, not wait out any window");
        }

        [Test]
        public void BoardResolve_WaitsUpToFourSeconds_WhenSeatLandsOnInversion()
        {
            // AC1: GDD §5.3's Inversión deposit choice is a real-time, <=4s window
            // (240 ticks @ 60Hz) -- BOARD_RESOLVE must genuinely wait it out
            // (mirrors BoardModel's own already-approved TickResolve/ResolveFinished
            // contract; nothing here shortens it even though every seat's stick
            // stays neutral the whole window).
            (SessionStateMachine fsm, FakeInputs inputs, int tapOffset) = DriveToBoardMove_RingReaches(BoardTileType.Inversion);

            // DriveToBoardMove_RingReaches leaves buttons held from Join -- release
            // them first, or the very first BOARD_MOVE tick freezes everyone at
            // meterValue==1 regardless of tapOffset (level-triggered tap detection).
            inputs.Set(PlayerSlot.Rojo, false);
            inputs.Set(PlayerSlot.Azul, false);
            inputs.Set(PlayerSlot.Amarillo, false);
            inputs.Set(PlayerSlot.Verde, false);

            int t = 100;
            for (int i = 0; i < 15 * (tapOffset - 1); i++) fsm.Tick(inputs.Build(t++));
            inputs.Set(PlayerSlot.Rojo, true);
            inputs.Set(PlayerSlot.Azul, true);
            inputs.Set(PlayerSlot.Amarillo, true);
            inputs.Set(PlayerSlot.Verde, true);
            fsm.Tick(inputs.Build(t++));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardResolve), "test setup sanity");
            Assert.That(fsm.BoardSnapshot.Ring[tapOffset], Is.EqualTo(BoardTileType.Inversion), "test setup sanity");
            Assert.That(fsm.BoardSnapshot.Positions[0], Is.EqualTo(tapOffset), "test setup sanity");

            for (int i = 0; i < 239; i++) fsm.Tick(inputs.Build(t++));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardResolve), "must not resolve before the 4s (240-tick) Inversión window elapses");

            fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro), "must resolve once the 4s (240-tick) Inversión window elapses");
        }

        // ── AC2 (TASK-068): coin-pool reconciliation, single source of truth ─────

        [Test]
        public void BoardResolve_ReconcilesCoinFlowsIntoSessionCoins_SingleSourceOfTruth()
        {
            // AC2 [ASSUMED default]: SessionStateMachine.Coins is the single
            // source of truth (GDD §5.6: microgames ~60% / casillas ~40% feed the
            // SAME pool). BoardModel's own internal balances are seeded FROM Coins
            // at BOARD_MOVE entry (SetBalances) and every BoardResolution.CoinFlows
            // delta this round produced is replayed back onto Coins at
            // BOARD_RESOLVE exit -- the two ledgers must therefore match EXACTLY
            // the instant BOARD_RESOLVE completes, for any random session (no
            // seed/tile engineering needed for this particular check).
            var fsm = new SessionStateMachine(new SeededRandom(710), FastConfig(totalRounds: 1));
            var inputs = JoinReadyInputs(); // button held -- BOARD_MOVE resolves in 1 tick
            fsm.InsertCredit();

            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.MgIntro; t++)
                fsm.Tick(inputs.Build(t));

            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro), "test setup sanity: must have completed BOARD_RESOLVE");
            Assert.That(fsm.BoardSnapshot, Is.Not.Null);
            Assert.That(fsm.Coins, Is.EqualTo(fsm.BoardSnapshot.Balances),
                "Coins and BoardModel's own balances must be exactly reconciled the instant BOARD_RESOLVE completes");
        }

        [Test]
        public void BoardMove_SeedsBoardBalancesFromSessionCoins_IncludingPriorMicrogamePayout()
        {
            // AC2: the OTHER half of the reconciliation contract -- BOARD_MOVE
            // entry must seed BoardModel's balances FROM the session's current
            // Coins (SetBalances), including whatever the PRIOR round's
            // microgame+board resolution already paid out. Verified across round
            // 1's exit into round 2's own BOARD_MOVE entry.
            var config = FastConfig(totalRounds: 2);
            var fsm = new SessionStateMachine(new SeededRandom(711), config);
            var round1 = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(round1, "R1", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();
            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.Intermission; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Intermission), "test setup sanity");
            int[] coinsAfterRound1 = (int[])fsm.Coins.Clone();

            for (int t = 1000; t < 1500 && fsm.CurrentPhase != SessionPhase.BoardMove; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardMove), "test setup sanity: round 2's BOARD_MOVE has begun");

            Assert.That(fsm.BoardSnapshot.Balances, Is.EqualTo(coinsAfterRound1),
                "round 2's BOARD_MOVE must have re-seeded the board from the session's post-round-1 Coins");
        }

        [Test]
        public void FullSession_CoinConservation_NoBalanceEverNegative_AndLedgersStaySynced()
        {
            // AC2: "no coin created or destroyed across the microgame<->board
            // boundary" over a full simulated session, with a REAL BoardModel (not
            // a hand-picked ring) driving every one of 7 rounds. Checked two ways
            // continuously: (1) no seat's Coins ever goes negative; (2) Coins and
            // BoardModel's own balances are reconciled (equal) the instant every
            // single BOARD_RESOLVE in the session completes -- proving no round
            // silently drops or double-counts a CoinDelta.
            var config = new SessionStateMachineConfig(
                joinTimeoutSeconds: 30f, joinMinReady: 2, mgIntroSeconds: 0.02f, mgResultSeconds: 0.02f,
                intermissionSeconds: 0.02f, finalWagerSeconds: 0.1f, gameOverSeconds: 0.02f,
                totalRounds: 7, ticksPerSecond: 60);
            var fsm = new SessionStateMachine(new SeededRandom(712), config);
            var fake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(fake, "R", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            int reconciliationChecks = 0;
            for (int t = 0; t < 20000 && fsm.CurrentPhase != SessionPhase.GameOver; t++)
            {
                SessionPhase before = fsm.CurrentPhase;
                fsm.Tick(inputs.Build(t));

                foreach (int c in fsm.Coins)
                    Assert.That(c, Is.GreaterThanOrEqualTo(0), $"tick {t}: no Coins balance may ever go negative");

                if (before == SessionPhase.BoardResolve && fsm.CurrentPhase != SessionPhase.BoardResolve)
                {
                    reconciliationChecks++;
                    Assert.That(fsm.Coins, Is.EqualTo(fsm.BoardSnapshot.Balances),
                        $"tick {t}: Coins and BoardModel balances must reconcile exactly the instant BOARD_RESOLVE completes");
                }
            }

            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver), "test setup sanity: the probe must complete all 7 rounds");
            Assert.That(reconciliationChecks, Is.EqualTo(7), "every one of the 7 rounds' BOARD_RESOLVE completions must have been observed and reconciled");
        }

        // ── AC3 (TASK-068): determinism through the wired board phases ───────────

        [Test]
        public void FullSession_WithBoardPhases_Deterministic_IdenticalFinalCoinAndBoardState()
        {
            // AC3: same (seed, input sequence) -> identical session final-state
            // hash across the board phases. "Hash" here is exact field-by-field
            // equality of every piece of state a replay could diverge on (Coins
            // plus every BoardSnapshot field) -- the same convention this file's
            // existing Determinism_SameSeedAndInputs_ProducesIdenticalTrace test
            // already uses, extended to cover the newly-wired board ledger too.
            (int[] coins, BoardSnapshot board) RunOnce()
            {
                var config = new SessionStateMachineConfig(
                    joinTimeoutSeconds: 30f, joinMinReady: 2, mgIntroSeconds: 0.02f, mgResultSeconds: 0.02f,
                    intermissionSeconds: 0.02f, finalWagerSeconds: 0.1f, gameOverSeconds: 0.02f,
                    totalRounds: 7, ticksPerSecond: 60);
                var fsm = new SessionStateMachine(new SeededRandom(713), config);
                var fake = new FakeMicrogame(finishAfterTicks: 1,
                    result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
                fsm.SetActiveMicrogame(fake, "R", playDurationSeconds: 0.02f);

                var inputs = JoinReadyInputs();
                fsm.InsertCredit();
                for (int t = 0; t < 20000 && fsm.CurrentPhase != SessionPhase.GameOver; t++)
                    fsm.Tick(inputs.Build(t));

                Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver), "test setup sanity");
                return ((int[])fsm.Coins.Clone(), fsm.BoardSnapshot);
            }

            (int[] coins, BoardSnapshot board) a = RunOnce();
            (int[] coins, BoardSnapshot board) b = RunOnce();

            Assert.That(b.coins, Is.EqualTo(a.coins));
            Assert.That(b.board.Ring, Is.EqualTo(a.board.Ring));
            Assert.That(b.board.Positions, Is.EqualTo(a.board.Positions));
            Assert.That(b.board.Balances, Is.EqualTo(a.board.Balances));
            Assert.That(b.board.PropertyOwner, Is.EqualTo(a.board.PropertyOwner));
            Assert.That(b.board.InversionOwner, Is.EqualTo(a.board.InversionOwner));
            Assert.That(b.board.Weapons, Is.EqualTo(a.board.Weapons));
            Assert.That(b.board.StarSquareIndex, Is.EqualTo(a.board.StarSquareIndex));
        }

        // ── AC5: determinism / replay ─────────────────────────────────────────────

        [Test]
        public void Determinism_SameSeedAndInputs_ProducesIdenticalTrace()
        {
            List<SessionStateSnapshot> RunOnce()
            {
                var fsm = new SessionStateMachine(new SeededRandom(42), FastConfig(totalRounds: 2));
                var fake = new FakeMicrogame(finishAfterTicks: 2);
                fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.05f);
                var inputs = JoinReadyInputs();
                fsm.InsertCredit();

                var trace = new List<SessionStateSnapshot> { fsm.Snapshot() };
                for (int t = 0; t < 2000; t++)
                {
                    fsm.Tick(inputs.Build(t));
                    trace.Add(fsm.Snapshot());
                }
                return trace;
            }

            List<SessionStateSnapshot> traceA = RunOnce();
            List<SessionStateSnapshot> traceB = RunOnce();

            Assert.That(traceB.Count, Is.EqualTo(traceA.Count));
            for (int i = 0; i < traceA.Count; i++)
            {
                Assert.That(traceB[i].Tick, Is.EqualTo(traceA[i].Tick), $"Tick mismatch at index {i}");
                Assert.That(traceB[i].Phase, Is.EqualTo(traceA[i].Phase), $"Phase mismatch at index {i}");
                Assert.That(traceB[i].RoundIndex, Is.EqualTo(traceA[i].RoundIndex), $"RoundIndex mismatch at index {i}");
                Assert.That(traceB[i].ReadyCount, Is.EqualTo(traceA[i].ReadyCount), $"ReadyCount mismatch at index {i}");
            }
        }

        // ── GDD §2.2 exact timeouts + [ASSUMED] defaults ─────────────────────────

        [Test]
        public void GddDefaults_MatchBudgetTableExactly()
        {
            var d = SessionStateMachineConfig.GddDefaults;
            Assert.That(d.JoinTimeoutSeconds, Is.EqualTo(30f), "GDD §2.1: Join timeout 30s");
            Assert.That(d.JoinMinReady, Is.EqualTo(2), "GDD §2.1: >=2 jugadores listos");
            Assert.That(d.MgIntroSeconds, Is.EqualTo(0.8f), "GDD §2.2: MG_INTRO 0.8s fijo");
            Assert.That(d.MgResultSeconds, Is.EqualTo(1.5f), "GDD §2.2: MG_RESULT 1.5s fijo");
            Assert.That(d.IntermissionSeconds, Is.EqualTo(2f), "GDD §2.2: INTERMISSION 2s fijo");
            Assert.That(d.FinalWagerSeconds, Is.EqualTo(5f), "GDD §6.2: apuesta final, 5s");
            Assert.That(d.GameOverSeconds, Is.EqualTo(20f), "GDD §2.1 diagram: GAME_OVER timeout 20s");
            Assert.That(d.TicksPerSecond, Is.EqualTo(60), "GDD §3.2: fixed 60Hz simulation tick");
            Assert.That(d.TotalRounds, Is.EqualTo(7), "[ASSUMED] GDD Annex B session.rounds (range 5-8), reused for the round-loop count");
        }

        [Test]
        public void Intermission_ExactDuration_AdvancesOnlyAfterConfiguredSeconds()
        {
            var config = new SessionStateMachineConfig(
                joinTimeoutSeconds: 30f, joinMinReady: 2, mgIntroSeconds: 0.02f, mgResultSeconds: 0.02f,
                intermissionSeconds: 0.1f, finalWagerSeconds: 0.02f, gameOverSeconds: 0.02f,
                totalRounds: 2, ticksPerSecond: 60);
            var fsm = new SessionStateMachine(new SeededRandom(8), config);
            var fake = new FakeMicrogame(finishAfterTicks: 1);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.02f);
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.Intermission; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Intermission));

            // 0.1s @ 60Hz = 6 ticks; assert it hasn't advanced 1 tick early.
            for (int i = 0; i < 5; i++) fsm.Tick(inputs.Build(1000 + i));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Intermission), "must not advance before the configured Intermission duration elapses");

            fsm.Tick(inputs.Build(2000));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardMove), "must advance once the configured Intermission duration has elapsed");
        }

        [Test]
        public void MgIntro_ExactDuration_PinnedAgainstDistinctiveConfigValue()
        {
            // MEDIUM-2 (TASK-024 review fix round): MgIntro's timing is wired into
            // the INNER RoundPhaseMachine's commandShowDuration constructor arg
            // (see class doc); if that wiring were ever dropped, RoundPhaseMachine's
            // own 1.0s default would silently take over and every OTHER existing
            // test would still pass (none of them measured MgIntro's own exact tick
            // count). A distinctive 7-tick value -- far from both GDD's exact 0.8s
            // (48 ticks) and RoundPhaseMachine's 1.0s default (60 ticks) -- makes
            // any such regression immediately obvious instead of silently passing.
            var config = new SessionStateMachineConfig(
                joinTimeoutSeconds: 30f, joinMinReady: 2, mgIntroSeconds: 7f / 60f, mgResultSeconds: 0.02f,
                intermissionSeconds: 0.02f, finalWagerSeconds: 0.02f, gameOverSeconds: 0.02f,
                totalRounds: 1, ticksPerSecond: 60);
            var fsm = new SessionStateMachine(new SeededRandom(20), config);
            var fake = new FakeMicrogame(finishAfterTicks: 1);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.02f);
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 400 && fsm.CurrentPhase != SessionPhase.MgIntro; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro));

            // 7 ticks configured; assert it hasn't advanced 1 tick early.
            for (int i = 0; i < 6; i++) fsm.Tick(inputs.Build(1000 + i));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro), "must not advance before the configured 7-tick MgIntro duration elapses");

            fsm.Tick(inputs.Build(2000));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgPlay), "must advance exactly once the configured MgIntro duration has elapsed");
        }

        [Test]
        public void MgResult_ExactDuration_PinnedAgainstDistinctiveConfigValue()
        {
            // MEDIUM-2 companion pin: MgResult's window is SessionStateMachine's
            // own external timer (RoundPhaseMachine's Result phase is
            // intentionally terminal, see class doc), armed with _config.MgResultSeconds
            // directly rather than any RoundPhaseMachine default -- still worth an
            // exact-tick pin against a distinctive value to guard against any
            // future refactor introducing an off-by-one or reusing the wrong field.
            var config = new SessionStateMachineConfig(
                joinTimeoutSeconds: 30f, joinMinReady: 2, mgIntroSeconds: 0.02f, mgResultSeconds: 9f / 60f,
                intermissionSeconds: 0.02f, finalWagerSeconds: 0.02f, gameOverSeconds: 0.02f,
                totalRounds: 1, ticksPerSecond: 60);
            var fsm = new SessionStateMachine(new SeededRandom(21), config);
            var fake = new FakeMicrogame(finishAfterTicks: 1);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.02f);
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 400 && fsm.CurrentPhase != SessionPhase.MgResult; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgResult));

            // 9 ticks configured. Unlike Intermission/FinalWager (entered via a
            // bare EnterSimplePhase from a DIFFERENT call, so the transition tick
            // itself doesn't also accumulate toward the new phase), MgResult's
            // "just arrived" reset-and-capture and its "accumulate, check timeout"
            // check both live in TickRoundSubLoop and run unconditionally every
            // call -- so the very tick MgResult is entered on already counts as
            // its first elapsed tick. 7 more ticks (8 total) must still be inside
            // the window; the 8th more (9 total) crosses it.
            for (int i = 0; i < 7; i++) fsm.Tick(inputs.Build(1000 + i));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgResult), "must not advance before the configured 9-tick MgResult window elapses");

            fsm.Tick(inputs.Build(2000));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Intermission), "must advance exactly once the configured MgResult window has elapsed");
        }

        // ── T-108 seam: difficultyMult passthrough ────────────────────────────────

        [Test]
        public void SetActiveMicrogame_DifficultyMult_PassesThroughToInitialize()
        {
            // MEDIUM-3 (TASK-024 review fix round): difficultyMult now flows
            // through SetActiveMicrogame (default 1f) instead of being hardcoded
            // at the Initialize() call sites -- a seam for T-108's difficulty
            // computation (SessionPhase.cs's own doc cites GDD §9.1's D_final=1.5x)
            // to plug into without any further FSM change. A non-default value
            // (2.5f, not 1f) proves genuine passthrough rather than a hardcoded
            // constant that happens to equal the default.
            var fsm = new SessionStateMachine(new SeededRandom(22), FastConfig(totalRounds: 1));
            var fake = new FakeMicrogame(finishAfterTicks: int.MaxValue);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.05f, difficultyMult: 2.5f);
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 400 && !fake.InitializeCalled; t++)
                fsm.Tick(inputs.Build(t));

            Assert.That(fake.InitializeCalled, Is.True);
            Assert.That(fake.LastDifficultyMult, Is.EqualTo(2.5f));
        }

        [Test]
        public void SetActiveMicrogame_DifficultyMultDefault_Passes1f()
        {
            var fsm = new SessionStateMachine(new SeededRandom(23), FastConfig(totalRounds: 1));
            var fake = new FakeMicrogame(finishAfterTicks: int.MaxValue);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.05f); // difficultyMult omitted

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 400 && !fake.InitializeCalled; t++)
                fsm.Tick(inputs.Build(t));

            Assert.That(fake.InitializeCalled, Is.True);
            Assert.That(fake.LastDifficultyMult, Is.EqualTo(1f));
        }

        // ── Result capture semantics ──────────────────────────────────────────────

        [Test]
        public void MicrogameFinishesEarly_ResultCapturedAtMgResult()
        {
            var expected = new V2Result(ResultKind.CoopSuccess, Array.Empty<PlayerRank>(), 7);
            var fsm = new SessionStateMachine(new SeededRandom(9), FastConfig(totalRounds: 1));
            var fake = new FakeMicrogame(finishAfterTicks: 1, result: expected);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.05f);
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.MgResult; t++)
                fsm.Tick(inputs.Build(t));

            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgResult));
            Assert.That(fsm.LastMicrogameResult.HasValue, Is.True);
            Assert.That(fsm.LastMicrogameResult.Value.Kind, Is.EqualTo(ResultKind.CoopSuccess));
            Assert.That(fsm.LastMicrogameResult.Value.CoopScore, Is.EqualTo(7));
        }

        [Test]
        public void MicrogameNeverFinishes_CeilingCutoff_LeavesResultNull()
        {
            var fsm = new SessionStateMachine(new SeededRandom(10), FastConfig(totalRounds: 1));
            var fake = new FakeMicrogame(finishAfterTicks: int.MaxValue); // never IsFinished; must not have GetResult() called on it
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.05f);
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.MgResult; t++)
                fsm.Tick(inputs.Build(t)); // would throw if SessionStateMachine ever called GetResult() early

            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgResult));
            Assert.That(fsm.LastMicrogameResult.HasValue, Is.False, "a ceiling cutoff without IsFinished must leave the result null, never call GetResult() early");
        }

        [Test]
        public void NoMicrogameInjected_MgPlaySkipsWithinOneTick()
        {
            var fsm = new SessionStateMachine(new SeededRandom(11), FastConfig(totalRounds: 1));
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.MgIntro; t++)
                fsm.Tick(inputs.Build(t));

            bool sawMgPlay = false;
            for (int t = 0; t < 20 && fsm.CurrentPhase != SessionPhase.MgResult; t++)
            {
                fsm.Tick(inputs.Build(1000 + t));
                if (fsm.CurrentPhase == SessionPhase.MgPlay) sawMgPlay = true;
            }

            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgResult));
            Assert.That(sawMgPlay, Is.False, "with no microgame injected, MgPlay's ceiling is 0 and is skipped within the same tick BoardResolve exits");
            Assert.That(fsm.LastMicrogameResult.HasValue, Is.False);
        }

        [Test]
        public void BeginRound_FlushCascadesDirectlyToResult_StillCapturesResult()
        {
            // LOW-1 (TASK-024 review fix round): mgIntroSeconds == 0 AND an
            // effective play duration of 0 make BeginRound's Tick(0f) flush
            // cascade straight through CommandShow and Play into Result within
            // that same call, before TickRoundSubLoop ever gets a chance to run
            // its own "just arrived at Result" capture. Without a matching capture
            // inside BeginRound itself, LastMicrogameResult would silently keep
            // whatever it was before this round (null, for a session's first
            // round) even though the microgame reports a real result.
            var config = new SessionStateMachineConfig(
                joinTimeoutSeconds: 30f, joinMinReady: 2, mgIntroSeconds: 0f, mgResultSeconds: 0.02f,
                intermissionSeconds: 0.02f, finalWagerSeconds: 0.02f, gameOverSeconds: 0.02f,
                totalRounds: 1, ticksPerSecond: 60);
            var expected = new V2Result(ResultKind.CoopSuccess, Array.Empty<PlayerRank>(), 9);
            var fsm = new SessionStateMachine(new SeededRandom(31), config);
            var fake = new FakeMicrogame(finishAfterTicks: 0, result: expected); // already finished the instant Initialize() runs
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0f); // 0 -> effectivePlayDuration is 0 too
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 400 && fsm.CurrentPhase != SessionPhase.MgResult; t++)
                fsm.Tick(inputs.Build(t));

            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgResult), "test setup sanity: the 0-duration flush must cascade directly to MgResult");
            Assert.That(fsm.LastMicrogameResult.HasValue, Is.True, "the flush-cascade path must still capture the result, not silently leave it at its previous value");
            Assert.That(fsm.LastMicrogameResult.Value.CoopScore, Is.EqualTo(9));
        }

        // ── FinalWager fixed window ───────────────────────────────────────────────

        [Test]
        public void FinalWager_ExactDuration_AdvancesOnlyAfterConfiguredSeconds()
        {
            var config = new SessionStateMachineConfig(
                joinTimeoutSeconds: 30f, joinMinReady: 2, mgIntroSeconds: 0.02f, mgResultSeconds: 0.02f,
                intermissionSeconds: 0.02f, finalWagerSeconds: 0.1f, gameOverSeconds: 0.02f,
                totalRounds: 1, ticksPerSecond: 60);
            var fsm = new SessionStateMachine(new SeededRandom(12), config);
            var fake = new FakeMicrogame(finishAfterTicks: 1);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.02f);
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalWager));

            for (int i = 0; i < 5; i++) fsm.Tick(inputs.Build(1000 + i)); // 0.1s @ 60Hz = 6 ticks
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalWager), "must not advance before the configured 5s (here, scaled-down) wager window elapses");

            fsm.Tick(inputs.Build(2000));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalMg));
        }

        // ── GameOver resets session state for the next Attract cycle ────────────

        [Test]
        public void GameOver_ReturnsToAttract_WithRosterAndRoundStateReset()
        {
            var fsm = new SessionStateMachine(new SeededRandom(13), FastConfig(totalRounds: 1));
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            bool loopedToAttract = false;
            for (int t = 0; t < 3000 && !loopedToAttract; t++)
            {
                fsm.Tick(inputs.Build(t));
                if (t > 50 && fsm.CurrentPhase == SessionPhase.Attract) loopedToAttract = true;
            }

            Assert.That(loopedToAttract, Is.True);
            Assert.That(fsm.ReadyCount, Is.EqualTo(0), "GameOver->Attract must reset ready-seat state for the next session");
            Assert.That(fsm.RoundIndex, Is.EqualTo(0), "GameOver->Attract must reset round index for the next session");

            // LOW-2 (TASK-024 review fix round): the roster from THIS session
            // (all 4 seats claimed, via JoinReadyInputs) must not leak into the
            // next Attract cycle's pre-Join state.
            Assert.That(fsm.Roster.Seats, Is.Not.Null, "Roster must be a valid (non-default) struct after reset, not a null-backed default");
            for (int i = 0; i < 4; i++)
                Assert.That(fsm.Roster.Seats[i], Is.EqualTo(SeatState.Empty), $"seat {i} must be cleared to Empty after GameOver->Attract, not still carry the previous session's claim");
        }

        // ── AC6 is verified by the full suite run (RoundPhaseMachine untouched) ──
        // ── AC7 is verified by process: this file's red commit precedes the impl ──

        // ── Zero-allocation steady state (avoiding the "measured a no-op" defect
        //    class hit repeatedly on Reaccion/Apunta: bracket IsFinished==false and
        //    CurrentPhase==MgPlay on every tick inside the measured window) ───────

        [Test]
        public void Tick_SteadyStateDuringMgPlay_NoAllocation()
        {
            var config = new SessionStateMachineConfig(
                joinTimeoutSeconds: 30f, joinMinReady: 2, mgIntroSeconds: 0.02f, mgResultSeconds: 0.02f,
                intermissionSeconds: 0.02f, finalWagerSeconds: 0.02f, gameOverSeconds: 0.02f,
                totalRounds: 1, ticksPerSecond: 60);
            var fsm = new SessionStateMachine(new SeededRandom(14), config);
            var fake = new FakeMicrogame(finishAfterTicks: int.MaxValue);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 10f); // 600-tick ceiling — comfortably longer than the measured window
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.MgPlay; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgPlay), "test setup sanity: must reach MgPlay before measuring");

            // Warm up: JIT the hot path first (established convention, see InputInterpreterTests).
            for (int t = 200; t < 264; t++)
            {
                inputs.Set(PlayerSlot.Rojo, (t % 2) == 0, Direction8.NE);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fake.IsFinished, Is.False, "sensor setup: must still be live after warmup");

            // No Assert.* calls inside the measured window itself -- NUnit's
            // constraint machinery (Is.EqualTo etc.) allocates on every call, which
            // would contaminate the very thing being measured. Track liveness with
            // plain booleans instead and assert on them only after the window closes.
            bool everLeftMgPlay = false;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 264; t < 464; t++)
            {
                inputs.Set(PlayerSlot.Rojo, (t % 2) == 0, Direction8.NE);
                inputs.Set(PlayerSlot.Amarillo, (t % 3) == 0, Direction8.SW);
                fsm.Tick(inputs.Build(t));
                if (fsm.CurrentPhase != SessionPhase.MgPlay) everLeftMgPlay = true;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(fake.IsFinished, Is.False, "sensor setup: must remain live for the whole measured window, or it wasn't measuring the live path");
            Assert.That(everLeftMgPlay, Is.False, "sensor setup: must remain in MgPlay for the whole measured window");
            Assert.That(after - before, Is.EqualTo(0L));
        }

        // ── TASK-051: ScoringV2-FSM wiring ────────────────────────────────────────

        private static V2Result Ranked(params (int seat, int place, int metric)[] entries)
        {
            var ranks = new PlayerRank[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                ranks[i] = new PlayerRank(entries[i].seat, entries[i].place, entries[i].metric);
            return new V2Result(ResultKind.Ranked, ranks, 0);
        }

        [Test]
        public void FinalWager_CapturesStickChoicePerSeat_TimeoutDefaultsForAbsentChoice()
        {
            // AC1: FINAL_WAGER captures per-seat 25/50/75 stick choices during its
            // 5s window via InputInterpreter. Verified indirectly through the
            // resulting stakes -- the captured choice has no public accessor of
            // its own, only its effect on the resolved wager.
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(50), config);
            var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(roundFake, "PRUEBA", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalWager));

            int[] preWagerCoins = (int[])fsm.Coins.Clone();
            // TASK-068: no longer pinned to the exact default-payout value {6,4,2,1}
            // -- BOARD_RESOLVE now also contributes real, seed-dependent coin flows
            // before the wager, so preWagerCoins legitimately varies with the seed.
            // Every assertion below derives its EXPECTED stake from this SAME
            // dynamically-captured baseline, so the wager-math invariant under test
            // (25/50/75% of whatever a seat actually had) stays fully rigorous
            // regardless of that baseline's exact value.

            // Rojo=Quarter(Left), Azul=Half(Up), Amarillo=ThreeQuarters(Right), Verde=untouched.
            var climaxFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(climaxFake, "FINAL", playDurationSeconds: 0.02f);

            for (int t = 1000; t < 1200 && fsm.CurrentPhase == SessionPhase.FinalWager; t++)
            {
                inputs.Set(PlayerSlot.Rojo, false, Direction8.W);
                inputs.Set(PlayerSlot.Azul, false, Direction8.N);
                inputs.Set(PlayerSlot.Amarillo, false, Direction8.E);
                inputs.Set(PlayerSlot.Verde, false, Direction8.None);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalMg), "test setup sanity: must have exited FinalWager into FinalMg");

            for (int t = 2000; t < 2020 && fsm.CurrentPhase != SessionPhase.GameOver; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver));

            Assert.That(fsm.LastWagerResult, Is.Not.Null);
            Assert.That(fsm.LastWagerResult.Stakes[0], Is.EqualTo(preWagerCoins[0] * 25 / 100), "Rojo chose Quarter (Left)");
            Assert.That(fsm.LastWagerResult.Stakes[1], Is.EqualTo(preWagerCoins[1] * 50 / 100), "Azul chose Half (Up)");
            Assert.That(fsm.LastWagerResult.Stakes[2], Is.EqualTo(preWagerCoins[2] * 75 / 100), "Amarillo chose ThreeQuarters (Right)");
            Assert.That(fsm.LastWagerResult.Stakes[3], Is.EqualTo(preWagerCoins[3] * 50 / 100), "Verde never chose a direction -- timeout defaults to Half");
        }

        [Test]
        public void Wager_PotResolvesOnFinalMgCompletion_MatchingFinalWagerResolveDirectly()
        {
            // AC1: pot resolution runs on FINAL_MG completion with exact GDD 6.2
            // math -- verified by independently calling FinalWager.Resolve with
            // the same (coins, choices, climax places) the FSM itself derived,
            // and asserting an exact field-by-field match (not just plausible
            // numbers).
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(54), config);
            var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(roundFake, "PRUEBA", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();
            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                fsm.Tick(inputs.Build(t));

            int[] preWagerCoins = (int[])fsm.Coins.Clone();

            var climaxRanks = new (int seat, int place, int metric)[] { (1, 1, 0), (0, 2, 0), (3, 3, 0), (2, 4, 0) };
            var climaxFake = new FakeMicrogame(finishAfterTicks: 1, result: Ranked(climaxRanks));
            fsm.SetActiveMicrogame(climaxFake, "FINAL", playDurationSeconds: 0.02f);

            for (int t = 1000; t < 1200 && fsm.CurrentPhase == SessionPhase.FinalWager; t++)
            {
                inputs.Set(PlayerSlot.Rojo, false, Direction8.E);   // ThreeQuarters
                inputs.Set(PlayerSlot.Azul, false, Direction8.W);   // Quarter
                inputs.Set(PlayerSlot.Amarillo, false, Direction8.N); // Half
                inputs.Set(PlayerSlot.Verde, false, Direction8.E);  // ThreeQuarters
                fsm.Tick(inputs.Build(t));
            }
            for (int t = 2000; t < 2020 && fsm.CurrentPhase != SessionPhase.GameOver; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver));

            var expectedChoices = new WagerChoice?[]
                { WagerChoice.ThreeQuarters, WagerChoice.Quarter, WagerChoice.Half, WagerChoice.ThreeQuarters };
            var expectedClimaxPlaces = new int[4];
            foreach ((int seat, int place, int metric) in climaxRanks) expectedClimaxPlaces[seat] = place;
            WagerResult expected = FinalWager.Resolve(preWagerCoins, expectedChoices, expectedClimaxPlaces);

            Assert.That(fsm.LastWagerResult.Pot, Is.EqualTo(expected.Pot));
            Assert.That(fsm.LastWagerResult.Stakes, Is.EqualTo(expected.Stakes));
            Assert.That(fsm.LastWagerResult.Shares, Is.EqualTo(expected.Shares));
            Assert.That(fsm.LastWagerResult.CoinsAfter, Is.EqualTo(expected.CoinsAfter));

            int totalShares = 0;
            foreach (int s in fsm.LastWagerResult.Shares) totalShares += s;
            Assert.That(totalShares, Is.EqualTo(fsm.LastWagerResult.Pot), "GDD 5.3: every pot coin is redistributed, none vanish");
        }

        [Test]
        public void SessionCounters_ReaccionaRound_FeedsGatilloLatency()
        {
            // AC2: Gatillo (best/mean REACCIONA latency) has a real feed today --
            // the cumulative tick-latency Metric a Reacciona-identified round
            // reports is converted to milliseconds and recorded per seat.
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(51), config);
            var fake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 60), (1, 2, 120), (2, 3, 180), (3, 4, 240)),
                id: MicrogameId.Reacciona);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.MgResult; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgResult));

            Assert.That(fsm.Counters.WinnerOf(StarKind.Gatillo, 0b1111), Is.EqualTo(0),
                "seat 0's metric (60 ticks = 1000ms @ 60Hz) is the lowest (best) latency");
        }

        [Test]
        public void SessionCounters_ApuntaRound_DoesNotFeedGatilloLatency()
        {
            // Companion pin: a non-Reacciona mechanic's Metric has no defined
            // counters mapping (see class doc) -- must not be misread as latency.
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(55), config);
            var fake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 5), (1, 2, 10), (2, 3, 15), (3, 4, 20)),
                id: MicrogameId.Apunta);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();
            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.MgResult; t++)
                fsm.Tick(inputs.Build(t));

            Assert.That(fsm.Counters.WinnerOf(StarKind.Gatillo, 0b1111), Is.EqualTo(-1),
                "no REACCIONA samples were ever recorded -- Apunta's Metric must not feed Gatillo");
        }

        [Test]
        public void MicrogameWins_IncrementsOnCompetitivePlace1_AndCoopSuccessCreditsEveryone()
        {
            var config = FastConfig(totalRounds: 2);
            var fsm = new SessionStateMachine(new SeededRandom(52), config);
            var round1 = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((1, 1, 0), (0, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(round1, "R1", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();
            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.Intermission; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Intermission));
            Assert.That(fsm.MicrogameWins, Is.EqualTo(new[] { 0, 1, 0, 0 }), "seat 1 won round 1 (place 1)");

            var round2 = new FakeMicrogame(finishAfterTicks: 1,
                result: new V2Result(ResultKind.CoopSuccess, Array.Empty<PlayerRank>(), 0));
            fsm.SetActiveMicrogame(round2, "R2", playDurationSeconds: 0.02f);

            for (int t = 1000; t < 1500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalWager));
            Assert.That(fsm.MicrogameWins, Is.EqualTo(new[] { 1, 2, 1, 1 }), "a coop success credits every active seat with a win");
        }

        // ── TASK-052: per-definition payoutTable threading ────────────────────────

        [Test]
        public void RegularRound_CompetitivePayoutTable_OverridesSessionDefault()
        {
            // AC1: a per-definition payoutTable set via SetActiveMicrogame replaces
            // the GDD §6.1 session-wide default {6,4,2,1} for that round.
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(70), config);
            var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            int[] customPayout = { 10, 8, 5, 3 };
            fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f, payoutTable: customPayout);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            // TASK-068: BOARD_RESOLVE now applies real, seed-dependent coin flows
            // before this round's microgame payout ever runs -- capture the
            // baseline the instant it completes (CurrentPhase reaches MgIntro) and
            // assert the payout table's effect as a DELTA from it, isolating the
            // invariant under test (payoutTable override) from board noise.
            int t = 0;
            for (; t < 500 && fsm.CurrentPhase != SessionPhase.MgIntro; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro), "test setup sanity: must have completed BOARD_RESOLVE");
            int[] coinsBeforePayout = (int[])fsm.Coins.Clone();

            for (; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalWager));

            int[] delta = new int[4];
            for (int i = 0; i < 4; i++) delta[i] = fsm.Coins[i] - coinsBeforePayout[i];
            Assert.That(delta, Is.EqualTo(new[] { 10, 8, 5, 3 }),
                "the custom payoutTable, not the GDD default {6,4,2,1}, must have applied");
        }

        [Test]
        public void RegularRound_CoopPayoutTable_OverridesSessionDefault_OnSuccess()
        {
            // AC1, coop shape (TASK-050 convention: 2 entries [success, fail]).
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(71), config);
            var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                result: new V2Result(ResultKind.CoopSuccess, Array.Empty<PlayerRank>(), 0));
            int[] customPayout = { 9, 2 }; // [success, fail]
            fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f, payoutTable: customPayout);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            // TASK-068: isolate the payoutTable's effect from BOARD_RESOLVE's real,
            // seed-dependent coin flows -- see the companion competitive-table test.
            int t = 0;
            for (; t < 500 && fsm.CurrentPhase != SessionPhase.MgIntro; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro), "test setup sanity: must have completed BOARD_RESOLVE");
            int[] coinsBeforePayout = (int[])fsm.Coins.Clone();

            for (; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalWager));

            int[] delta = new int[4];
            for (int i = 0; i < 4; i++) delta[i] = fsm.Coins[i] - coinsBeforePayout[i];
            Assert.That(delta, Is.EqualTo(new[] { 9, 9, 9, 9 }),
                "coop success must pay the custom table's success entry (9), not the GDD default (4), to every active seat");
        }

        [Test]
        public void RegularRound_CoopPayoutTable_OverridesSessionDefault_OnFail()
        {
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(72), config);
            var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                result: new V2Result(ResultKind.CoopFail, Array.Empty<PlayerRank>(), 0));
            int[] customPayout = { 9, 2 }; // [success, fail]
            fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f, payoutTable: customPayout);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            // TASK-068: isolate the payoutTable's effect from BOARD_RESOLVE's real,
            // seed-dependent coin flows -- see the companion competitive-table test.
            int t = 0;
            for (; t < 500 && fsm.CurrentPhase != SessionPhase.MgIntro; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro), "test setup sanity: must have completed BOARD_RESOLVE");
            int[] coinsBeforePayout = (int[])fsm.Coins.Clone();

            for (; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalWager));

            int[] delta = new int[4];
            for (int i = 0; i < 4; i++) delta[i] = fsm.Coins[i] - coinsBeforePayout[i];
            Assert.That(delta, Is.EqualTo(new[] { 2, 2, 2, 2 }),
                "coop fail must pay the custom table's fail entry (2), not the GDD default (1), to every active seat");
        }

        [Test]
        public void RegularRound_CoopPayoutTableWrongLength_ThrowsArgumentException()
        {
            // Defensive: a coop payoutTable must be exactly 2 entries [success,
            // fail] (TASK-050 shape convention) -- a mismatched length must fail
            // loudly at apply time, not silently mis-index or throw an opaque
            // IndexOutOfRangeException.
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(75), config);
            var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                result: new V2Result(ResultKind.CoopSuccess, Array.Empty<PlayerRank>(), 0));
            int[] wrongShapeTable = { 6, 4, 2, 1 }; // a competitive-shaped table on a coop round
            fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f, payoutTable: wrongShapeTable);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            Assert.Throws<ArgumentException>(() =>
            {
                for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                    fsm.Tick(inputs.Build(t));
            });
        }

        [Test]
        public void ClimaxRound_IgnoresPayoutTable_NoRegularPayoutApplied()
        {
            // AC3: the climax round is exempt from regular per-round payout (its
            // outcome feeds FinalWager instead, GDD §6.2) regardless of whether a
            // payoutTable was set on it via SetActiveMicrogame -- proven by an
            // independently-recomputed FinalWager.Resolve using ONLY the coins
            // that existed before the climax played. If a leak applied the
            // climax's table as a regular payout first, the FSM's actual result
            // would diverge from this expectation (which deliberately does NOT
            // add the huge climax table below).
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(74), config);
            var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();
            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                fsm.Tick(inputs.Build(t));

            int[] preWagerCoins = (int[])fsm.Coins.Clone();

            var climaxRanks = new (int seat, int place, int metric)[] { (0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0) };
            var climaxFake = new FakeMicrogame(finishAfterTicks: 1, result: Ranked(climaxRanks));
            // Deliberately huge/distinctive so any leak into a "regular payout" would be unmistakable.
            int[] climaxPayoutTable = { 100, 90, 80, 70 };
            fsm.SetActiveMicrogame(climaxFake, "FINAL", playDurationSeconds: 0.02f, payoutTable: climaxPayoutTable);

            for (int t = 1000; t < 1200 && fsm.CurrentPhase == SessionPhase.FinalWager; t++)
            {
                inputs.Set(PlayerSlot.Rojo, false, Direction8.N);     // Half
                inputs.Set(PlayerSlot.Azul, false, Direction8.N);     // Half
                inputs.Set(PlayerSlot.Amarillo, false, Direction8.N); // Half
                inputs.Set(PlayerSlot.Verde, false, Direction8.N);    // Half
                fsm.Tick(inputs.Build(t));
            }
            for (int t = 2000; t < 2020 && fsm.CurrentPhase != SessionPhase.GameOver; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver));

            var expectedChoices = new WagerChoice?[] { WagerChoice.Half, WagerChoice.Half, WagerChoice.Half, WagerChoice.Half };
            var expectedClimaxPlaces = new int[4];
            foreach ((int seat, int place, int metric) in climaxRanks) expectedClimaxPlaces[seat] = place;
            WagerResult expected = FinalWager.Resolve(preWagerCoins, expectedChoices, expectedClimaxPlaces);

            Assert.That(fsm.LastWagerResult.CoinsAfter, Is.EqualTo(expected.CoinsAfter),
                "the climax's payoutTable must never be applied as a regular per-round payout -- only FinalWager.Resolve may change coins here");
        }

        [Test]
        public void GameOver_RanksViaFinalRanking_UsingDrawnBonusStars()
        {
            // AC3: GAME_OVER ranks via FinalRanking (estrellas -> monedas ->
            // victorias -> shared podium) using the drawn bonus stars.
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(53), config);
            var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();
            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                fsm.Tick(inputs.Build(t));

            var climaxFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(climaxFake, "FINAL", playDurationSeconds: 0.02f);

            for (int t = 1000; t < 1200 && fsm.CurrentPhase != SessionPhase.GameOver; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver));

            Assert.That(fsm.LastBonusStarResult, Is.Not.Null);
            Assert.That(fsm.FinalPlaces, Is.Not.Null);

            // Independently recompute via the same public API the FSM itself uses.
            int[] expectedPlaces = FinalRanking.Rank(fsm.LastBonusStarResult.StarsAfter, fsm.Coins, fsm.MicrogameWins, 0b1111);
            Assert.That(fsm.FinalPlaces, Is.EqualTo(expectedPlaces));

            foreach (int place in fsm.FinalPlaces)
                Assert.That(place, Is.InRange(1, 4), "every active seat must land on a valid podium slot");
        }

        [Test]
        public void FullSessionReplay_Deterministic_IncludingWagerChoices()
        {
            // AC3/AC4: deterministic replay through the FULL FSM path (attract ->
            // game over) with wager choices in the input sequence.
            (WagerResult wager, BonusStarResult stars, int[] places) RunOnce()
            {
                var config = FastConfig(totalRounds: 1);
                var fsm = new SessionStateMachine(new SeededRandom(99), config);
                var round1 = new FakeMicrogame(finishAfterTicks: 2,
                    result: Ranked((0, 1, 60), (1, 2, 90), (2, 3, 120), (3, 4, 150)),
                    id: MicrogameId.Reacciona);
                fsm.SetActiveMicrogame(round1, "R1", playDurationSeconds: 0.05f);

                var inputs = JoinReadyInputs();
                fsm.InsertCredit();
                for (int t = 0; t < 600 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                    fsm.Tick(inputs.Build(t));

                var climax = new FakeMicrogame(finishAfterTicks: 2,
                    result: Ranked((0, 2, 0), (1, 1, 0), (2, 4, 0), (3, 3, 0)));
                fsm.SetActiveMicrogame(climax, "FINAL", playDurationSeconds: 0.05f);

                for (int t = 1000; t < 1300 && fsm.CurrentPhase == SessionPhase.FinalWager; t++)
                {
                    inputs.Set(PlayerSlot.Rojo, false, Direction8.W);
                    inputs.Set(PlayerSlot.Azul, false, Direction8.N);
                    inputs.Set(PlayerSlot.Amarillo, false, Direction8.E);
                    fsm.Tick(inputs.Build(t));
                }

                for (int t = 2000; t < 2300 && fsm.CurrentPhase != SessionPhase.GameOver; t++)
                    fsm.Tick(inputs.Build(t));

                Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver), "test setup sanity");
                return (fsm.LastWagerResult, fsm.LastBonusStarResult, fsm.FinalPlaces);
            }

            (WagerResult wager, BonusStarResult stars, int[] places) a = RunOnce();
            (WagerResult wager, BonusStarResult stars, int[] places) b = RunOnce();

            Assert.That(b.wager.Pot, Is.EqualTo(a.wager.Pot));
            Assert.That(b.wager.Stakes, Is.EqualTo(a.wager.Stakes));
            Assert.That(b.wager.Shares, Is.EqualTo(a.wager.Shares));
            Assert.That(b.wager.CoinsAfter, Is.EqualTo(a.wager.CoinsAfter));
            Assert.That(b.stars.First, Is.EqualTo(a.stars.First));
            Assert.That(b.stars.Second, Is.EqualTo(a.stars.Second));
            Assert.That(b.stars.FirstWinner, Is.EqualTo(a.stars.FirstWinner));
            Assert.That(b.stars.SecondWinner, Is.EqualTo(a.stars.SecondWinner));
            Assert.That(b.stars.StarsAfter, Is.EqualTo(a.stars.StarsAfter));
            Assert.That(b.places, Is.EqualTo(a.places));
        }

        [Test]
        public void ExplicitSessionSeedOverload_IsDeterministic_AndIndependentOfRngDraws()
        {
            // M3 (TASK-051 review fix round): the SessionStateMachine(rng,
            // sessionSeed, config) overload takes the scoring seed directly
            // instead of deriving it from rng's own first draw -- pinned by
            // running two sessions that use the SAME explicit sessionSeed but
            // DIFFERENT rng seeds, and confirming the bonus-star draw (which only
            // depends on sessionSeed, not rng) comes out identical regardless.
            (BonusStarResult stars, int[] places) RunWith(int rngSeed, int sessionSeed)
            {
                var config = FastConfig(totalRounds: 1);
                var fsm = new SessionStateMachine(new SeededRandom(rngSeed), sessionSeed, config);
                var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                    result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
                fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f);

                var inputs = JoinReadyInputs();
                fsm.InsertCredit();
                for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                    fsm.Tick(inputs.Build(t));

                var climaxFake = new FakeMicrogame(finishAfterTicks: 1,
                    result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
                fsm.SetActiveMicrogame(climaxFake, "FINAL", playDurationSeconds: 0.02f);

                for (int t = 1000; t < 1200 && fsm.CurrentPhase != SessionPhase.GameOver; t++)
                    fsm.Tick(inputs.Build(t));

                Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver), "test setup sanity");
                return (fsm.LastBonusStarResult, fsm.FinalPlaces);
            }

            (BonusStarResult stars, int[] places) a = RunWith(rngSeed: 1, sessionSeed: 777);
            (BonusStarResult stars, int[] places) b = RunWith(rngSeed: 2, sessionSeed: 777);

            Assert.That(b.stars.First, Is.EqualTo(a.stars.First), "same sessionSeed must draw the same bonus stars even with a different rng seed");
            Assert.That(b.stars.Second, Is.EqualTo(a.stars.Second));
            Assert.That(b.stars.StarsAfter, Is.EqualTo(a.stars.StarsAfter));
            Assert.That(b.places, Is.EqualTo(a.places));
        }

        [Test]
        public void DifferentSessionSeeds_ProduceDifferentBonusStarDraws()
        {
            // TASK-052 rider (rev-t051 origin): the complement of
            // ExplicitSessionSeedOverload_IsDeterministic_AndIndependentOfRngDraws
            // above -- a DIFFERENT explicit sessionSeed (same rng seed) must draw
            // a DIFFERENT (First, Second) bonus-star pair. Together the two tests
            // show sessionSeed, not rng, is what actually drives the draw.
            (BonusStarResult stars, int[] places) RunWith(int rngSeed, int sessionSeed)
            {
                var config = FastConfig(totalRounds: 1);
                var fsm = new SessionStateMachine(new SeededRandom(rngSeed), sessionSeed, config);
                var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                    result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
                fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f);

                var inputs = JoinReadyInputs();
                fsm.InsertCredit();
                for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                    fsm.Tick(inputs.Build(t));

                var climaxFake = new FakeMicrogame(finishAfterTicks: 1,
                    result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
                fsm.SetActiveMicrogame(climaxFake, "FINAL", playDurationSeconds: 0.02f);

                for (int t = 1000; t < 1200 && fsm.CurrentPhase != SessionPhase.GameOver; t++)
                    fsm.Tick(inputs.Build(t));

                Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver), "test setup sanity");
                return (fsm.LastBonusStarResult, fsm.FinalPlaces);
            }

            (BonusStarResult stars, int[] places) a = RunWith(rngSeed: 1, sessionSeed: 777);
            (BonusStarResult stars, int[] places) b = RunWith(rngSeed: 1, sessionSeed: 778);

            Assert.That((b.stars.First, b.stars.Second), Is.Not.EqualTo((a.stars.First, a.stars.Second)),
                "different sessionSeeds should draw a different bonus-star pair");
        }

        [Test]
        public void TwoArgConstructor_StillDerivesTheScoringSeedFromRngsFirstDraw()
        {
            // M3 companion pin: the original two-argument constructor's
            // first-draw derivation must still produce IDENTICAL results to
            // before this overload was added -- proving DeriveScoringSeed's
            // extraction into a separate static method changed nothing
            // byte-for-byte. Cross-checked against the explicit-seed overload:
            // constructing SeededRandom(seed) and immediately deriving
            // NextInt(int.MinValue, int.MaxValue) from it must equal whatever
            // the two-arg constructor's own internal derivation produces --
            // observed indirectly through the resulting bonus-star draw, since
            // _scoringSeed itself has no public accessor.
            var oracleRng = new SeededRandom(42);
            int expectedScoringSeed = oracleRng.NextInt(int.MinValue, int.MaxValue);

            (BonusStarResult stars, int[] places) RunSession(SessionStateMachine fsm)
            {
                var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                    result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
                fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f);

                var inputs = JoinReadyInputs();
                fsm.InsertCredit();
                for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                    fsm.Tick(inputs.Build(t));

                var climaxFake = new FakeMicrogame(finishAfterTicks: 1,
                    result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
                fsm.SetActiveMicrogame(climaxFake, "FINAL", playDurationSeconds: 0.02f);

                for (int t = 1000; t < 1200 && fsm.CurrentPhase != SessionPhase.GameOver; t++)
                    fsm.Tick(inputs.Build(t));

                Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver), "test setup sanity");
                return (fsm.LastBonusStarResult, fsm.FinalPlaces);
            }

            var config = FastConfig(totalRounds: 1);
            var viaTwoArg = new SessionStateMachine(new SeededRandom(42), config);
            var viaExplicitSeed = new SessionStateMachine(new SeededRandom(999) /* deliberately different rng */, expectedScoringSeed, config);

            (BonusStarResult stars, int[] places) fromTwoArg = RunSession(viaTwoArg);
            (BonusStarResult stars, int[] places) fromExplicitSeed = RunSession(viaExplicitSeed);

            Assert.That(fromTwoArg.stars.First, Is.EqualTo(fromExplicitSeed.stars.First),
                "the two-arg constructor's first-draw derivation must equal SeededRandom(42).NextInt(int.MinValue, int.MaxValue) exactly");
            Assert.That(fromTwoArg.stars.StarsAfter, Is.EqualTo(fromExplicitSeed.stars.StarsAfter));
            Assert.That(fromTwoArg.places, Is.EqualTo(fromExplicitSeed.places));
        }

        // ── M1 (TASK-051 review fix round): FinalWager gesture-capture semantics ──

        private static (SessionStateMachine fsm, FakeInputs inputs, int[] preWagerCoins) DriveToFinalWager(int seed)
        {
            // A longer FinalWager window than FastConfig's default 0.05s (3
            // ticks) -- these tests need room to hold one direction for a while,
            // then switch/release, and still be inside the window when checked.
            var config = new SessionStateMachineConfig(
                joinTimeoutSeconds: 30f, joinMinReady: 2, mgIntroSeconds: 0.02f, mgResultSeconds: 0.02f,
                intermissionSeconds: 0.02f, finalWagerSeconds: 0.5f, gameOverSeconds: 0.02f,
                totalRounds: 1, ticksPerSecond: 60);
            var fsm = new SessionStateMachine(new SeededRandom(seed), config);
            var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();
            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalWager), "test setup sanity");

            int[] preWagerCoins = (int[])fsm.Coins.Clone();

            var climaxFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(climaxFake, "FINAL", playDurationSeconds: 0.02f);

            return (fsm, inputs, preWagerCoins);
        }

        private static void FinishWagerAndGameOver(SessionStateMachine fsm, FakeInputs inputs, int startTick)
        {
            for (int t = startTick; t < startTick + 20 && fsm.CurrentPhase != SessionPhase.GameOver; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver), "test setup sanity");
        }

        [Test]
        public void FinalWager_LaterGestureOverridesEarlierOne()
        {
            // M1(i): a seat that changes their mind mid-window (Quarter then
            // ThreeQuarters) must resolve to the LATEST choice -- pins against a
            // first-write-wins refactor (sanity-checked non-vacuous below by
            // temporarily flipping the latch and confirming this exact test goes
            // red; see hand-off for the quoted failure).
            (SessionStateMachine fsm, FakeInputs inputs, int[] preWagerCoins) = DriveToFinalWager(60);

            // The window is 30 ticks total (finalWagerSeconds=0.5s @ 60Hz) --
            // hold Quarter for only the first 10, leaving plenty of the window
            // remaining for the switch to ThreeQuarters to register before it closes.
            int t = 1000;
            for (; t < 1000 + 10 && fsm.CurrentPhase == SessionPhase.FinalWager; t++)
            {
                inputs.Set(PlayerSlot.Rojo, false, Direction8.W); // Quarter first
                fsm.Tick(inputs.Build(t));
            }
            for (; t < 1000 + 200 && fsm.CurrentPhase == SessionPhase.FinalWager; t++)
            {
                inputs.Set(PlayerSlot.Rojo, false, Direction8.E); // then ThreeQuarters
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalMg), "test setup sanity: must have exited FinalWager");

            FinishWagerAndGameOver(fsm, inputs, 2000);

            Assert.That(fsm.LastWagerResult.Stakes[0], Is.EqualTo(preWagerCoins[0] * 75 / 100),
                "the LATER gesture (ThreeQuarters) must win, not the first (Quarter)");
        }

        [Test]
        public void FinalWager_ChooseThenReleaseToNeutral_StaysSticky()
        {
            // M1(ii): releasing to neutral (None) after picking a direction must
            // NOT clear the choice -- it stays sticky until overridden by another
            // MAPPED direction (Left/Up/Right).
            (SessionStateMachine fsm, FakeInputs inputs, int[] preWagerCoins) = DriveToFinalWager(61);

            int t = 1000;
            for (; t < 1000 + 10 && fsm.CurrentPhase == SessionPhase.FinalWager; t++)
            {
                inputs.Set(PlayerSlot.Rojo, false, Direction8.W); // picks Quarter
                fsm.Tick(inputs.Build(t));
            }
            for (; t < 1000 + 200 && fsm.CurrentPhase == SessionPhase.FinalWager; t++)
            {
                inputs.Set(PlayerSlot.Rojo, false, Direction8.None); // releases to neutral
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalMg), "test setup sanity");

            FinishWagerAndGameOver(fsm, inputs, 2000);

            Assert.That(fsm.LastWagerResult.Stakes[0], Is.EqualTo(preWagerCoins[0] * 25 / 100),
                "Quarter must stick even after releasing to neutral for the rest of the window");
        }

        [Test]
        public void FinalWager_DownOrNeverTouched_ResolveToTimeoutDefaultHalf()
        {
            // M1(iii): Down is a documented no-op, same as never touching the
            // stick at all -- both must resolve to FinalWager.DefaultChoice (Half).
            (SessionStateMachine fsm, FakeInputs inputs, int[] preWagerCoins) = DriveToFinalWager(62);

            for (int t = 1000; t < 1200 && fsm.CurrentPhase == SessionPhase.FinalWager; t++)
            {
                inputs.Set(PlayerSlot.Rojo, false, Direction8.S); // Down -- documented no-op
                // Azul: never touched at all.
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalMg), "test setup sanity");

            FinishWagerAndGameOver(fsm, inputs, 2000);

            Assert.That(fsm.LastWagerResult.Stakes[0], Is.EqualTo(preWagerCoins[0] * 50 / 100), "Rojo held Down the whole window -- must default to Half");
            Assert.That(fsm.LastWagerResult.Stakes[1], Is.EqualTo(preWagerCoins[1] * 50 / 100), "Azul never touched the stick -- must default to Half");
        }

        [Test]
        public void FinalWager_GestureDuringIntermission_DoesNotCarryOver()
        {
            // Optional M1 pin: a direction held during Intermission (before
            // FinalWager even begins) must not carry over into the wager --
            // true by construction (BeginFinalWager resets every seat's choice
            // to null on entry), pinned explicitly per the review's suggestion.
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(63), config);
            var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            bool sawIntermission = false;
            int t = 0;
            for (; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
            {
                if (fsm.CurrentPhase == SessionPhase.Intermission)
                {
                    sawIntermission = true;
                    inputs.Set(PlayerSlot.Rojo, false, Direction8.E); // held during Intermission: ThreeQuarters, if it counted
                }
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(sawIntermission, Is.True, "test setup sanity: the probe must actually pass through Intermission");
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalWager));

            int[] preWagerCoins = (int[])fsm.Coins.Clone();

            // Release to neutral before FinalWager's own window ever reads a
            // direction, and never touch the stick again for the rest of the window.
            inputs.Set(PlayerSlot.Rojo, false, Direction8.None);
            var climaxFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(climaxFake, "FINAL", playDurationSeconds: 0.02f);

            for (int t2 = 1000; t2 < 1200 && fsm.CurrentPhase == SessionPhase.FinalWager; t2++)
                fsm.Tick(inputs.Build(t2));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalMg), "test setup sanity");

            FinishWagerAndGameOver(fsm, inputs, 2000);

            Assert.That(fsm.LastWagerResult.Stakes[0], Is.EqualTo(preWagerCoins[0] * 50 / 100),
                "a gesture held only during Intermission must not carry into FinalWager -- must default to Half");
        }

        [Test]
        public void GameOver_ReturnsToAttract_ClearsScoringState()
        {
            // Scoring-side companion to the existing GameOver roster/ready/round
            // reset pin: a fresh session must not read back the previous
            // session's coins/wins/wager/stars/podium before its own Join even
            // completes.
            var config = FastConfig(totalRounds: 1);
            var fsm = new SessionStateMachine(new SeededRandom(56), config);
            var roundFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(roundFake, "R1", playDurationSeconds: 0.02f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();
            for (int t = 0; t < 500 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
                fsm.Tick(inputs.Build(t));

            var climaxFake = new FakeMicrogame(finishAfterTicks: 1,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(climaxFake, "FINAL", playDurationSeconds: 0.02f);

            bool loopedToAttract = false;
            for (int t = 1000; t < 5000 && !loopedToAttract; t++)
            {
                fsm.Tick(inputs.Build(t));
                if (t > 1050 && fsm.CurrentPhase == SessionPhase.Attract) loopedToAttract = true;
            }
            Assert.That(loopedToAttract, Is.True, "test setup sanity: the probe must complete a full session");

            Assert.That(fsm.Coins, Is.EqualTo(new[] { 0, 0, 0, 0 }));
            Assert.That(fsm.MicrogameWins, Is.EqualTo(new[] { 0, 0, 0, 0 }));
            Assert.That(fsm.LastWagerResult, Is.Null);
            Assert.That(fsm.LastBonusStarResult, Is.Null);
            Assert.That(fsm.FinalPlaces, Is.Null);
            Assert.That(fsm.Counters.WinnerOf(StarKind.Kamikaze, 0b1111), Is.EqualTo(-1), "a fresh SessionCounters has no eliminations recorded");
        }

        // ── TASK-056: dead-seat ("puesto muerto") detection ───────────────────────
        //
        // GDD §3.2, line 224 (verified firsthand): "Detección de puesto muerto: si un
        // puesto no genera ningún flanco durante 45 s en estados de juego, se marca
        // `Idle` y se excluye del reparto de la ronda (no elimina al jugador: puede
        // volver pulsando)." => 45 s (2700 ticks @ 60 Hz) with NO input edge while in
        // game states => SeatState.HumanIdle; excluded from that round's payout;
        // NOT eliminated; resumes on the next edge.
        //
        // [ASSUMED] activity source (GDD-silent on the exact stream): a "flanco" for a
        //   seat this tick = a debounced button press OR release edge (InputInterpreter)
        //   OR a change in the raw 8-way stick direction since the previous tick. Any
        //   button edge OR stick movement counts as activity.
        // [ASSUMED] "estados de juego" = BoardMove/BoardResolve/MgIntro/MgPlay/MgResult/
        //   Intermission plus FinalWager/FinalMg; NOT Attract/Join/GameOver (lobby/
        //   game-over menus, where a claimed seat's idleness is meaningless).
        // [ASSUMED] resume ("puede volver pulsando") fires on ANY flanco (button or
        //   stick), symmetric with the no-flanco detection, evaluated the tick the edge
        //   is seen; exclusion is evaluated at payout-application (CaptureResult) time.
        // [ASSUMED] scope = the coin "reparto" only. FINAL_WAGER/climax participation is
        //   unchanged (Idle seats still stake/receive via their coins), and MicrogameWins
        //   win-crediting is unchanged (win count is a tiebreak, not "reparto"). HUD/View
        //   signaling of Idle is out of Core scope (Unity-gated, a later ticket).

        /// <summary>
        /// A fast session config with a caller-chosen dead-seat window. Every other
        /// timeout is tiny so a full round completes quickly; only the idle window is
        /// under test here.
        /// </summary>
        private static SessionStateMachineConfig DeadSeatConfig(float idleTimeoutSeconds, int totalRounds = 1) =>
            new SessionStateMachineConfig(
                joinTimeoutSeconds: 30f, joinMinReady: 2, mgIntroSeconds: 0.02f, mgResultSeconds: 0.02f,
                intermissionSeconds: 0.02f, finalWagerSeconds: 0.02f, gameOverSeconds: 0.02f,
                totalRounds: totalRounds, ticksPerSecond: 60, idleTimeoutSeconds: idleTimeoutSeconds);

        /// <summary>
        /// Drives the FSM into a persistent MgPlay (never-finishing microgame, long
        /// ceiling) and then establishes a deterministic "last flanco" for Rojo via a
        /// single stick pulse (None-&gt;E-&gt;None). A raw-direction change is detected
        /// immediately (no debounce), so the return-to-None tick is Rojo's last edge and
        /// its idle counter is 0 right after this returns — every subsequent neutral
        /// (button held, stick None) tick is a no-flanco game tick.
        /// </summary>
        private static (SessionStateMachine fsm, FakeInputs inputs, int nextTick) ReachMgPlayAndResetRojo(
            SessionStateMachineConfig config, int seed)
        {
            var fsm = new SessionStateMachine(new SeededRandom(seed), config);
            var fake = new FakeMicrogame(finishAfterTicks: int.MaxValue); // never finishes -> MgPlay persists
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 100f); // 6000-tick ceiling
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 400 && fsm.CurrentPhase != SessionPhase.MgPlay; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgPlay), "test setup: must reach a persistent MgPlay");
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.Human), "test setup: Rojo starts Human");

            int t2 = 1000;
            inputs.Set(PlayerSlot.Rojo, true, Direction8.E);    fsm.Tick(inputs.Build(t2++)); // flanco: None->E
            inputs.Set(PlayerSlot.Rojo, true, Direction8.None); fsm.Tick(inputs.Build(t2++)); // flanco: E->None (counter := 0)
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.Human), "test setup: still Human right after the reset pulse");
            return (fsm, inputs, t2);
        }

        [Test]
        public void GddDefaults_IdleTimeout_Is45Seconds()
        {
            Assert.That(SessionStateMachineConfig.GddDefaults.IdleTimeoutSeconds, Is.EqualTo(45f),
                "GDD §3.2 line 224: 'Detección de puesto muerto ... durante 45 s en estados de juego'");
        }

        [Test]
        public void DeadSeat_MarkedIdle_ExactlyAtThreshold_NotOneTickEarly()
        {
            // Distinctive small window (0.5 s @ 60 Hz = 30 ticks) so the off-by-one is
            // pinned cheaply; the real 45 s -> 2700 conversion is pinned separately below.
            (SessionStateMachine fsm, FakeInputs inputs, int t) = ReachMgPlayAndResetRojo(DeadSeatConfig(0.5f), seed: 200);

            // 29 further neutral (no-flanco) game ticks must keep Rojo Human...
            for (int i = 0; i < 29; i++)
            {
                inputs.Set(PlayerSlot.Rojo, true, Direction8.None);
                fsm.Tick(inputs.Build(t++));
            }
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.Human),
                "must NOT be Idle one tick before the 30-tick (0.5 s) threshold");

            // ...the 30th crosses it.
            inputs.Set(PlayerSlot.Rojo, true, Direction8.None);
            fsm.Tick(inputs.Build(t++));
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.HumanIdle),
                "must be marked Idle exactly at the 30-tick (0.5 s @ 60 Hz) threshold");
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgPlay), "Idle does not eliminate the seat — the round continues");
        }

        [Test]
        public void DeadSeat_RealGddWindow_CrossesAtExactly2700Ticks()
        {
            // AC1 firsthand-GDD pin: 45 s @ 60 Hz must convert to exactly 2700 ticks
            // (off-by-one guard on the seconds->ticks conversion via TicksPerSecond).
            (SessionStateMachine fsm, FakeInputs inputs, int t) = ReachMgPlayAndResetRojo(DeadSeatConfig(45f), seed: 201);

            for (int i = 0; i < 2699; i++)
            {
                inputs.Set(PlayerSlot.Rojo, true, Direction8.None);
                fsm.Tick(inputs.Build(t++));
            }
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.Human),
                "still active at 2699 ticks (< 45 s)");

            inputs.Set(PlayerSlot.Rojo, true, Direction8.None);
            fsm.Tick(inputs.Build(t++));
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.HumanIdle),
                "marked Idle exactly at 2700 ticks (45 s @ 60 Hz)");
        }

        [Test]
        public void DeadSeat_ButtonEdgeCountsAsActivity_KeepsSeatAlive()
        {
            // A seat that keeps producing button edges (press/release) never crosses the
            // idle window even though its stick stays neutral — the button flanco alone
            // is activity.
            (SessionStateMachine fsm, FakeInputs inputs, int t) = ReachMgPlayAndResetRojo(DeadSeatConfig(0.5f), seed: 202);

            // Toggle Rojo's button every 3 ticks (well inside the 30-tick window). The
            // interpreter's 2-tick debounce still confirms an edge each toggle.
            for (int i = 0; i < 120; i++)
            {
                bool down = (i / 3) % 2 == 0;
                inputs.Set(PlayerSlot.Rojo, down, Direction8.None);
                fsm.Tick(inputs.Build(t++));
            }
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.Human),
                "sustained button edges are activity — the seat must stay alive well past the window");
        }

        [Test]
        public void IdleSeat_ExcludedFromCompetitiveRoundPayout_OthersUnaffected()
        {
            // AC2: an Idle seat "se excluye del reparto de la ronda". Its per-place
            // payout is withheld; every other seat's payout is unchanged; no coins are
            // created or destroyed for the excluded seat.
            var config = DeadSeatConfig(0.1f); // 6-tick window
            var fsm = new SessionStateMachine(new SeededRandom(210), config);
            var fake = new FakeMicrogame(finishAfterTicks: 20,
                result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(fake, "R1", playDurationSeconds: 1f); // 60-tick MgPlay ceiling

            var inputs = new FakeInputs();
            fsm.InsertCredit();

            // TASK-068: BOARD_RESOLVE now applies real, seed-dependent coin flows
            // before this round's microgame payout ever runs -- capture the
            // baseline the instant it completes (CurrentPhase reaches MgIntro) so
            // the exclusion check below is a DELTA, isolated from board noise.
            int t = 0;
            for (; fsm.CurrentPhase != SessionPhase.MgIntro && t < 2000; t++)
            {
                inputs.Set(PlayerSlot.Rojo, true, Direction8.None);           // neutral -> goes Idle
                Direction8 wig = (t % 2 == 0) ? Direction8.E : Direction8.W;  // flanco every tick -> stays active
                inputs.Set(PlayerSlot.Azul, true, wig);
                inputs.Set(PlayerSlot.Amarillo, true, wig);
                inputs.Set(PlayerSlot.Verde, true, wig);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro), "test setup sanity: must have completed BOARD_RESOLVE");
            int[] coinsBeforePayout = (int[])fsm.Coins.Clone();

            for (; fsm.CurrentPhase != SessionPhase.FinalWager && t < 2000; t++)
            {
                inputs.Set(PlayerSlot.Rojo, true, Direction8.None);
                Direction8 wig = (t % 2 == 0) ? Direction8.E : Direction8.W;
                inputs.Set(PlayerSlot.Azul, true, wig);
                inputs.Set(PlayerSlot.Amarillo, true, wig);
                inputs.Set(PlayerSlot.Verde, true, wig);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalWager), "test setup: reached end of the single regular round");
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.HumanIdle),
                "Rojo produced no input during the round and must be Idle at payout time");

            // Default competitive payout {6,4,2,1} for places [1,2,3,4]; Idle Rojo
            // (place 1) is excluded (0), the other three keep their per-place payout.
            int[] delta = new int[4];
            for (int i = 0; i < 4; i++) delta[i] = fsm.Coins[i] - coinsBeforePayout[i];
            Assert.That(delta, Is.EqualTo(new[] { 0, 4, 2, 1 }),
                "Idle Rojo (place 1) is excluded from the reparto; Azul/Amarillo/Verde keep their per-place payout");
        }

        [Test]
        public void IdleSeat_ExcludedFromCoopRoundPayout_OthersStillPaid()
        {
            // AC2, coop shape: a coop success pays every ACTIVE, non-Idle seat; the Idle
            // seat receives nothing.
            var config = DeadSeatConfig(0.1f);
            var fsm = new SessionStateMachine(new SeededRandom(211), config);
            var fake = new FakeMicrogame(finishAfterTicks: 20,
                result: new V2Result(ResultKind.CoopSuccess, Array.Empty<PlayerRank>(), 0));
            fsm.SetActiveMicrogame(fake, "R1", playDurationSeconds: 1f);

            var inputs = new FakeInputs();
            fsm.InsertCredit();

            // TASK-068: see the companion competitive-payout test for why this
            // captures a pre-payout baseline and asserts a DELTA.
            int t = 0;
            for (; fsm.CurrentPhase != SessionPhase.MgIntro && t < 2000; t++)
            {
                inputs.Set(PlayerSlot.Rojo, true, Direction8.None);
                Direction8 wig = (t % 2 == 0) ? Direction8.E : Direction8.W;
                inputs.Set(PlayerSlot.Azul, true, wig);
                inputs.Set(PlayerSlot.Amarillo, true, wig);
                inputs.Set(PlayerSlot.Verde, true, wig);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro), "test setup sanity: must have completed BOARD_RESOLVE");
            int[] coinsBeforePayout = (int[])fsm.Coins.Clone();

            for (; fsm.CurrentPhase != SessionPhase.FinalWager && t < 2000; t++)
            {
                inputs.Set(PlayerSlot.Rojo, true, Direction8.None);
                Direction8 wig = (t % 2 == 0) ? Direction8.E : Direction8.W;
                inputs.Set(PlayerSlot.Azul, true, wig);
                inputs.Set(PlayerSlot.Amarillo, true, wig);
                inputs.Set(PlayerSlot.Verde, true, wig);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.HumanIdle));
            int[] delta = new int[4];
            for (int i = 0; i < 4; i++) delta[i] = fsm.Coins[i] - coinsBeforePayout[i];
            Assert.That(delta, Is.EqualTo(new[] { 0, 4, 4, 4 }),
                "coop success (default 4) pays each active non-Idle seat; Idle Rojo is excluded");
        }

        [Test]
        public void IdleSeat_ResumesOnInput_AndRejoinsNextRoundPayout()
        {
            // AC2: an Idle seat is NOT eliminated — it resumes on the next input edge and
            // is included in the following round's payout.
            var config = DeadSeatConfig(0.1f, totalRounds: 2);
            var fsm = new SessionStateMachine(new SeededRandom(220), config);
            var r1 = new FakeMicrogame(finishAfterTicks: 20, result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(r1, "R1", playDurationSeconds: 1f);

            var inputs = new FakeInputs();
            fsm.InsertCredit();

            // TASK-068: BOARD_RESOLVE now applies real, seed-dependent coin flows
            // before EACH round's microgame payout runs -- every exclusion/resume
            // check below is a DELTA from that round's own BOARD_RESOLVE-complete
            // baseline (captured at MgIntro), isolating the microgame-payout
            // invariant under test from board noise. See the companion
            // IdleSeat_ExcludedFromCompetitiveRoundPayout_OthersUnaffected test.
            int t = 0;
            for (; fsm.CurrentPhase != SessionPhase.MgIntro && t < 2000; t++)
            {
                inputs.Set(PlayerSlot.Rojo, true, Direction8.None);
                Direction8 wig = (t % 2 == 0) ? Direction8.E : Direction8.W;
                inputs.Set(PlayerSlot.Azul, true, wig);
                inputs.Set(PlayerSlot.Amarillo, true, wig);
                inputs.Set(PlayerSlot.Verde, true, wig);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro), "test setup sanity: must have completed round 1's BOARD_RESOLVE");
            int[] coinsBeforeR1Payout = (int[])fsm.Coins.Clone();

            // Round 1: Rojo silent -> Idle -> excluded from R1's microgame payout.
            for (; fsm.CurrentPhase != SessionPhase.Intermission && t < 2000; t++)
            {
                inputs.Set(PlayerSlot.Rojo, true, Direction8.None);
                Direction8 wig = (t % 2 == 0) ? Direction8.E : Direction8.W;
                inputs.Set(PlayerSlot.Azul, true, wig);
                inputs.Set(PlayerSlot.Amarillo, true, wig);
                inputs.Set(PlayerSlot.Verde, true, wig);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Intermission), "test setup: reached end of round 1");
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.HumanIdle), "Rojo Idle after a silent round 1");
            Assert.That(fsm.Coins[(int)PlayerSlot.Rojo] - coinsBeforeR1Payout[(int)PlayerSlot.Rojo], Is.EqualTo(0),
                "Idle Rojo is excluded from round 1's microgame payout");

            // Round 2: Rojo now gives input -> resumes to Human and is paid again.
            var r2 = new FakeMicrogame(finishAfterTicks: 20, result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(r2, "R2", playDurationSeconds: 1f);
            for (; fsm.CurrentPhase != SessionPhase.MgIntro && t < 3000; t++)
            {
                Direction8 wig = (t % 2 == 0) ? Direction8.E : Direction8.W;
                inputs.Set(PlayerSlot.Rojo, true, wig); // Rojo active again
                inputs.Set(PlayerSlot.Azul, true, wig);
                inputs.Set(PlayerSlot.Amarillo, true, wig);
                inputs.Set(PlayerSlot.Verde, true, wig);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro), "test setup sanity: must have completed round 2's BOARD_RESOLVE");
            int[] coinsBeforeR2Payout = (int[])fsm.Coins.Clone();

            for (; fsm.CurrentPhase != SessionPhase.FinalWager && t < 3000; t++)
            {
                Direction8 wig = (t % 2 == 0) ? Direction8.E : Direction8.W;
                inputs.Set(PlayerSlot.Rojo, true, wig);
                inputs.Set(PlayerSlot.Azul, true, wig);
                inputs.Set(PlayerSlot.Amarillo, true, wig);
                inputs.Set(PlayerSlot.Verde, true, wig);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.FinalWager), "test setup: reached end of round 2");
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.Human),
                "Rojo resumed to Human on input — never eliminated");
            Assert.That(fsm.Coins[(int)PlayerSlot.Rojo] - coinsBeforeR2Payout[(int)PlayerSlot.Rojo], Is.EqualTo(6),
                "resumed Rojo (place 1) is included in round 2's microgame payout (+6)");
        }

        [Test]
        public void SessionWithIdleSeat_PreservesPotConservation_AndNonNegativeBalances()
        {
            // AC3: dead-seat exclusion must not corrupt the pot math — the FINAL_WAGER
            // still conserves every coin (sum of shares == pot) and no balance goes
            // negative, exactly as the TASK-051/052 invariants require.
            var config = new SessionStateMachineConfig(
                joinTimeoutSeconds: 30f, joinMinReady: 2, mgIntroSeconds: 0.02f, mgResultSeconds: 0.02f,
                intermissionSeconds: 0.02f, finalWagerSeconds: 0.2f, gameOverSeconds: 0.02f,
                totalRounds: 1, ticksPerSecond: 60, idleTimeoutSeconds: 0.1f);
            var fsm = new SessionStateMachine(new SeededRandom(230), config);
            var r1 = new FakeMicrogame(finishAfterTicks: 20, result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(r1, "R1", playDurationSeconds: 1f);

            var inputs = new FakeInputs();
            fsm.InsertCredit();
            for (int t = 0; fsm.CurrentPhase != SessionPhase.FinalWager && t < 2000; t++)
            {
                inputs.Set(PlayerSlot.Rojo, true, Direction8.None); // idle target
                Direction8 wig = (t % 2 == 0) ? Direction8.E : Direction8.W;
                inputs.Set(PlayerSlot.Azul, true, wig);
                inputs.Set(PlayerSlot.Amarillo, true, wig);
                inputs.Set(PlayerSlot.Verde, true, wig);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.Roster.Seats[(int)PlayerSlot.Rojo], Is.EqualTo(SeatState.HumanIdle), "test setup: Rojo Idle for the regular round");

            var climax = new FakeMicrogame(finishAfterTicks: 20, result: Ranked((0, 1, 0), (1, 2, 0), (2, 3, 0), (3, 4, 0)));
            fsm.SetActiveMicrogame(climax, "FINAL", playDurationSeconds: 1f);
            for (int t = 1000; fsm.CurrentPhase != SessionPhase.GameOver && t < 4000; t++)
            {
                inputs.Set(PlayerSlot.Rojo, true, Direction8.N); // everyone stakes Half
                inputs.Set(PlayerSlot.Azul, true, Direction8.N);
                inputs.Set(PlayerSlot.Amarillo, true, Direction8.N);
                inputs.Set(PlayerSlot.Verde, true, Direction8.N);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.GameOver), "test setup: reached GameOver");

            int totalShares = 0;
            foreach (int s in fsm.LastWagerResult.Shares) totalShares += s;
            Assert.That(totalShares, Is.EqualTo(fsm.LastWagerResult.Pot),
                "GDD 5.3/6.2 pot conservation must hold even with an Idle seat in the session");
            foreach (int c in fsm.LastWagerResult.CoinsAfter)
                Assert.That(c, Is.GreaterThanOrEqualTo(0), "no balance may go negative");
        }
    }
}
