using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD §4 MECH_01 — ¡MANTÉN! bot policy. Reconciles
    /// <c>MantenMicrogameV2Tests</c>'s pre-existing private <c>CorrectionOracle</c>
    /// (a reactive PD-style controller): the "ideal decision" step below is that
    /// SAME algorithm, generalized into the shared <see cref="IBotPolicy"/>
    /// shape and humanized per <see cref="BotSkill"/>.
    ///
    /// <para>
    /// <b>Ideal decision.</b> Reads <see cref="RenderEntity.Rotation"/> (theta,
    /// degrees) off this seat's own <see cref="EntityKind.PlayerAvatar"/> entity
    /// — the ONLY signal <see cref="MantenMicrogame"/> publishes for its balance
    /// state (see that mechanic's class doc: "the actual balance signal is
    /// Rotation... not position"). A finite-difference estimate of theta's
    /// angular velocity (this tick's theta minus the LAST tick this policy was
    /// called, over Dt) feeds a PD correction:
    /// <c>errorSignal = theta + Kd·thetaVelocity</c> (same sign convention
    /// <see cref="MantenMicrogame"/>'s own class doc derives: the corrective
    /// input has the SAME sign as the disturbance, because the GDD formula
    /// SUBTRACTS k·input). <c>errorSignal &gt; deadband → E</c>,
    /// <c>&lt; -deadband → W</c>, else <see cref="Direction8.None"/> — Manten
    /// only ever reads the horizontal stick component (see that mechanic's own
    /// class doc), so E/W is the entire useful action space.
    /// </para>
    ///
    /// <para>
    /// <b>Humanization.</b> <see cref="BotSkill.ErrorRate"/>: a rolled error
    /// FLIPS the correction to the opposite sign — literally pushing the wrong
    /// way, worsening the disturbance instead of countering it, the most
    /// "suboptimal" a 1-DOF correction can be. <see cref="BotSkill.ReactionDelayTicksMean"/>:
    /// the (possibly-flipped) ideal direction is pushed through a
    /// <see cref="ReactionDelayBuffer"/> and returned <c>reactionDelayTicks</c>
    /// ticks stale, modeling decision-to-actuation lag without touching
    /// <see cref="RenderState"/>'s own mutated-in-place instance (see that
    /// buffer's class doc). <see cref="BotSkill.MashHzMean"/> is unused — Manten
    /// reads no mash signal.
    /// </para>
    ///
    /// One instance per seat (see <see cref="IBotPolicy"/>'s own doc) — the
    /// finite-difference/reaction-delay/sampled-trait state below is scoped to
    /// whichever single seat this instance drives.
    /// </summary>
    public sealed class MantenBotPolicy : IBotPolicy
    {
        private const float Kd = 0.15f;
        private const float Deadband = 0.001f;
        private const float DtSeconds = 1f / 60f;

        private readonly ReactionDelayBuffer _delayBuffer = new ReactionDelayBuffer();

        private float _prevTheta;
        private bool _hasPrevTheta;

        private int? _sampledDelayTicks;

        public PlayerInput Decide(BotSkill skill, in BotView view, SeededRandom rng)
        {
            Direction8 ideal = Direction8.None;

            if (view.TryFindMyAvatar(out RenderEntity avatar))
            {
                float theta = avatar.Rotation * (System.MathF.PI / 180f);
                float thetaVelocity = _hasPrevTheta ? (theta - _prevTheta) / DtSeconds : 0f;
                _prevTheta = theta;
                _hasPrevTheta = true;

                float errorSignal = theta + Kd * thetaVelocity;
                ideal = errorSignal > Deadband ? Direction8.E : (errorSignal < -Deadband ? Direction8.W : Direction8.None);

                if (BotSkillSampler.RollError(skill, rng) && ideal != Direction8.None)
                    ideal = ideal == Direction8.E ? Direction8.W : Direction8.E;
            }

            if (_sampledDelayTicks == null)
                _sampledDelayTicks = BotSkillSampler.SampleReactionDelayTicks(skill, rng);

            return _delayBuffer.PushAndPop(new PlayerInput(ideal, false), _sampledDelayTicks.Value);
        }
    }
}
