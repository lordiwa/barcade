using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
// Barcade.Core.Tests is lexically nested inside Barcade.Core, so an unqualified
// name resolves against Barcade.Core's own members before "using"-imported ones --
// see the identical note in ReaccionaMicrogameTests.cs/ApuntaMicrogameV2Tests.cs/
// EsquivaMicrogameV2Tests.cs. Renamed aliases sidestep the collision.
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Manten = Barcade.Core.Microgames.V2.MantenMicrogame;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-107 slice 3 -- coverage for <see cref="V2Manten"/> (§4 MECH_01,
    /// ¡MANTÉN!): v2 IMicrogame contract shape, the fail-threshold boundary
    /// (strict <c>&gt;</c>, not <c>&gt;=</c>), the GDD-literal ACs (null input
    /// always falls, perfect correction always survives, replay determinism),
    /// the GDD casos borde (Idle seat falls naturally; diagonal input uses only
    /// its horizontal component), ranking (last-standing order, the literal
    /// mean-|theta| tiebreak among timeout survivors, exact-tie sharing),
    /// difficulty scaling, zero-alloc steady Tick, and the TASK-030
    /// validator/schema pass.
    ///
    /// No Unity scene required -- pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class MantenMicrogameV2Tests
    {
        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        /// <summary>
        /// Every force term (gravity, perturbation, damping) removed --
        /// <c>SetForcedTheta</c>'s initial (theta, velocity) then holds EXACTLY
        /// (not probabilistically) for as many ticks as the test runs, since
        /// angularAccel evaluates to precisely 0 every tick with these params
        /// and zero input. Used throughout for exact, deterministic boundary/
        /// ranking scenarios that don't depend on the noise curve at all.
        /// </summary>
        private static MantenParams ZeroForceParams(float torqueGain = 10f, float durationSeconds = 8f, float thetaMaxDegrees = 35f)
            => new MantenParams(
                gravityFactor: 0f, perturbAmplitude0: 0f, perturbRamp: 0f, torqueGain: torqueGain,
                thetaMaxDegrees: thetaMaxDegrees, damping: 0f, durationSeconds: durationSeconds, dashRecoveryEnabled: false);

        // ── Test double: builds a v2 InputSnapshot from per-seat stick state ─────

        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(PlayerSlot slot, Direction8 stick = Direction8.None, bool button = false)
                => _players[(int)slot] = new PlayerInput(stick, button);

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        // ── AC1: v2 IMicrogame contract shape ────────────────────────────────────

        [Test]
        public void MantenMicrogame_Id_IsManten()
        {
            V2Microgame mg = new V2Manten();
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.Manten));
        }

        [Test]
        public void MantenMicrogame_GetResult_BeforeFinished_Throws()
        {
            var mg = new V2Manten();
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            Assert.Throws<InvalidOperationException>(() => mg.GetResult());
        }

        // ── MantenParams constructor validation ──────────────────────────────────

        [Test]
        public void MantenParams_NonPositiveThetaMaxDegrees_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MantenParams(3f, 1f, 0.3f, 10f, thetaMaxDegrees: 0f, damping: 1.2f, durationSeconds: 8f, dashRecoveryEnabled: false));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MantenParams(3f, 1f, 0.3f, 10f, thetaMaxDegrees: -5f, damping: 1.2f, durationSeconds: 8f, dashRecoveryEnabled: false));
        }

        [Test]
        public void MantenParams_NonPositiveDurationSeconds_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MantenParams(3f, 1f, 0.3f, 10f, 35f, 1.2f, durationSeconds: 0f, dashRecoveryEnabled: false));
        }

        [Test]
        public void MantenParams_NegativeGravityFactor_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MantenParams(gravityFactor: -1f, 1f, 0.3f, 10f, 35f, 1.2f, 8f, false));
        }

        [Test]
        public void MantenParams_NegativePerturbAmplitude0OrRamp_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MantenParams(3f, perturbAmplitude0: -1f, 0.3f, 10f, 35f, 1.2f, 8f, false));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MantenParams(3f, 1f, perturbRamp: -1f, 10f, 35f, 1.2f, 8f, false));
        }

        [Test]
        public void MantenParams_NegativeTorqueGain_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MantenParams(3f, 1f, 0.3f, torqueGain: -1f, 35f, 1.2f, 8f, false));
        }

        [Test]
        public void MantenParams_NegativeDamping_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MantenParams(3f, 1f, 0.3f, 10f, 35f, damping: -1f, 8f, false));
        }

        // ── Fail-threshold boundary (GDD: "fallo cuando |theta| > thetaMax" -- strict) ──

        [Test]
        public void Theta_ExactlyAtThetaMax_IsSafe()
        {
            float thetaMaxRad = V2Manten.DegreesToRadians(35f);
            var mg = new V2Manten(ZeroForceParams());
            mg.SetForcedTheta(new[] { thetaMaxRad, 0f, 0f, 0f });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.Tick(inputs.Build(0));

            Assert.That(mg.HasFallen(PlayerSlot.Rojo), Is.False, "theta exactly at thetaMax must be safe (boundary: strict >, not >=)");
        }

        [Test]
        public void Theta_SlightlyAboveThetaMax_Falls()
        {
            float thetaMaxRad = V2Manten.DegreesToRadians(35f);
            var mg = new V2Manten(ZeroForceParams());
            mg.SetForcedTheta(new[] { thetaMaxRad + 0.001f, 0f, 0f, 0f });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.Tick(inputs.Build(0));

            Assert.That(mg.HasFallen(PlayerSlot.Rojo), Is.True, "theta slightly above thetaMax must fall");
        }

        // ── GDD casos borde ───────────────────────────────────────────────────────

        [Test]
        public void IdleSeat_StillFallsNaturally_NotFrozen()
        {
            // GDD: "jugador Idle -> su péndulo cae naturalmente (no se congela)".
            // HumanIdle is still an ACTIVE seat (PlayerRoster.IsActive), so this
            // requires no special-casing in the sim -- regression-locks that
            // Initialize/Tick never special-case SeatState and accidentally
            // "freeze" (skip integrating) an Idle seat.
            var roster = new PlayerRoster(new[] { SeatState.HumanIdle, SeatState.Empty, SeatState.Empty, SeatState.Empty });
            var mg = new V2Manten(MantenParams.GddDefaults);
            mg.Initialize(new SeededRandom(5), roster, 1f);

            var inputs = new FakeInputs(); // Rojo (HumanIdle) never receives input
            int durationTicks = (int)MathF.Round(MantenParams.GddDefaults.DurationSeconds * 60f);
            int t = 0;
            while (!mg.IsFinished && t < durationTicks + 10) { mg.Tick(inputs.Build(t)); t++; }

            Assert.That(mg.HasFallen(PlayerSlot.Rojo), Is.True, "an Idle seat's pendulum must fall naturally, not freeze in place");
        }

        [Test]
        public void DiagonalInput_UsesHorizontalComponentOnly()
        {
            // GDD: "entrada diagonal -> se usa componente horizontal." NE and SE
            // share the SAME horizontal component (+0.70710678) despite opposite
            // vertical components -- if the vertical leaked in, they'd diverge.
            // E's horizontal component (1.0) is strictly larger than either
            // diagonal's (0.7071), so it must produce a strictly larger effect --
            // proving the vertical truly never contributes (not folded in via
            // some magnitude computation).
            float ThetaAfterOneTick(Direction8 stick)
            {
                var mg = new V2Manten(ZeroForceParams());
                mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
                var inputs = new FakeInputs();
                inputs.Set(PlayerSlot.Rojo, stick);
                mg.Tick(inputs.Build(0));
                return mg.GetTheta(PlayerSlot.Rojo);
            }

            float thetaAfterNE = ThetaAfterOneTick(Direction8.NE);
            float thetaAfterSE = ThetaAfterOneTick(Direction8.SE);
            float thetaAfterE = ThetaAfterOneTick(Direction8.E);

            Assert.That(thetaAfterSE, Is.EqualTo(thetaAfterNE),
                "NE and SE share the same horizontal component -- their vertical difference must not affect the sim");
            Assert.That(MathF.Abs(thetaAfterE), Is.GreaterThan(MathF.Abs(thetaAfterNE)),
                "E's horizontal component (1.0) is stronger than a diagonal's (0.7071) -- proves vertical is truly ignored");
        }

        // ── Difficulty scaling ────────────────────────────────────────────────────

        [Test]
        public void DifficultyMult_ScalesPerturbationAmplitudeAndRampDirectly()
        {
            var p = new MantenParams(
                gravityFactor: 3f, perturbAmplitude0: 1f, perturbRamp: 0.2f, torqueGain: 10f,
                thetaMaxDegrees: 35f, damping: 1.2f, durationSeconds: 8f, dashRecoveryEnabled: false);
            var mg = new V2Manten(p);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, difficultyMult: 2f);

            Assert.That(mg.EffectivePerturbAmplitude0, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(mg.EffectivePerturbRamp, Is.EqualTo(0.4f).Within(1e-5f));
        }

        // ── AC (GDD literal): null input -> every pendulum falls before duration ──

        [Test]
        public void NullInput_EveryPendulumFalls_BeforeDuration_AcrossTestSeeds()
        {
            int durationTicks = (int)MathF.Round(MantenParams.GddDefaults.DurationSeconds * 60f);

            for (int seed = 0; seed < 20; seed++)
            {
                var mg = new V2Manten(MantenParams.GddDefaults);
                mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                var inputs = new FakeInputs(); // every seat defaults to Direction8.None
                int t = 0;
                while (!mg.IsFinished && t < durationTicks + 10) { mg.Tick(inputs.Build(t)); t++; }

                foreach (PlayerSlot slot in AllSlots)
                {
                    Assert.That(mg.HasFallen(slot), Is.True, $"seed {seed}, seat {slot}: must fall with null input");
                    Assert.That(mg.GetFallenTick(slot), Is.LessThan(durationTicks), $"seed {seed}, seat {slot}: must fall strictly before duration");
                }
            }
        }

        // ── AC (GDD literal): perfect correction -> 100% survive ─────────────────

        [Test]
        public void PerfectCorrectionOracle_EveryPendulumSurvivesFullDuration_AcrossTestSeeds()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var mg = new V2Manten(MantenParams.GddDefaults);
                mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                var oracle = new CorrectionOracle();
                var inputs = new FakeInputs();
                int t = 0;
                while (!mg.IsFinished && t < 3000)
                {
                    RenderState rs = mg.GetRenderState();
                    foreach (PlayerSlot slot in AllSlots)
                        inputs.Set(slot, oracle.Decide(rs, (int)slot));
                    mg.Tick(inputs.Build(t));
                    t++;
                }

                foreach (PlayerSlot slot in AllSlots)
                    Assert.That(mg.HasFallen(slot), Is.False, $"seed {seed}, seat {slot}: perfect correction must survive the full duration");
            }
        }

        // ── Ranking: last-standing order, exact-tie sharing, no fabricated winners ──

        [Test]
        public void SimultaneousFalls_SharePlace()
        {
            float thetaMaxRad = V2Manten.DegreesToRadians(35f);
            var mg = new V2Manten(ZeroForceParams());
            // Rojo and Azul start already past thetaMax (fall tick 0, exact same
            // tick); Amarillo/Verde start at 0 with zero velocity under
            // zero-force params -- provably never move, survive to timeout.
            mg.SetForcedTheta(new[] { thetaMaxRad + 0.01f, thetaMaxRad + 0.01f, 0f, 0f });
            mg.Initialize(new SeededRandom(10), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            int t = 0;
            while (!mg.IsFinished && t < 600) { mg.Tick(inputs.Build(t)); t++; }

            Assert.That(mg.HasFallen(PlayerSlot.Rojo), Is.True, "test setup sanity");
            Assert.That(mg.HasFallen(PlayerSlot.Azul), Is.True, "test setup sanity");
            Assert.That(mg.GetFallenTick(PlayerSlot.Rojo), Is.EqualTo(mg.GetFallenTick(PlayerSlot.Azul)), "test setup sanity: must fall on the exact same tick");

            V2Result result = mg.GetResult();
            var bySeat = new Dictionary<int, PlayerRank>();
            foreach (PlayerRank r in result.Ranks) bySeat[r.Seat] = r;

            Assert.That(bySeat[(int)PlayerSlot.Rojo].Place, Is.EqualTo(bySeat[(int)PlayerSlot.Azul].Place),
                "Rojo and Azul fell on the exact same tick -- must share a place");
            Assert.That(bySeat[(int)PlayerSlot.Amarillo].Place, Is.EqualTo(bySeat[(int)PlayerSlot.Verde].Place),
                "Amarillo and Verde both survived to timeout -- must share a place");
            Assert.That(bySeat[(int)PlayerSlot.Amarillo].Place, Is.LessThan(bySeat[(int)PlayerSlot.Rojo].Place),
                "survivors must outrank the fallen pair -- no fabricated winner among the fallen seats");
        }

        [Test]
        public void MixedFallTicks_RankInSurvivalOrder_NoFabricatedWinner()
        {
            float thetaMaxRad = V2Manten.DegreesToRadians(35f);
            var mg = new V2Manten(ZeroForceParams());
            // Rojo: fast constant angular velocity -> falls quickly. Azul: slower
            // constant velocity -> falls later, but well before duration.
            // Amarillo/Verde: zero velocity -> provably never fall.
            mg.SetForcedTheta(
                new[] { 0f, 0f, 0f, 0f },
                new[] { thetaMaxRad * 60f / 5f * 1.2f, thetaMaxRad * 60f / 40f * 1.2f, 0f, 0f });
            mg.Initialize(new SeededRandom(9), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            int t = 0;
            while (!mg.IsFinished && t < 600) { mg.Tick(inputs.Build(t)); t++; }

            Assert.That(mg.HasFallen(PlayerSlot.Rojo), Is.True, "test setup sanity");
            Assert.That(mg.HasFallen(PlayerSlot.Azul), Is.True, "test setup sanity");
            Assert.That(mg.HasFallen(PlayerSlot.Amarillo), Is.False, "test setup sanity");
            Assert.That(mg.HasFallen(PlayerSlot.Verde), Is.False, "test setup sanity");
            Assert.That(mg.GetFallenTick(PlayerSlot.Rojo), Is.LessThan(mg.GetFallenTick(PlayerSlot.Azul)),
                "test setup sanity: Rojo must fall strictly before Azul");

            V2Result result = mg.GetResult();
            var bySeat = new Dictionary<int, PlayerRank>();
            foreach (PlayerRank r in result.Ranks) bySeat[r.Seat] = r;

            Assert.That(bySeat[(int)PlayerSlot.Amarillo].Place, Is.EqualTo(bySeat[(int)PlayerSlot.Verde].Place),
                "the two survivors share Place 1");
            Assert.That(bySeat[(int)PlayerSlot.Azul].Place, Is.EqualTo(3),
                "the later-fallen seat is 3rd -- the survivor pair's tie at 1st skips to 3rd, standard competition ranking");
            Assert.That(bySeat[(int)PlayerSlot.Rojo].Place, Is.EqualTo(4),
                "the earliest-fallen seat is last -- no fabricated winner among ties");
        }

        [Test]
        public void FullDurationSurvivors_LowerMeanAbsTheta_RanksBetter()
        {
            // GDD literal: "Si el tiempo agota con >=2 en pie, gana el de menor
            // |theta| medio acumulado." Unlike EsquivaMicrogame's own optional
            // near-miss cosmetic tiebreak (left unimplemented there), this is the
            // GDD's actual primary mechanism for resolving a multi-survivor
            // timeout, not a cosmetic afterthought -- must be a real ranking
            // criterion, not just shared-Place-1 for every survivor.
            var shortParams = ZeroForceParams(durationSeconds: 1f);
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Empty, SeatState.Empty });

            var mg = new V2Manten(shortParams);
            // Rojo drifts slowly, Azul faster -- both comfortably stay under
            // thetaMax (35deg=0.6109rad) for the whole 1s/60-tick round, so
            // neither ever falls; Azul's steeper linear ramp keeps |theta| larger
            // than Rojo's at every tick after 0, so its mean must end up higher.
            mg.SetForcedTheta(new[] { 0f, 0f, 0f, 0f }, new[] { 0.3f, 0.5f, 0f, 0f });
            mg.Initialize(new SeededRandom(12), roster, 1f);

            var inputs = new FakeInputs();
            int t = 0;
            while (!mg.IsFinished && t < 100) { mg.Tick(inputs.Build(t)); t++; }

            Assert.That(mg.HasFallen(PlayerSlot.Rojo), Is.False, "test setup sanity");
            Assert.That(mg.HasFallen(PlayerSlot.Azul), Is.False, "test setup sanity");
            Assert.That(mg.GetMeanAbsTheta(PlayerSlot.Rojo), Is.LessThan(mg.GetMeanAbsTheta(PlayerSlot.Azul)), "test setup sanity");

            V2Result result = mg.GetResult();
            var bySeat = new Dictionary<int, PlayerRank>();
            foreach (PlayerRank r in result.Ranks) bySeat[r.Seat] = r;

            Assert.That(bySeat[(int)PlayerSlot.Rojo].Place, Is.LessThan(bySeat[(int)PlayerSlot.Azul].Place),
                "Rojo's steadier (lower mean |theta|) pendulum must rank better among timeout survivors");
        }

        [Test]
        public void FullDurationSurvivors_ExactMeanAbsThetaTie_SharesPlace()
        {
            var shortParams = ZeroForceParams(durationSeconds: 1f);
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Empty, SeatState.Empty });

            var mg = new V2Manten(shortParams);
            mg.SetForcedTheta(new[] { 0f, 0f, 0f, 0f }, new[] { 0.4f, 0.4f, 0f, 0f }); // identical trajectories
            mg.Initialize(new SeededRandom(13), roster, 1f);

            var inputs = new FakeInputs();
            int t = 0;
            while (!mg.IsFinished && t < 100) { mg.Tick(inputs.Build(t)); t++; }

            Assert.That(mg.HasFallen(PlayerSlot.Rojo), Is.False, "test setup sanity");
            Assert.That(mg.HasFallen(PlayerSlot.Azul), Is.False, "test setup sanity");
            Assert.That(mg.GetMeanAbsTheta(PlayerSlot.Rojo), Is.EqualTo(mg.GetMeanAbsTheta(PlayerSlot.Azul)), "test setup sanity: identical trajectories");

            V2Result result = mg.GetResult();
            var bySeat = new Dictionary<int, PlayerRank>();
            foreach (PlayerRank r in result.Ranks) bySeat[r.Seat] = r;

            Assert.That(bySeat[(int)PlayerSlot.Rojo].Place, Is.EqualTo(bySeat[(int)PlayerSlot.Azul].Place),
                "identical trajectories -> exact tie in mean |theta| -> must share a place (GDD: 'empate exacto -> reparto del mismo puesto')");
        }

        // ── AC1: zero heap allocation in steady-state Tick ───────────────────────

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            // gravityFactor/perturbAmplitude0/perturbRamp all zero makes every
            // force term evaluate to EXACTLY 0 every tick (theta provably never
            // moves, for any duration -- not a probabilistic safety margin), while
            // every line of the hot path still executes every tick:
            // SeededValueNoise1D.Sample (array lookup + smoothstep interpolation),
            // the amplitude/perturbation formula, the full Euler integration, and
            // the mean-|theta| accumulation. Fixed None input throughout -- no
            // oracle (CorrectionOracle below allocates per Decide() call, same
            // reason EsquivaMicrogameV2Tests' own EscapeBot can't drive this kind
            // of sensor -- see that ticket's rev-t058 M1 lesson).
            var p = new MantenParams(
                gravityFactor: 0f, perturbAmplitude0: 0f, perturbRamp: 0f, torqueGain: 10f,
                thetaMaxDegrees: 35f, damping: 1.2f, durationSeconds: 30f, dashRecoveryEnabled: false);

            var mg = new V2Manten(p);
            mg.Initialize(new SeededRandom(11), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs(); // every seat defaults to Direction8.None

            for (int i = 0; i < 100; i++) mg.Tick(inputs.Build(i));
            Assert.That(mg.IsFinished, Is.False, "sensor setup: the round must still be live after warmup");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 100; i < 1100; i++) mg.Tick(inputs.Build(i));
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(mg.IsFinished, Is.False, "sensor setup: the round must still be live after the measured window, or it wasn't measuring the live path");
            Assert.That(after - before, Is.EqualTo(0L));
        }

        // ── Replay determinism ────────────────────────────────────────────────────

        [Test]
        public void Determinism_SameSeedAndInputScript_ProducesIdenticalTrajectoryAndResult()
        {
            var p = MantenParams.GddDefaults;
            var runA = RunDeterminismTrace(p, seed: 21);
            var runB = RunDeterminismTrace(p, seed: 21);

            Assert.That(runB.Frames.Count, Is.EqualTo(runA.Frames.Count), "same seed + same input script must produce the same number of frames");
            for (int i = 0; i < runA.Frames.Count; i++)
                Assert.That(runB.Frames[i], Is.EqualTo(runA.Frames[i]), $"trajectory diverged at frame {i} (tick {runA.Frames[i].Tick})");

            Assert.That(runB.Result.Kind, Is.EqualTo(runA.Result.Kind));
            Assert.That(runB.Result.Ranks.Length, Is.EqualTo(runA.Result.Ranks.Length));
            for (int i = 0; i < runA.Result.Ranks.Length; i++)
            {
                Assert.That(runB.Result.Ranks[i].Seat, Is.EqualTo(runA.Result.Ranks[i].Seat), $"rank[{i}] seat diverged");
                Assert.That(runB.Result.Ranks[i].Place, Is.EqualTo(runA.Result.Ranks[i].Place), $"rank[{i}] place diverged");
                Assert.That(runB.Result.Ranks[i].Metric, Is.EqualTo(runA.Result.Ranks[i].Metric), $"rank[{i}] metric diverged");
            }
        }

        [Test]
        public void Determinism_DifferentSeed_Diverges()
        {
            // Proves the lock above is non-vacuous: a genuinely different seed
            // (different noise curve) must diverge the trajectory SOMEWHERE.
            var p = MantenParams.GddDefaults;
            var runA = RunDeterminismTrace(p, seed: 21);
            var runB = RunDeterminismTrace(p, seed: 22);

            bool anyDifferent = runA.Frames.Count != runB.Frames.Count;
            int shorter = Math.Min(runA.Frames.Count, runB.Frames.Count);
            for (int i = 0; i < shorter && !anyDifferent; i++)
                if (!runA.Frames[i].Equals(runB.Frames[i])) anyDifferent = true;

            Assert.That(anyDifferent, Is.True, "a different seed must diverge somewhere -- otherwise the determinism lock isn't discriminating anything");
        }

        // ── AC5: v2 definition + TASK-030 validator/schema pass ──────────────────

        [Test]
        public void V2Definition_ForManten_PassesValidator()
        {
            var def = new MicrogameDefinitionV2
            {
                SchemaVersion = 2,
                Id = "mg_manten_equilibrio_01",
                Mechanic = "MECH_01",
                DisplayVerb = "¡MANTÉN!",
                Dynamics = MicrogameDynamics.Competitive,
                Duration = 8.0f,
                DifficultyScaling = new[] { "perturbAmplitude0", "perturbRamp" },
                Params = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["gravityFactor"] = 3.0,
                    ["perturbAmplitude0"] = 1.2,
                    ["perturbRamp"] = 0.3,
                    ["torqueGain"] = 10.0,
                    ["thetaMax"] = 35.0,
                    ["dashRecoveryEnabled"] = false,
                },
                PayoutTable = new[] { 6, 4, 2, 1 },
                MinPlayers = 2,
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);
            Assert.That(result.IsValid, Is.True, $"field '{result.OffendingField}' -- {result.Message}");
        }

        [Test]
        public void V2Definition_ForManten_TorqueGainOutOfRange_FailsValidator()
        {
            var def = new MicrogameDefinitionV2
            {
                SchemaVersion = 2,
                Id = "mg_manten_bad_01",
                Mechanic = "MECH_01",
                DisplayVerb = "¡MANTÉN!",
                Dynamics = MicrogameDynamics.Competitive,
                Duration = 8.0f,
                DifficultyScaling = Array.Empty<string>(),
                Params = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["torqueGain"] = 999.0, // out of Mech01Manten's declared range
                },
                PayoutTable = new[] { 6, 4, 2, 1 },
                MinPlayers = 2,
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);
            Assert.That(result.IsValid, Is.False,
                "an out-of-range torqueGain must fail validation -- proves the new MECH_01 schema entry actually gates something");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static (List<(int Tick, int Seat, float Theta, bool Fallen)> Frames, V2Result Result)
            RunDeterminismTrace(MantenParams p, int seed)
        {
            var mg = new V2Manten(p);
            mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

            var frames = new List<(int, int, float, bool)>();
            var inputs = new FakeInputs();
            int t = 0;
            while (!mg.IsFinished && t < 4000)
            {
                foreach (PlayerSlot slot in AllSlots)
                    inputs.Set(slot, ScheduledDirection((int)slot, t));
                mg.Tick(inputs.Build(t));

                foreach (PlayerSlot slot in AllSlots)
                    frames.Add((t, (int)slot, mg.GetTheta(slot), mg.HasFallen(slot)));

                t++;
            }

            return (frames, mg.GetResult());
        }

        private static readonly Direction8[] ScheduleCycle =
        {
            Direction8.N, Direction8.NE, Direction8.E, Direction8.SE,
            Direction8.S, Direction8.SW, Direction8.W, Direction8.NW,
        };

        /// <summary>Fixed, non-reactive per-seat schedule -- a pure function of (seat, tick), never RenderState-derived.</summary>
        private static Direction8 ScheduledDirection(int seat, int tick)
            => ScheduleCycle[(tick + seat * 3) % ScheduleCycle.Length];

        /// <summary>
        /// "Corrección perfecta simulada" (GDD AC2) -- a real, reactive
        /// controller, not a hardcoded survivor. Reacts only to the pendulum's
        /// current lean, published via <see cref="RenderEntity.Rotation"/>
        /// (degrees) -- exactly what an idealized observer of the rendered tilt
        /// could compute -- plus an angular-velocity ESTIMATE it derives itself
        /// from the previous tick's own reading (finite difference over the
        /// fixed 1/60s tick), never reading the sim's private state directly.
        ///
        /// Full-authority bang-bang: the stick is digital (GDD §1.3), so E/W/None
        /// are the only horizontally-distinct choices that matter. Pushes E or W
        /// at maximum whenever the PREDICTED lean (current theta + Kd * estimated
        /// velocity) crosses a tiny deadband around 0, using the SAME sign as
        /// that predicted lean -- correct given the sim's literal formula
        /// subtracts <c>torqueGain * input</c> (see MantenMicrogame's class doc
        /// for the sign derivation: countering a disturbance of sign D requires
        /// input of the SAME sign as D, not the opposite). Stateful (remembers
        /// the previous tick's theta per seat, unlike EsquivaMicrogameV2Tests'
        /// EscapeBot, which is a pure function of RenderState plus its own
        /// separate hysteresis memory) -- but allocates nothing per Decide()
        /// call, unlike EscapeBot; still never used inside the zero-alloc
        /// sensor's measured window above (MECH_02 rev-t058 M1 lesson: an oracle
        /// this reactive belongs only in survival-proof tests).
        /// </summary>
        private sealed class CorrectionOracle
        {
            private const float Kd = 0.15f;
            private const float Deadband = 0.001f;
            private const float DtSeconds = 1f / 60f;

            private readonly float[] _prevTheta = new float[4];
            private readonly bool[] _hasPrev = new bool[4];

            public Direction8 Decide(RenderState rs, int seat)
            {
                float theta = 0f;
                bool found = false;
                for (int i = 0; i < rs.EntityCount; i++)
                {
                    if (rs.Entities[i].Kind == EntityKind.PlayerAvatar && rs.Entities[i].OwnerSeat == seat)
                    {
                        theta = rs.Entities[i].Rotation * (MathF.PI / 180f);
                        found = true;
                        break;
                    }
                }
                if (!found) return Direction8.None;

                float velocityEstimate = _hasPrev[seat] ? (theta - _prevTheta[seat]) / DtSeconds : 0f;
                _prevTheta[seat] = theta;
                _hasPrev[seat] = true;

                float errorSignal = theta + Kd * velocityEstimate;
                if (errorSignal > Deadband) return Direction8.E;
                if (errorSignal < -Deadband) return Direction8.W;
                return Direction8.None;
            }
        }
    }
}
