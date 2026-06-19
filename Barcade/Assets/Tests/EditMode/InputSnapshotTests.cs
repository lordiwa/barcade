using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// AC2: InputSnapshot — immutable value type (struct) with stick direction
    ///      (float x, y), action-button state, and derived helpers.
    ///      No UnityEngine dependency.
    /// </summary>
    [TestFixture]
    public class InputSnapshotTests
    {
        // --- Construction and field access --------------------------------

        [Test]
        public void Constructor_StoresStickAndButtonState()
        {
            var snap = new InputSnapshot(0.5f, -0.3f, ButtonState.Pressed);
            Assert.That(snap.StickX, Is.EqualTo(0.5f));
            Assert.That(snap.StickY, Is.EqualTo(-0.3f));
            Assert.That(snap.Button, Is.EqualTo(ButtonState.Pressed));
        }

        [Test]
        public void DefaultInstance_HasZeroStickAndReleasedButton()
        {
            var snap = default(InputSnapshot);
            Assert.That(snap.StickX, Is.EqualTo(0f));
            Assert.That(snap.StickY, Is.EqualTo(0f));
            Assert.That(snap.Button, Is.EqualTo(ButtonState.Released));
        }

        // --- Immutability (struct copy semantics) -------------------------

        [Test]
        public void InputSnapshot_IsValueType_CopyIsIndependent()
        {
            // Structs are value types — copying must produce an independent value.
            var original = new InputSnapshot(1f, 0f, ButtonState.Held);
            var copy = original;            // value-type copy

            // Verify copy holds the same data (not testing mutation — structs can't
            // mutate fields externally, but we confirm copy semantics are clean).
            Assert.That(copy.StickX, Is.EqualTo(original.StickX));
            Assert.That(copy.StickY, Is.EqualTo(original.StickY));
            Assert.That(copy.Button, Is.EqualTo(original.Button));
        }

        // --- ButtonState variants -----------------------------------------

        [Test]
        public void ButtonState_Pressed_IsDistinctFromHeldAndReleased()
        {
            Assert.That(ButtonState.Pressed,  Is.Not.EqualTo(ButtonState.Held));
            Assert.That(ButtonState.Pressed,  Is.Not.EqualTo(ButtonState.Released));
            Assert.That(ButtonState.Held,     Is.Not.EqualTo(ButtonState.Released));
        }

        // --- Magnitude helper --------------------------------------------

        [Test]
        public void Magnitude_UnitVector_IsOne()
        {
            var snap = new InputSnapshot(1f, 0f, ButtonState.Released);
            Assert.That(snap.Magnitude(), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void Magnitude_ZeroVector_IsZero()
        {
            var snap = new InputSnapshot(0f, 0f, ButtonState.Released);
            Assert.That(snap.Magnitude(), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void Magnitude_DiagonalUnit_IsCorrect()
        {
            float v = 1f / System.MathF.Sqrt(2f);
            var snap = new InputSnapshot(v, v, ButtonState.Released);
            Assert.That(snap.Magnitude(), Is.EqualTo(1f).Within(0.0001f));
        }

        // --- Normalized direction helper ----------------------------------

        [Test]
        public void Normalized_NonZeroVector_HasMagnitudeOne()
        {
            var snap = new InputSnapshot(3f, 4f, ButtonState.Released);
            var norm = snap.Normalized();
            float mag = System.MathF.Sqrt(norm.StickX * norm.StickX + norm.StickY * norm.StickY);
            Assert.That(mag, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void Normalized_ZeroVector_ReturnsZeroVector()
        {
            var snap = new InputSnapshot(0f, 0f, ButtonState.Released);
            var norm = snap.Normalized();
            Assert.That(norm.StickX, Is.EqualTo(0f));
            Assert.That(norm.StickY, Is.EqualTo(0f));
        }

        [Test]
        public void Normalized_PreservesButtonState()
        {
            var snap = new InputSnapshot(1f, 0f, ButtonState.Pressed);
            var norm = snap.Normalized();
            Assert.That(norm.Button, Is.EqualTo(ButtonState.Pressed));
        }

        // --- DeadZone helper ----------------------------------------------

        [Test]
        public void WithDeadZone_BelowThreshold_ZerosStick()
        {
            var snap = new InputSnapshot(0.1f, 0.05f, ButtonState.Released);
            var result = snap.WithDeadZone(0.2f);
            Assert.That(result.StickX, Is.EqualTo(0f));
            Assert.That(result.StickY, Is.EqualTo(0f));
        }

        [Test]
        public void WithDeadZone_AboveThreshold_PreservesStick()
        {
            var snap = new InputSnapshot(0.8f, 0.0f, ButtonState.Released);
            var result = snap.WithDeadZone(0.2f);
            Assert.That(result.StickX, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void WithDeadZone_PreservesButtonState()
        {
            var snap = new InputSnapshot(0f, 0f, ButtonState.Held);
            var result = snap.WithDeadZone(0.2f);
            Assert.That(result.Button, Is.EqualTo(ButtonState.Held));
        }

        // --- Cardinal direction helper ------------------------------------

        [Test]
        public void CardinalDirection_RightVector_ReturnsRight()
        {
            var snap = new InputSnapshot(1f, 0f, ButtonState.Released);
            Assert.That(snap.CardinalDirection(), Is.EqualTo(CardinalDir.Right));
        }

        [Test]
        public void CardinalDirection_LeftVector_ReturnsLeft()
        {
            var snap = new InputSnapshot(-1f, 0f, ButtonState.Released);
            Assert.That(snap.CardinalDirection(), Is.EqualTo(CardinalDir.Left));
        }

        [Test]
        public void CardinalDirection_UpVector_ReturnsUp()
        {
            var snap = new InputSnapshot(0f, 1f, ButtonState.Released);
            Assert.That(snap.CardinalDirection(), Is.EqualTo(CardinalDir.Up));
        }

        [Test]
        public void CardinalDirection_DownVector_ReturnsDown()
        {
            var snap = new InputSnapshot(0f, -1f, ButtonState.Released);
            Assert.That(snap.CardinalDirection(), Is.EqualTo(CardinalDir.Down));
        }

        [Test]
        public void CardinalDirection_ZeroVector_ReturnsNone()
        {
            var snap = new InputSnapshot(0f, 0f, ButtonState.Released);
            Assert.That(snap.CardinalDirection(), Is.EqualTo(CardinalDir.None));
        }

        // --- Equality (value type) ----------------------------------------

        [Test]
        public void TwoSnapshots_SameValues_AreEqual()
        {
            var a = new InputSnapshot(0.5f, -0.5f, ButtonState.Held);
            var b = new InputSnapshot(0.5f, -0.5f, ButtonState.Held);
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void TwoSnapshots_DifferentValues_AreNotEqual()
        {
            var a = new InputSnapshot(1f, 0f, ButtonState.Pressed);
            var b = new InputSnapshot(0f, 1f, ButtonState.Pressed);
            Assert.That(a, Is.Not.EqualTo(b));
        }
    }
}
