using System;

namespace Barcade.Core
{
    /// <summary>
    /// An IMMUTABLE snapshot of one player's input for a single frame.
    /// Uses plain floats instead of UnityEngine.Vector2 so it is testable
    /// without a Unity runtime dependency.
    ///
    /// Being a readonly struct guarantees value-type copy semantics and
    /// prevents field mutation after construction.
    /// </summary>
    public readonly struct InputSnapshot : IEquatable<InputSnapshot>
    {
        // -----------------------------------------------------------------
        // Fields (all readonly — immutability enforced by the compiler)
        // -----------------------------------------------------------------

        /// <summary>Horizontal stick axis, clamped to -1..1 by the input layer.</summary>
        public readonly float StickX;

        /// <summary>Vertical stick axis, clamped to -1..1 by the input layer.</summary>
        public readonly float StickY;

        /// <summary>State of the single action button this frame.</summary>
        public readonly ButtonState Button;

        // -----------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------

        public InputSnapshot(float stickX, float stickY, ButtonState button)
        {
            StickX = stickX;
            StickY = stickY;
            Button = button;
        }

        // -----------------------------------------------------------------
        // Derived helpers — pure functions, return new value types
        // -----------------------------------------------------------------

        /// <summary>
        /// Length of the stick vector (Euclidean magnitude).
        /// Returns 0 for the zero vector.
        /// </summary>
        public float Magnitude() => MathF.Sqrt(StickX * StickX + StickY * StickY);

        /// <summary>
        /// Returns a new snapshot whose stick is scaled to unit length.
        /// If the stick is at the zero vector, returns a zero-stick snapshot
        /// (avoids divide-by-zero).
        /// The button state is preserved unchanged.
        /// </summary>
        public InputSnapshot Normalized()
        {
            float mag = Magnitude();
            if (mag < 1e-6f)
                return new InputSnapshot(0f, 0f, Button);
            return new InputSnapshot(StickX / mag, StickY / mag, Button);
        }

        /// <summary>
        /// Returns a new snapshot with the stick zeroed when its magnitude is
        /// below <paramref name="threshold"/>; otherwise the raw stick values
        /// are preserved.
        /// The button state is preserved unchanged.
        /// </summary>
        public InputSnapshot WithDeadZone(float threshold)
        {
            if (Magnitude() < threshold)
                return new InputSnapshot(0f, 0f, Button);
            return this;
        }

        /// <summary>
        /// Returns the dominant cardinal direction of the stick vector.
        /// Uses the axis with greater absolute value to break ties.
        /// Returns <see cref="CardinalDir.None"/> when the stick is at rest
        /// (zero vector or magnitude == 0).
        /// </summary>
        public CardinalDir CardinalDirection()
        {
            if (StickX == 0f && StickY == 0f)
                return CardinalDir.None;

            float absX = MathF.Abs(StickX);
            float absY = MathF.Abs(StickY);

            if (absX >= absY)
                return StickX > 0f ? CardinalDir.Right : CardinalDir.Left;
            else
                return StickY > 0f ? CardinalDir.Up : CardinalDir.Down;
        }

        // -----------------------------------------------------------------
        // Equality (value semantics)
        // -----------------------------------------------------------------

        public bool Equals(InputSnapshot other) =>
            StickX == other.StickX &&
            StickY == other.StickY &&
            Button == other.Button;

        public override bool Equals(object obj) =>
            obj is InputSnapshot other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(StickX, StickY, Button);

        public static bool operator ==(InputSnapshot left, InputSnapshot right) => left.Equals(right);
        public static bool operator !=(InputSnapshot left, InputSnapshot right) => !left.Equals(right);

        public override string ToString() =>
            $"InputSnapshot(x={StickX:F3}, y={StickY:F3}, btn={Button})";
    }
}
