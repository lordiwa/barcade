using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Microgames.V2;
// This test file's own namespace (Barcade.Core.Tests) is lexically nested inside
// Barcade.Core, and an unqualified name is resolved against member declarations
// of enclosing namespaces before "using"-imported names — even before using
// aliases declared at file scope. Barcade.Core.MicrogameResult/IMicrogame/InputSnapshot
// (v1) therefore win over the "using Barcade.Core.Microgames.V2;" import for those
// plain names. Renamed aliases (V2Result/V2Microgame/V2Snapshot) sidestep the
// collision entirely instead of fighting that precedence rule.
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-103 — coverage for <see cref="ReaccionaMicrogame"/> (§4 MECH_05):
    /// v2 IMicrogame contract shape, signal-delay determinism, tick-resolution
    /// latency/ties, false start + anti-spam, the 90 ms anticipation threshold
    /// (and its interaction with InputInterpreter's debounce), fakeouts/rounds,
    /// empty-seat exclusion, all-false-start safety, and zero allocation.
    ///
    /// Drives the mechanic through the GDD-literal, session-level v2
    /// <see cref="InputSnapshot"/>/<see cref="PlayerInput"/> contract — not the v1
    /// per-seat <c>IReadOnlyPlayerInputs</c> (that stays internal to
    /// <c>ReaccionaMicrogame</c>'s private input bridge).
    ///
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class ReaccionaMicrogameTests
    {
        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        // ── Test double: builds a v2 InputSnapshot from per-seat pressed flags ────

        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(PlayerSlot slot, bool pressed, Direction8 stick = Direction8.None)
                => _players[(int)slot] = new PlayerInput(stick, pressed);

            /// <summary>Wraps the current per-seat state into a v2 snapshot for this tick. No allocation beyond the struct itself (the backing array is reused).</summary>
            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        // ── Test helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Runs a fresh microgame with no presses at all until the true signal's
        /// CueSignal feedback event is observed, and returns the tick it fired on.
        /// Discovers timing via the public contract only — never assumes anything
        /// about SeededRandom's internal sequence.
        /// </summary>
        private static int ProbeSignalTick(ReaccionaParams p, int seed, PlayerRoster roster)
        {
            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(seed), roster, 1f);
            var inputs = new FakeInputs();

            for (int t = 0; t < 20000; t++)
            {
                mg.Tick(inputs.Build(t));
                RenderState rs = mg.GetRenderState();
                for (int i = 0; i < rs.FeedbackCount; i++)
                    if (rs.Feedback[i].Cue == ReaccionaMicrogame.CueSignal)
                        return rs.Tick;
                if (mg.IsFinished) break;
            }
            throw new InvalidOperationException("signal never fired within probe bound");
        }

        /// <summary>
        /// Drives ticks 0..N with each scheduled seat's raw button held pressed on
        /// exactly the two ticks needed to make InputInterpreter confirm the press
        /// edge on <c>confirmAtTick</c> (its 2-tick-confirm debounce means the
        /// confirmed edge lands one tick after the second raw sample) — i.e. set
        /// pressed at confirmAtTick-1 and confirmAtTick. Unscheduled seats stay
        /// released throughout. Stops early once the microgame finishes.
        /// </summary>
        private static void RunScheduled(ReaccionaMicrogame mg, IDictionary<PlayerSlot, int> confirmAtTick, int maxTicks = 5000)
        {
            var inputs = new FakeInputs();
            for (int t = 0; t < maxTicks; t++)
            {
                foreach (PlayerSlot slot in AllSlots)
                {
                    bool scheduled = confirmAtTick.TryGetValue(slot, out int target);
                    bool pressed = scheduled && (t == target - 1 || t == target);
                    inputs.Set(slot, pressed);
                }
                mg.Tick(inputs.Build(t));
                if (mg.IsFinished) return;
            }
            throw new InvalidOperationException("microgame did not finish within maxTicks");
        }

        private static PlayerRank FindRank(V2Result result, PlayerSlot slot)
        {
            foreach (PlayerRank r in result.Ranks)
                if (r.Seat == (int)slot) return r;
            throw new InvalidOperationException("seat not found in result");
        }

        // ── AC1 — v2 contract shape ─────────────────────────────────────────────

        [Test]
        public void Contract_InitialState_IdAndShapeAreCorrect()
        {
            var mg = new ReaccionaMicrogame(ReaccionaParams.GddDefaults);
            Assert.That(mg, Is.InstanceOf<V2Microgame>());
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.Reacciona));
            Assert.That(mg.IsFinished, Is.False);
        }

        [Test]
        public void Contract_GetResult_BeforeFinished_Throws()
        {
            var mg = new ReaccionaMicrogame(ReaccionaParams.GddDefaults);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            Assert.Throws<InvalidOperationException>(() => mg.GetResult());
        }

        [Test]
        public void Contract_RenderState_ReflectsActiveSeatsAfterFirstTick()
        {
            var mg = new ReaccionaMicrogame(ReaccionaParams.GddDefaults);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();
            mg.Tick(inputs.Build(0));

            RenderState rs = mg.GetRenderState();
            Assert.That(rs.EntityCount, Is.EqualTo(4));
            for (int i = 0; i < rs.EntityCount; i++)
                Assert.That(rs.Entities[i].Kind, Is.EqualTo(EntityKind.PlayerAvatar));
        }

        // ── AC2 — determinism ───────────────────────────────────────────────────

        [Test]
        public void SignalDelay_SameSeed_ProducesSameSignalTick()
        {
            ReaccionaParams p = ReaccionaParams.GddDefaults;
            int tickA = ProbeSignalTick(p, seed: 777, PlayerRoster.AllHuman);
            int tickB = ProbeSignalTick(p, seed: 777, PlayerRoster.AllHuman);
            Assert.That(tickB, Is.EqualTo(tickA));
        }

        [Test]
        public void SignalDelay_DifferentSeeds_CanProduceDifferentSignalTicks()
        {
            ReaccionaParams p = ReaccionaParams.GddDefaults;
            int tickA = ProbeSignalTick(p, seed: 1, PlayerRoster.AllHuman);
            int tickB = ProbeSignalTick(p, seed: 2, PlayerRoster.AllHuman);
            // Not a hard guarantee for arbitrary seeds in general, but true for this
            // pinned pair — guards against a constant-signal-tick regression.
            Assert.That(tickB, Is.Not.EqualTo(tickA));
        }

        [Test]
        public void Determinism_SameSeedAndInputScript_ProducesIdenticalFeedbackAndResult()
        {
            var p = new ReaccionaParams(
                fakeouts: 2, rounds: 3,
                signalDelayMinSeconds: 1.5f, signalDelayMaxSeconds: 4.5f,
                anticipationThresholdTicks: 5, reactionTimeoutSeconds: 0.5f,
                inputConfig: InputInterpreterConfig.GddDefaults);
            const int seed = 4242;

            var traceA = RunReactiveTrace(p, seed, PlayerSlot.Rojo, latencyAfterSignal: 20);
            var traceB = RunReactiveTrace(p, seed, PlayerSlot.Rojo, latencyAfterSignal: 20);

            Assert.That(traceB.Feedback, Is.EqualTo(traceA.Feedback));
            Assert.That(traceB.Result.Ranks.Length, Is.EqualTo(traceA.Result.Ranks.Length));
            for (int i = 0; i < traceA.Result.Ranks.Length; i++)
            {
                Assert.That(traceB.Result.Ranks[i].Seat, Is.EqualTo(traceA.Result.Ranks[i].Seat));
                Assert.That(traceB.Result.Ranks[i].Place, Is.EqualTo(traceA.Result.Ranks[i].Place));
                Assert.That(traceB.Result.Ranks[i].Metric, Is.EqualTo(traceA.Result.Ranks[i].Metric));
            }
        }

        /// <summary>
        /// Drives a fresh microgame reactively: watches for each tanda's own
        /// CueSignal event and schedules <paramref name="reactSlot"/>'s confirmed
        /// press <paramref name="latencyAfterSignal"/> ticks after it. Other seats
        /// never press (they resolve via the reaction timeout). Returns the full
        /// (seat, level, cue) feedback trace and the final result.
        /// </summary>
        private static (List<(int Seat, int Level, byte Cue)> Feedback, V2Result Result) RunReactiveTrace(
            ReaccionaParams p, int seed, PlayerSlot reactSlot, int latencyAfterSignal)
        {
            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();
            var feedback = new List<(int, int, byte)>();
            int pendingConfirmTick = -1;

            for (int t = 0; t < 20000 && !mg.IsFinished; t++)
            {
                bool pressNow = pendingConfirmTick >= 0 && (t == pendingConfirmTick - 1 || t == pendingConfirmTick);
                foreach (PlayerSlot slot in AllSlots)
                    inputs.Set(slot, slot == reactSlot && pressNow);

                mg.Tick(inputs.Build(t));

                RenderState rs = mg.GetRenderState();
                for (int i = 0; i < rs.FeedbackCount; i++)
                {
                    FeedbackEvent fe = rs.Feedback[i];
                    feedback.Add((fe.Seat, (int)fe.Level, fe.Cue));
                    if (fe.Cue == ReaccionaMicrogame.CueSignal && pendingConfirmTick < 0)
                        pendingConfirmTick = rs.Tick + latencyAfterSignal;
                }

                if (t == pendingConfirmTick) pendingConfirmTick = -1;
            }

            if (!mg.IsFinished) throw new InvalidOperationException("did not finish within bound");
            return (feedback, mg.GetResult());
        }

        // ── AC3 — tick resolution + genuine ties ───────────────────────────────

        [Test]
        public void TwoPressesSameTick_ProduceGenuineTieAtPlaceOne()
        {
            ReaccionaParams p = ReaccionaParams.GddDefaults; // rounds=1, fakeouts=0
            int signalTick = ProbeSignalTick(p, seed: 9, PlayerRoster.AllHuman);

            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(9), PlayerRoster.AllHuman, 1f);

            int fastConfirm = signalTick + 20;
            int slowConfirm = signalTick + 40;
            RunScheduled(mg, new Dictionary<PlayerSlot, int>
            {
                [PlayerSlot.Rojo] = fastConfirm,
                [PlayerSlot.Azul] = fastConfirm,
                [PlayerSlot.Amarillo] = slowConfirm,
                [PlayerSlot.Verde] = slowConfirm,
            });

            V2Result result = mg.GetResult();
            Assert.That(FindRank(result, PlayerSlot.Rojo).Place, Is.EqualTo(1));
            Assert.That(FindRank(result, PlayerSlot.Azul).Place, Is.EqualTo(1));
            Assert.That(FindRank(result, PlayerSlot.Rojo).Metric, Is.EqualTo(FindRank(result, PlayerSlot.Azul).Metric));
            // Skip-style competition ranking: a 2-way tie for 1st is followed by 3rd, not 2nd.
            Assert.That(FindRank(result, PlayerSlot.Amarillo).Place, Is.EqualTo(3));
            Assert.That(FindRank(result, PlayerSlot.Verde).Place, Is.EqualTo(3));
        }

        // ── AC4 — false start + anti-spam ───────────────────────────────────────

        [Test]
        public void PressBeforeSignal_IsFlaggedFalseStartInRenderState()
        {
            ReaccionaParams p = ReaccionaParams.GddDefaults;
            int signalTick = ProbeSignalTick(p, seed: 5, PlayerRoster.AllHuman);

            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(5), PlayerRoster.AllHuman, 1f);
            RunScheduled(mg, new Dictionary<PlayerSlot, int> { [PlayerSlot.Rojo] = signalTick - 10 });

            V2Result result = mg.GetResult();
            Assert.That(FindRank(result, PlayerSlot.Rojo).Metric, Is.EqualTo(ReaccionaMicrogame.FailurePenaltyTicks));

            RenderState rs = mg.GetRenderState();
            RenderEntity rojoEntity = FindEntity(rs, (int)PlayerSlot.Rojo);
            Assert.That(rojoEntity.VisualVariant, Is.EqualTo(ReaccionaMicrogame.VariantFalseStarted));
        }

        [Test]
        public void MultiplePressesBeforeSignal_CountAsSingleFalseStart()
        {
            ReaccionaParams p = ReaccionaParams.GddDefaults;
            int signalTick = ProbeSignalTick(p, seed: 6, PlayerRoster.AllHuman);

            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(6), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            int falseStartCues = 0;
            for (int t = 0; t < 5000 && !mg.IsFinished; t++)
            {
                // Rojo mashes repeatedly (2 ticks down, 2 up) well before the signal.
                bool spamPressed = t < signalTick - 20 && (t % 4) < 2;
                inputs.Set(PlayerSlot.Rojo, spamPressed);
                foreach (PlayerSlot slot in AllSlots)
                    if (slot != PlayerSlot.Rojo) inputs.Set(slot, false);

                mg.Tick(inputs.Build(t));

                RenderState rs = mg.GetRenderState();
                for (int i = 0; i < rs.FeedbackCount; i++)
                    if (rs.Feedback[i].Seat == (int)PlayerSlot.Rojo && rs.Feedback[i].Cue == ReaccionaMicrogame.CueFalseStart)
                        falseStartCues++;
            }

            Assert.That(falseStartCues, Is.EqualTo(1), "repeated pre-signal presses must count as a single false start");
            Assert.That(FindRank(mg.GetResult(), PlayerSlot.Rojo).Metric, Is.EqualTo(ReaccionaMicrogame.FailurePenaltyTicks));
        }

        private static RenderEntity FindEntity(RenderState rs, int ownerSeat)
        {
            for (int i = 0; i < rs.EntityCount; i++)
                if (rs.Entities[i].OwnerSeat == ownerSeat) return rs.Entities[i];
            throw new InvalidOperationException("entity not found for seat");
        }

        // ── AC5 — 90 ms / 5-tick anticipation threshold ────────────────────────

        [Test]
        public void Latency_OneTickUnderThreshold_IsAnticipationFalseStart()
        {
            ReaccionaParams p = ReaccionaParams.GddDefaults; // AnticipationThresholdTicks = 5
            int signalTick = ProbeSignalTick(p, seed: 11, PlayerRoster.AllHuman);

            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(11), PlayerRoster.AllHuman, 1f);
            RunScheduled(mg, new Dictionary<PlayerSlot, int>
            {
                [PlayerSlot.Rojo] = signalTick + (p.AnticipationThresholdTicks - 1),
            });

            Assert.That(FindRank(mg.GetResult(), PlayerSlot.Rojo).Metric, Is.EqualTo(ReaccionaMicrogame.FailurePenaltyTicks));
        }

        [Test]
        public void Latency_ExactlyAtThreshold_IsLegitimateReaction()
        {
            ReaccionaParams p = ReaccionaParams.GddDefaults;
            int signalTick = ProbeSignalTick(p, seed: 11, PlayerRoster.AllHuman);

            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(11), PlayerRoster.AllHuman, 1f);
            RunScheduled(mg, new Dictionary<PlayerSlot, int>
            {
                [PlayerSlot.Rojo] = signalTick + p.AnticipationThresholdTicks,
            });

            PlayerRank rank = FindRank(mg.GetResult(), PlayerSlot.Rojo);
            Assert.That(rank.Metric, Is.EqualTo(p.AnticipationThresholdTicks));
        }

        [Test]
        public void AnticipationThreshold_AppliesToDebounceConfirmedLatency_NotRawPressTick()
        {
            // Schedule so the RAW first-sample tick (confirmAtTick - 1) sits at
            // signalTick + threshold - 1 (i.e. would look like anticipation if
            // classification used the raw sample) but the debounce-CONFIRMED edge
            // lands at signalTick + threshold (legitimate). Proves classification
            // uses the confirmed edge InputInterpreter reports, not the raw sample.
            ReaccionaParams p = ReaccionaParams.GddDefaults;
            int signalTick = ProbeSignalTick(p, seed: 13, PlayerRoster.AllHuman);
            int confirmTick = signalTick + p.AnticipationThresholdTicks;

            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(13), PlayerRoster.AllHuman, 1f);
            RunScheduled(mg, new Dictionary<PlayerSlot, int> { [PlayerSlot.Rojo] = confirmTick });

            PlayerRank rank = FindRank(mg.GetResult(), PlayerSlot.Rojo);
            Assert.That(rank.Metric, Is.EqualTo(p.AnticipationThresholdTicks),
                "the raw first-sample tick (one before confirm) sits inside the anticipation window, " +
                "but the confirmed edge is exactly at the threshold and must resolve as a legitimate reaction");
        }

        [Test]
        public void BotSpam_ContinuingThroughAndPastSignal_NeverEarnsALegitimateReaction()
        {
            // A spam bot that starts mashing well before the signal and never stops
            // (continuing through the anticipation window and beyond) must still
            // resolve as exactly one false start — its first (pre-signal) press
            // already resolves the seat, so later presses are never evaluated.
            ReaccionaParams p = ReaccionaParams.GddDefaults;
            int signalTick = ProbeSignalTick(p, seed: 14, PlayerRoster.AllHuman);

            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(14), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            for (int t = 0; t < 5000 && !mg.IsFinished; t++)
            {
                bool spamPressed = (t % 4) < 2; // spams from tick 0, long before the signal
                inputs.Set(PlayerSlot.Rojo, spamPressed);
                foreach (PlayerSlot slot in AllSlots)
                    if (slot != PlayerSlot.Rojo) inputs.Set(slot, false);
                mg.Tick(inputs.Build(t));
            }

            Assert.That(FindRank(mg.GetResult(), PlayerSlot.Rojo).Metric, Is.EqualTo(ReaccionaMicrogame.FailurePenaltyTicks));
        }

        // ── AC6 — fakeouts and rounds honored ──────────────────────────────────

        [Test]
        public void Fakeouts_ScheduledCount_FireBeforeTheRealSignal()
        {
            var p = new ReaccionaParams(
                fakeouts: 2, rounds: 1,
                signalDelayMinSeconds: 1.5f, signalDelayMaxSeconds: 4.5f,
                anticipationThresholdTicks: 5, reactionTimeoutSeconds: 0.5f,
                inputConfig: InputInterpreterConfig.GddDefaults);

            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(21), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            int fakeoutCount = 0;
            int signalTick = -1;
            var fakeoutTicks = new List<int>();

            for (int t = 0; t < 5000 && !mg.IsFinished; t++)
            {
                mg.Tick(inputs.Build(t));
                RenderState rs = mg.GetRenderState();
                for (int i = 0; i < rs.FeedbackCount; i++)
                {
                    if (rs.Feedback[i].Cue == ReaccionaMicrogame.CueFakeout) { fakeoutCount++; fakeoutTicks.Add(rs.Tick); }
                    if (rs.Feedback[i].Cue == ReaccionaMicrogame.CueSignal) signalTick = rs.Tick;
                }
                if (signalTick >= 0) break;
            }

            Assert.That(fakeoutCount, Is.EqualTo(2));
            foreach (int ft in fakeoutTicks)
                Assert.That(ft, Is.LessThan(signalTick), "every fakeout must fire strictly before the real signal");
        }

        [Test]
        public void Rounds_BestOfThree_AggregatesAcrossTandas()
        {
            var p = new ReaccionaParams(
                fakeouts: 0, rounds: 3,
                signalDelayMinSeconds: 1.5f, signalDelayMaxSeconds: 4.5f,
                anticipationThresholdTicks: 5, reactionTimeoutSeconds: 0.5f,
                inputConfig: InputInterpreterConfig.GddDefaults);
            const int seed = 55;

            // Rojo reacts fast (latency 10) every tanda; Azul reacts slower (latency 30)
            // every tanda. Across 3 tandas Rojo's cumulative sum must stay lower, and
            // both totals must be the per-tanda latency times 3 (proves summation, not
            // "last tanda wins" or "first tanda wins").
            var traceRojo = RunMultiSeatReactive(p, seed, new Dictionary<PlayerSlot, int>
            {
                [PlayerSlot.Rojo] = 10,
                [PlayerSlot.Azul] = 30,
            });

            PlayerRank rojoRank = FindRank(traceRojo, PlayerSlot.Rojo);
            PlayerRank azulRank = FindRank(traceRojo, PlayerSlot.Azul);

            Assert.That(rojoRank.Metric, Is.EqualTo(30));  // 10 ticks x 3 tandas
            Assert.That(azulRank.Metric, Is.EqualTo(90));  // 30 ticks x 3 tandas
            Assert.That(rojoRank.Place, Is.LessThan(azulRank.Place));
        }

        /// <summary>
        /// Like <see cref="RunReactiveTrace"/> but schedules a fixed per-seat latency
        /// (relative to each tanda's own signal) for multiple seats simultaneously.
        /// Non-scheduled active seats resolve via the reaction timeout.
        /// </summary>
        private static V2Result RunMultiSeatReactive(ReaccionaParams p, int seed, IDictionary<PlayerSlot, int> latencyBySlot)
        {
            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();
            var pendingConfirmTick = new Dictionary<PlayerSlot, int>();

            for (int t = 0; t < 20000 && !mg.IsFinished; t++)
            {
                foreach (PlayerSlot slot in AllSlots)
                {
                    bool pressed = pendingConfirmTick.TryGetValue(slot, out int target) && (t == target - 1 || t == target);
                    inputs.Set(slot, pressed);
                }

                mg.Tick(inputs.Build(t));

                RenderState rs = mg.GetRenderState();
                for (int i = 0; i < rs.FeedbackCount; i++)
                {
                    FeedbackEvent fe = rs.Feedback[i];
                    if (fe.Cue == ReaccionaMicrogame.CueSignal)
                    {
                        foreach (KeyValuePair<PlayerSlot, int> kv in latencyBySlot)
                            pendingConfirmTick[kv.Key] = rs.Tick + kv.Value;
                    }
                }

                var toClear = new List<PlayerSlot>();
                foreach (KeyValuePair<PlayerSlot, int> kv in pendingConfirmTick)
                    if (t == kv.Value) toClear.Add(kv.Key);
                foreach (PlayerSlot slot in toClear) pendingConfirmTick.Remove(slot);
            }

            if (!mg.IsFinished) throw new InvalidOperationException("did not finish within bound");
            return mg.GetResult();
        }

        // ── AC7 — empty seats excluded; all-false-start is safe ────────────────

        [Test]
        public void EmptySeats_AreExcludedFromRanking()
        {
            ReaccionaParams p = ReaccionaParams.GddDefaults;
            var roster = new PlayerRoster(new[]
            {
                SeatState.Human, SeatState.Human, SeatState.Empty, SeatState.Empty
            });
            int signalTick = ProbeSignalTick(p, seed: 31, roster);

            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(31), roster, 1f);
            RunScheduled(mg, new Dictionary<PlayerSlot, int>
            {
                [PlayerSlot.Rojo] = signalTick + 10,
                [PlayerSlot.Azul] = signalTick + 20,
            });

            V2Result result = mg.GetResult();
            Assert.That(result.Ranks.Length, Is.EqualTo(2));
            foreach (PlayerRank r in result.Ranks)
                Assert.That(r.Seat, Is.AnyOf((int)PlayerSlot.Rojo, (int)PlayerSlot.Azul));
        }

        [Test]
        public void AllActiveSeatsFalseStart_ProducesValidSharedResultNoCrash()
        {
            ReaccionaParams p = ReaccionaParams.GddDefaults;
            int signalTick = ProbeSignalTick(p, seed: 32, PlayerRoster.AllHuman);

            var mg = new ReaccionaMicrogame(p);
            mg.Initialize(new SeededRandom(32), PlayerRoster.AllHuman, 1f);

            var schedule = new Dictionary<PlayerSlot, int>();
            foreach (PlayerSlot slot in AllSlots) schedule[slot] = signalTick - 10; // all press before the signal
            Assert.DoesNotThrow(() => RunScheduled(mg, schedule));

            V2Result result = mg.GetResult();
            Assert.That(result.Kind, Is.EqualTo(ResultKind.Ranked));
            Assert.That(result.Ranks.Length, Is.EqualTo(4));
            foreach (PlayerRank r in result.Ranks)
            {
                Assert.That(r.Place, Is.EqualTo(1), "no seat legitimately reacted, so nobody may be fabricated as a winner");
                Assert.That(r.Metric, Is.EqualTo(ReaccionaMicrogame.FailurePenaltyTicks));
            }
        }

        // ── AC8 — zero heap allocation in steady-state Tick ────────────────────

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            var mg = new ReaccionaMicrogame(ReaccionaParams.GddDefaults);
            mg.Initialize(new SeededRandom(99), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, false);
            inputs.Set(PlayerSlot.Azul, true);
            inputs.Set(PlayerSlot.Amarillo, true);
            inputs.Set(PlayerSlot.Verde, false);

            // Warm up: JIT the hot path (covers signal fire, DNF resolution, and the
            // finished no-op path since GddDefaults is best-of-1 and reactionTimeout
            // is comfortably inside this window).
            for (int i = 0; i < 500; i++) mg.Tick(inputs.Build(i));

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 500; i < 1500; i++) mg.Tick(inputs.Build(i));
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L));
        }

        // ── PlayerRoster / SeatState sanity ─────────────────────────────────────

        [Test]
        public void PlayerRoster_WrongLength_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PlayerRoster(new[] { SeatState.Human, SeatState.Human }));
        }

        [Test]
        public void PlayerRoster_IsActive_TrueForNonEmptySeats()
        {
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Empty, SeatState.Bot, SeatState.HumanIdle });
            Assert.That(roster.IsActive(PlayerSlot.Rojo), Is.True);
            Assert.That(roster.IsActive(PlayerSlot.Azul), Is.False);
            Assert.That(roster.IsActive(PlayerSlot.Amarillo), Is.True);
            Assert.That(roster.IsActive(PlayerSlot.Verde), Is.True);
        }

        // ── v2 InputSnapshot / PlayerInput sanity ──────────────────────────────

        [Test]
        public void V2InputSnapshot_WrongPlayerCount_Throws()
        {
            Assert.Throws<ArgumentException>(() => new V2Snapshot(0, new PlayerInput[3]));
        }

        [Test]
        public void V2InputSnapshot_NullPlayers_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new V2Snapshot(0, null));
        }
    }
}
