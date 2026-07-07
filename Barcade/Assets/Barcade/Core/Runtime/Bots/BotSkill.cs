using System;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD Annex D.3 "Modelo de habilidad" — the three global humanizer knobs a
    /// calibration is made of, plus per-mechanic overrides (<see cref="With"/>).
    /// A <see cref="BotSkill"/> is pure DATA: it says nothing about HOW a given
    /// mechanic turns these numbers into a <see cref="PlayerInput"/> stream —
    /// that is each mechanic's own <see cref="IBotPolicy"/> implementation.
    ///
    /// <para>
    /// <b>reactionDelayTicks</b> (mean/stdDev): how many ticks late the bot's
    /// muscles fire relative to its own (instantaneous) read of the round state —
    /// GDD: Novato ~N(20,5) ticks (~330ms), Óptimo = 6 ticks (100ms) fixed
    /// (stdDev 0). <b>errorRate</b>: per-decision probability of a suboptimal
    /// action — GDD: Novato 0.25, Óptimo 0.0. <b>mashHz</b> (mean/stdDev): the
    /// bot's typical sustained mash frequency for mechanics that read one — GDD:
    /// Novato ~N(4.5,1), Óptimo 9 (= GameTuning Annex B's <c>mash.satHz</c>,
    /// saturation).
    /// </para>
    ///
    /// <para>
    /// A stdDev of exactly 0 is a deliberate DETERMINISTIC-CEILING marker (see
    /// <see cref="BotSkillSampler"/>): sampling a 0-stdDev trait returns the mean
    /// without consuming any <see cref="SeededRandom"/> draw, which is why
    /// <see cref="Bot.Optimo"/> — a fixed, no-jitter ceiling calibration — costs
    /// zero RNG draws for its reaction-delay/mash traits (only <c>errorRate</c>'s
    /// per-decision roll ever fires it, and Optimo's errorRate is itself 0.0).
    /// </para>
    /// </summary>
    public readonly struct BotSkill
    {
        public readonly float ReactionDelayTicksMean;
        public readonly float ReactionDelayTicksStdDev;
        public readonly float ErrorRate;
        public readonly float MashHzMean;
        public readonly float MashHzStdDev;

        public BotSkill(
            float reactionDelayTicksMean,
            float reactionDelayTicksStdDev,
            float errorRate,
            float mashHzMean,
            float mashHzStdDev)
        {
            if (reactionDelayTicksMean < 0f)
                throw new ArgumentOutOfRangeException(nameof(reactionDelayTicksMean), "must be non-negative.");
            if (reactionDelayTicksStdDev < 0f)
                throw new ArgumentOutOfRangeException(nameof(reactionDelayTicksStdDev), "must be non-negative.");
            if (errorRate < 0f || errorRate > 1f)
                throw new ArgumentOutOfRangeException(nameof(errorRate), "must be in [0,1].");
            if (mashHzMean < 0f)
                throw new ArgumentOutOfRangeException(nameof(mashHzMean), "must be non-negative.");
            if (mashHzStdDev < 0f)
                throw new ArgumentOutOfRangeException(nameof(mashHzStdDev), "must be non-negative.");

            ReactionDelayTicksMean = reactionDelayTicksMean;
            ReactionDelayTicksStdDev = reactionDelayTicksStdDev;
            ErrorRate = errorRate;
            MashHzMean = mashHzMean;
            MashHzStdDev = mashHzStdDev;
        }

        /// <summary>
        /// GDD Annex D.3 "overrides por mecánica": returns a copy with only the
        /// given fields replaced — the mechanism a specific <see cref="IBotPolicy"/>
        /// (or its caller) uses to widen/narrow one global knob for a mechanic
        /// where the calibration's own default doesn't fit (e.g. a mechanic with
        /// no meaningful mash channel can leave <see cref="MashHzMean"/>
        /// untouched; one wants a distinct value can override just that field).
        /// </summary>
        public BotSkill With(
            float? reactionDelayTicksMean = null,
            float? reactionDelayTicksStdDev = null,
            float? errorRate = null,
            float? mashHzMean = null,
            float? mashHzStdDev = null)
            => new BotSkill(
                reactionDelayTicksMean ?? ReactionDelayTicksMean,
                reactionDelayTicksStdDev ?? ReactionDelayTicksStdDev,
                errorRate ?? ErrorRate,
                mashHzMean ?? MashHzMean,
                mashHzStdDev ?? MashHzStdDev);
    }
}
