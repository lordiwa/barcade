using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Constructor-injected tuning for <see cref="EsquivaMicrogame"/> (GDD §4
    /// MECH_02). Plain engine-free POCO standing in for the not-yet-migrated v2
    /// <c>MicrogameDefinition.params</c> block (T-106) — same approach as
    /// <see cref="ReaccionaParams"/>/<see cref="ApuntaParams"/>.
    ///
    /// <see cref="AvatarSpeed"/>, <see cref="AvatarRadius"/> and
    /// <see cref="HazardRadius"/> are GDD gaps filled with documented
    /// [ASSUMED] engineering defaults — GDD §4 names <c>avatarRadius=0.03</c>
    /// literally but never names a player movement speed or a hazard radius (only
    /// "AABB/círculo del peligro", not a size). See
    /// <see cref="EsquivaMicrogame"/>'s class doc for the full rationale,
    /// including why <see cref="AvatarSpeed"/> is set comfortably above
    /// <see cref="HazardSpeed"/> (the escapability guarantee's real margin).
    /// </summary>
    public readonly struct EsquivaParams
    {
        /// <summary>GDD "spawnRatePerSec" base rate r0 (hazards/second at t=0).</summary>
        public readonly float SpawnRateBasePerSec;

        /// <summary>GDD "spawnRatePerSec" ramp coefficient: spawnRate(t) = r0 * (1 + rampCoef * t).</summary>
        public readonly float SpawnRampCoef;

        /// <summary>Hazard travel speed, logical units/second, in the normalized [0,1]² arena.</summary>
        public readonly float HazardSpeed;

        /// <summary>Which of the 4 GDD-named patterns this definition's hazards follow.</summary>
        public readonly EsquivaHazardPattern HazardPattern;

        /// <summary>
        /// [ASSUMED] Player movement speed, logical units/second — GDD names no
        /// value for MECH_02 (unlike MECH_01's torqueGain, this mechanic's
        /// "Parámetros" list never names a player-speed field). Set well above
        /// <see cref="HazardSpeed"/> so the escapability guarantee has real margin.
        /// </summary>
        public readonly float AvatarSpeed;

        /// <summary>GDD §4 literal: player collision circle radius (0.03).</summary>
        public readonly float AvatarRadius;

        /// <summary>[ASSUMED] Hazard collision circle radius — GDD names the shape ("AABB/círculo") but not a size; matched to <see cref="AvatarRadius"/> for symmetry.</summary>
        public readonly float HazardRadius;

        /// <summary>Round length in seconds — must be within the TASK-030 validator's GDD §11.1 [3,8] bound.</summary>
        public readonly float DurationSeconds;

        /// <summary>
        /// GDD "jumpEnabled" — declared for definition/schema completeness only.
        /// [ASSUMED SCOPE] The jump/dash mechanic itself ("botón tap (salto/dash
        /// corto, según variante)") is a GDD-named VARIANT, not part of MECH_02's
        /// core rule (one hit = eliminated; escapability is guaranteed by movement
        /// alone via Homing_soft's turn cap, not by jumping). Out of scope for this
        /// slice — no AC names it, and every hazard pattern is escapable by stick
        /// movement alone. This field exists so a definition can still declare the
        /// param name without the validator treating it as unexpected; the
        /// mechanic does not read it yet.
        /// </summary>
        public readonly bool JumpEnabled;

        public EsquivaParams(
            float spawnRateBasePerSec,
            float spawnRampCoef,
            float hazardSpeed,
            EsquivaHazardPattern hazardPattern,
            float avatarSpeed,
            float avatarRadius,
            float hazardRadius,
            float durationSeconds,
            bool jumpEnabled)
        {
            if (spawnRateBasePerSec <= 0f) throw new ArgumentOutOfRangeException(nameof(spawnRateBasePerSec), "must be positive.");
            if (spawnRampCoef < 0f) throw new ArgumentOutOfRangeException(nameof(spawnRampCoef), "must be non-negative.");
            if (hazardSpeed < 0f) throw new ArgumentOutOfRangeException(nameof(hazardSpeed), "must be non-negative.");
            if (avatarSpeed <= 0f) throw new ArgumentOutOfRangeException(nameof(avatarSpeed), "must be positive.");
            if (avatarRadius <= 0f) throw new ArgumentOutOfRangeException(nameof(avatarRadius), "must be positive.");
            if (hazardRadius <= 0f) throw new ArgumentOutOfRangeException(nameof(hazardRadius), "must be positive.");
            if (durationSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(durationSeconds), "must be positive.");

            SpawnRateBasePerSec = spawnRateBasePerSec;
            SpawnRampCoef = spawnRampCoef;
            HazardSpeed = hazardSpeed;
            HazardPattern = hazardPattern;
            AvatarSpeed = avatarSpeed;
            AvatarRadius = avatarRadius;
            HazardRadius = hazardRadius;
            DurationSeconds = durationSeconds;
            JumpEnabled = jumpEnabled;
        }

        /// <summary>
        /// GDD-plausible baseline calibration. AvatarSpeed (0.35 units/s) is set
        /// to a healthy multiple of HazardSpeed (0.15 units/s) — see
        /// <see cref="EsquivaMicrogame"/>'s escapability discussion — so a
        /// reactive avoidance policy has real margin to dodge every pattern,
        /// including Homing_soft's 30°/s-capped pursuit.
        /// </summary>
        public static EsquivaParams GddDefaults => new EsquivaParams(
            spawnRateBasePerSec: 0.6f,
            spawnRampCoef: 0.15f,
            hazardSpeed: 0.15f,
            hazardPattern: EsquivaHazardPattern.Rain,
            avatarSpeed: 0.35f,
            avatarRadius: 0.03f,
            hazardRadius: 0.03f,
            durationSeconds: 5.0f,
            jumpEnabled: false);
    }
}
