using System;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Bots;
using Barcade.Core.Microgames.V2;
// Barcade.Core.Tests is lexically nested inside Barcade.Core, so an unqualified
// name resolves against Barcade.Core's own members before "using"-imported ones --
// see the identical note in ReaccionaMicrogameTests.cs/ApuntaMicrogameV2Tests.cs/
// EsquivaMicrogameV2Tests.cs/MantenMicrogameV2Tests.cs. Renamed aliases sidestep
// the collision. Barcade.Core.Bots's own types (BotSkill/Bot/BotView/IBotPolicy/
// *BotPolicy/BotStatisticalHarness) have no v1 namesake, so they need none.
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Esquiva = Barcade.Core.Microgames.V2.EsquivaMicrogame;
using V2Apunta = Barcade.Core.Microgames.V2.ApuntaMicrogame;
using V2Manten = Barcade.Core.Microgames.V2.MantenMicrogame;
using V2Corre = Barcade.Core.Microgames.V2.CorreMicrogame;
using V2Reacciona = Barcade.Core.Microgames.V2.ReaccionaMicrogame;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-110 (Annex D.3, §16) — coverage for the bot system: IBotPolicy +
    /// BotSkill + the 3 named calibrations + the statistical harness.
    ///
    /// AC1 — BotView's compile-enforced human-visible-only shape (its single
    /// constructor's parameter shape, plus a real-mechanic projection check).
    /// AC2 — seeded determinism/replay across every mechanic's policy.
    /// AC3 — Bot.Optimo drives the escapability (MECH_02) and reachability
    /// (MECH_04) ceilings.
    /// AC4 — the harness runs a 1000-seed batch of a full mechanic round
    /// within the fast suite; wall-time is measured and logged.
    /// AC5 (SessionStateMachine seat-fill integration) lives in its own file,
    /// <c>SessionStateMachineBotSeatFillTests.cs</c>.
    ///
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class BotsTests
    {
        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        // ── AC1: BotView is constructible ONLY from RenderState+seat ────────────

        [Test]
        public void BotView_HasExactlyOneConstructor_TakingRenderStateAndSeatOnly()
        {
            // The structural half of AC1's "no reference to hidden sim internals
            // compiles": there is no constructor/factory anywhere on BotView that
            // accepts a concrete mechanic instance or any of its private fields —
            // proven here by asserting the ENTIRE public constructor surface is
            // exactly (RenderState, int).
            ConstructorInfo[] ctors = typeof(BotView).GetConstructors();
            Assert.That(ctors.Length, Is.EqualTo(1), "BotView must expose exactly one constructor");

            ParameterInfo[] parameters = ctors[0].GetParameters();
            Assert.That(parameters.Length, Is.EqualTo(2));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(RenderState)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(int)));
        }

        [Test]
        public void BotView_ProjectsOnlyThePublicRenderState_TryFindMyAvatar()
        {
            // The behavioral half of AC1: a BotView built off a real mechanic's
            // own GetRenderState() output correctly recovers per-seat data that
            // is genuinely public (published to the presenter/human) — nothing
            // more, nothing from the mechanic's private fields.
            var mg = new V2Esquiva(EsquivaParams.GddDefaults);
            mg.SetForcedAvatarPositions(new[] { (0.11f, 0.22f), (0.33f, 0.44f), (0.55f, 0.66f), (0.77f, 0.88f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var view = new BotView(mg.GetRenderState(), (int)PlayerSlot.Azul);

            Assert.That(view.TryFindMyAvatar(out RenderEntity avatar), Is.True);
            Assert.That(avatar.X, Is.EqualTo(0.33f).Within(1e-5f));
            Assert.That(avatar.Y, Is.EqualTo(0.44f).Within(1e-5f));
            Assert.That(avatar.OwnerSeat, Is.EqualTo((int)PlayerSlot.Azul));
        }

        [Test]
        public void BotView_TryFindMyAvatar_ReturnsFalse_ForInactiveSeat()
        {
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Empty, SeatState.Empty, SeatState.Empty });
            var mg = new V2Esquiva(EsquivaParams.GddDefaults);
            mg.Initialize(new SeededRandom(2), roster, 1f);

            var view = new BotView(mg.GetRenderState(), (int)PlayerSlot.Azul);

            Assert.That(view.TryFindMyAvatar(out _), Is.False);
        }

        // ── AC2: seeded determinism/replay — same seed -> identical bot behavior ──

        [Test]
        public void Determinism_Esquiva_SameSeed_ProducesIdenticalResult()
        {
            AssertHarnessDeterministic(
                () => new V2Esquiva(EsquivaParams.GddDefaults),
                seat => new EsquivaBotPolicy(),
                Bot.Novato, PlayerRoster.AllHuman, seed: 7);
        }

        [Test]
        public void Determinism_Manten_SameSeed_ProducesIdenticalResult()
        {
            AssertHarnessDeterministic(
                () => new V2Manten(MantenParams.GddDefaults),
                seat => new MantenBotPolicy(),
                Bot.Novato, PlayerRoster.AllHuman, seed: 8);
        }

        [Test]
        public void Determinism_Corre_SameSeed_ProducesIdenticalResult()
        {
            AssertHarnessDeterministic(
                () => new V2Corre(CorreParams.GddDefaults),
                seat => new CorreBotPolicy(),
                Bot.Novato, PlayerRoster.AllHuman, seed: 9);
        }

        [Test]
        public void Determinism_Apunta_SameSeed_ProducesIdenticalResult()
        {
            AssertHarnessDeterministic(
                () => new V2Apunta(ApuntaParams.GddDefaults),
                seat => new ApuntaBotPolicy(),
                Bot.Novato, PlayerRoster.AllHuman, seed: 10);
        }

        [Test]
        public void Determinism_Reacciona_SameSeed_ProducesIdenticalResult()
        {
            AssertHarnessDeterministic(
                () => new V2Reacciona(ReaccionaParams.GddDefaults),
                seat => new ReaccionaBotPolicy(),
                Bot.Novato, PlayerRoster.AllHuman, seed: 11);
        }

        [Test]
        public void Determinism_DifferentSeeds_UsuallyProduceDifferentResults()
        {
            // Non-vacuity pin for the determinism tests above: proves the harness
            // (and the Novato calibration's non-zero variance/error) actually
            // produces DIFFERENT behavior across seeds, so "same seed -> same
            // result" isn't trivially true because the bot always does the exact
            // same thing regardless of seed.
            bool sawDifference = false;
            BotStatisticalHarness.RunResult first = BotStatisticalHarness.RunOne(
                () => new V2Esquiva(EsquivaParams.GddDefaults), seat => new EsquivaBotPolicy(),
                Bot.Novato, PlayerRoster.AllHuman, seed: 100);

            for (int seed = 101; seed < 110; seed++)
            {
                BotStatisticalHarness.RunResult other = BotStatisticalHarness.RunOne(
                    () => new V2Esquiva(EsquivaParams.GddDefaults), seat => new EsquivaBotPolicy(),
                    Bot.Novato, PlayerRoster.AllHuman, seed: seed);
                if (!ResultsEqual(first, other)) { sawDifference = true; break; }
            }

            Assert.That(sawDifference, Is.True, "varying the seed must eventually vary the bot's behavior/outcome");
        }

        private static void AssertHarnessDeterministic(
            BotStatisticalHarness.MicrogameFactory mechanicFactory,
            BotStatisticalHarness.BotPolicyFactory policyFactory,
            BotSkill skill, PlayerRoster roster, int seed)
        {
            BotStatisticalHarness.RunResult a = BotStatisticalHarness.RunOne(mechanicFactory, policyFactory, skill, roster, seed);
            BotStatisticalHarness.RunResult b = BotStatisticalHarness.RunOne(mechanicFactory, policyFactory, skill, roster, seed);

            Assert.That(a.Finished, Is.True, "the round must legitimately finish for a determinism comparison to be meaningful");
            Assert.That(b.TicksElapsed, Is.EqualTo(a.TicksElapsed), "same seed must take the same number of ticks to finish");
            Assert.That(ResultsEqual(a, b), Is.True, "same seed must produce byte-identical MicrogameResult ranks/metrics");
        }

        private static bool ResultsEqual(BotStatisticalHarness.RunResult a, BotStatisticalHarness.RunResult b)
        {
            if (a.Finished != b.Finished) return false;
            if (a.TicksElapsed != b.TicksElapsed) return false;
            if (!a.Finished) return true;

            V2Result ra = a.Result.Value, rb = b.Result.Value;
            if (ra.Kind != rb.Kind) return false;
            if (ra.CoopScore != rb.CoopScore) return false;
            if (ra.Ranks.Length != rb.Ranks.Length) return false;
            for (int i = 0; i < ra.Ranks.Length; i++)
            {
                if (ra.Ranks[i].Seat != rb.Ranks[i].Seat) return false;
                if (ra.Ranks[i].Place != rb.Ranks[i].Place) return false;
                if (ra.Ranks[i].Metric != rb.Ranks[i].Metric) return false;
            }
            return true;
        }

        // ── AC3: Bot.Optimo drives the MECH_02/MECH_04 ceiling tests ────────────

        [Test]
        public void Optimo_Esquiva_SurvivesFullDuration_EveryPatternManySeeds()
        {
            // Same sweep shape (patterns x 20 seeds, 8s stress duration) as the
            // pre-existing EsquivaMicrogameV2Tests.EscapabilityBot_Survives... —
            // reproduced here against the GENERALIZED, humanized-pipeline
            // Bot.Optimo (reactionDelayTicks=6, errorRate=0) rather than the raw
            // zero-latency solver, proving the calibration survives composition
            // through ReactionDelayBuffer before any existing file is reconciled
            // to depend on it.
            var patterns = new[]
            {
                EsquivaHazardPattern.Rain, EsquivaHazardPattern.Sides,
                EsquivaHazardPattern.Cross, EsquivaHazardPattern.HomingSoft,
            };

            foreach (EsquivaHazardPattern pattern in patterns)
            {
                for (int seed = 0; seed < 20; seed++)
                {
                    EsquivaParams parms = new EsquivaParams(
                        spawnRateBasePerSec: 0.6f, spawnRampCoef: 0.15f, hazardSpeed: 0.15f,
                        hazardPattern: pattern, avatarSpeed: 0.35f,
                        avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 8f, jumpEnabled: false);

                    BotStatisticalHarness.RunResult result = BotStatisticalHarness.RunOne(
                        () => new V2Esquiva(parms), seatIdx => new EsquivaBotPolicy(),
                        Bot.Optimo, PlayerRoster.AllHuman, seed, maxTicks: 1000);

                    Assert.That(result.Finished, Is.True, $"pattern {pattern}, seed {seed}: round must finish within the tick bound");
                    Assert.That(result.Result.Value.Ranks[0].Metric, Is.GreaterThanOrEqualTo(479),
                        $"pattern {pattern}, seed {seed}: Bot.Optimo must survive (near-)full duration (8s @ 60Hz = 480 ticks)");
                }
            }
        }

        [Test]
        public void Optimo_Esquiva_SurvivesFullDuration_AtProductionDuration()
        {
            // The GDD-canonical 5s Esquiva duration (EsquivaParams.GddDefaults),
            // not the 8s stress duration above — the parameters the mechanic
            // ACTUALLY ships with.
            for (int seed = 0; seed < 20; seed++)
            {
                BotStatisticalHarness.RunResult result = BotStatisticalHarness.RunOne(
                    () => new V2Esquiva(EsquivaParams.GddDefaults), seatIdx => new EsquivaBotPolicy(),
                    Bot.Optimo, PlayerRoster.AllHuman, seed, maxTicks: 1000);

                Assert.That(result.Finished, Is.True, $"seed {seed}: round must finish");
                foreach (PlayerRank rank in result.Result.Value.Ranks)
                    Assert.That(rank.Metric, Is.GreaterThanOrEqualTo(299),
                        $"seed {seed}, seat {rank.Seat}: Bot.Optimo must survive full duration (5s @ 60Hz = 300 ticks)");
            }
        }

        [Test]
        public void Optimo_Apunta_HitsEveryReachableTarget_SoloSeat_ManySeeds()
        {
            // AC3's MECH_04 reachability ceiling, as a LIVE-PLAY proof (unlike
            // the pre-existing ApuntaMicrogameV2Tests.CanReach, a static
            // geometric existence check that never simulates charge/hold/release
            // timing at all — see ApuntaBotPolicy's own class doc). Solo seat
            // (no contention for the shared target pool — ApuntaMicrogame's
            // targets are a single shared array, not per-seat) so a clean hit
            // count is directly comparable to TargetCount.
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Empty, SeatState.Empty, SeatState.Empty });

            for (int seed = 0; seed < 30; seed++)
            {
                BotStatisticalHarness.RunResult result = BotStatisticalHarness.RunOne(
                    () => new V2Apunta(ApuntaParams.GddDefaults), seatIdx => new ApuntaBotPolicy(),
                    Bot.Optimo, roster, seed, maxTicks: 1000);

                Assert.That(result.Finished, Is.True, $"seed {seed}: round must finish");
                Assert.That(result.Result.Value.Ranks[0].Metric, Is.EqualTo(ApuntaParams.GddDefaults.TargetCount),
                    $"seed {seed}: a solo Bot.Optimo must hit every reachable target — the reachability ceiling");
            }
        }

        // ── AC4: 1000-seed statistical batch, in the fast suite, within budget ──

        [Test]
        public void Harness_1000SeedBatch_Esquiva_CompletesWithinBudget()
        {
            RunAndTimeBatch("ESQUIVA", () => new V2Esquiva(EsquivaParams.GddDefaults), seat => new EsquivaBotPolicy(), Bot.Medio);
        }

        [Test]
        public void Harness_1000SeedBatch_Manten_CompletesWithinBudget()
        {
            RunAndTimeBatch("MANTEN", () => new V2Manten(MantenParams.GddDefaults), seat => new MantenBotPolicy(), Bot.Medio);
        }

        [Test]
        public void Harness_1000SeedBatch_Corre_CompletesWithinBudget()
        {
            RunAndTimeBatch("CORRE", () => new V2Corre(CorreParams.GddDefaults), seat => new CorreBotPolicy(), Bot.Medio);
        }

        [Test]
        public void Harness_1000SeedBatch_Apunta_CompletesWithinBudget()
        {
            RunAndTimeBatch("APUNTA", () => new V2Apunta(ApuntaParams.GddDefaults), seat => new ApuntaBotPolicy(), Bot.Medio);
        }

        [Test]
        public void Harness_1000SeedBatch_Reacciona_CompletesWithinBudget()
        {
            RunAndTimeBatch("REACCIONA", () => new V2Reacciona(ReaccionaParams.GddDefaults), seat => new ReaccionaBotPolicy(), Bot.Medio);
        }

        /// <summary>
        /// AC4: runs a 1000-seed batch of a full round through
        /// <see cref="BotStatisticalHarness"/>, asserts every seed legitimately
        /// finished (proves the harness API is genuinely mechanic-agnostic — no
        /// mechanic-specific code lives in the harness itself), and logs the
        /// measured wall-time (captured verbatim into the TASK-038 hand-off).
        /// A generous 60s per-mechanic budget is asserted so a genuine
        /// performance regression fails loudly, without pinning to today's
        /// exact measured number (machine-dependent).
        /// </summary>
        private static void RunAndTimeBatch(
            string label, BotStatisticalHarness.MicrogameFactory mechanicFactory,
            BotStatisticalHarness.BotPolicyFactory policyFactory, BotSkill skill)
        {
            const int seedCount = 1000;
            var sw = Stopwatch.StartNew();
            BotStatisticalHarness.RunResult[] results = BotStatisticalHarness.RunBatch(
                mechanicFactory, policyFactory, skill, PlayerRoster.AllHuman, seedCount, maxTicks: 5000);
            sw.Stop();

            int unfinished = 0;
            foreach (BotStatisticalHarness.RunResult r in results)
                if (!r.Finished) unfinished++;

            TestContext.Progress.WriteLine(
                $"[AC4] {label}: {seedCount} seeds in {sw.Elapsed.TotalSeconds:F2}s " +
                $"({sw.Elapsed.TotalMilliseconds / seedCount:F3}ms/seed), {unfinished} unfinished.");

            Assert.That(unfinished, Is.EqualTo(0), $"{label}: every one of {seedCount} seeds must legitimately finish (IsFinished) within the tick bound");
            Assert.That(sw.Elapsed.TotalSeconds, Is.LessThan(60.0),
                $"{label}: a 1000-seed batch must complete within a sane fast-suite time budget (measured {sw.Elapsed.TotalSeconds:F2}s)");
        }
    }
}
