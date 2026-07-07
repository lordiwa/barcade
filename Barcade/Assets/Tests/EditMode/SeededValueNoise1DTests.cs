using System;
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-060 (T-107 slice 3, GDD MECH_01) — <see cref="SeededValueNoise1D"/>,
    /// the engineering substitute for GDD §4's literal "ruido_perlin" (human
    /// ruling 2026-07-07: a deterministic seeded-noise substitute -- value-noise
    /// / interpolated, NOT required to be true Perlin -- is acceptable; see
    /// <c>MantenMicrogame</c>'s class doc for the full ruling this cites).
    ///
    /// Covers: construction validation, the [-1,1] output range, continuity
    /// (no sudden jumps between adjacent ticks -- the whole point of choosing
    /// interpolated value-noise over a discrete re-roll like AlcocheckSim's own
    /// drunk sway), determinism (same seed -> identical curve), and
    /// discrimination (different seed -> the curve actually differs; proves the
    /// determinism lock isn't vacuous).
    ///
    /// No Unity scene required -- pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class SeededValueNoise1DTests
    {
        [Test]
        public void Constructor_NullRng_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SeededValueNoise1D(null, 8f, 4f));
        }

        [Test]
        public void Constructor_ZeroOrNegativeDuration_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SeededValueNoise1D(new SeededRandom(1), 0f, 4f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SeededValueNoise1D(new SeededRandom(1), -1f, 4f));
        }

        [Test]
        public void Constructor_ZeroOrNegativePointsPerSecond_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SeededValueNoise1D(new SeededRandom(1), 8f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SeededValueNoise1D(new SeededRandom(1), 8f, -2f));
        }

        [Test]
        public void Sample_StaysWithinMinusOneToOne_AcrossManySeedsAndTimes()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var noise = new SeededValueNoise1D(new SeededRandom(seed), 8f, 4f);
                for (int tick = 0; tick < 480; tick++) // 8s at 60 Hz
                {
                    float t = tick / 60f;
                    float sample = noise.Sample(t);
                    Assert.That(sample, Is.InRange(-1f, 1f), $"seed {seed}, t={t}: sample {sample} out of [-1,1]");
                }
            }
        }

        [Test]
        public void Sample_IsContinuous_NoLargeJumpBetweenAdjacentTicks()
        {
            // The whole point of interpolated value-noise over a discrete re-roll:
            // consecutive 1/60s samples must never jump by anywhere near the full
            // [-1,1] span (a discrete re-roll could jump by up to 2.0 every
            // interval boundary). 0.25 is a generous bound -- comfortably above
            // the largest per-tick delta smoothstep interpolation can produce at
            // this lattice density, but well below what an uninterpolated re-roll
            // would produce.
            const float maxAllowedJump = 0.25f;

            for (int seed = 0; seed < 20; seed++)
            {
                var noise = new SeededValueNoise1D(new SeededRandom(seed), 8f, 4f);
                float prev = noise.Sample(0f);
                for (int tick = 1; tick < 480; tick++)
                {
                    float t = tick / 60f;
                    float sample = noise.Sample(t);
                    Assert.That(MathF.Abs(sample - prev), Is.LessThanOrEqualTo(maxAllowedJump),
                        $"seed {seed}, t={t}: jumped from {prev} to {sample}, exceeding the continuity bound");
                    prev = sample;
                }
            }
        }

        [Test]
        public void Sample_SameSeed_ProducesIdenticalCurve()
        {
            var noiseA = new SeededValueNoise1D(new SeededRandom(7), 8f, 4f);
            var noiseB = new SeededValueNoise1D(new SeededRandom(7), 8f, 4f);

            for (int tick = 0; tick < 480; tick++)
            {
                float t = tick / 60f;
                Assert.That(noiseB.Sample(t), Is.EqualTo(noiseA.Sample(t)), $"t={t}: same seed must produce identical samples");
            }
        }

        [Test]
        public void Sample_DifferentSeed_ProducesDifferentCurve()
        {
            var noiseA = new SeededValueNoise1D(new SeededRandom(7), 8f, 4f);
            var noiseB = new SeededValueNoise1D(new SeededRandom(8), 8f, 4f);

            bool anyDifferent = false;
            for (int tick = 0; tick < 480 && !anyDifferent; tick++)
            {
                float t = tick / 60f;
                if (noiseA.Sample(t) != noiseB.Sample(t)) anyDifferent = true;
            }

            Assert.That(anyDifferent, Is.True, "different seeds must diverge somewhere -- the determinism lock would be vacuous otherwise");
        }

        [Test]
        public void Sample_BeforeZeroOrPastDuration_ClampsGracefullyRatherThanThrowing()
        {
            var noise = new SeededValueNoise1D(new SeededRandom(3), 8f, 4f);

            float atNegative = noise.Sample(-1f);
            float atZero = noise.Sample(0f);
            float wayPastDuration = noise.Sample(1000f);

            Assert.That(atNegative, Is.EqualTo(atZero), "negative time must clamp to t=0's sample");
            Assert.That(wayPastDuration, Is.InRange(-1f, 1f), "sampling past the constructed duration must not throw or go out of range");
        }
    }
}
