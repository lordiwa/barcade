using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD §4 MECH_05 — ¡REACCIONA! bot policy. No pre-existing test solver
    /// existed for this mechanic (unlike ESQUIVA/MANTÉN) — built fresh.
    ///
    /// <para>
    /// <b>Ideal decision.</b> Watches <see cref="RenderState.Feedback"/> every
    /// tick — the ONLY way to learn the real signal fired
    /// (<see cref="ReaccionaMicrogame.CueSignal"/>, seat -1) or a fakeout showed
    /// (<see cref="ReaccionaMicrogame.CueFakeout"/>, seat -1); neither is a
    /// persistent <see cref="RenderState"/> field, matching exactly what a human
    /// watching the screen sees (a transient flash), not a hidden countdown.
    /// Tracks one latch, <c>_pressFromNowOn</c>: once armed, the ideal decision
    /// is "press, held" (sustained true — never a single-tick blip, which
    /// <see cref="InputInterpreter"/>'s 2-tick debounce confirm could otherwise
    /// swallow) for the rest of the tanda. It arms two ways: (1) the real signal
    /// fires — the correct reaction; or (2) a fakeout fires AND a
    /// <see cref="BotSkill.ErrorRate"/> roll says this bot bites it — an early,
    /// incorrect press the mechanic resolves as a false start on its own (GDD-
    /// flavored: a worse bot bites fakeouts more often). A SECOND, independent
    /// error roll, taken only at the moment the real signal fires, sets
    /// <c>_fumbled</c> — suppressing the (otherwise-correct) press entirely for
    /// this tanda, a distinct suboptimal action ("freezing up") from biting a
    /// fakeout. Every tanda-scoped flag re-arms cleanly off <c>signalThisTick</c>
    /// alone: each tanda fires its real signal exactly once, strictly after any
    /// of its own fakeouts and strictly after the PREVIOUS tanda's every active
    /// seat has already been latched to a non-Pending outcome by the mechanic
    /// (see <see cref="ReaccionaMicrogame.Tick"/>'s own tandaDone/StartTanda
    /// sequencing) — so using it as the sole reset trigger correctly re-arms
    /// across both a multi-tanda best-of-3 round AND a brand-new round for a
    /// reused policy instance (one instance already spans a whole session — see
    /// <see cref="IBotPolicy"/>'s own doc), with no separate "new round" hook
    /// needed.
    /// </para>
    ///
    /// <para>
    /// <b>Humanization.</b> The two error rolls above already ARE this
    /// mechanic's errorRate application (a binary press/no-press game has no
    /// graded "how wrong" axis the way a continuous-position mechanic does).
    /// <see cref="BotSkill.ReactionDelayTicksMean"/>: the ideal press/no-press
    /// boolean is pushed through a <see cref="ReactionDelayBuffer"/> every tick
    /// and returned stale by the sampled tick count — this IS literally the
    /// mechanic's own reaction-latency axis (<see cref="PlayerRank.Metric"/> is
    /// ticks-since-signal), so a Novato's larger sampled delay directly produces
    /// a larger (worse) latency once the round resolves, and Bot.Optimo's fixed
    /// 6-tick delay lands just past
    /// <see cref="ReaccionaParams.AnticipationThresholdTicks"/>'s own GDD default
    /// (6) plus the loop's inherent one-tick observe-then-act lag plus
    /// <see cref="InputInterpreter"/>'s own +1-tick debounce confirm — safely a
    /// legitimate "Reacted" outcome, never anticipation, by construction (see
    /// <see cref="Bot.Optimo"/>'s own class doc for why 6 is not a coincidence).
    /// <see cref="BotSkill.MashHzMean"/> is unused — Reacciona reads a single
    /// press edge, not mash.
    /// </para>
    ///
    /// One instance per seat (see <see cref="IBotPolicy"/>'s own doc).
    /// </summary>
    public sealed class ReaccionaBotPolicy : IBotPolicy
    {
        private readonly ReactionDelayBuffer _delayBuffer = new ReactionDelayBuffer();
        private bool _pressFromNowOn;
        private bool _fumbled;
        private int? _sampledDelayTicks;

        public PlayerInput Decide(BotSkill skill, in BotView view, SeededRandom rng)
        {
            RenderState rs = view.RenderState;

            bool fakeoutThisTick = false;
            bool signalThisTick = false;
            for (int i = 0; i < rs.FeedbackCount; i++)
            {
                if (rs.Feedback[i].Seat != -1) continue;
                if (rs.Feedback[i].Cue == ReaccionaMicrogame.CueFakeout) fakeoutThisTick = true;
                else if (rs.Feedback[i].Cue == ReaccionaMicrogame.CueSignal) signalThisTick = true;
            }

            if (signalThisTick)
            {
                // The real signal — (re)arms this tanda's decision state. See
                // class doc: this is the sole reset trigger, correct for both a
                // multi-tanda round and a brand-new round on a reused instance.
                _pressFromNowOn = true;
                _fumbled = BotSkillSampler.RollError(skill, rng);
            }
            else if (fakeoutThisTick && !_pressFromNowOn)
            {
                if (BotSkillSampler.RollError(skill, rng))
                    _pressFromNowOn = true; // bit the fakeout — an early, sustained (debounce-safe) press
            }

            bool idealPress = _pressFromNowOn && !_fumbled;

            if (_sampledDelayTicks == null)
                _sampledDelayTicks = BotSkillSampler.SampleReactionDelayTicks(skill, rng);

            return _delayBuffer.PushAndPop(new PlayerInput(Direction8.None, idealPress), _sampledDelayTicks.Value);
        }
    }
}
