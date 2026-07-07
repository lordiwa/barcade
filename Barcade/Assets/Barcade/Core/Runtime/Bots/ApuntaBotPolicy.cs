using System;
using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD §4 MECH_04 — ¡APUNTA! bot policy. Reconciles AC3's reachability
    /// ceiling requirement — see the class doc's "static rules vs. hidden
    /// state" note for why this is a NEW live-play proof, not a duplicate of
    /// <c>ApuntaMicrogameV2Tests</c>'s pre-existing <c>CanReach</c> helper
    /// (kept untouched: a static geometric existence proof over power/direction
    /// samples, never simulating charge/hold/release timing at all — a
    /// different kind of check this policy's own harness-driven test
    /// complements rather than replaces).
    ///
    /// <para>
    /// <b>Ideal decision — closed-loop timing, not an inverted formula.</b>
    /// While NOT charging, scans <see cref="BotView.RenderState"/> for every
    /// unconsumed <see cref="EntityKind.Target"/> and, for each of the 8
    /// <see cref="Direction8"/> values, computes the closest achievable landing
    /// point along that ray (projected distance clamped into
    /// <c>[ProjectileSpeedMin, ProjectileSpeedMax]</c> — both constructor-
    /// injected, see below) from this seat's own turret (<see cref="BotView.TryFindMyAvatar"/>,
    /// whose <see cref="RenderEntity.X"/>/<see cref="RenderEntity.Y"/> ARE the
    /// turret corner for this mechanic — see <c>ApuntaMicrogame.PublishRenderState</c>).
    /// Picks the globally best (direction, target) pair whose landing point is
    /// within <c>HitRadius</c>, and its required "power" — then holds the
    /// button and, every subsequent tick, watches the LIVE
    /// <see cref="HudState.Meters"/> reading (the exact same oscillating value a
    /// human times against, GDD's own "medidor oscilante") and releases the
    /// instant it comes within a small tolerance of the required power. This
    /// avoids inverting <c>ApuntaMicrogame.ChargePower</c>'s sine (which has two
    /// solutions per period) entirely — it is thematically the correct model
    /// for a "stop the needle" timing mechanic, not an approximation of one.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] constructor-injected <see cref="ApuntaParams"/> is NOT a
    /// hidden-state leak.</b> <c>ProjectileSpeedMin</c>/<c>ProjectileSpeedMax</c>/
    /// <c>HitRadius</c> are fixed ballistics TUNING constants for a given
    /// microgame definition — identical every round, knowable in advance the
    /// same way a human eventually learns "how far a full-power shot travels"
    /// through practice — not per-round STATE a bot would otherwise have no way
    /// to see. <see cref="Decide"/> still reads ONLY <see cref="BotView"/> for
    /// every PER-TICK observation (target/turret positions, the live meter);
    /// AC1's guarantee is about that per-tick/per-round state, not compile-time
    /// tuning data (same reasoning as <c>CorreBotPolicy</c>'s identical note).
    /// </para>
    ///
    /// <para>
    /// <b>Humanization — NOT the generic <see cref="ReactionDelayBuffer"/>.</b>
    /// Every OTHER mechanic's policy delays its OUTPUT (buffer the ideal
    /// decision, replay it stale) because each one's "ideal" computation is
    /// open-loop — it never needs to know what its OWN past output actually
    /// did. APUNTA's release timing is deliberately closed-loop (it reacts to
    /// the LIVE <see cref="HudState.Meters"/> reading its own held button is
    /// currently producing) — buffering the output would desync this policy's
    /// internal charge-tracking from the mechanic's actual (delayed) button
    /// state: by the time the buffered "start charging" reaches the mechanic,
    /// this policy would already be reading a meter that reflects that STALE
    /// delayed reality while believing it reflects its own undelayed timeline.
    /// Confirmed by an early implementation attempt that used
    /// <see cref="ReactionDelayBuffer"/> here and reliably under-hit reachable
    /// targets. Instead: <see cref="BotSkill.ReactionDelayTicksMean"/> becomes
    /// an ENGAGE countdown — once a valid shot is found, the bot waits that
    /// many ticks (modeling "noticing and committing to a target") before
    /// actually pressing; the release loop itself runs in real time against
    /// the true live meter, exactly like a human timing against a needle they
    /// are actually watching. <see cref="BotSkill.ErrorRate"/> is rolled once
    /// per tick while UNARMED, gating whether this bot even starts the engage
    /// countdown for a shot it has found — a failed roll means "sit this
    /// attempt out" and re-rolls fresh next tick (a geometric distribution of
    /// ticks-until-engage, not a permanent freeze), thinning a Novato's shot
    /// cadence without extra state. <see cref="BotSkill.MashHzMean"/> is
    /// unused — Apunta reads hold/release, not mash.
    /// </para>
    ///
    /// One instance per seat (see <see cref="IBotPolicy"/>'s own doc).
    /// </summary>
    public sealed class ApuntaBotPolicy : IBotPolicy
    {
        /// <summary>[ASSUMED] release tolerance on the live [0,1] charge meter — see class doc's reachability-margin reasoning.</summary>
        private const float PowerTolerance = 0.02f;

        private readonly ApuntaParams _params;

        private bool _charging;
        private int _engageCountdown = -1; // -1 = unarmed; >=0 = ticks left before pressing
        private Direction8 _lockedAim = Direction8.NE;
        private float _desiredPower;
        private float? _prevAbsError;

        public ApuntaBotPolicy() : this(ApuntaParams.GddDefaults)
        {
        }

        public ApuntaBotPolicy(ApuntaParams mechanicParams)
        {
            _params = mechanicParams;
        }

        public PlayerInput Decide(BotSkill skill, in BotView view, SeededRandom rng)
        {
            if (_charging)
                return new PlayerInput(_lockedAim, DecideRelease(view));

            if (_engageCountdown > 0)
            {
                _engageCountdown--;
                return new PlayerInput(_lockedAim, false);
            }

            if (_engageCountdown == 0)
            {
                _charging = true;
                _prevAbsError = null;
                _engageCountdown = -1;
                return new PlayerInput(_lockedAim, true); // press now — engage countdown elapsed
            }

            // Unarmed: look for a shot; a successful RollError arms the engage countdown.
            if (view.TryFindMyAvatar(out RenderEntity turret)
                && TryPickShot(view.RenderState, turret.X, turret.Y, out Direction8 direction, out float power)
                && !BotSkillSampler.RollError(skill, rng))
            {
                _lockedAim = direction;
                _desiredPower = power;
                _engageCountdown = BotSkillSampler.SampleReactionDelayTicks(skill, rng);
            }

            return new PlayerInput(_lockedAim, false);
        }

        /// <summary>
        /// Real-time closed-loop release decision — no output delay (see class
        /// doc). Releases EITHER once within <see cref="PowerTolerance"/> of the
        /// desired power, OR the tick the error starts growing again (meaning
        /// the closest approach was the PREVIOUS tick — releasing one tick late
        /// bounds the miss instead of waiting indefinitely for a tolerance
        /// window a fast-oscillating region of the sine can hop clean over
        /// between two 60Hz samples).
        /// </summary>
        private bool DecideRelease(in BotView view)
        {
            float currentPower = view.Hud.Meters[view.Seat];
            float currentAbsError = MathF.Abs(currentPower - _desiredPower);

            bool closeEnough = currentAbsError <= PowerTolerance;
            bool justPassedClosestPoint = _prevAbsError.HasValue && currentAbsError > _prevAbsError.Value;

            if (closeEnough || justPassedClosestPoint)
            {
                _charging = false;
                return false; // release this tick
            }

            _prevAbsError = currentAbsError;
            return true; // keep holding
        }

        /// <summary>
        /// Globally best (direction, unconsumed target) pair whose achievable
        /// landing point (projected distance clamped into
        /// <c>[ProjectileSpeedMin, ProjectileSpeedMax]</c>) lands within
        /// <c>HitRadius</c> — "best" = smallest landing-to-target distance,
        /// mirroring <c>ApuntaMicrogame</c>'s own precision tiebreak.
        /// </summary>
        private bool TryPickShot(RenderState rs, float turretX, float turretY, out Direction8 direction, out float power)
        {
            direction = Direction8.None;
            power = 0f;
            float bestDist = float.MaxValue;
            bool found = false;

            for (int i = 0; i < rs.EntityCount; i++)
            {
                if (rs.Entities[i].Kind != EntityKind.Target || rs.Entities[i].VisualVariant != 0) continue;

                float tx = rs.Entities[i].X, ty = rs.Entities[i].Y;
                float toTargetX = tx - turretX, toTargetY = ty - turretY;

                foreach (Direction8 d in EightDirections)
                {
                    (float ux, float uy) = ApuntaMicrogame.DirectionToUnit(d);
                    float t = toTargetX * ux + toTargetY * uy;
                    float clampedT = t < _params.ProjectileSpeedMin ? _params.ProjectileSpeedMin
                        : (t > _params.ProjectileSpeedMax ? _params.ProjectileSpeedMax : t);

                    float landX = turretX + ux * clampedT;
                    float landY = turretY + uy * clampedT;
                    float dist = MathF.Sqrt((landX - tx) * (landX - tx) + (landY - ty) * (landY - ty));

                    if (dist <= _params.HitRadius && dist < bestDist)
                    {
                        bestDist = dist;
                        direction = d;
                        float span = _params.ProjectileSpeedMax - _params.ProjectileSpeedMin;
                        float rawPower = span > 0f ? (clampedT - _params.ProjectileSpeedMin) / span : 0f;
                        power = rawPower < 0f ? 0f : (rawPower > 1f ? 1f : rawPower);
                        found = true;
                    }
                }
            }

            return found;
        }

        private static readonly Direction8[] EightDirections =
        {
            Direction8.N, Direction8.NE, Direction8.E, Direction8.SE,
            Direction8.S, Direction8.SW, Direction8.W, Direction8.NW,
        };
    }
}
