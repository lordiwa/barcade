using System;

namespace Barcade.Core
{
    /// <summary>
    /// Deterministic 1-D value noise: a fixed lattice of independently-seeded
    /// random values in [-1, 1], smoothly interpolated (smoothstep ease) between
    /// neighboring lattice points so the sampled curve has no sudden jumps.
    ///
    /// <para>
    /// <b>[ASSUMED] engineering substitute for GDD §4 MECH_01's literal
    /// "ruido_perlin"</b> (human ruling 2026-07-07: a deterministic seeded-noise
    /// substitute -- value-noise / interpolated, NOT required to be true Perlin
    /// -- is acceptable, reviewer-verified; see
    /// <see cref="Barcade.Core.Microgames.V2.MantenMicrogame"/>'s class doc for
    /// the full ruling this cites). True Perlin noise (gradient noise, dot
    /// products against a permutation-indexed gradient table) is a strictly
    /// higher-fidelity technique this substitutes for; value noise needs only
    /// one random draw per lattice point while still producing the continuous,
    /// unpredictable wobble the GDD's "ruido_perlin" name is really asking for --
    /// a smoothed curve, not a discrete step re-roll like
    /// <see cref="Barcade.Core.Alcocheck.AlcocheckSim"/>'s own drunk sway (that
    /// class's re-roll approach was deliberately NOT reused here; only its
    /// surrounding pendulum-integration structure was mined for
    /// <c>MantenMicrogame</c>).
    /// </para>
    ///
    /// The entire lattice is drawn from the injected <see cref="SeededRandom"/>
    /// once, at construction -- the only allocation and the only RNG consumption
    /// this class ever does. <see cref="Sample"/> is a pure, zero-allocation
    /// function of time thereafter (array reads plus arithmetic), safe to call
    /// every simulation tick. GDD §13 compliance: consumes randomness only
    /// through the injected <see cref="SeededRandom"/>, never System.Random.
    /// </summary>
    public sealed class SeededValueNoise1D
    {
        private readonly float[] _lattice;
        private readonly float _pointsPerSecond;

        /// <param name="rng">Consumed here only, at construction -- one draw per lattice point.</param>
        /// <param name="durationSeconds">The longest time span <see cref="Sample"/> is expected to be called with; must be positive.</param>
        /// <param name="pointsPerSecond">Lattice density (independent samples per second); must be positive. Higher = more rapid wobble.</param>
        public SeededValueNoise1D(SeededRandom rng, float durationSeconds, float pointsPerSecond)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (durationSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(durationSeconds), "must be positive.");
            if (pointsPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(pointsPerSecond), "must be positive.");

            _pointsPerSecond = pointsPerSecond;
            // +2: covers the fractional remainder past the last full lattice
            // interval within [0, durationSeconds], plus one extra point so
            // Sample() always has a valid i0+1 neighbor to interpolate toward
            // without any special-casing at the far end.
            int count = (int)MathF.Ceiling(durationSeconds * pointsPerSecond) + 2;
            _lattice = new float[count];
            for (int i = 0; i < count; i++)
                _lattice[i] = rng.NextFloat() * 2f - 1f;
        }

        /// <summary>
        /// Samples the noise curve at time <paramref name="t"/> seconds. Range
        /// [-1, 1]. Zero-alloc. Gracefully clamps rather than throwing for
        /// <paramref name="t"/> outside [0, durationSeconds] (negative time
        /// clamps to the t=0 sample; time past the constructed duration holds at
        /// the last lattice value) -- callers integrating with a floating
        /// elapsed-seconds accumulator can drift a hair past the nominal
        /// duration on the final tick, and this must never throw or read out of
        /// bounds when that happens.
        /// </summary>
        public float Sample(float t)
        {
            if (t < 0f) t = 0f;
            float f = t * _pointsPerSecond;

            int maxIndex = _lattice.Length - 1;
            int i0 = (int)f;
            if (i0 > maxIndex - 1) i0 = maxIndex - 1;
            if (i0 < 0) i0 = 0;
            int i1 = i0 + 1;

            float frac = f - i0;
            if (frac < 0f) frac = 0f;
            else if (frac > 1f) frac = 1f;

            float eased = frac * frac * (3f - 2f * frac); // smoothstep
            return _lattice[i0] + (_lattice[i1] - _lattice[i0]) * eased;
        }
    }
}
