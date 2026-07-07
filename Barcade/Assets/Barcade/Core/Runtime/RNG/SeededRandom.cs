using System;

namespace Barcade.Core
{
    /// <summary>
    /// Deterministic RNG — a genuine PCG32 implementation (GDD §13: "Generador:
    /// PCG32"; the previous System.Random wrapper is banned by that same section
    /// and by this repo's analyzer test). Algorithm: PCG-XSH-RR 64/32 from the
    /// canonical minimal C reference (pcg-random.org); outputs are pinned against
    /// the published pcg32_srandom(42, 54) demo vectors in the fast suite, which
    /// makes the sequence a cross-process, cross-.NET-version guarantee — the
    /// property the §13/D.4 replay contract needs.
    ///
    /// Identical construction produces identical sequences; the seed is fixed at
    /// construction time and cannot be changed. Stream derivation per §13
    /// (sessionSeed → roundSeed(r) → per-subsystem streams) lives in
    /// <see cref="Derive"/>; PCG's native stream mechanism (the increment) keeps
    /// subsystem streams independent, so consuming randomness in one never
    /// shifts another.
    ///
    /// Thread-safety: not thread-safe — use one instance per context/system.
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class SeededRandom : ISeededRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;

        /// <summary>
        /// Stream id used by the <see cref="SeededRandom(int)"/> constructor: the
        /// pcg32 demo's stream (54), so the int-seed path is pinnable against the
        /// published reference vectors.
        /// </summary>
        public const ulong DefaultStream = 54UL;

        private ulong _state;
        private readonly ulong _inc;

        /// <summary>
        /// Creates a new <see cref="SeededRandom"/> with the given seed on the
        /// <see cref="DefaultStream"/>. Two instances created with the same seed
        /// produce identical sequences (pre-existing public API, preserved).
        /// </summary>
        public SeededRandom(int seed)
            : this(unchecked((ulong)seed), DefaultStream)
        {
        }

        /// <summary>
        /// Full PCG32 construction — <c>pcg32_srandom_r(initState, streamId)</c>
        /// from the minimal C reference.
        /// </summary>
        /// <param name="initState">The 64-bit seed (state initializer).</param>
        /// <param name="streamId">
        /// Stream selector; each stream is an independent sequence for the same
        /// seed (only the low 63 bits are significant).
        /// </param>
        public SeededRandom(ulong initState, ulong streamId)
        {
            _inc = (streamId << 1) | 1UL;
            _state = 0UL;
            NextUInt32();
            _state += initState;
            NextUInt32();
        }

        /// <summary>
        /// GDD §13 derivation: sessionSeed → roundSeed(r) → named per-subsystem
        /// stream. The round mixes into the 64-bit state (SplitMix64 finalizer,
        /// so adjacent rounds land far apart) and the subsystem selects the PCG
        /// stream, keeping subsystems independent within the same round.
        /// </summary>
        public static SeededRandom Derive(int sessionSeed, int roundNumber, RngStream stream)
        {
            ulong mixed = Mix64(((ulong)(uint)sessionSeed << 32) | (uint)roundNumber);
            return new SeededRandom(mixed, (ulong)stream);
        }

        /// <summary>
        /// The raw PCG32 output — <c>pcg32_random_r</c> from the minimal C
        /// reference. All other members reduce to this.
        /// </summary>
        public uint NextUInt32()
        {
            ulong oldState = _state;
            _state = unchecked(oldState * Multiplier + _inc);
            uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rot = (int)(oldState >> 59);
            return (xorShifted >> rot) | (xorShifted << (-rot & 31));
        }

        /// <inheritdoc/>
        /// <remarks>Top 24 bits scaled by 2^-24: exactly representable in float, strictly &lt; 1.</remarks>
        public float NextFloat() => (NextUInt32() >> 8) * (1f / 16777216f);

        /// <inheritdoc/>
        /// <remarks>
        /// Preserves the System.Random.Next(min, max) edge contract this class
        /// always had: empty range returns min; inverted range throws. Uniformity
        /// via the pcg32_boundedrand_r rejection method (no modulo bias).
        /// </remarks>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive > maxExclusive)
                throw new ArgumentOutOfRangeException(nameof(minInclusive), "minInclusive must be <= maxExclusive");
            if (minInclusive == maxExclusive)
                return minInclusive;

            uint bound = (uint)((long)maxExclusive - minInclusive);
            uint threshold = (uint)(-bound) % bound;
            while (true)
            {
                uint r = NextUInt32();
                if (r >= threshold)
                    return (int)(minInclusive + (long)(r % bound));
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Uniform angle in [0, 2π) from a single draw, converted to
        /// (cos θ, sin θ) — consumes exactly one output per call so state
        /// consumption stays deterministic regardless of call order.
        /// </remarks>
        public Direction2D NextDirection()
        {
            double angle = NextFloat() * (2.0 * Math.PI);
            return new Direction2D((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        /// <summary>SplitMix64 finalizer — full-avalanche 64-bit mix for seed derivation.</summary>
        private static ulong Mix64(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
