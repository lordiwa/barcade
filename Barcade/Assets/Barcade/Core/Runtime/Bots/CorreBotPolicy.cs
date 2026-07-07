using System;
using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD §4 MECH_03 — ¡CORRE! bot policy. Built fresh — the pre-existing
    /// <c>CorreMicrogameV2Tests</c>/slow-sweep <c>CorrePerfectRunner</c> reads
    /// PRODUCTION ACCESSORS (<c>GetDistance</c>/<c>GetVelocity</c>/
    /// <c>GetObstaclePosition</c>) for an exact-distance ceiling proof; a
    /// <see cref="BotView"/>-only policy cannot see obstacle distance past
    /// <see cref="RenderState"/>'s own 20-unit look-ahead clamp (see
    /// <c>CorreMicrogame.PublishRenderState</c>'s Hazard entity: <c>Progress01</c>
    /// saturates at 1 for anything farther out), so reconciling it into
    /// <see cref="IBotPolicy"/> would trade away exactly the precision that
    /// proof needs. AC3 scopes the mandatory ceiling reconciliation to
    /// MECH_02/MECH_04 only — CorrePerfectRunner stays separate, documented
    /// here rather than silently duplicated.
    ///
    /// <para>
    /// <b>Ideal decision.</b> Mash: sustained at the sampled
    /// <see cref="BotSkill.MashHzMean"/> trait via <see cref="BotMash"/> (its own
    /// randomness channel — errorRate is NOT rolled against the mash button;
    /// see below). Jump: reads this seat's own upcoming
    /// <see cref="EntityKind.Hazard"/> entity's <see cref="RenderEntity.Progress01"/>
    /// (== <c>relativeDistance / 20</c>, the mechanic's own look-ahead window —
    /// see <c>CorreMicrogame</c>'s <c>ObstacleLookAhead</c> constant, mirrored
    /// here since it is not exposed on <see cref="CorreParams"/>) and this
    /// seat's own approximate velocity (<see cref="HudState.Meters"/> ==
    /// <c>velocity/(VBase+VGain)</c>, un-normalized via the constructor-injected
    /// <see cref="CorreParams"/> — see the "static rules vs. hidden state" note
    /// below), reproducing <c>CorrePerfectRunner</c>'s own lead-window formula
    /// (<c>relative &lt;= velocity·JumpAirtimeSeconds·0.6</c>) but sourced
    /// entirely from <see cref="BotView"/>'s public projection. Only attempts a
    /// jump while this seat's avatar is <see cref="CorreMicrogame.VariantRunning"/>
    /// (grounded, not stunned — the mechanic's own eligibility condition).
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] constructor-injected <see cref="CorreParams"/> is NOT a
    /// hidden-state leak.</b> <see cref="VBase"/>/<see cref="VGain"/>/
    /// <see cref="JumpAirtimeSeconds"/> are fixed TUNING constants for a given
    /// microgame definition (identical every round it plays, and — same as a
    /// human player after enough practice — knowable in advance), not PER-ROUND
    /// state a bot would otherwise have no way to see. AC1's anti-cheat
    /// guarantee (BotView carries no hidden state) is about per-tick/per-round
    /// STATE (opponent positions not yet drawn, RNG outcomes not yet resolved);
    /// <see cref="Decide"/> itself still reads ONLY <see cref="BotView"/> for
    /// every PER-TICK observation. See <c>ApuntaBotPolicy</c>'s class doc for
    /// the identical reasoning (ballistics constants there).
    /// </para>
    ///
    /// <para>
    /// <b>Humanization.</b> <see cref="BotSkill.ErrorRate"/> is rolled only
    /// against the JUMP decision (suppressing an otherwise-correct jump —
    /// "misses the timing," the mechanic's real skill axis) — NOT against the
    /// mash button every tick, which would corrupt <see cref="InputInterpreter"/>'s
    /// own mash-frequency estimation with per-tick noise duplicating what
    /// <see cref="BotSkill.MashHzMean"/>'s own sampled variance already models.
    /// <see cref="BotSkill.ReactionDelayTicksMean"/>: the combined
    /// (stick,button) ideal decision is pushed through a
    /// <see cref="ReactionDelayBuffer"/> and returned stale by the sampled tick
    /// count, shifting the whole mash cadence and jump timing later without
    /// changing its shape.
    /// </para>
    ///
    /// One instance per seat (see <see cref="IBotPolicy"/>'s own doc).
    /// </summary>
    public sealed class CorreBotPolicy : IBotPolicy
    {
        /// <summary>Mirrors CorreMicrogame's own private ObstacleLookAhead — see class doc.</summary>
        private const float ObstacleLookAheadUnits = 20f;

        /// <summary>[ASSUMED] jump-lead safety margin — matches CorrePerfectRunner's own 0.6 factor.</summary>
        private const float LeadWindowFactor = 0.6f;

        private readonly CorreParams _params;
        private readonly ReactionDelayBuffer _delayBuffer = new ReactionDelayBuffer();

        private int _localTick;
        private float? _sampledMashHz;
        private int? _sampledDelayTicks;

        public CorreBotPolicy() : this(CorreParams.GddDefaults)
        {
        }

        public CorreBotPolicy(CorreParams mechanicParams)
        {
            _params = mechanicParams;
        }

        public PlayerInput Decide(BotSkill skill, in BotView view, SeededRandom rng)
        {
            if (_sampledMashHz == null)
                _sampledMashHz = BotSkillSampler.SampleMashHz(skill, rng);
            if (_sampledDelayTicks == null)
                _sampledDelayTicks = BotSkillSampler.SampleReactionDelayTicks(skill, rng);

            bool idealButton = BotMash.ButtonDown(_localTick, _sampledMashHz.Value);
            _localTick++;

            Direction8 idealStick = Direction8.None;

            if (view.TryFindMyAvatar(out RenderEntity avatar) && avatar.VisualVariant == CorreMicrogame.VariantRunning)
            {
                RenderState rs = view.RenderState;
                for (int i = 0; i < rs.EntityCount; i++)
                {
                    if (rs.Entities[i].Kind != EntityKind.Hazard || rs.Entities[i].OwnerSeat != view.Seat) continue;

                    float relative = rs.Entities[i].Progress01 * ObstacleLookAheadUnits;
                    float speedSpan = _params.VBase + _params.VGain;
                    float velocity = view.Hud.Meters[view.Seat] * speedSpan;
                    float span = MathF.Max(velocity, 0.01f) * _params.JumpAirtimeSeconds;

                    if (relative <= span * LeadWindowFactor)
                        idealStick = Direction8.N;
                    break;
                }

                if (idealStick == Direction8.N && BotSkillSampler.RollError(skill, rng))
                    idealStick = Direction8.None; // missed the jump timing — the mechanic's own skill axis
            }

            return _delayBuffer.PushAndPop(new PlayerInput(idealStick, idealButton), _sampledDelayTicks.Value);
        }
    }
}
