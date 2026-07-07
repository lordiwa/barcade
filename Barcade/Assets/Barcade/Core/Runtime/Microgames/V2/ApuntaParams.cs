using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Constructor-injected tuning for <see cref="ApuntaMicrogame"/> (GDD §4
    /// MECH_04, §11.1 example). Plain engine-free POCO standing in for the
    /// not-yet-migrated v2 <c>MicrogameDefinition.params</c> block (T-106) — same
    /// approach as <see cref="ReaccionaParams"/>.
    ///
    /// Several fields are GDD gaps filled with documented assumptions (see
    /// <see cref="ApuntaMicrogame"/>'s class doc for the full geometric
    /// derivation): <see cref="HitRadius"/>, <see cref="CentralZoneMin"/>/
    /// <see cref="CentralZoneMax"/>, and <see cref="ProjectileFlightSeconds"/> are
    /// not named anywhere in the GDD text, only implied by "objetivos en zona
    /// central" and "proyectil balístico."
    /// </summary>
    public readonly struct ApuntaParams
    {
        /// <summary>Full period of the oscillating charge meter, in seconds (GDD §4: 1.2).</summary>
        public readonly float ChargeCycleSeconds;

        /// <summary>Number of targets spawned in the central zone (GDD §11.1 example: 3).</summary>
        public readonly int TargetCount;

        /// <summary>Whether targets drift and bounce within the central zone (GDD §4 "targetMoving").</summary>
        public readonly bool TargetMovingEnabled;

        /// <summary>Target movement speed, logical units/second, only used when <see cref="TargetMovingEnabled"/> (GDD §11.1 example: 0.15).</summary>
        public readonly float TargetMovingSpeed;

        /// <summary>
        /// Deterministic +X landing-point drift, scaled by shot distance (GDD §4
        /// "windAccel" variant; GDD §11.1 example: 0.05). 0 disables wind.
        /// </summary>
        public readonly float WindAccel;

        /// <summary>Landing distance (logical units) at 0 charge power (GDD §4 "projectileSpeedMin").</summary>
        public readonly float ProjectileSpeedMin;

        /// <summary>Landing distance (logical units) at full (1.0) charge power (GDD §4 "projectileSpeedMax").</summary>
        public readonly float ProjectileSpeedMax;

        /// <summary>
        /// Fixed flight duration (seconds) every projectile takes to arrive,
        /// regardless of its landing distance — an engineering addition (GDD gap);
        /// see <see cref="ApuntaMicrogame"/> class doc.
        /// </summary>
        public readonly float ProjectileFlightSeconds;

        /// <summary>Round length in seconds (GDD §11.1 example: 5.0).</summary>
        public readonly float DurationSeconds;

        /// <summary>Max distance from a target's center for a landing point to count as a hit (GDD gap, engineering addition).</summary>
        public readonly float HitRadius;

        /// <summary>Lower bound (both axes) of the square central zone targets spawn/move within (GDD gap).</summary>
        public readonly float CentralZoneMin;

        /// <summary>Upper bound (both axes) of the square central zone targets spawn/move within (GDD gap).</summary>
        public readonly float CentralZoneMax;

        /// <summary>Tuning for the internal <see cref="InputInterpreter"/> instance used for hold/press-edge detection.</summary>
        public readonly InputInterpreterConfig InputConfig;

        public ApuntaParams(
            float chargeCycleSeconds,
            int targetCount,
            bool targetMovingEnabled,
            float targetMovingSpeed,
            float windAccel,
            float projectileSpeedMin,
            float projectileSpeedMax,
            float projectileFlightSeconds,
            float durationSeconds,
            float hitRadius,
            float centralZoneMin,
            float centralZoneMax,
            InputInterpreterConfig inputConfig)
        {
            if (chargeCycleSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(chargeCycleSeconds), "must be positive.");
            if (targetCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetCount), "must be positive.");
            if (targetMovingSpeed < 0f)
                throw new ArgumentOutOfRangeException(nameof(targetMovingSpeed), "must be non-negative.");
            if (projectileSpeedMin < 0f)
                throw new ArgumentOutOfRangeException(nameof(projectileSpeedMin), "must be non-negative.");
            if (projectileSpeedMax < projectileSpeedMin)
                throw new ArgumentOutOfRangeException(nameof(projectileSpeedMax), "must be greater than or equal to projectileSpeedMin.");
            if (projectileFlightSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(projectileFlightSeconds), "must be positive.");
            if (durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "must be positive.");
            if (hitRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(hitRadius), "must be positive.");
            if (centralZoneMax < centralZoneMin)
                throw new ArgumentOutOfRangeException(nameof(centralZoneMax), "must be greater than or equal to centralZoneMin.");
            if (centralZoneMin < 0f || centralZoneMax > 1f)
                throw new ArgumentOutOfRangeException(nameof(centralZoneMin), "central zone must be within [0,1].");

            ChargeCycleSeconds = chargeCycleSeconds;
            TargetCount = targetCount;
            TargetMovingEnabled = targetMovingEnabled;
            TargetMovingSpeed = targetMovingSpeed;
            WindAccel = windAccel;
            ProjectileSpeedMin = projectileSpeedMin;
            ProjectileSpeedMax = projectileSpeedMax;
            ProjectileFlightSeconds = projectileFlightSeconds;
            DurationSeconds = durationSeconds;
            HitRadius = hitRadius;
            CentralZoneMin = centralZoneMin;
            CentralZoneMax = centralZoneMax;
            InputConfig = inputConfig;
        }

        /// <summary>
        /// GDD baseline calibration (no wind, stationary targets — the "plain"
        /// variant; wind/moving targets are opt-in per GDD §4's own "variantes"
        /// framing, mirroring <see cref="ReaccionaParams.GddDefaults"/>'s choice
        /// of fakeouts=0). Central-zone/hitRadius/speed values are chosen and
        /// verified (see <c>Reachability_EveryTargetReachableFromEveryCorner_AcrossManySeeds</c>)
        /// so every seeded target is reachable from all 4 corners.
        /// </summary>
        public static ApuntaParams GddDefaults => new ApuntaParams(
            chargeCycleSeconds: 1.2f,
            targetCount: 3,
            targetMovingEnabled: false,
            targetMovingSpeed: 0.15f,
            windAccel: 0f,
            projectileSpeedMin: 0.45f,
            projectileSpeedMax: 0.95f,
            projectileFlightSeconds: 0.3f,
            durationSeconds: 5.0f,
            hitRadius: 0.18f,
            centralZoneMin: 0.4f,
            centralZoneMax: 0.6f,
            inputConfig: InputInterpreterConfig.GddDefaults);
    }
}
