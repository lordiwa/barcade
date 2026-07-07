using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
// Barcade.Core.Tests is lexically nested inside Barcade.Core, so an unqualified
// name resolves against Barcade.Core's own members before "using"-imported ones --
// see the identical note in ReaccionaMicrogameTests.cs/ApuntaMicrogameV2Tests.cs/
// EsquivaMicrogameV2Tests.cs/MantenMicrogameV2Tests.cs. Renamed aliases sidestep
// the MicrogameResult/IMicrogame/InputSnapshot collisions.
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Corre = Barcade.Core.Microgames.V2.CorreMicrogame;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-107 slice 2 -- coverage for <see cref="V2Corre"/> (§4 MECH_03,
    /// ¡CORRE!): v2 IMicrogame contract shape, the mash→velocity §3.3 curve, the
    /// jump-only control scheme ("mash acelera, palanca-arriba salta -- nunca al
    /// revés"), the shared-across-4-lanes obstacle track (AC3, byte-identical),
    /// the 0.6 s obstacle stun (no elimination), the perfect-jump-at-6 Hz survival
    /// bot (AC2), the 1000-seed rubber-band-non-inversion fairness pin (AC3 in the
    /// GDD's own numbering), ranking by distance, difficulty scaling, zero-alloc
    /// steady Tick, replay determinism, and the TASK-030 validator/schema pass.
    ///
    /// No Unity scene required -- pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class CorreMicrogameV2Tests
    {
        private const float Dt = 1f / 60f;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        // ── Test double: builds a v2 InputSnapshot from per-seat stick+button ─────

        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(PlayerSlot slot, Direction8 stick = Direction8.None, bool button = false)
                => _players[(int)slot] = new PlayerInput(stick, button);

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        /// <summary>
        /// A button-mash pattern at approximately <paramref name="hz"/> presses per
        /// second on the 60 Hz tick clock: button down for the first half of each
        /// period, up for the second half. Both half-widths are &gt;= the
        /// InputInterpreter's 2-tick debounce confirm, so every period registers
        /// exactly one confirmed press edge (§3.3 mash counting).
        /// </summary>
        private static bool MashButton(int tick, int hz)
        {
            int period = (int)MathF.Round(60f / hz);
            return (tick % period) < (period / 2);
        }

        // ── AC1: v2 IMicrogame contract shape ────────────────────────────────────

        [Test]
        public void CorreMicrogame_Id_IsCorre()
        {
            V2Microgame mg = new V2Corre();
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.Corre));
        }

        [Test]
        public void CorreMicrogame_GetResult_BeforeFinished_Throws()
        {
            var mg = new V2Corre();
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            Assert.Throws<InvalidOperationException>(() => mg.GetResult());
        }

        // ── CorreParams constructor validation ────────────────────────────────────

        [Test]
        public void CorreParams_NonPositiveVBase_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WithVBase(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => WithVBase(-1f));
        }

        [Test]
        public void CorreParams_NegativeVGain_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WithVGain(-0.01f));
        }

        [Test]
        public void CorreParams_NonPositiveDuration_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WithDuration(0f));
        }

        [Test]
        public void CorreParams_NegativeRubberBandPct_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WithRubberBandPct(-0.01f));
        }

        // ── AC1: mash → velocity via the §3.3 curve (v = vBase + vGain·mashNorm) ──

        [Test]
        public void NoMash_VelocityEqualsVBase()
        {
            // With no button presses, mashNorm is 0, so v = vBase exactly (the
            // seat still creeps forward at the base speed -- an endless runner is
            // never fully stopped except while stunned).
            var p = SoloParams();
            var mg = new V2Corre(p);
            mg.Initialize(new SeededRandom(1), OnlyRojo(), 1f);

            var inputs = new FakeInputs(); // Rojo: no button, no stick
            for (int t = 0; t < 60; t++) mg.Tick(inputs.Build(t));

            Assert.That(mg.GetVelocity(PlayerSlot.Rojo), Is.EqualTo(p.VBase).Within(1e-4f),
                "no mash → mashNorm 0 → v = vBase");
        }

        [Test]
        public void SaturatedMash_VelocityApproachesVBasePlusVGain()
        {
            // Mashing well above the §3.3 saturation frequency (9 Hz) drives
            // mashNorm to 1, so v → vBase + vGain. 12 Hz saturates.
            var p = SoloParams();
            var mg = new V2Corre(p);
            mg.Initialize(new SeededRandom(1), OnlyRojo(), 1f);

            var inputs = new FakeInputs();
            for (int t = 0; t < 120; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.None, MashButton(t, 12));
                mg.Tick(inputs.Build(t));
            }

            Assert.That(mg.GetVelocity(PlayerSlot.Rojo), Is.EqualTo(p.VBase + p.VGain).Within(1e-3f),
                "saturated mash → mashNorm 1 → v = vBase + vGain");
        }

        [Test]
        public void SixHzMash_VelocityMatchesSection33Curve()
        {
            // §3.3: fuerza = clamp01((f - 2) / 7). At 6 Hz → (6-2)/7 = 0.5714…, so
            // v = vBase + vGain·0.5714. Uses a no-obstacle track so nothing stuns
            // or halts the seat, isolating the velocity formula.
            var p = SoloParamsNoObstacles();
            var mg = new V2Corre(p);
            mg.Initialize(new SeededRandom(1), OnlyRojo(), 1f);

            var inputs = new FakeInputs();
            // Warm the 500 ms mash window fully before sampling.
            for (int t = 0; t < 120; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.None, MashButton(t, 6));
                mg.Tick(inputs.Build(t));
            }

            float expectedMashNorm = (6f - 2f) / 7f;
            float expected = p.VBase + p.VGain * expectedMashNorm;
            // Tolerance covers the ±1 press fluctuation as edges enter/leave the
            // 30-tick window (≈ one vGain/7/window step).
            Assert.That(mg.GetVelocity(PlayerSlot.Rojo), Is.EqualTo(expected).Within(p.VGain * 0.12f),
                "6 Hz mash must map through the §3.3 curve to v = vBase + vGain·((6-2)/7)");
        }

        // ── AC2: "nunca al revés" — mash accelerates (never jumps); stick-up jumps
        //         (never accelerates); lateral/down stick does nothing ────────────

        [Test]
        public void MashButton_Accelerates_NeverJumps()
        {
            var p = SoloParamsNoObstacles();

            var mashed = new V2Corre(p);
            mashed.Initialize(new SeededRandom(1), OnlyRojo(), 1f);
            var baseline = new V2Corre(p);
            baseline.Initialize(new SeededRandom(1), OnlyRojo(), 1f);

            var inputs = new FakeInputs();
            var idle = new FakeInputs();
            bool everAirborne = false;
            for (int t = 0; t < 120; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.None, MashButton(t, 9));
                mashed.Tick(inputs.Build(t));
                baseline.Tick(idle.Build(t));
                if (mashed.IsAirborne(PlayerSlot.Rojo)) everAirborne = true;
            }

            Assert.That(mashed.GetDistance(PlayerSlot.Rojo), Is.GreaterThan(baseline.GetDistance(PlayerSlot.Rojo)),
                "mashing the button must accelerate (cover more distance than the base creep)");
            Assert.That(everAirborne, Is.False,
                "mashing the button must NEVER cause a jump -- that is the reverse mapping the GDD forbids");
        }

        [Test]
        public void StickUp_Jumps_NeverAccelerates()
        {
            var p = SoloParamsNoObstacles();

            var jumping = new V2Corre(p);
            jumping.Initialize(new SeededRandom(1), OnlyRojo(), 1f);
            var baseline = new V2Corre(p);
            baseline.Initialize(new SeededRandom(1), OnlyRojo(), 1f);

            var inputs = new FakeInputs();
            var idle = new FakeInputs();
            bool everAirborne = false;
            for (int t = 0; t < 120; t++)
            {
                // Tap up (rising edge) every 12 ticks; neutral between so each is a
                // fresh tap. Button is NEVER pressed.
                bool up = (t % 12) == 0;
                inputs.Set(PlayerSlot.Rojo, up ? Direction8.N : Direction8.None, button: false);
                jumping.Tick(inputs.Build(t));
                baseline.Tick(idle.Build(t));
                if (jumping.IsAirborne(PlayerSlot.Rojo)) everAirborne = true;
            }

            Assert.That(everAirborne, Is.True, "tapping the stick up must jump");
            Assert.That(jumping.GetVelocity(PlayerSlot.Rojo), Is.EqualTo(p.VBase).Within(1e-4f),
                "stick-up must NOT accelerate -- velocity stays at vBase (mashNorm 0)");
            Assert.That(jumping.GetDistance(PlayerSlot.Rojo), Is.EqualTo(baseline.GetDistance(PlayerSlot.Rojo)).Within(1e-4f),
                "jumping alone must cover the same distance as pure base creep -- the stick never accelerates");
        }

        [Test]
        public void HeldUpStick_JumpsOnlyOnce_NotWhileHeld()
        {
            // "tap" (rising edge), not "hold": holding up must not produce a
            // continuous stream of jumps. After the single jump completes and the
            // seat lands, a still-held stick must NOT re-jump.
            var p = SoloParamsNoObstacles();
            var mg = new V2Corre(p);
            mg.Initialize(new SeededRandom(1), OnlyRojo(), 1f);

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, Direction8.N, button: false); // held up the whole time

            int airborneStretches = 0;
            bool prevAirborne = false;
            for (int t = 0; t < 240; t++)
            {
                mg.Tick(inputs.Build(t));
                bool a = mg.IsAirborne(PlayerSlot.Rojo);
                if (a && !prevAirborne) airborneStretches++;
                prevAirborne = a;
            }

            Assert.That(airborneStretches, Is.EqualTo(1),
                "a held-up stick must produce exactly one jump, not continuous jumping");
        }

        [Test]
        public void LateralAndDownStick_HaveNoEffect()
        {
            // No lane-switching, no slide, no duck: only up (jump) and button
            // (mash) do anything. Driving W/E/S must be indistinguishable from
            // no input at all.
            var p = SoloParamsNoObstacles();
            foreach (Direction8 d in new[] { Direction8.W, Direction8.E, Direction8.S })
            {
                var driven = new V2Corre(p);
                driven.Initialize(new SeededRandom(3), OnlyRojo(), 1f);
                var idle = new V2Corre(p);
                idle.Initialize(new SeededRandom(3), OnlyRojo(), 1f);

                var inputs = new FakeInputs();
                var noInput = new FakeInputs();
                bool everAirborne = false;
                for (int t = 0; t < 120; t++)
                {
                    inputs.Set(PlayerSlot.Rojo, d, button: false);
                    driven.Tick(inputs.Build(t));
                    idle.Tick(noInput.Build(t));
                    if (driven.IsAirborne(PlayerSlot.Rojo)) everAirborne = true;
                }

                Assert.That(everAirborne, Is.False, $"stick {d} must not jump");
                Assert.That(driven.GetDistance(PlayerSlot.Rojo), Is.EqualTo(idle.GetDistance(PlayerSlot.Rojo)).Within(1e-4f),
                    $"stick {d} must have no effect -- same distance as no input (no lane-switch / no slide)");
            }
        }

        // ── AC2: obstacle hit = 0.6 s stun, no elimination ───────────────────────

        [Test]
        public void GroundedObstacleCrossing_Stuns_ForExactlyStunDuration_NoElimination()
        {
            var p = SoloParams(); // stunSeconds 0.6 → 36 ticks @ 60 Hz
            int expectedStunTicks = (int)MathF.Round(p.StunSeconds * 60f);
            Assert.That(expectedStunTicks, Is.EqualTo(36), "test setup sanity: 0.6 s = 36 ticks");

            var mg = new V2Corre(p);
            mg.SetForcedObstacles(10f); // single obstacle at distance 10
            mg.Initialize(new SeededRandom(1), OnlyRojo(), 1f);

            var inputs = new FakeInputs();
            // Mash to advance, but NEVER jump → cross the obstacle grounded.
            int stunOnsetTick = -1;
            int stunnedReadings = 0;
            int t = 0;
            for (; t < 400; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.None, MashButton(t, 9));
                mg.Tick(inputs.Build(t));

                if (mg.IsStunned(PlayerSlot.Rojo))
                {
                    if (stunOnsetTick < 0)
                    {
                        stunOnsetTick = t;
                        Assert.That(mg.GetStunTicksRemaining(PlayerSlot.Rojo), Is.EqualTo(expectedStunTicks),
                            "the stun must start at the full 0.6 s / 36-tick duration");
                    }
                    stunnedReadings++;
                    Assert.That(mg.GetVelocity(PlayerSlot.Rojo), Is.EqualTo(0f),
                        "a stunned seat does not advance (velocity 0)");
                }
                else if (stunOnsetTick >= 0)
                {
                    break; // stun ended
                }
            }

            Assert.That(stunOnsetTick, Is.GreaterThanOrEqualTo(0), "the grounded seat must have hit the obstacle");
            Assert.That(stunnedReadings, Is.EqualTo(expectedStunTicks),
                "the stun must last exactly 0.6 s (36 ticks), no more, no less");

            // Not eliminated: it recovers and keeps advancing past the obstacle.
            float distAfterRecovery = mg.GetDistance(PlayerSlot.Rojo);
            for (; t < 500; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.None, MashButton(t, 9));
                mg.Tick(inputs.Build(t));
            }
            Assert.That(mg.GetDistance(PlayerSlot.Rojo), Is.GreaterThan(distAfterRecovery),
                "a stun is not an elimination -- the seat resumes running afterward");
            Assert.That(mg.GetDistance(PlayerSlot.Rojo), Is.GreaterThan(10f),
                "the seat eventually clears the obstacle it was stunned on");
        }

        [Test]
        public void PerfectlyTimedJump_ClearsObstacle_NoStun()
        {
            var p = SoloParams();
            var mg = new V2Corre(p);
            mg.SetForcedObstacles(10f);
            mg.Initialize(new SeededRandom(1), OnlyRojo(), 1f);

            var bot = new PerfectRunner();
            var inputs = new FakeInputs();
            bool everStunned = false;
            for (int t = 0; t < 400 && mg.GetDistance(PlayerSlot.Rojo) < 12f; t++)
            {
                bot.Drive(mg, inputs, PlayerSlot.Rojo, t);
                mg.Tick(inputs.Build(t));
                if (mg.IsStunned(PlayerSlot.Rojo)) everStunned = true;
            }

            Assert.That(everStunned, Is.False, "a perfectly-timed jump must clear the obstacle without a stun");
            Assert.That(mg.GetDistance(PlayerSlot.Rojo), Is.GreaterThan(10f), "the seat cleared and passed the obstacle");
        }

        // ── AC3 (ticket): the same seed → an identical obstacle track in all 4 lanes ──

        [Test]
        public void SameSeed_ProducesIdenticalObstacleTrack()
        {
            var a = new V2Corre(CorreParams.GddDefaults);
            a.Initialize(new SeededRandom(77), PlayerRoster.AllHuman, 1f);
            var b = new V2Corre(CorreParams.GddDefaults);
            b.Initialize(new SeededRandom(77), PlayerRoster.AllHuman, 1f);

            Assert.That(a.ObstacleCount, Is.GreaterThan(0), "test setup sanity: obstacles were generated");
            Assert.That(b.ObstacleCount, Is.EqualTo(a.ObstacleCount), "same seed must yield the same obstacle count");
            for (int i = 0; i < a.ObstacleCount; i++)
                Assert.That(b.GetObstaclePosition(i), Is.EqualTo(a.GetObstaclePosition(i)),
                    $"obstacle {i} must be byte-identical for the same seed");
        }

        [Test]
        public void AllFourLanes_ShareOneTrack_ByteIdenticalUnderIdenticalInput()
        {
            // The headline AC3 pin: one seed → one track, shared across the 4
            // lanes (not one draw per lane, the way RunnerSim gives each player its
            // own RNG stream). Drive all 4 seats with IDENTICAL input for a full
            // round and assert every seat's per-tick (distance, airborne, stun)
            // trace is byte-identical to Rojo's. If any lane drew its own track,
            // identical input would collide with different obstacles and the traces
            // would diverge. Mutation-verified: giving each lane its own obstacle
            // draw makes this fail.
            var mg = new V2Corre(CorreParams.GddDefaults);
            mg.Initialize(new SeededRandom(88), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            int t = 0;
            while (!mg.IsFinished && t < 600)
            {
                // Same input for all four seats: mash + a fixed (non-perfect) jump
                // cadence, so some obstacles are cleared and some are hit -- either
                // way, identically across lanes.
                bool button = MashButton(t, 7);
                Direction8 stick = (t % 17 == 0) ? Direction8.N : Direction8.None;
                foreach (PlayerSlot slot in AllSlots) inputs.Set(slot, stick, button);
                mg.Tick(inputs.Build(t));

                float d0 = mg.GetDistance(PlayerSlot.Rojo);
                bool a0 = mg.IsAirborne(PlayerSlot.Rojo);
                bool s0 = mg.IsStunned(PlayerSlot.Rojo);
                for (int k = 1; k < 4; k++)
                {
                    Assert.That(mg.GetDistance(AllSlots[k]), Is.EqualTo(d0), $"tick {t}: lane {k} distance diverged from lane 0");
                    Assert.That(mg.IsAirborne(AllSlots[k]), Is.EqualTo(a0), $"tick {t}: lane {k} airborne diverged from lane 0");
                    Assert.That(mg.IsStunned(AllSlots[k]), Is.EqualTo(s0), $"tick {t}: lane {k} stun diverged from lane 0");
                }
                t++;
            }
            Assert.That(t, Is.GreaterThan(60), "test setup sanity: the round ran for a meaningful number of ticks");
        }

        // ── AC2 (GDD): 6 Hz constant mash + perfect jumps finishes unstunned ─────

        [Test]
        public void ConstantSixHzMash_WithPerfectJumps_FinishesUnstunned_AcrossSeeds()
        {
            // A REAL reactive jump bot (PerfectRunner, see its own doc) drives all
            // four seats: 6 Hz mash on the button, and a stick-up tap timed off the
            // published obstacle geometry so it is airborne when it crosses each
            // one. Proves the track is fair (every obstacle jumpable) by
            // construction across seeds, not tuned for one lucky layout.
            // Mutation-verified: making jumps NOT clear obstacles turns this red.
            for (int seed = 0; seed < 20; seed++)
            {
                var mg = new V2Corre(CorreParams.GddDefaults);
                mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                var bot = new PerfectRunner();
                var inputs = new FakeInputs();
                var everStunned = new bool[4];
                int t = 0;
                while (!mg.IsFinished && t < 700)
                {
                    foreach (PlayerSlot slot in AllSlots) bot.Drive(mg, inputs, slot, t);
                    mg.Tick(inputs.Build(t));
                    for (int k = 0; k < 4; k++)
                        if (mg.IsStunned(AllSlots[k])) everStunned[k] = true;
                    t++;
                }

                Assert.That(mg.IsFinished, Is.True, $"seed {seed}: the round must complete");
                foreach (PlayerSlot slot in AllSlots)
                    Assert.That(everStunned[(int)slot], Is.False,
                        $"seed {seed}, seat {slot}: a 6 Hz masher with perfect jumps must finish unstunned");
            }
        }

        // ── AC3 (GDD): rubber-band never inverts two identical-mash players ───────

        [Test]
        public void RubberBand_NeverInvertsTwoIdenticalMashPlayers_Over1000Seeds()
        {
            // GDD AC3: "rubber-band nunca invierte por sí solo un resultado entre
            // dos jugadores con mash idéntico (test estadístico sobre 1 000
            // semillas)." Rojo and Azul receive byte-identical input every tick and
            // never jump, so they crawl and stun in lockstep -- both permanently
            // tied for last. The rubber-band boosts last place: because the tie is
            // symmetric it boosts BOTH equally, so they stay EXACTLY tied. Any
            // asymmetry (boosting only one of two tied-last seats) would break the
            // tie and hand one an unearned lead -- exactly the inversion this AC
            // forbids. Mutation-verified: an asymmetric (lowest-seat-only) boost
            // makes this fail.
            var p = CorreParams.GddDefaults;
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Empty, SeatState.Empty });

            for (int seed = 0; seed < 1000; seed++)
            {
                var mg = new V2Corre(p);
                mg.Initialize(new SeededRandom(seed), roster, 1f);

                var inputs = new FakeInputs();
                int t = 0;
                while (!mg.IsFinished && t < 700)
                {
                    bool button = MashButton(t, 6);
                    // Identical input for both seats; no jumps.
                    inputs.Set(PlayerSlot.Rojo, Direction8.None, button);
                    inputs.Set(PlayerSlot.Azul, Direction8.None, button);
                    mg.Tick(inputs.Build(t));
                    t++;
                }

                Assert.That(mg.GetDistance(PlayerSlot.Azul), Is.EqualTo(mg.GetDistance(PlayerSlot.Rojo)),
                    $"seed {seed}: two players with identical mash must finish exactly tied -- rubber-band must never invert them");

                V2Result result = mg.GetResult();
                var bySeat = new Dictionary<int, PlayerRank>();
                foreach (PlayerRank r in result.Ranks) bySeat[r.Seat] = r;
                Assert.That(bySeat[(int)PlayerSlot.Rojo].Place, Is.EqualTo(bySeat[(int)PlayerSlot.Azul].Place),
                    $"seed {seed}: identical-mash players must share a place");
            }
        }

        [Test]
        public void RubberBand_BoostsLastPlaceBaseSpeed()
        {
            // Non-vacuity for the fairness pin: the rubber-band actually engages.
            // Rojo saturates the mash and pulls ahead; Azul never mashes and falls
            // to strict last place, so its base speed is boosted by rubberBandPct.
            // Azul's velocity (no mash → mashNorm 0) must read vBase·(1+pct), not
            // vBase.
            var p = SoloParamsNoObstacles(); // rubberBandPct from GddDefaults (0.08)
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Empty, SeatState.Empty });

            var mg = new V2Corre(p);
            mg.Initialize(new SeededRandom(1), roster, 1f);

            var inputs = new FakeInputs();
            for (int t = 0; t < 120; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.None, MashButton(t, 12)); // pulls ahead
                inputs.Set(PlayerSlot.Azul, Direction8.None, button: false);      // strict last
                mg.Tick(inputs.Build(t));
            }

            Assert.That(mg.GetDistance(PlayerSlot.Rojo), Is.GreaterThan(mg.GetDistance(PlayerSlot.Azul)),
                "test setup sanity: Rojo must be ahead so Azul is strictly last");
            Assert.That(mg.GetVelocity(PlayerSlot.Azul), Is.EqualTo(p.VBase * (1f + p.RubberBandPct)).Within(1e-3f),
                "the strict-last seat's base speed must be boosted by rubberBandPct");
            // The leader (saturated mash, mashNorm→1) is NOT last, so it gets no
            // boost: its velocity is vBase + vGain, not vBase·(1+pct) + vGain.
            Assert.That(mg.GetVelocity(PlayerSlot.Rojo), Is.EqualTo(p.VBase + p.VGain).Within(p.VGain * 0.05f),
                "the leading seat must NOT receive the rubber-band boost");
        }

        // ── Ranking: greater distance wins; exact ties share a place ─────────────

        [Test]
        public void GreaterDistance_RanksBetter_ExactTiesSharePlace()
        {
            var p = SoloParamsNoObstacles();
            var mg = new V2Corre(p);
            mg.Initialize(new SeededRandom(4), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            int t = 0;
            while (!mg.IsFinished && t < 700)
            {
                // Rojo fastest, Azul medium, Amarillo & Verde identical slow (tie).
                inputs.Set(PlayerSlot.Rojo, Direction8.None, MashButton(t, 12));
                inputs.Set(PlayerSlot.Azul, Direction8.None, MashButton(t, 6));
                inputs.Set(PlayerSlot.Amarillo, Direction8.None, button: false);
                inputs.Set(PlayerSlot.Verde, Direction8.None, button: false);
                mg.Tick(inputs.Build(t));
                t++;
            }

            V2Result result = mg.GetResult();
            Assert.That(result.Kind, Is.EqualTo(ResultKind.Ranked));
            var bySeat = new Dictionary<int, PlayerRank>();
            foreach (PlayerRank r in result.Ranks) bySeat[r.Seat] = r;

            Assert.That(bySeat[(int)PlayerSlot.Rojo].Place, Is.EqualTo(1), "fastest covers the most distance → 1st");
            Assert.That(bySeat[(int)PlayerSlot.Azul].Place, Is.EqualTo(2), "medium → 2nd");
            Assert.That(bySeat[(int)PlayerSlot.Amarillo].Place, Is.EqualTo(bySeat[(int)PlayerSlot.Verde].Place),
                "the two identical slow seats must share a place");
            Assert.That(bySeat[(int)PlayerSlot.Amarillo].Place, Is.EqualTo(3),
                "the tied pair takes 3rd (standard competition ranking, no fabricated 4th-place split)");
        }

        // ── Difficulty scaling (GDD §9.1: "aplicado a velocidad/densidad") ───────

        [Test]
        public void DifficultyMult_ScalesObstacleDensityDirectly()
        {
            var mg = new V2Corre(CorreParams.GddDefaults);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, difficultyMult: 2f);

            Assert.That(mg.EffectiveObstacleDensity, Is.EqualTo(CorreParams.GddDefaults.ObstacleDensity * 2f).Within(1e-5f),
                "difficultyMult must scale obstacle density directly (GDD §9.1 'densidad')");
        }

        // ── AC1: zero heap allocation in steady-state Tick ───────────────────────

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            // Long round (30 s -- bypasses the validator's [3,8] bound, as MECH_01's
            // own zero-alloc test does, because CorreParams is constructed directly)
            // so the measured window stays live. Fixed, non-reactive input (a mash
            // pattern plus a periodic up-tap) exercises every hot-path branch: mash
            // interpretation, the velocity formula, jump/airborne, obstacle
            // crossing (both cleared and stunned), stun countdown, rubber-band, and
            // RenderState publish. No bot (a reactive bot would allocate and corrupt
            // the measurement -- MECH_02 rev-t058 M1 lesson).
            var p = new CorreParams(
                vBase: 3f, vGain: 4f, obstacleDensity: 0.2f, stunSeconds: 0.6f,
                rubberBandPct: 0.08f, durationSeconds: 30f, raceToFinish: false,
                jumpAirtimeSeconds: 0.5f, minObstacleGap: 4f, firstObstacleDistance: 5f,
                inputConfig: InputInterpreterConfig.GddDefaults);

            var mg = new V2Corre(p);
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            void SetTick(int t)
            {
                bool button = MashButton(t, 7);
                Direction8 stick = (t % 20 == 0) ? Direction8.N : Direction8.None;
                foreach (PlayerSlot slot in AllSlots) inputs.Set(slot, stick, button);
            }

            for (int t = 0; t < 100; t++) { SetTick(t); mg.Tick(inputs.Build(t)); }
            Assert.That(mg.IsFinished, Is.False, "sensor setup: the round must still be live after warmup");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 100; t < 1100; t++) { SetTick(t); mg.Tick(inputs.Build(t)); }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(mg.IsFinished, Is.False, "sensor setup: the round must still be live after the measured window");
            Assert.That(after - before, Is.EqualTo(0L));
        }

        // ── Replay determinism ────────────────────────────────────────────────────

        [Test]
        public void Determinism_SameSeedAndInputScript_ProducesIdenticalTraceAndResult()
        {
            var p = CorreParams.GddDefaults;
            var runA = RunDeterminismTrace(p, seed: 21);
            var runB = RunDeterminismTrace(p, seed: 21);

            Assert.That(runB.Frames.Count, Is.EqualTo(runA.Frames.Count),
                "same seed + same input script must produce the same number of frames");
            for (int i = 0; i < runA.Frames.Count; i++)
                Assert.That(runB.Frames[i], Is.EqualTo(runA.Frames[i]), $"trace diverged at frame {i}");

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
            // Non-vacuity: a different seed → a different track → the fixed
            // (non-perfect) input script mistimes different obstacles → different
            // stun timing → the trace must diverge somewhere.
            var p = CorreParams.GddDefaults;
            var runA = RunDeterminismTrace(p, seed: 21);
            var runB = RunDeterminismTrace(p, seed: 22);

            bool anyDifferent = runA.Frames.Count != runB.Frames.Count;
            int shorter = Math.Min(runA.Frames.Count, runB.Frames.Count);
            for (int i = 0; i < shorter && !anyDifferent; i++)
                if (!runA.Frames[i].Equals(runB.Frames[i])) anyDifferent = true;

            Assert.That(anyDifferent, Is.True, "a different seed must diverge somewhere");
        }

        // ── AC5: v2 definition + TASK-030 validator/schema pass ──────────────────

        [Test]
        public void V2Definition_ForCorre_PassesValidator()
        {
            var def = new MicrogameDefinitionV2
            {
                SchemaVersion = 2,
                Id = "mg_corre_endless_01",
                Mechanic = "MECH_03",
                DisplayVerb = "¡CORRE!",
                Dynamics = MicrogameDynamics.Competitive,
                Duration = 8.0f,
                DifficultyScaling = new[] { "obstacleDensity" },
                Params = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["vBase"] = 3.0,
                    ["vGain"] = 4.0,
                    ["obstacleDensity"] = 0.2,
                    ["stunSeconds"] = 0.6,
                    ["rubberBandPct"] = 0.08,
                    ["raceToFinish"] = false,
                },
                PayoutTable = new[] { 6, 4, 2, 1 },
                MinPlayers = 2,
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);
            Assert.That(result.IsValid, Is.True, $"field '{result.OffendingField}' -- {result.Message}");
        }

        [Test]
        public void V2Definition_ForCorre_VBaseOutOfRange_FailsValidator()
        {
            var def = new MicrogameDefinitionV2
            {
                SchemaVersion = 2,
                Id = "mg_corre_bad_01",
                Mechanic = "MECH_03",
                DisplayVerb = "¡CORRE!",
                Dynamics = MicrogameDynamics.Competitive,
                Duration = 8.0f,
                DifficultyScaling = Array.Empty<string>(),
                Params = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["vBase"] = 999.0, // out of Mech03Corre's declared range
                },
                PayoutTable = new[] { 6, 4, 2, 1 },
                MinPlayers = 2,
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);
            Assert.That(result.IsValid, Is.False,
                "an out-of-range vBase must fail validation -- proves the new MECH_03 schema entry gates something");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static PlayerRoster OnlyRojo() =>
            new PlayerRoster(new[] { SeatState.Human, SeatState.Empty, SeatState.Empty, SeatState.Empty });

        /// <summary>GddDefaults but with a longer duration for single-seat probing.</summary>
        private static CorreParams SoloParams() => CorreParams.GddDefaults;

        /// <summary>GddDefaults but with obstacle generation disabled (a clear track), for velocity/jump isolation.</summary>
        private static CorreParams SoloParamsNoObstacles() => new CorreParams(
            vBase: CorreParams.GddDefaults.VBase,
            vGain: CorreParams.GddDefaults.VGain,
            obstacleDensity: CorreParams.GddDefaults.ObstacleDensity,
            stunSeconds: CorreParams.GddDefaults.StunSeconds,
            rubberBandPct: CorreParams.GddDefaults.RubberBandPct,
            durationSeconds: CorreParams.GddDefaults.DurationSeconds,
            raceToFinish: false,
            jumpAirtimeSeconds: CorreParams.GddDefaults.JumpAirtimeSeconds,
            minObstacleGap: CorreParams.GddDefaults.MinObstacleGap,
            firstObstacleDistance: 1e9f, // first obstacle unreachable → effectively no obstacles
            inputConfig: InputInterpreterConfig.GddDefaults);

        private static CorreParams WithVBase(float vBase) => new CorreParams(
            vBase, 4f, 0.2f, 0.6f, 0.08f, 8f, false, 0.5f, 4f, 5f, InputInterpreterConfig.GddDefaults);

        private static CorreParams WithVGain(float vGain) => new CorreParams(
            3f, vGain, 0.2f, 0.6f, 0.08f, 8f, false, 0.5f, 4f, 5f, InputInterpreterConfig.GddDefaults);

        private static CorreParams WithDuration(float dur) => new CorreParams(
            3f, 4f, 0.2f, 0.6f, 0.08f, dur, false, 0.5f, 4f, 5f, InputInterpreterConfig.GddDefaults);

        private static CorreParams WithRubberBandPct(float pct) => new CorreParams(
            3f, 4f, 0.2f, 0.6f, pct, 8f, false, 0.5f, 4f, 5f, InputInterpreterConfig.GddDefaults);

        private static (List<(int Tick, int Seat, int DistMilli, bool Airborne, bool Stunned)> Frames, V2Result Result)
            RunDeterminismTrace(CorreParams p, int seed)
        {
            var mg = new V2Corre(p);
            mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

            var frames = new List<(int, int, int, bool, bool)>();
            var inputs = new FakeInputs();
            int t = 0;
            while (!mg.IsFinished && t < 700)
            {
                foreach (PlayerSlot slot in AllSlots)
                {
                    int s = (int)slot;
                    bool button = MashButton(t + s, 7);
                    Direction8 stick = ((t % 23) == (s * 5) % 23) ? Direction8.N : Direction8.None;
                    inputs.Set(slot, stick, button);
                }
                mg.Tick(inputs.Build(t));

                foreach (PlayerSlot slot in AllSlots)
                    frames.Add((t, (int)slot,
                        (int)MathF.Round(mg.GetDistance(slot) * 1000f),
                        mg.IsAirborne(slot), mg.IsStunned(slot)));
                t++;
            }
            return (frames, mg.GetResult());
        }

        /// <summary>
        /// A real reactive jump bot (not a hardcoded path). Drives PUBLIC input
        /// only: sets a 6 Hz mash pattern on the button and taps the stick up when
        /// the next obstacle on the shared track is close enough ahead that the
        /// jump's airtime will span it, reading only the production surface
        /// (GetDistance / GetVelocity / IsAirborne / IsStunned and the exposed
        /// obstacle track). Because a jump requested this tick counts as airborne
        /// for this tick's crossing (the generous, player-friendly rule, mirroring
        /// RunnerSim), timing the tap a little before the obstacle guarantees the
        /// crossing happens airborne. One tap per obstacle: once airborne it emits
        /// neutral, so the held-up edge resets before the next obstacle.
        /// </summary>
        private sealed class PerfectRunner
        {
            public void Drive(V2Corre mg, FakeInputs inputs, PlayerSlot slot, int tick)
            {
                bool button = MashButton(tick, 6);
                Direction8 stick = Direction8.None;

                if (!mg.IsAirborne(slot) && !mg.IsStunned(slot))
                {
                    float dist = mg.GetDistance(slot);
                    float v = mg.GetVelocity(slot);
                    float nextObs = float.MaxValue;
                    for (int i = 0; i < mg.ObstacleCount; i++)
                    {
                        float pos = mg.GetObstaclePosition(i);
                        if (pos > dist && pos < nextObs) nextObs = pos;
                    }
                    if (nextObs < float.MaxValue)
                    {
                        // Jump when the obstacle is within ~60% of the airborne span
                        // ahead -- margin at both ends so the crossing lands mid-air.
                        float span = MathF.Max(v, 0.01f) * mg.JumpAirtimeSeconds;
                        if (nextObs - dist <= span * 0.6f) stick = Direction8.N;
                    }
                }

                inputs.Set(slot, stick, button);
            }
        }
    }
}
