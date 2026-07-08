using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Constructor-injected tuning for <see cref="BombardeaMicrogame"/> (GDD §4
    /// MECH_07). Plain engine-free POCO — same approach as
    /// <see cref="PersigueParams"/>/<see cref="EsquivaParams"/>.
    ///
    /// <para>
    /// <b>P4 guarantee, hard-floored (not just schema-range-checked).</b>
    /// <see cref="TelegraphSeconds"/>'s constructor floor of 0.5s is the actual
    /// mechanism behind AC4 ("&gt;=0.5s guaranteed between telegraph visibility
    /// and damage application") — GDD literal is 0.7s, comfortably above the
    /// floor, but the invariant is enforced here so no data-driven recalibration
    /// (GDD's own "remote recalibration" goal for these knobs) can silently
    /// violate the "never damage without visible warning" rule.
    /// </para>
    /// </summary>
    public readonly struct BombardeaParams
    {
        /// <summary>Seat index (0..3) that is solo this round — decided externally by <see cref="SoloSeatRotation"/>, same convention as <see cref="PersigueParams.SoloSeat"/>.</summary>
        public readonly int SoloSeat;

        /// <summary>GDD "fireCooldown": minimum seconds between shots (GDD literal 0.9).</summary>
        public readonly float FireCooldownSeconds;

        /// <summary>GDD "telegraphSec": seconds between a shot's impact zone becoming visible and the blast applying (GDD literal 0.7). Hard floor 0.5 (see class doc).</summary>
        public readonly float TelegraphSeconds;

        /// <summary>GDD "blastRadius": logical-unit radius of a shot's impact zone.</summary>
        public readonly float BlastRadius;

        /// <summary>GDD "soloScorePerHit": HUD score credited to the solo seat per landed hit (telemetry only — does not affect Place, see class doc).</summary>
        public readonly int SoloScorePerHit;

        /// <summary>[ASSUMED] Trio dodge movement speed, logical units/second.</summary>
        public readonly float TrioAvatarSpeed;

        /// <summary>[ASSUMED] Solo reticle movement speed, logical units/second.</summary>
        public readonly float ReticleSpeed;

        /// <summary>[ASSUMED] Trio avatar collision circle radius, added to <see cref="BlastRadius"/> for the hit test.</summary>
        public readonly float AvatarRadius;

        /// <summary>Round length in seconds — must be within the TASK-030 validator's GDD §11.1 [3,8] bound.</summary>
        public readonly float DurationSeconds;

        public const float MinTelegraphSeconds = 0.5f;

        public BombardeaParams(
            int soloSeat,
            float fireCooldownSeconds,
            float telegraphSeconds,
            float blastRadius,
            int soloScorePerHit,
            float trioAvatarSpeed,
            float reticleSpeed,
            float avatarRadius,
            float durationSeconds)
        {
            if (soloSeat < 0 || soloSeat > 3)
                throw new ArgumentOutOfRangeException(nameof(soloSeat), "must be a seat index 0..3.");
            if (fireCooldownSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(fireCooldownSeconds), "must be positive.");
            if (telegraphSeconds < MinTelegraphSeconds)
                throw new ArgumentOutOfRangeException(nameof(telegraphSeconds),
                    $"GDD P4: telegraph must guarantee >= {MinTelegraphSeconds}s of visible warning before damage.");
            if (blastRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(blastRadius), "must be positive.");
            if (soloScorePerHit <= 0)
                throw new ArgumentOutOfRangeException(nameof(soloScorePerHit), "must be positive.");
            if (trioAvatarSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(trioAvatarSpeed), "must be positive.");
            if (reticleSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(reticleSpeed), "must be positive.");
            if (avatarRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(avatarRadius), "must be positive.");
            if (durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "must be positive.");

            SoloSeat = soloSeat;
            FireCooldownSeconds = fireCooldownSeconds;
            TelegraphSeconds = telegraphSeconds;
            BlastRadius = blastRadius;
            SoloScorePerHit = soloScorePerHit;
            TrioAvatarSpeed = trioAvatarSpeed;
            ReticleSpeed = reticleSpeed;
            AvatarRadius = avatarRadius;
            DurationSeconds = durationSeconds;
        }

        /// <summary>
        /// GDD-plausible baseline calibration: FireCooldownSeconds/TelegraphSeconds
        /// are GDD literals (0.9/0.7). BlastRadius/TrioAvatarSpeed are engineering
        /// defaults tuned (see slow-suite winrate sweep) so an attentive
        /// Bot.Medio trio's dodge-the-telegraph play lands the calibrated
        /// mechanic in the GDD [40,60]% solo-winrate band.
        /// </summary>
        public static BombardeaParams GddDefaults(int soloSeat) => new BombardeaParams(
            soloSeat: soloSeat,
            fireCooldownSeconds: 0.9f,
            telegraphSeconds: 0.7f,
            blastRadius: 0.12f,
            soloScorePerHit: 1,
            trioAvatarSpeed: 0.4f,
            reticleSpeed: 0.6f,
            avatarRadius: 0.03f,
            durationSeconds: 6.0f);
    }
}
