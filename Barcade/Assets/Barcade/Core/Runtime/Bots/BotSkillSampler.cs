using System;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD Annex D.3: turns a <see cref="BotSkill"/>'s distribution parameters
    /// into concrete, per-bot SEEDED draws (AC2: "same seed -&gt; same bot
    /// behavior"). Every method here is a pure function of
    /// (<see cref="BotSkill"/>, <see cref="SeededRandom"/>) — no
    /// <c>System.Random</c>, no static/global state, matching the repo-wide
    /// GDD §13 RNG rule (the same one <see cref="SeededRandom"/>'s own class doc
    /// cites).
    /// </summary>
    public static class BotSkillSampler
    {
        /// <summary>
        /// Standard normal draw via the Box-Muller transform (no Gaussian
        /// sampler exists on <see cref="SeededRandom"/> itself — see class doc).
        /// Consumes exactly two <see cref="SeededRandom.NextFloat"/> draws.
        /// <paramref name="u1"/>'s zero case (which would make <c>Log(u1)</c>
        /// diverge) is floored to a tiny epsilon — <see cref="SeededRandom.NextFloat"/>'s
        /// own doc guarantees strictly-&lt;1 but not strictly-&gt;0, so 0 is a
        /// real, if rare (2^-24 probability), possible draw.
        /// </summary>
        private static float NextStandardNormal(SeededRandom rng)
        {
            float u1 = rng.NextFloat();
            float u2 = rng.NextFloat();
            if (u1 < 1e-7f) u1 = 1e-7f;
            return MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(2f * MathF.PI * u2);
        }

        /// <summary>
        /// <paramref name="mean"/> + N(0,1)·<paramref name="stdDev"/>. A stdDev
        /// of exactly 0 short-circuits WITHOUT consuming any RNG draw (see
        /// <see cref="BotSkill"/>'s class doc on the deterministic-ceiling
        /// convention <see cref="Bot.Optimo"/> relies on) — a bot trait with no
        /// declared variance is not randomness Core silently spends anyway.
        /// </summary>
        public static float NextGaussian(SeededRandom rng, float mean, float stdDev)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (stdDev <= 0f) return mean;
            return mean + NextStandardNormal(rng) * stdDev;
        }

        /// <summary>
        /// GDD Annex D.3 reactionDelayTicks: a per-bot trait, sampled once (see
        /// each <see cref="IBotPolicy"/> implementation's lazy-sample-on-first-
        /// decide pattern) and reused for the bot's lifetime — not re-rolled
        /// every tick. Rounded to the nearest tick, floored at 0.
        /// </summary>
        public static int SampleReactionDelayTicks(BotSkill skill, SeededRandom rng)
        {
            float raw = NextGaussian(rng, skill.ReactionDelayTicksMean, skill.ReactionDelayTicksStdDev);
            int ticks = (int)MathF.Round(raw);
            return ticks < 0 ? 0 : ticks;
        }

        /// <summary>
        /// GDD Annex D.3 mashHz: a per-bot trait (same once-sampled convention
        /// as <see cref="SampleReactionDelayTicks"/>), clamped into GameTuning
        /// Annex B's <c>[mash.minHz, mash.satHz]</c> range (2..9 Hz,
        /// <see cref="InputInterpreterConfig.GddDefaults"/>) — a sampled tail
        /// value outside the range InputInterpreter's own §3.3 curve responds to
        /// would just silently saturate/floor there anyway; clamping here keeps
        /// the SAMPLED trait itself an honest description of achievable mash
        /// rates.
        /// </summary>
        public static float SampleMashHz(BotSkill skill, SeededRandom rng)
        {
            float raw = NextGaussian(rng, skill.MashHzMean, skill.MashHzStdDev);
            float min = InputInterpreterConfig.GddDefaults.MashMinHz;
            float max = InputInterpreterConfig.GddDefaults.MashSatHz;
            return raw < min ? min : (raw > max ? max : raw);
        }

        /// <summary>
        /// GDD Annex D.3 errorRate: rolled FRESH every decision (not a per-bot
        /// trait) — "probabilidad POR DECISIÓN de acción subóptima."
        /// </summary>
        public static bool RollError(BotSkill skill, SeededRandom rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (skill.ErrorRate <= 0f) return false;
            return rng.NextFloat() < skill.ErrorRate;
        }
    }
}
