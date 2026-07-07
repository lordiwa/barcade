using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Microgames.V2;
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

            public FakeMicrogame(int finishAfterTicks, V2Result? result = null)
            {
                _finishAfterTicks = finishAfterTicks;
                _result = result ?? new V2Result(ResultKind.CoopSuccess, Array.Empty<PlayerRank>(), 0);
            }

            public MicrogameId Id => MicrogameId.Reacciona;

            public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult)
            {
                InitializeCalled = true;
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

        private static FakeInputs JoinReadyInputs()
        {
            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, true);
            inputs.Set(PlayerSlot.Azul, true);
            return inputs;
        }

        // ── AC1: full graph shape ────────────────────────────────────────────────

        [Test]
        public void FullGraph_WalksEveryStateInOrder()
        {
            var fsm = new SessionStateMachine(new SeededRandom(1), FastConfig(totalRounds: 2));
            var fake = new FakeMicrogame(finishAfterTicks: 2);
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.05f);

            var inputs = JoinReadyInputs();
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Attract));
            fsm.InsertCredit();

            var seq = new List<SessionPhase> { fsm.CurrentPhase };
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
        public void Join_TwoReadyPlayers_AdvancesLongBeforeTimeout()
        {
            var fsm = new SessionStateMachine(new SeededRandom(2), FastConfig(totalRounds: 1));
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 10 && fsm.CurrentPhase == SessionPhase.Join; t++)
                fsm.Tick(inputs.Build(t));

            Assert.That(fsm.CurrentPhase, Is.Not.EqualTo(SessionPhase.Join), "2 ready seats must exit Join well before the 30s timeout");
            Assert.That(fsm.ReadyCount, Is.EqualTo(2));
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
            for (int t = 0; t < 8000 && !loopedToAttract; t++)
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
            var fsm = new SessionStateMachine(new SeededRandom(5), FastConfig(totalRounds: 1));
            var fake = new FakeMicrogame(finishAfterTicks: int.MaxValue); // ceiling drives every transition, never self-finishes
            fsm.SetActiveMicrogame(fake, "PRUEBA", playDurationSeconds: 0.05f);

            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            int expectedForwardedTicks = 0;
            bool loopedToAttract = false;
            for (int t = 0; t < 5000 && !loopedToAttract; t++)
            {
                bool shouldForward = fsm.CurrentPhase == SessionPhase.MgPlay || fsm.CurrentPhase == SessionPhase.FinalMg;
                if (shouldForward) expectedForwardedTicks++;
                fsm.Tick(inputs.Build(t));
                if (t > 50 && fsm.CurrentPhase == SessionPhase.Attract) loopedToAttract = true;
            }

            Assert.That(loopedToAttract, Is.True, "test setup sanity: the probe must complete a full session");
            Assert.That(expectedForwardedTicks, Is.GreaterThan(0), "test setup sanity: MgPlay/FinalMg must actually have been visited");
            Assert.That(fake.TickCount, Is.EqualTo(expectedForwardedTicks), "the active microgame must be ticked in exactly the ticks where CurrentPhase was MgPlay or FinalMg, and never otherwise");
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

        // ── AC4: board pass-through stubs ────────────────────────────────────────

        [Test]
        public void BoardStubs_AdvanceWithinOneTick()
        {
            var fsm = new SessionStateMachine(new SeededRandom(7), FastConfig(totalRounds: 1));
            var inputs = JoinReadyInputs();
            fsm.InsertCredit();

            for (int t = 0; t < 20 && fsm.CurrentPhase != SessionPhase.BoardMove; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardMove));

            fsm.Tick(inputs.Build(9000));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardResolve), "BoardMove must advance in <=1 tick with no board model wired");

            fsm.Tick(inputs.Build(9001));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgIntro), "BoardResolve must advance in <=1 tick with no board model wired");
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

            for (int t = 0; t < 200 && fsm.CurrentPhase != SessionPhase.Intermission; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Intermission));

            // 0.1s @ 60Hz = 6 ticks; assert it hasn't advanced 1 tick early.
            for (int i = 0; i < 5; i++) fsm.Tick(inputs.Build(1000 + i));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.Intermission), "must not advance before the configured Intermission duration elapses");

            fsm.Tick(inputs.Build(2000));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.BoardMove), "must advance once the configured Intermission duration has elapsed");
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

            for (int t = 0; t < 200 && fsm.CurrentPhase != SessionPhase.MgResult; t++)
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

            for (int t = 0; t < 200 && fsm.CurrentPhase != SessionPhase.MgResult; t++)
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

            for (int t = 0; t < 200 && fsm.CurrentPhase != SessionPhase.MgIntro; t++)
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

            for (int t = 0; t < 200 && fsm.CurrentPhase != SessionPhase.FinalWager; t++)
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

            for (int t = 0; t < 200 && fsm.CurrentPhase != SessionPhase.MgPlay; t++)
                fsm.Tick(inputs.Build(t));
            Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgPlay), "test setup sanity: must reach MgPlay before measuring");

            // Warm up: JIT the hot path first (established convention, see InputInterpreterTests).
            for (int t = 200; t < 264; t++)
            {
                inputs.Set(PlayerSlot.Rojo, (t % 2) == 0, Direction8.NE);
                fsm.Tick(inputs.Build(t));
            }
            Assert.That(fake.IsFinished, Is.False, "sensor setup: must still be live after warmup");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 264; t < 464; t++)
            {
                inputs.Set(PlayerSlot.Rojo, (t % 2) == 0, Direction8.NE);
                inputs.Set(PlayerSlot.Amarillo, (t % 3) == 0, Direction8.SW);
                fsm.Tick(inputs.Build(t));
                Assert.That(fake.IsFinished, Is.False, "sensor setup: must remain live for the whole measured window, or it wasn't measuring the live path");
                Assert.That(fsm.CurrentPhase, Is.EqualTo(SessionPhase.MgPlay), "sensor setup: must remain in MgPlay for the whole measured window");
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L));
        }
    }
}
