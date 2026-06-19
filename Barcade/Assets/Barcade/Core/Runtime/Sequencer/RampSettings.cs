using System;

namespace Barcade.Core
{
    /// <summary>
    /// Defines the difficulty/speed ramp curve applied by the sequencer across a session.
    ///
    /// Ramp formula (deterministic, explicit):
    ///   effectiveDuration = Max(minDuration, baseDuration * pow(decayPerRound, roundIndex))
    ///   speedMultiplier   = baseDuration / effectiveDuration
    ///
    /// where:
    ///   - <see cref="DecayPerRound"/> is in (0, 1) — 0.95 means 5% shorter per round.
    ///   - <see cref="MinDuration"/> is the hard floor (seconds) — default 1.0 s.
    ///   - roundIndex is zero-based (round 0 = full baseDuration; no decay on first round).
    ///
    /// The formula is monotone non-increasing: effective duration either decreases or
    /// stays at the floor as roundIndex grows. It is fully deterministic given the same
    /// inputs and produces the same float on every platform (IEEE 754 double promoted to
    /// float; no Unity API involved).
    ///
    /// No UnityEngine dependency — safe for dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class RampSettings
    {
        // ── Defaults ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Default ramp: 5% decay per round, 1.0 s minimum duration.
        /// </summary>
        public static readonly RampSettings Default = new RampSettings(
            decayPerRound: 0.95f,
            minDuration:   1.0f);

        // ── Properties ───────────────────────────────────────────────────────────

        /// <summary>
        /// Multiplicative decay applied each round. Range (0, 1).
        /// 0.95 = effective duration is 5% shorter than the previous round.
        /// </summary>
        public float DecayPerRound { get; }

        /// <summary>
        /// Hard lower bound on effective duration in seconds.
        /// The effective duration is clamped to this floor regardless of how many
        /// rounds have passed.
        /// </summary>
        public float MinDuration { get; }

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a <see cref="RampSettings"/> instance.
        /// </summary>
        /// <param name="decayPerRound">Decay factor per round; must be in (0, 1).</param>
        /// <param name="minDuration">Minimum effective duration in seconds; must be > 0.</param>
        public RampSettings(float decayPerRound, float minDuration)
        {
            if (decayPerRound <= 0f || decayPerRound >= 1f)
                throw new ArgumentException(
                    "decayPerRound must be strictly between 0 and 1", "decayPerRound");

            if (minDuration <= 0f)
                throw new ArgumentException("minDuration must be positive", "minDuration");

            DecayPerRound = decayPerRound;
            MinDuration   = minDuration;
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Computes the effective round duration for a microgame given its base duration
        /// and the current zero-based round index.
        ///
        /// Formula: Max(<see cref="MinDuration"/>, baseDuration * pow(<see cref="DecayPerRound"/>, roundIndex))
        /// </summary>
        /// <param name="baseDuration">The microgame's base duration in seconds.</param>
        /// <param name="roundIndex">Zero-based round number (0 = no decay).</param>
        /// <returns>Effective duration in seconds, >= <see cref="MinDuration"/>.</returns>
        public float EffectiveDuration(float baseDuration, int roundIndex)
        {
            double raw = baseDuration * Math.Pow(DecayPerRound, roundIndex);
            double clamped = Math.Max(MinDuration, raw);
            return (float)clamped;
        }

        /// <summary>
        /// Computes the speed multiplier corresponding to the effective duration.
        ///
        /// Formula: baseDuration / EffectiveDuration(baseDuration, roundIndex)
        ///
        /// When the floor is not yet reached this equals pow(1 / decayPerRound, roundIndex).
        /// When the floor is active, speedMultiplier = baseDuration / minDuration.
        /// </summary>
        /// <param name="baseDuration">The microgame's base duration in seconds.</param>
        /// <param name="roundIndex">Zero-based round number.</param>
        /// <returns>Speed multiplier >= 1.0.</returns>
        public float SpeedMultiplier(float baseDuration, int roundIndex)
        {
            float eff = EffectiveDuration(baseDuration, roundIndex);
            return baseDuration / eff;
        }
    }
}
