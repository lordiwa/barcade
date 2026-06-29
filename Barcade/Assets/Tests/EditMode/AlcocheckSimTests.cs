using System;
using NUnit.Framework;
using Barcade.Core.Alcocheck;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="AlcocheckSim"/>: inverted-pendulum instability, drunk sway,
    /// corrective input, terminal conditions, determinism, and lifecycle.
    /// </summary>
    [TestFixture]
    public class AlcocheckSimTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a sim with zero drunk torque so physics tests are free of RNG noise.
        /// </summary>
        private static AlcocheckSim NoiselessSim(
            float maxLean          = 0.7853982f,
            float gravityGain      = 5f,
            float playerTorque     = 8f,
            float damping          = 0.5f,
            float survivalDuration = 100f)
            => new AlcocheckSim(
                maxLean:             maxLean,
                gravityGain:         gravityGain,
                playerTorque:        playerTorque,
                drunkTorque:         0f,
                drunkChangeInterval: 1f,   // irrelevant — torque is 0
                damping:             damping,
                survivalDuration:    survivalDuration,
                seed:                0);

        // ── Initial state ─────────────────────────────────────────────────────────

        [Test]
        public void InitialState_IsPlaying_AndUpright()
        {
            var sim = new AlcocheckSim();
            Assert.That(sim.State,           Is.EqualTo(AlcocheckState.Playing));
            Assert.That(sim.LostReason,      Is.EqualTo(AlcocheckLostReason.None));
            Assert.That(sim.Lean,            Is.EqualTo(0f).Within(1e-6f));
            Assert.That(sim.LeanVelocity,    Is.EqualTo(0f).Within(1e-6f));
            Assert.That(sim.SurvivalElapsed, Is.EqualTo(0f).Within(1e-6f));
        }

        // ── Instability: zero input, zero drunk ───────────────────────────────────

        [Test]
        public void Instability_ZeroInputZeroDrunk_LeanMagnitudeGrows()
        {
            // A small initial lean with zero corrective input and zero drunk noise must
            // grow steadily — the inverted-pendulum is unstable, not self-righting.
            var sim = NoiselessSim(gravityGain: 5f, damping: 0.5f);
            sim.SetForcedLean(0.2f);
            sim.Restart();

            float initialAbsLean = Math.Abs(sim.Lean);

            for (int i = 0; i < 5; i++)
                sim.Tick(0.1f, 0f);

            Assert.That(Math.Abs(sim.Lean), Is.GreaterThan(initialAbsLean),
                "inverted pendulum must be unstable: lean must grow without corrective input");
        }

        [Test]
        public void Instability_ZeroInputZeroDrunk_EventuallyLost()
        {
            // Starting with a non-zero lean and no correction, the sim must reach the
            // fall threshold and transition to Lost(ToppledOver).
            var sim = NoiselessSim(gravityGain: 5f, damping: 0.5f, survivalDuration: 999f);
            sim.SetForcedLean(0.2f);
            sim.Restart();

            // Tick up to 50 steps of 0.1 s (5 seconds) — gravity should dominate well within this.
            for (int i = 0; i < 50 && sim.State == AlcocheckState.Playing; i++)
                sim.Tick(0.1f, 0f);

            Assert.That(sim.State,      Is.EqualTo(AlcocheckState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(AlcocheckLostReason.ToppledOver));
        }

        // ── Corrective input reduces lean ─────────────────────────────────────────

        [Test]
        public void CorrectiveInput_PositiveLean_NegativeInputReducesLean()
        {
            // With a rightward lean (Lean > 0), full left input (inputX = -1) should
            // reduce |Lean| because playerTorque * (-1) overcomes gravity near 45°.
            var sim = NoiselessSim(gravityGain: 3.5f, playerTorque: 8f, damping: 1.2f,
                                   survivalDuration: 999f);
            sim.SetForcedLean(0.4f);
            sim.Restart();

            float initialLean = sim.Lean;   // 0.4 rad

            sim.Tick(0.1f, -1f);

            Assert.That(sim.Lean, Is.LessThan(initialLean),
                "corrective input (left when leaning right) must reduce lean toward upright");
        }

        // ── Wrong input + drunk → topples ─────────────────────────────────────────

        [Test]
        public void WrongInputWithDrunk_PositiveLean_EventuallyLost()
        {
            // Applying input in the WRONG direction (positive inputX when Lean is positive)
            // adds to the instability.  Even with maximum drunk opposition the combined
            // gravity+wrong-input torque is always positive → guaranteed topple.
            //
            // Analysis at Lean=0.3: gravity=5*sin(0.3)≈1.48, wrong-input=6*0.5=3,
            // max-drunk-opposition=-2  →  net_min = 1.48-2+3 = 2.48 > 0  (always grows)
            var sim = new AlcocheckSim(
                maxLean:             0.7853982f,
                gravityGain:         5f,
                playerTorque:        6f,
                drunkTorque:         2f,
                drunkChangeInterval: 0.4f,
                damping:             0.5f,
                survivalDuration:    9999f,
                seed:                42);

            sim.SetForcedLean(0.3f);
            sim.Restart();

            for (int i = 0; i < 200 && sim.State == AlcocheckState.Playing; i++)
                sim.Tick(0.05f, 0.5f);  // consistently wrong-direction input

            Assert.That(sim.State,      Is.EqualTo(AlcocheckState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(AlcocheckLostReason.ToppledOver));
        }

        [Test]
        public void ZeroInputNearThreshold_HighGravity_EventuallyLost()
        {
            // Near the fall threshold with zero input, even maximum drunk opposition cannot
            // prevent toppling: gravity (15*sin(0.5)≈7.18) > drunkTorque (4).
            var sim = new AlcocheckSim(
                maxLean:             0.7853982f,
                gravityGain:         15f,
                playerTorque:        8f,
                drunkTorque:         4f,
                drunkChangeInterval: 0.4f,
                damping:             1.2f,
                survivalDuration:    9999f,
                seed:                42);

            sim.SetForcedLean(0.5f);
            sim.Restart();

            for (int i = 0; i < 200 && sim.State == AlcocheckState.Playing; i++)
                sim.Tick(0.05f, 0f);

            Assert.That(sim.State,      Is.EqualTo(AlcocheckState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(AlcocheckLostReason.ToppledOver));
        }

        // ── Timer win ─────────────────────────────────────────────────────────────

        [Test]
        public void Survive_ZeroDrunkStartUpright_NoInput_TimerWin()
        {
            // At exact upright (Lean=0, LV=0) with zero drunk noise, sin(0)=0 so gravity
            // produces no torque — the equilibrium holds and Won fires when the timer lapses.
            var sim = NoiselessSim(gravityGain: 3.5f, survivalDuration: 3f);
            // No SetForcedLean → starts upright (Lean=0)
            sim.Restart();

            sim.Tick(2.99f, 0f);
            Assert.That(sim.State, Is.EqualTo(AlcocheckState.Playing),
                "must still be Playing just before survival timer");

            sim.Tick(0.02f, 0f);
            Assert.That(sim.State,           Is.EqualTo(AlcocheckState.Won));
            Assert.That(sim.SurvivalElapsed, Is.GreaterThanOrEqualTo(3f));
        }

        // ── Fall threshold ────────────────────────────────────────────────────────

        [Test]
        public void FallThreshold_AtMaxLean_IsLost()
        {
            // Starting exactly at the fall threshold must trigger Lost on the first Tick
            // (pre-integration guard), not after further integration past the edge.
            const float maxLean = 0.7853982f;
            var sim = NoiselessSim(maxLean: maxLean, gravityGain: 3.5f, survivalDuration: 999f);
            sim.SetForcedLean(maxLean);
            sim.Restart();

            sim.Tick(0.001f, 0f);

            Assert.That(sim.State,      Is.EqualTo(AlcocheckState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(AlcocheckLostReason.ToppledOver));
        }

        [Test]
        public void FallThreshold_SlightlyBelowMaxLean_StillPlayingAfterRestart()
        {
            // Just under maxLean: the sim must be Playing right after construction
            // (forced lean is below the threshold so Tick hasn't had a chance to integrate past it).
            const float maxLean = 0.7853982f;
            var sim = NoiselessSim(maxLean: maxLean, gravityGain: 3.5f, survivalDuration: 999f);
            sim.SetForcedLean(maxLean - 0.01f);
            sim.Restart();

            Assert.That(sim.State, Is.EqualTo(AlcocheckState.Playing),
                "just below maxLean — must be Playing before any Tick");
        }

        // ── Determinism ───────────────────────────────────────────────────────────

        [Test]
        public void Determinism_SameSeedSameInput_IdenticalLeanTrajectory()
        {
            // Two independent sims with the same seed and the same input sequence must
            // produce byte-for-byte identical Lean values at every tick (within float tolerance).
            const int seed = 99;
            var sim1 = new AlcocheckSim(seed: seed);
            var sim2 = new AlcocheckSim(seed: seed);

            for (int i = 0; i < 100; i++)
            {
                float inputX = (i % 3 == 0) ? -0.5f : (i % 3 == 1) ? 0f : 0.5f;
                sim1.Tick(0.016f, inputX);
                sim2.Tick(0.016f, inputX);

                if (sim1.State != AlcocheckState.Playing) break;

                Assert.That(sim1.Lean, Is.EqualTo(sim2.Lean).Within(1e-4f),
                    $"Lean mismatch at tick {i}");
                Assert.That(sim1.DrunkTorqueNow, Is.EqualTo(sim2.DrunkTorqueNow).Within(1e-4f),
                    $"DrunkTorqueNow mismatch at tick {i}");
            }
        }

        // ── Drunk force varies sign ───────────────────────────────────────────────

        [Test]
        public void DrunkForce_VariesSign_TakesBothPositiveAndNegativeValues()
        {
            // Over 200 ticks (10 s) with drunkChangeInterval=0.1 s (~100 re-rolls),
            // the drunk torque must have taken at least one positive and one negative value.
            // Use a huge maxLean and zero gravity so the sim never topples during the survey.
            var sim = new AlcocheckSim(
                maxLean:             (float)Math.PI,   // 180° — never topples
                gravityGain:         0f,               // no drift
                drunkTorque:         4f,
                drunkChangeInterval: 0.1f,             // fast re-rolls
                damping:             10f,              // heavy damping so lean stays near 0
                survivalDuration:    9999f,
                seed:                42);

            float minDrunk = float.MaxValue;
            float maxDrunk = float.MinValue;

            for (int i = 0; i < 200; i++)
            {
                sim.Tick(0.05f, 0f);
                float d = sim.DrunkTorqueNow;
                if (d < minDrunk) minDrunk = d;
                if (d > maxDrunk) maxDrunk = d;
            }

            Assert.That(minDrunk, Is.LessThan(-0.1f),
                "drunk torque must go negative at least once over 200 ticks");
            Assert.That(maxDrunk, Is.GreaterThan(0.1f),
                "drunk torque must go positive at least once over 200 ticks");
        }

        // ── Terminal no-op ────────────────────────────────────────────────────────

        [Test]
        public void Tick_NoOp_AfterLost()
        {
            // Lean near maxLean → topples quickly; then Tick must be a no-op.
            var sim = NoiselessSim(gravityGain: 5f, damping: 0.5f, survivalDuration: 999f);
            sim.SetForcedLean(0.78f); // just under maxLean=0.785
            sim.Restart();

            for (int i = 0; i < 50 && sim.State == AlcocheckState.Playing; i++)
                sim.Tick(0.1f, 0f);

            Assert.That(sim.State, Is.EqualTo(AlcocheckState.Lost));

            float elapsed = sim.SurvivalElapsed;
            float lean    = sim.Lean;

            sim.Tick(1f, 0f);

            Assert.That(sim.SurvivalElapsed, Is.EqualTo(elapsed).Within(1e-6f),
                "timer must not advance in Lost state");
            Assert.That(sim.Lean, Is.EqualTo(lean).Within(1e-6f),
                "lean must not change in Lost state");
        }

        [Test]
        public void Tick_NoOp_AfterWon()
        {
            var sim = NoiselessSim(gravityGain: 3.5f, survivalDuration: 2f);
            sim.Restart();

            sim.Tick(2.01f, 0f);
            Assert.That(sim.State, Is.EqualTo(AlcocheckState.Won));

            float elapsed = sim.SurvivalElapsed;
            sim.Tick(1f, 0f);
            Assert.That(sim.SurvivalElapsed, Is.EqualTo(elapsed).Within(1e-6f),
                "timer must not advance after Won");
        }

        // ── Restart resets state ──────────────────────────────────────────────────

        [Test]
        public void Restart_AfterLoss_ResetsToPlaying()
        {
            var sim = NoiselessSim(gravityGain: 5f, damping: 0.5f, survivalDuration: 999f);
            sim.SetForcedLean(0.78f);
            sim.Restart();

            for (int i = 0; i < 50 && sim.State == AlcocheckState.Playing; i++)
                sim.Tick(0.1f, 0f);

            Assert.That(sim.State, Is.EqualTo(AlcocheckState.Lost));

            sim.Restart();

            Assert.That(sim.State,           Is.EqualTo(AlcocheckState.Playing));
            Assert.That(sim.LostReason,      Is.EqualTo(AlcocheckLostReason.None));
            Assert.That(sim.SurvivalElapsed, Is.EqualTo(0f).Within(1e-6f));
            // Forced lean persists across Restart — same as DodgeSim.SetForcedPlayerStart pattern.
            Assert.That(sim.Lean,            Is.EqualTo(0.78f).Within(1e-5f),
                "forced lean must be restored on Restart");
        }

        // ── LeanFraction accessor ─────────────────────────────────────────────────

        [Test]
        public void LeanFraction_IsProportionalToMaxLean()
        {
            const float maxLean = 0.7853982f;
            var sim = NoiselessSim(maxLean: maxLean, survivalDuration: 999f);
            sim.SetForcedLean(maxLean * 0.5f);
            sim.Restart();

            Assert.That(sim.LeanFraction, Is.EqualTo(0.5f).Within(1e-5f),
                "LeanFraction must equal Lean / maxLean");
        }
    }
}
