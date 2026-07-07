namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD Annex D.3 "Calibraciones estándar" — the three named <see cref="BotSkill"/>
    /// presets. One system (<see cref="IBotPolicy"/>), three calibrations:
    /// <list type="bullet">
    /// <item><description><see cref="Novato"/> — production seat-filler (GDD:
    /// "rellena puestos vacíos en producción — debe perder más que ganar pero no
    /// ser trivial: winrate objetivo 10-20% contra humanos medios"). That winrate
    /// band is a DESIGN target, not something a headless unit test can assert —
    /// it depends on real human opponents — so it is documented here, not
    /// pinned by a fast-suite AC (see TASK-038 hand-off).</description></item>
    /// <item><description><see cref="Medio"/> — statistical reference calibration,
    /// the "average human" §16 fairness/winrate-band tests are measured against
    /// (e.g. "winrate del solista con 4×Bot.Medio sobre 1000 semillas ∈
    /// [40%,60%]"). GDD names Novato's and Óptimo's three numbers literally but
    /// not Medio's — [ASSUMED] engineering midpoint between the two, documented
    /// below per-field.</description></item>
    /// <item><description><see cref="Optimo"/> — test-only ceiling driver
    /// (escapability, reachability, mechanic ceilings). Zero variance
    /// (stdDev 0 on every trait) by design: a ceiling proof needs a
    /// deterministic, reproducible upper bound, not a distribution.</description></item>
    /// </list>
    /// </summary>
    public static class Bot
    {
        /// <summary>GDD literal: reactionDelayTicks ~N(20,5) (~330ms @ 60Hz), errorRate 0.25, mashHz ~N(4.5,1).</summary>
        public static BotSkill Novato => new BotSkill(
            reactionDelayTicksMean: 20f,
            reactionDelayTicksStdDev: 5f,
            errorRate: 0.25f,
            mashHzMean: 4.5f,
            mashHzStdDev: 1f);

        /// <summary>
        /// [ASSUMED] GDD names Medio as "referencia estadística" but gives no
        /// numbers (unlike Novato/Óptimo, both fully specified). Engineering
        /// midpoint: reactionDelayTicks mean/stdDev halfway between Novato(20,5)
        /// and Óptimo(6,0) → (13,2.5); errorRate halfway between 0.25 and 0.0 →
        /// 0.12 (rounded, not exactly half — a "average human" should still miss
        /// noticeably less often than a first-timer, not merely average the two
        /// numbers); mashHz mean halfway between 4.5 and 9 (saturation) → 6.5,
        /// stdDev kept at Novato's 1 (Óptimo's 0 stdDev is a ceiling-specific
        /// zero-variance marker, not a "better" value to interpolate toward).
        /// Flagged for design review — this is the one calibration this ticket
        /// invents rather than transcribes from the GDD.
        /// </summary>
        public static BotSkill Medio => new BotSkill(
            reactionDelayTicksMean: 13f,
            reactionDelayTicksStdDev: 2.5f,
            errorRate: 0.12f,
            mashHzMean: 6.5f,
            mashHzStdDev: 1f);

        /// <summary>
        /// GDD literal: reactionDelayTicks = 6 (100ms @ 60Hz — not coincidentally
        /// exactly <see cref="Barcade.Core.Microgames.V2.ReaccionaParams.AnticipationThresholdTicks"/>'s
        /// GDD default: the fastest a reaction can be without the mechanic itself
        /// flagging it as anticipation/false-start), errorRate 0.0, mashHz 9
        /// (= GameTuning Annex B's mash.satHz, saturation). Every stdDev is 0 —
        /// a deterministic ceiling (see <see cref="BotSkillSampler"/>'s
        /// zero-stdDev convention: these traits cost no RNG draws to "sample").
        /// </summary>
        public static BotSkill Optimo => new BotSkill(
            reactionDelayTicksMean: 6f,
            reactionDelayTicksStdDev: 0f,
            errorRate: 0f,
            mashHzMean: 9f,
            mashHzStdDev: 0f);
    }
}
