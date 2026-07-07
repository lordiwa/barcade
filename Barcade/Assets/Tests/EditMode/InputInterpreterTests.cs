using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-101 — coverage for <see cref="InputInterpreter"/> (§3.2/§3.3):
    /// press/release edges, tap/hold/mash derivation, debounce, diagonal collapse,
    /// Reset(), determinism, and zero-allocation steady state.
    ///
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class InputInterpreterTests
    {
        // ── Test double ─────────────────────────────────────────────────────────

        private sealed class FakeInputs : IReadOnlyPlayerInputs
        {
            private readonly InputSnapshot[] _snaps = new InputSnapshot[4];

            public void Set(PlayerSlot slot, InputSnapshot snap) => _snaps[(int)slot] = snap;

            public InputSnapshot For(PlayerSlot slot) => _snaps[(int)slot];
        }

        // ── Test helpers ────────────────────────────────────────────────────────

        private static void Drive(InputInterpreter interp, FakeInputs inputs, PlayerSlot slot, ButtonState state, int ticks)
        {
            var snap = new InputSnapshot(0f, 0f, state);
            for (int i = 0; i < ticks; i++)
            {
                inputs.Set(slot, snap);
                interp.Tick(inputs);
            }
        }

        private static void DriveMashCycles(InputInterpreter interp, FakeInputs inputs, PlayerSlot slot, int cycles)
        {
            for (int c = 0; c < cycles; c++)
            {
                Drive(interp, inputs, slot, ButtonState.Pressed, 2);
                Drive(interp, inputs, slot, ButtonState.Released, 2);
            }
        }

        private static InputSnapshot[] BuildDeterminismScript()
        {
            var list = new List<InputSnapshot>();

            void Add(float x, float y, ButtonState b, int repeat = 1)
            {
                for (int i = 0; i < repeat; i++) list.Add(new InputSnapshot(x, y, b));
            }

            Add(0f, 0f, ButtonState.Released, 3);
            Add(1f, 1f, ButtonState.Pressed, 2);   // NE stick + press confirm
            Add(1f, 1f, ButtonState.Held, 4);
            Add(1f, 0f, ButtonState.Released, 1);  // 1-tick bounce
            Add(1f, 1f, ButtonState.Held, 2);
            Add(0f, 1f, ButtonState.Released, 2);  // release confirm, N
            Add(0f, 0f, ButtonState.Released, 5);
            Add(1f, -1f, ButtonState.Pressed, 2);

            return list.ToArray();
        }

        // ── Config ──────────────────────────────────────────────────────────────

        [Test]
        public void GddDefaults_MatchSpecCalibration()
        {
            var config = InputInterpreterConfig.GddDefaults;
            Assert.That(config.TicksPerSecond, Is.EqualTo(60));
            Assert.That(config.TapWindowTicks, Is.EqualTo(9));
            Assert.That(config.MashMinHz, Is.EqualTo(2f));
            Assert.That(config.MashSatHz, Is.EqualTo(9f));
            Assert.That(config.MashWindowSeconds, Is.EqualTo(0.5f));
            Assert.That(config.MashWindowTicks, Is.EqualTo(30));
        }

        [Test]
        public void Constructor_NegativeMashMinHz_Throws()
        {
            // A negative floor would make MashForce report a phantom positive force
            // at 0 Hz (no presses at all) — must be rejected at construction.
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new InputInterpreterConfig(ticksPerSecond: 60, tapWindowTicks: 9, mashMinHz: -1f, mashSatHz: 9f, mashWindowSeconds: 0.5f));
        }

        // ── AC1/AC2 — press/release edges, tap vs hold ─────────────────────────

        [Test]
        public void Press_ConfirmedAfterTwoConsistentTicks_EmitsPressedEdgeAndStartsHold()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 1);
            Assert.That(interp.ButtonPressedThisTick(PlayerSlot.Rojo), Is.False, "single raw sample must not confirm yet");

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 1);
            Assert.That(interp.ButtonPressedThisTick(PlayerSlot.Rojo), Is.True, "second consistent sample confirms the press");
            Assert.That(interp.HoldDurationTicks(PlayerSlot.Rojo), Is.EqualTo(1));
        }

        [Test]
        public void Tap_ReleaseExactlyAtWindowBoundary_IsTap()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 2);   // confirm press @ tick1
            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 7);   // ticks2..8 steady hold
            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Released, 2);  // confirm release @ tick10

            Assert.That(interp.ButtonReleasedThisTick(PlayerSlot.Rojo), Is.True);
            Assert.That(interp.IsTapThisTick(PlayerSlot.Rojo), Is.True, "10 - 1 = 9 ticks == window boundary");
        }

        [Test]
        public void Tap_ReleaseOneTickPastWindow_IsNotTap()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 2);   // confirm press @ tick1
            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 8);   // ticks2..9 steady hold
            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Released, 2);  // confirm release @ tick11

            Assert.That(interp.ButtonReleasedThisTick(PlayerSlot.Rojo), Is.True);
            Assert.That(interp.IsTapThisTick(PlayerSlot.Rojo), Is.False, "11 - 1 = 10 ticks exceeds the window");
        }

        [Test]
        public void HoldDuration_ResetsToZeroOnRelease()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 5);
            Assert.That(interp.HoldDurationTicks(PlayerSlot.Rojo), Is.GreaterThan(0));

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Released, 2);
            Assert.That(interp.HoldDurationTicks(PlayerSlot.Rojo), Is.EqualTo(0));
        }

        [Test]
        public void Seats_AreIndependent_PressingOneDoesNotAffectAnother()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 2);

            Assert.That(interp.ButtonPressedThisTick(PlayerSlot.Rojo), Is.True);
            Assert.That(interp.ButtonPressedThisTick(PlayerSlot.Azul), Is.False);
            Assert.That(interp.HoldDurationTicks(PlayerSlot.Azul), Is.EqualTo(0));
        }

        // ── AC3 — mash response curve ───────────────────────────────────────────

        [Test]
        public void Mash_NoPresses_FrequencyAndForceAreZero()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            interp.Tick(inputs);

            Assert.That(interp.MashFrequencyHz(PlayerSlot.Rojo), Is.EqualTo(0f));
            Assert.That(interp.MashForce(PlayerSlot.Rojo), Is.EqualTo(0f));
        }

        [Test]
        public void Mash_AtMinimumFrequency_ForceIsZero()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 2); // one confirmed press = 1 / 0.5s = 2 Hz

            Assert.That(interp.MashFrequencyHz(PlayerSlot.Rojo), Is.EqualTo(2f).Within(0.001f));
            Assert.That(interp.MashForce(PlayerSlot.Rojo), Is.EqualTo(0f));
        }

        [Test]
        public void Mash_MidRangeFrequency_MapsToExpectedForceCurve()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            DriveMashCycles(interp, inputs, PlayerSlot.Rojo, 3); // 3 presses / 0.5s = 6 Hz

            Assert.That(interp.MashFrequencyHz(PlayerSlot.Rojo), Is.EqualTo(6.0f).Within(0.001f));
            Assert.That(interp.MashForce(PlayerSlot.Rojo), Is.EqualTo(4f / 7f).Within(0.0001f));
        }

        [Test]
        public void Mash_AboveSaturation_ForceClampsToOne()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            DriveMashCycles(interp, inputs, PlayerSlot.Rojo, 5); // 5 presses / 0.5s = 10 Hz > satHz

            Assert.That(interp.MashFrequencyHz(PlayerSlot.Rojo), Is.EqualTo(10f).Within(0.001f));
            Assert.That(interp.MashForce(PlayerSlot.Rojo), Is.EqualTo(1f));
        }

        [Test]
        public void Mash_CustomCurveParameters_AreHonoredNotHardcoded()
        {
            var config = new InputInterpreterConfig(
                ticksPerSecond: 60, tapWindowTicks: 9,
                mashMinHz: 0f, mashSatHz: 4f, mashWindowSeconds: 0.5f);
            var interp = new InputInterpreter(config);
            var inputs = new FakeInputs();

            DriveMashCycles(interp, inputs, PlayerSlot.Rojo, 1); // 1 press / 0.5s = 2 Hz

            Assert.That(interp.MashFrequencyHz(PlayerSlot.Rojo), Is.EqualTo(2f).Within(0.001f));
            // With GDD defaults (min2/sat9) this would be force 0; with custom (min0/sat4) it must be (2-0)/4 = 0.5.
            Assert.That(interp.MashForce(PlayerSlot.Rojo), Is.EqualTo(0.5f).Within(0.001f));
        }

        // ── AC4 — debounce ──────────────────────────────────────────────────────

        [Test]
        public void Debounce_SingleTickBounce_ProducesNoSpuriousEdgesAndDoesNotInterruptHold()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 4); // confirm press @ tick1, steady through tick3

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Released, 1); // tick4: 1-tick bounce
            Assert.That(interp.ButtonReleasedThisTick(PlayerSlot.Rojo), Is.False, "bounce must not confirm a release");
            int holdDuringBounce = interp.HoldDurationTicks(PlayerSlot.Rojo);

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 1); // tick5: bounce recovers
            Assert.That(interp.ButtonPressedThisTick(PlayerSlot.Rojo), Is.False, "bounce recovery must not confirm a new press");

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 2); // ticks6,7: steady hold resumes

            Assert.That(interp.HoldDurationTicks(PlayerSlot.Rojo), Is.EqualTo(holdDuringBounce + 3),
                "hold duration must keep incrementing straight through the bounce");
            Assert.That(interp.MashFrequencyHz(PlayerSlot.Rojo), Is.EqualTo(1f / 0.5f).Within(0.001f),
                "the bounce must not mint an extra mash press");
        }

        // ── AC5 — Direction8 conversion + diagonal collapse ────────────────────

        [TestCase(0f, 0f, Direction8.None)]
        [TestCase(1f, 0f, Direction8.E)]
        [TestCase(-1f, 0f, Direction8.W)]
        [TestCase(0f, 1f, Direction8.N)]
        [TestCase(0f, -1f, Direction8.S)]
        [TestCase(1f, 1f, Direction8.NE)]
        [TestCase(1f, -1f, Direction8.SE)]
        [TestCase(-1f, 1f, Direction8.NW)]
        [TestCase(-1f, -1f, Direction8.SW)]
        public void ToDirection8_MapsStickVectorToExpected8WayDirection(float x, float y, Direction8 expected)
        {
            Assert.That(InputInterpreter.ToDirection8(x, y), Is.EqualTo(expected));
        }

        [Test]
        public void CollapseDiagonal_PureCardinal_PassesThrough()
        {
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.N, CardinalDir.None), Is.EqualTo(CardinalDir.Up));
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.E, CardinalDir.Up), Is.EqualTo(CardinalDir.Right));
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.S, CardinalDir.Right), Is.EqualTo(CardinalDir.Down));
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.W, CardinalDir.Down), Is.EqualTo(CardinalDir.Left));
        }

        [Test]
        public void CollapseDiagonal_None_ReturnsNone()
        {
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.None, CardinalDir.Up), Is.EqualTo(CardinalDir.None));
        }

        [Test]
        public void CollapseDiagonal_DiagonalWithPriorDominant_CollapsesToThatDominant()
        {
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.NE, CardinalDir.Up), Is.EqualTo(CardinalDir.Up));
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.NE, CardinalDir.Right), Is.EqualTo(CardinalDir.Right));
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.SW, CardinalDir.Down), Is.EqualTo(CardinalDir.Down));
        }

        [Test]
        public void CollapseDiagonal_ColdStartDiagonal_PrefersHorizontalComponent()
        {
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.NE, CardinalDir.None), Is.EqualTo(CardinalDir.Right));
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.SE, CardinalDir.None), Is.EqualTo(CardinalDir.Right));
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.NW, CardinalDir.None), Is.EqualTo(CardinalDir.Left));
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.SW, CardinalDir.None), Is.EqualTo(CardinalDir.Left));
        }

        [Test]
        public void CollapseDiagonal_PreviousDominantNotAComponentOfDiagonal_FallsBackToHorizontalRule()
        {
            // Up is not a component of SE (Down/Right) — a stale, unrelated dominant must not leak through.
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.SE, CardinalDir.Up), Is.EqualTo(CardinalDir.Right));
            // Down is not a component of NW (Up/Left).
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.NW, CardinalDir.Down), Is.EqualTo(CardinalDir.Left));
            // Left is not a component of NE (Up/Right).
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.NE, CardinalDir.Left), Is.EqualTo(CardinalDir.Right));
            // Right is not a component of SW (Down/Left).
            Assert.That(InputInterpreter.CollapseDiagonal(Direction8.SW, CardinalDir.Right), Is.EqualTo(CardinalDir.Left));
        }

        [Test]
        public void CollapsedCardinal_NthenCenterThenSE_FallsBackToEastNotStaleNorth()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            inputs.Set(PlayerSlot.Rojo, new InputSnapshot(0f, 1f, ButtonState.Released)); // N
            interp.Tick(inputs);
            Assert.That(interp.CollapsedCardinal(PlayerSlot.Rojo), Is.EqualTo(CardinalDir.Up));

            inputs.Set(PlayerSlot.Rojo, new InputSnapshot(0f, 0f, ButtonState.Released)); // centered — dominant memory persists
            interp.Tick(inputs);
            Assert.That(interp.CollapsedCardinal(PlayerSlot.Rojo), Is.EqualTo(CardinalDir.None));

            inputs.Set(PlayerSlot.Rojo, new InputSnapshot(1f, -1f, ButtonState.Released)); // SE held
            interp.Tick(inputs);
            Assert.That(interp.CollapsedCardinal(PlayerSlot.Rojo), Is.EqualTo(CardinalDir.Right),
                "Up is not a component of SE; a stale dominant from an unrelated axis must not leak into it");

            // Continuing to hold SE must keep reporting East, not drift back to the stale North.
            interp.Tick(inputs);
            Assert.That(interp.CollapsedCardinal(PlayerSlot.Rojo), Is.EqualTo(CardinalDir.Right));
        }

        [Test]
        public void CollapsedCardinal_NtoNEtoE_TransitStaysStableThenSwitches()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            inputs.Set(PlayerSlot.Rojo, new InputSnapshot(0f, 1f, ButtonState.Released)); // N
            interp.Tick(inputs);
            Assert.That(interp.CollapsedCardinal(PlayerSlot.Rojo), Is.EqualTo(CardinalDir.Up));

            inputs.Set(PlayerSlot.Rojo, new InputSnapshot(1f, 1f, ButtonState.Released)); // NE transit
            interp.Tick(inputs);
            Assert.That(interp.CollapsedCardinal(PlayerSlot.Rojo), Is.EqualTo(CardinalDir.Up), "NE arriving from N must collapse to N");

            inputs.Set(PlayerSlot.Rojo, new InputSnapshot(1f, 0f, ButtonState.Released)); // E
            interp.Tick(inputs);
            Assert.That(interp.CollapsedCardinal(PlayerSlot.Rojo), Is.EqualTo(CardinalDir.Right));

            inputs.Set(PlayerSlot.Rojo, new InputSnapshot(1f, 1f, ButtonState.Released)); // NE transit again
            interp.Tick(inputs);
            Assert.That(interp.CollapsedCardinal(PlayerSlot.Rojo), Is.EqualTo(CardinalDir.Right), "NE arriving from E must collapse to E");
        }

        // ── AC6 — Reset() ───────────────────────────────────────────────────────

        [Test]
        public void Reset_ClearsHoldMashEdgeAndDirectionState()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Pressed, 5);
            inputs.Set(PlayerSlot.Rojo, new InputSnapshot(1f, 1f, ButtonState.Held));
            interp.Tick(inputs);

            Assert.That(interp.HoldDurationTicks(PlayerSlot.Rojo), Is.GreaterThan(0));
            Assert.That(interp.MashFrequencyHz(PlayerSlot.Rojo), Is.GreaterThan(0f));

            interp.Reset();

            Assert.That(interp.HoldDurationTicks(PlayerSlot.Rojo), Is.EqualTo(0));
            Assert.That(interp.MashFrequencyHz(PlayerSlot.Rojo), Is.EqualTo(0f));
            Assert.That(interp.ButtonPressedThisTick(PlayerSlot.Rojo), Is.False);
            Assert.That(interp.ButtonReleasedThisTick(PlayerSlot.Rojo), Is.False);
            Assert.That(interp.CollapsedCardinal(PlayerSlot.Rojo), Is.EqualTo(CardinalDir.None));
        }

        [Test]
        public void Reset_ButtonHeldAcrossBoundary_SurfacesAsFreshPress()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Held, 5); // held continuously
            interp.Reset();

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Held, 1);
            Assert.That(interp.ButtonPressedThisTick(PlayerSlot.Rojo), Is.False, "debounce still needs 2 confirming ticks post-reset");

            Drive(interp, inputs, PlayerSlot.Rojo, ButtonState.Held, 1);
            Assert.That(interp.ButtonPressedThisTick(PlayerSlot.Rojo), Is.True, "post-reset hold must surface as a brand-new press edge");
        }

        // ── AC7 — determinism and zero allocation ──────────────────────────────

        [Test]
        public void Tick_SameSnapshotSequence_IsFullyDeterministicAcrossInstances()
        {
            var interpA = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var interpB = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputsA = new FakeInputs();
            var inputsB = new FakeInputs();

            InputSnapshot[] script = BuildDeterminismScript();

            for (int t = 0; t < script.Length; t++)
            {
                inputsA.Set(PlayerSlot.Rojo, script[t]);
                inputsB.Set(PlayerSlot.Rojo, script[t]);
                interpA.Tick(inputsA);
                interpB.Tick(inputsB);

                Assert.That(interpB.ButtonPressedThisTick(PlayerSlot.Rojo), Is.EqualTo(interpA.ButtonPressedThisTick(PlayerSlot.Rojo)), "tick " + t);
                Assert.That(interpB.ButtonReleasedThisTick(PlayerSlot.Rojo), Is.EqualTo(interpA.ButtonReleasedThisTick(PlayerSlot.Rojo)), "tick " + t);
                Assert.That(interpB.HoldDurationTicks(PlayerSlot.Rojo), Is.EqualTo(interpA.HoldDurationTicks(PlayerSlot.Rojo)), "tick " + t);
                Assert.That(interpB.MashFrequencyHz(PlayerSlot.Rojo), Is.EqualTo(interpA.MashFrequencyHz(PlayerSlot.Rojo)), "tick " + t);
                Assert.That(interpB.CollapsedCardinal(PlayerSlot.Rojo), Is.EqualTo(interpA.CollapsedCardinal(PlayerSlot.Rojo)), "tick " + t);
            }
        }

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            var interp = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            var inputs = new FakeInputs();

            inputs.Set(PlayerSlot.Rojo, new InputSnapshot(1f, 1f, ButtonState.Pressed));
            inputs.Set(PlayerSlot.Azul, new InputSnapshot(-1f, 0f, ButtonState.Held));
            inputs.Set(PlayerSlot.Amarillo, new InputSnapshot(0f, -1f, ButtonState.Released));
            inputs.Set(PlayerSlot.Verde, new InputSnapshot(1f, -1f, ButtonState.Held));

            // Warm up: JIT the hot path and let any one-time static init happen first.
            for (int i = 0; i < 64; i++) interp.Tick(inputs);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) interp.Tick(inputs);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L));
        }
    }
}
