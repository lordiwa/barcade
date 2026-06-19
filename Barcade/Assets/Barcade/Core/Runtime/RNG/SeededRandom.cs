using System;

namespace Barcade.Core
{
    /// <summary>
    /// Deterministic RNG wrapper over <see cref="System.Random"/>.
    /// Identical seeds produce identical sequences; the seed is fixed at
    /// construction time and cannot be changed.
    ///
    /// Thread-safety: not thread-safe — use one instance per context/system.
    /// </summary>
    public sealed class SeededRandom : ISeededRandom
    {
        private readonly Random _rng;

        /// <summary>
        /// Creates a new <see cref="SeededRandom"/> with the given seed.
        /// Two instances created with the same seed will produce identical
        /// sequences of values.
        /// </summary>
        public SeededRandom(int seed)
        {
            _rng = new Random(seed);
        }

        /// <inheritdoc/>
        public float NextFloat() => (float)_rng.NextDouble();

        /// <inheritdoc/>
        public int NextInt(int minInclusive, int maxExclusive) =>
            _rng.Next(minInclusive, maxExclusive);

        /// <inheritdoc/>
        /// <remarks>
        /// Uses the Box-Muller / angle method: draws a uniform angle in
        /// [0, 2π) and converts to (cos θ, sin θ).  A single NextDouble
        /// call keeps the internal state consumption deterministic.
        /// </remarks>
        public Direction2D NextDirection()
        {
            // Consume exactly one double per call so the sequence is
            // deterministic regardless of call order.
            double angle = _rng.NextDouble() * (2.0 * Math.PI);
            return new Direction2D((float)Math.Cos(angle), (float)Math.Sin(angle));
        }
    }
}
