using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Microgames.V2;
// Same alias rationale as ReaccionaMicrogameTests: this namespace is lexically
// nested inside Barcade.Core, so plain v2 names that collide with v1 members
// (IMicrogame, InputSnapshot, MicrogameResult) resolve to the v1 types. Aliases
// sidestep the precedence rule.
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-102 — coverage for <see cref="SessionStateMachine"/> (§2.1/§2.2):
    /// the full session graph ATTRACT → JOIN → (BOARD_MOVE → BOARD_RESOLVE →
    /// MG_INTRO → MG_PLAY → MG_RESULT → INTERMISSION)* → FINAL_WAGER → FINAL_MG
    /// → GAME_OVER → ATTRACT, per-state timeouts from the §2.2 budget table,
    /// JOIN color-claim (>=2 players, 30 s), zero-input liveness, MG_PLAY-only
    /// gameplay input forwarding, board pass-through stubs, the wrapped
    /// <see cref="RoundPhaseMachine"/>, per-tick POCO state serialization, and
    /// replay determinism.
    ///
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class SessionStateMachineTests
    {
        private const int TicksPerSecond = 60;

        // ── Test doubles ─────────────────────────────────────────────────────

        /// <summary>Builds v2 snapshots from per-seat button state; backing array reused (no per-tick alloc).</summary>
        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(int seat, bool pressed, Direction8 stick = Direction8.None)
                => _players[seat] = new PlayerInput(stick, pressed);

            public void Clear()
            {
                for (int i = 0; i < 4; i++) _players[i] = default;
            }

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        /// <summary>
        /// v2 microgame double that records every Tick call and finishes after a
        /// configurable number of ticks (int.MaxValue = never finishes on its own).
        /// </summary>
        private sealed class RecordingMicrogame : V2Microgame
        {
            private readonly int _finishAfterTicks;
            private readonly RenderState _render = new RenderState(4, 4);
            private int _ticks;

            public RecordingMicrogame(int finishAfterTicks = int.MaxValue)
                => _finishAfterTicks = finishAfterTicks;

            public int TickCalls => _ticks;

            public MicrogameId Id => MicrogameId.Reacciona;

            public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult) { }

            public void Tick(in V2Snapshot input) => _ticks++;

            public bool IsFinished => _ticks >= _finishAfterTicks;

            public V2Result GetResult() => new V2Result(ResultKind.Ranked, Array.Empty<PlayerRank>(), 0);

            public RenderState GetRenderState() => _render;
        }

        // ── Drivers ──────────────────────────────────────────────────────────

        private static SessionConfig TwoRoundConfig()
        {
            var cfg = new SessionConfig();
            cfg.RoundsTotal = 2;
            return cfg;
        }

        /// <summary>Ticks the machine once with no buttons held.</summary>
        private static void TickIdle(SessionStateMachine m, FakeInputs inputs, ref int tick)
        {
            inputs.Clear();
            m.Tick(inputs.Build(tick));
            tick++;
        }

        /// <summary>
        /// Presses and releases the given seat's button for one tick each so the
        /// machine sees a clean rising edge.
        /// </summary>
        private static void Press(SessionStateMachine m, FakeInputs inputs, ref int tick, int seat)
        {
            inputs.Clear();
            inputs.Set(seat, true);
            m.Tick(inputs.Build(tick));
            tick++;
            inputs.Clear();
            m.Tick(inputs.Build(tick));
            tick++;
        }

        /// <summary>Inserts a credit and joins the given seats, leaving the machine just inside JOIN.</summary>
        private static void JoinSeats(SessionStateMachine m, FakeInputs inputs, ref int tick, params int[] seats)
        {
            m.InsertCredit();
            TickIdle(m, inputs, ref tick);
            Assert.That(m.State, Is.EqualTo(SessionState.Join), "credit should open the JOIN state");
            foreach (int seat in seats)
                Press(m, inputs, ref tick, seat);
        }

        /// <summary>
        /// Runs the machine with no input, staging a fresh microgame for every
        /// round when <paramref name="makeMicrogame"/> is non-null, until the
        /// predicate holds or the bound is hit. Returns ticks consumed.
        /// </summary>
        private static int RunUntil(
            SessionStateMachine m,
            FakeInputs inputs,
            ref int tick,
            Func<SessionStateMachine, bool> done,
            int maxTicks,
            Func<RecordingMicrogame> makeMicrogame = null,
            List<SessionState> transitionLog = null,
            Action<SessionStateMachine> onTicked = null)
        {
            int consumed = 0;
            SessionState last = m.State;
            for (; consumed < maxTicks; consumed++)
            {
                if (done(m)) return consumed;

                if (makeMicrogame != null && !m.HasStagedRound &&
                    (m.State == SessionState.BoardMove ||
                     m.State == SessionState.BoardResolve ||
                     m.State == SessionState.FinalWager))
                {
                    m.StageRound(makeMicrogame(), "¡PRUEBA!", 5f);
                }

                TickIdle(m, inputs, ref tick);
                onTicked?.Invoke(m);

                if (transitionLog != null && m.State != last)
                    transitionLog.Add(m.State);
                last = m.State;
            }
            Assert.Fail($"condition not reached within {maxTicks} ticks; stuck in {m.State}");
            return consumed;
        }

        /// <summary>Counts how many ticks the machine spends in <paramref name="state"/> the next time it passes through it.</summary>
        private static int MeasureStateDuration(
            SessionStateMachine m, FakeInputs inputs, ref int tick, SessionState state,
            int maxTicks, Func<RecordingMicrogame> makeMicrogame = null)
        {
            RunUntil(m, inputs, ref tick, x => x.State == state, maxTicks, makeMicrogame);
            int duration = 0;
            for (int i = 0; i < maxTicks && m.State == state; i++)
            {
                TickIdle(m, inputs, ref tick);
                duration++;
            }
            Assert.That(m.State, Is.Not.EqualTo(state), $"never left {state} within {maxTicks} ticks");
            return duration;
        }

        // ── AC1: graph shape ─────────────────────────────────────────────────

        [Test]
        public void Ctor_StartsInAttract_WithGddDefaults()
        {
            var m = new SessionStateMachine();

            Assert.That(m.State, Is.EqualTo(SessionState.Attract));
            Assert.That(m.RoundsTotal, Is.EqualTo(7), "GDD 9.3 reference timeline is 7 rounds");
            Assert.That(m.JoinedSeatsMask, Is.EqualTo(0));
            Assert.That(m.RoundIndex, Is.EqualTo(0));
        }

        [Test]
        public void FullSession_TraversesGddGraphInOrder()
        {
            var m = new SessionStateMachine(TwoRoundConfig());
            var inputs = new FakeInputs();
            int tick = 0;

            var log = new List<SessionState> { m.State };
            var seen = new List<SessionState>();
            m.InsertCredit();
            TickIdle(m, inputs, ref tick);
            log.Add(m.State);
            Press(m, inputs, ref tick, 0);
            Press(m, inputs, ref tick, 1);
            Press(m, inputs, ref tick, 2);
            Press(m, inputs, ref tick, 3); // all 4 claimed -> starts immediately
            seen.Add(m.State);

            RunUntil(m, inputs, ref tick,
                x => x.State == SessionState.Attract,
                maxTicks: 100_000,
                makeMicrogame: () => new RecordingMicrogame(finishAfterTicks: 60),
                transitionLog: seen);

            var expected = new List<SessionState>
            {
                SessionState.BoardMove, SessionState.BoardResolve,
                SessionState.MgIntro, SessionState.MgPlay,
                SessionState.MgResult, SessionState.Intermission,
                SessionState.BoardMove, SessionState.BoardResolve,
                SessionState.MgIntro, SessionState.MgPlay,
                SessionState.MgResult, SessionState.Intermission,
                SessionState.FinalWager, SessionState.FinalMg,
                SessionState.GameOver, SessionState.Attract
            };
            Assert.That(log, Is.EqualTo(new List<SessionState> { SessionState.Attract, SessionState.Join }));
            Assert.That(seen, Is.EqualTo(expected),
                "session must follow the GDD 2.1 graph exactly (2-round config)");
        }

        // ── AC1: JOIN color-claim ────────────────────────────────────────────

        [Test]
        public void Join_ClaimsSeatsOnButtonEdge_AndTracksMask()
        {
            var m = new SessionStateMachine();
            var inputs = new FakeInputs();
            int tick = 0;

            JoinSeats(m, inputs, ref tick, 0, 2);

            Assert.That(m.State, Is.EqualTo(SessionState.Join));
            Assert.That(m.IsSeatJoined(PlayerSlot.Rojo), Is.True);
            Assert.That(m.IsSeatJoined(PlayerSlot.Amarillo), Is.True);
            Assert.That(m.IsSeatJoined(PlayerSlot.Azul), Is.False);
            Assert.That(m.IsSeatJoined(PlayerSlot.Verde), Is.False);
            Assert.That(m.JoinedSeatsMask, Is.EqualTo(0b0101));
            Assert.That(m.JoinedCount, Is.EqualTo(2));
        }

        [Test]
        public void Join_RepeatPressBySameSeat_DoesNotDoubleClaim()
        {
            var m = new SessionStateMachine();
            var inputs = new FakeInputs();
            int tick = 0;

            JoinSeats(m, inputs, ref tick, 1);
            Press(m, inputs, ref tick, 1);
            Press(m, inputs, ref tick, 1);

            Assert.That(m.JoinedCount, Is.EqualTo(1));
            Assert.That(m.JoinedSeatsMask, Is.EqualTo(0b0010));
        }

        [Test]
        public void Join_TwoPlayersReady_ProceedsAtThirtySecondTimeout()
        {
            var m = new SessionStateMachine();
            var inputs = new FakeInputs();
            int tick = 0;

            JoinSeats(m, inputs, ref tick, 0, 1);

            int spent = RunUntil(m, inputs, ref tick,
                x => x.State != SessionState.Join, maxTicks: 31 * TicksPerSecond);

            Assert.That(m.State, Is.EqualTo(SessionState.BoardMove),
                ">=2 ready players at the 30 s timeout start the session");
            // The JOIN window is 30 s total; we already consumed a few ticks joining.
            Assert.That(spent, Is.LessThanOrEqualTo(30 * TicksPerSecond));
            Assert.That(spent, Is.GreaterThan(29 * TicksPerSecond),
                "JOIN must hold the full 30 s window open for late joiners");
        }

        [Test]
        public void Join_FewerThanTwoAtTimeout_ReturnsToAttract()
        {
            var m = new SessionStateMachine();
            var inputs = new FakeInputs();
            int tick = 0;

            JoinSeats(m, inputs, ref tick, 3); // only one player

            RunUntil(m, inputs, ref tick,
                x => x.State != SessionState.Join, maxTicks: 31 * TicksPerSecond);

            Assert.That(m.State, Is.EqualTo(SessionState.Attract),
                "a lone player at the 30 s timeout aborts back to ATTRACT");
            Assert.That(m.JoinedSeatsMask, Is.EqualTo(0), "claims are cleared on abort");
        }

        [Test]
        public void Join_AllFourClaimed_StartsWithoutWaitingForTimeout()
        {
            var m = new SessionStateMachine();
            var inputs = new FakeInputs();
            int tick = 0;

            JoinSeats(m, inputs, ref tick, 0, 1, 2, 3);

            Assert.That(m.State, Is.EqualTo(SessionState.BoardMove),
                "a full cabinet has nobody left to wait for");
        }

        [Test]
        public void Attract_InsertCredit_OpensJoin()
        {
            var m = new SessionStateMachine();
            var inputs = new FakeInputs();
            int tick = 0;

            m.InsertCredit();
            TickIdle(m, inputs, ref tick);

            Assert.That(m.State, Is.EqualTo(SessionState.Join));
        }

        [Test]
        public void Attract_ButtonPress_ActsAsFreePlayCredit()
        {
            var m = new SessionStateMachine();
            var inputs = new FakeInputs();
            int tick = 0;

            Press(m, inputs, ref tick, 2);

            Assert.That(m.State, Is.EqualTo(SessionState.Join));
        }

        // ── AC2: timeouts + zero-input liveness ──────────────────────────────

        [Test]
        public void ZeroInputAfterJoin_ReachesGameOver_ThenAttract()
        {
            var m = new SessionStateMachine(); // full 7 rounds, nothing staged
            var inputs = new FakeInputs();
            int tick = 0;

            JoinSeats(m, inputs, ref tick, 0, 1);

            RunUntil(m, inputs, ref tick,
                x => x.State == SessionState.GameOver, maxTicks: 200_000);
            RunUntil(m, inputs, ref tick,
                x => x.State == SessionState.Attract, maxTicks: 21 * TicksPerSecond);

            Assert.That(m.State, Is.EqualTo(SessionState.Attract),
                "an abandoned session must drain to GAME_OVER and re-enter ATTRACT unaided");
        }

        [Test]
        public void StateDurations_MatchGddBudgetTable()
        {
            var m = new SessionStateMachine(TwoRoundConfig());
            var inputs = new FakeInputs();
            int tick = 0;
            JoinSeats(m, inputs, ref tick, 0, 1, 2, 3);

            int intro = MeasureStateDuration(m, inputs, ref tick, SessionState.MgIntro, 5_000);
            int result = MeasureStateDuration(m, inputs, ref tick, SessionState.MgResult, 5_000);
            int intermission = MeasureStateDuration(m, inputs, ref tick, SessionState.Intermission, 5_000);

            Assert.That(intro, Is.EqualTo((int)(0.8f * TicksPerSecond)).Within(1), "MG_INTRO is 0.8 s fixed");
            Assert.That(result, Is.EqualTo((int)(1.5f * TicksPerSecond)).Within(1), "MG_RESULT is 1.5 s fixed");
            Assert.That(intermission, Is.EqualTo(2 * TicksPerSecond).Within(1), "INTERMISSION is 2 s fixed");
        }

        [Test]
        public void FinalWagerAndGameOver_TimeOutWithZeroInput()
        {
            var cfg = TwoRoundConfig();
            cfg.RoundsTotal = 1;
            var m = new SessionStateMachine(cfg);
            var inputs = new FakeInputs();
            int tick = 0;
            JoinSeats(m, inputs, ref tick, 0, 1, 2, 3);

            int wager = MeasureStateDuration(m, inputs, ref tick, SessionState.FinalWager, 60_000);
            Assert.That(wager, Is.GreaterThan(0), "FINAL_WAGER must have a timeout (every state does)");
            Assert.That(wager, Is.LessThanOrEqualTo(30 * TicksPerSecond),
                "FINAL_WAGER cannot hold the session hostage");

            int gameOver = MeasureStateDuration(m, inputs, ref tick, SessionState.GameOver, 60_000);
            Assert.That(gameOver, Is.EqualTo(20 * TicksPerSecond).Within(1),
                "GAME_OVER returns to ATTRACT after 20 s (GDD 2.1)");
            Assert.That(m.State, Is.EqualTo(SessionState.Attract));
        }

        [Test]
        public void MgPlay_NoMicrogameStaged_RunsDefaultDurationThenAdvances()
        {
            var m = new SessionStateMachine(TwoRoundConfig());
            var inputs = new FakeInputs();
            int tick = 0;
            JoinSeats(m, inputs, ref tick, 0, 1, 2, 3);

            int play = MeasureStateDuration(m, inputs, ref tick, SessionState.MgPlay, 20_000);

            Assert.That(play, Is.GreaterThanOrEqualTo(3 * TicksPerSecond), "GDD play window is 3-5 s");
            Assert.That(play, Is.LessThanOrEqualTo(5 * TicksPerSecond + 1));
        }

        [Test]
        public void MgPlay_MicrogameNeverFinishes_HardCappedAtEightSeconds()
        {
            var m = new SessionStateMachine(TwoRoundConfig());
            var inputs = new FakeInputs();
            int tick = 0;
            JoinSeats(m, inputs, ref tick, 0, 1, 2, 3);

            int play = MeasureStateDuration(m, inputs, ref tick, SessionState.MgPlay, 20_000,
                makeMicrogame: () => new RecordingMicrogame(finishAfterTicks: int.MaxValue));

            Assert.That(play, Is.EqualTo(8 * TicksPerSecond).Within(1),
                "GDD 2.2: MG_PLAY hard maximum is 8 s even if the mechanic never reports finished");
        }

        [Test]
        public void MgPlay_MicrogameFinishesEarly_AdvancesEarly()
        {
            var m = new SessionStateMachine(TwoRoundConfig());
            var inputs = new FakeInputs();
            int tick = 0;
            JoinSeats(m, inputs, ref tick, 0, 1, 2, 3);

            int play = MeasureStateDuration(m, inputs, ref tick, SessionState.MgPlay, 20_000,
                makeMicrogame: () => new RecordingMicrogame(finishAfterTicks: 60));

            Assert.That(play, Is.EqualTo(60).Within(1),
                "MG_PLAY ends as soon as the mechanic reports finished");
        }

        // ── AC3: gameplay input gating ───────────────────────────────────────

        [Test]
        public void GameplayInput_ReachesMicrogameOnlyDuringPlayStates()
        {
            var m = new SessionStateMachine(TwoRoundConfig());
            var inputs = new FakeInputs();
            int tick = 0;
            JoinSeats(m, inputs, ref tick, 0, 1, 2, 3);

            RecordingMicrogame current = null;
            int forwardedOutsidePlay = 0;
            int totalForwarded = 0;
            int lastSeen = 0;

            RunUntil(m, inputs, ref tick,
                x => x.State == SessionState.GameOver,
                maxTicks: 100_000,
                makeMicrogame: () =>
                {
                    current = new RecordingMicrogame(finishAfterTicks: 90);
                    lastSeen = 0;
                    return current;
                },
                onTicked: x =>
                {
                    if (current == null) return;
                    int delta = current.TickCalls - lastSeen;
                    lastSeen = current.TickCalls;
                    totalForwarded += delta;
                    if (delta > 0 &&
                        x.State != SessionState.MgPlay &&
                        x.State != SessionState.FinalMg)
                        forwardedOutsidePlay++;
                });

            Assert.That(totalForwarded, Is.GreaterThan(0), "microgames must actually receive input during play");
            Assert.That(forwardedOutsidePlay, Is.EqualTo(0),
                "GDD 2.1 invariant: only the play states forward gameplay input to the mechanic");
        }

        // ── AC4: board pass-through stubs ────────────────────────────────────

        [Test]
        public void BoardStates_PassThroughInOneTickEach()
        {
            var m = new SessionStateMachine(TwoRoundConfig());
            var inputs = new FakeInputs();
            int tick = 0;
            JoinSeats(m, inputs, ref tick, 0, 1, 2, 3);

            int move = MeasureStateDuration(m, inputs, ref tick, SessionState.BoardMove, 1_000);
            int resolve = MeasureStateDuration(m, inputs, ref tick, SessionState.BoardResolve, 1_000);

            Assert.That(move, Is.LessThanOrEqualTo(1), "BOARD_MOVE is a <=1-tick stub until Hito 4");
            Assert.That(resolve, Is.LessThanOrEqualTo(1), "BOARD_RESOLVE is a <=1-tick stub until Hito 4");
        }

        // ── AC5: serialization + replay determinism ──────────────────────────

        [Test]
        public void Snapshot_IsAPlainSerializableValueType()
        {
            var m = new SessionStateMachine();
            SessionStateSnapshot snap = m.Capture();

            Assert.That(typeof(SessionStateSnapshot).IsValueType, Is.True,
                "per-tick state must be a POCO value type (GDD 13 replay)");
            Assert.That(snap.State, Is.EqualTo(SessionState.Attract));
            Assert.That(snap.Tick, Is.EqualTo(0));
            Assert.That(snap.RoundIndex, Is.EqualTo(0));
            Assert.That(snap.JoinedSeatsMask, Is.EqualTo(0));
        }

        [Test]
        public void Replay_SameSeedAndInputSequence_ProducesIdenticalTrace()
        {
            SessionConfig cfg = TwoRoundConfig();
            var a = new SessionStateMachine(cfg, sessionSeed: 1234);
            var b = new SessionStateMachine(cfg, sessionSeed: 1234);

            var inputsA = new FakeInputs();
            var inputsB = new FakeInputs();
            var traceA = new List<SessionStateSnapshot>();
            var traceB = new List<SessionStateSnapshot>();

            void Drive(SessionStateMachine m, FakeInputs inputs, List<SessionStateSnapshot> trace)
            {
                for (int t = 0; t < 40_000; t++)
                {
                    if (t == 5) m.InsertCredit();
                    inputs.Clear();
                    // scripted joins: seat 0 at ticks 10-11, seat 1 at 20-21,
                    // plus stray gameplay presses mid-session
                    if (t == 10 || t == 20 || t == 500 || t == 2000) inputs.Set(t == 20 ? 1 : 0, true);
                    m.Tick(inputs.Build(t));
                    trace.Add(m.Capture());
                }
            }

            Drive(a, inputsA, traceA);
            Drive(b, inputsB, traceB);

            Assert.That(traceA.Count, Is.EqualTo(traceB.Count));
            for (int i = 0; i < traceA.Count; i++)
                Assert.That(traceA[i].Equals(traceB[i]), Is.True,
                    $"trace diverged at tick {i}: {traceA[i].State}/{traceA[i].StateElapsedTicks} vs {traceB[i].State}/{traceB[i].StateElapsedTicks}");
        }

        // ── AC6: RoundPhaseMachine is wrapped, not rewritten ─────────────────

        [Test]
        public void RoundMachine_IsExposed_AndCarriesTheStagedVerb()
        {
            var m = new SessionStateMachine(TwoRoundConfig());
            var inputs = new FakeInputs();
            int tick = 0;
            JoinSeats(m, inputs, ref tick, 0, 1, 2, 3);

            Assert.That(m.RoundMachine, Is.Not.Null,
                "T-102 wraps the existing RoundPhaseMachine (Annex D.1: extend, don't rewrite)");

            RunUntil(m, inputs, ref tick,
                x => x.State == SessionState.MgIntro, maxTicks: 5_000,
                makeMicrogame: () => new RecordingMicrogame(finishAfterTicks: 60));

            Assert.That(m.RoundMachine.VerbText, Is.EqualTo("¡PRUEBA!"),
                "the staged verb flows through RoundPhaseMachine.StartRound");
            Assert.That(m.RoundMachine.CurrentPhase, Is.EqualTo(PhaseKind.CommandShow),
                "MG_INTRO maps onto the wrapped machine's CommandShow phase");
        }

        // ── Session reset ────────────────────────────────────────────────────

        [Test]
        public void GameOverToAttract_ClearsSessionState()
        {
            var cfg = TwoRoundConfig();
            cfg.RoundsTotal = 1;
            var m = new SessionStateMachine(cfg);
            var inputs = new FakeInputs();
            int tick = 0;
            JoinSeats(m, inputs, ref tick, 0, 1, 2, 3);

            RunUntil(m, inputs, ref tick,
                x => x.State == SessionState.Attract, maxTicks: 100_000);

            Assert.That(m.JoinedSeatsMask, Is.EqualTo(0), "claims are cleared for the next group");
            Assert.That(m.RoundIndex, Is.EqualTo(0), "round counter resets");
        }

        // ── Zero allocation in steady state ──────────────────────────────────

        [Test]
        public void Tick_SteadyStateDuringPlay_AllocatesNothing()
        {
            var m = new SessionStateMachine(TwoRoundConfig());
            var inputs = new FakeInputs();
            int tick = 0;
            JoinSeats(m, inputs, ref tick, 0, 1, 2, 3);

            m.StageRound(new RecordingMicrogame(finishAfterTicks: int.MaxValue), "¡PRUEBA!", 5f);
            RunUntil(m, inputs, ref tick,
                x => x.State == SessionState.MgPlay, maxTicks: 5_000);

            // Prime any lazy one-time work, then measure.
            for (int i = 0; i < 10; i++) TickIdle(m, inputs, ref tick);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                inputs.Clear();
                m.Tick(inputs.Build(tick));
                tick++;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0),
                "SessionStateMachine.Tick must allocate zero heap bytes in steady state");
        }
    }
}
