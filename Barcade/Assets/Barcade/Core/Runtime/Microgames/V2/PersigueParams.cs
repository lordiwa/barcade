using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Constructor-injected tuning for <see cref="PersigueMicrogame"/> (GDD §4
    /// MECH_06). Plain engine-free POCO standing in for the not-yet-migrated v2
    /// <c>MicrogameDefinition.params</c> block (T-106) — same approach as
    /// <see cref="EsquivaParams"/>/<see cref="CorreParams"/>.
    ///
    /// <para>
    /// <b><see cref="SoloSeat"/> — [ASSUMED] decided OUTSIDE this params
    /// struct.</b> GDD's own round-robin-forced-solo-role text
    /// ("nunca lo elige el juego dos veces seguidas") is a SESSION-level
    /// decision (see <see cref="SoloSeatRotation"/>'s own doc for the full
    /// rationale) — by the time a round's <see cref="PersigueParams"/> is
    /// constructed, "which seat is solo" is already decided data, the same way
    /// every other mechanic-specific tuning value here is data, not something
    /// this mechanic itself chooses.
    /// </para>
    ///
    /// <para>
    /// <b>Mobility-only balance (GDD §4 hard design rule, AC6).</b>
    /// <see cref="SoloSpeedBonus"/>/<see cref="SoloDashDistance"/> are the ONLY
    /// two numbers that differ by role; <see cref="TrioDashDistance"/> and
    /// <see cref="BaseAvatarSpeed"/> are shared. <see cref="PersigueMicrogame"/>
    /// reads these through one pair of "effective speed / effective dash
    /// distance for seat i" lookups, never a role-conditioned rule branch — see
    /// that class's own doc for the structural proof.
    /// </para>
    /// </summary>
    public readonly struct PersigueParams
    {
        /// <summary>Seat index (0..3) that is solo this round — decided by <see cref="SoloSeatRotation"/>, not by this mechanic.</summary>
        public readonly int SoloSeat;

        /// <summary>GDD "mode": soloHunts or soloFlees.</summary>
        public readonly PersigueMode Mode;

        /// <summary>GDD "arenaLayout": which of the 4 fixed cover layouts is active.</summary>
        public readonly PersigueArenaLayout ArenaLayout;

        /// <summary>
        /// [ASSUMED] Trio/base movement speed, logical units/second — GDD names
        /// no explicit value for MECH_06 (same gap as Esquiva's AvatarSpeed).
        /// </summary>
        public readonly float BaseAvatarSpeed;

        /// <summary>GDD "soloSpeedBonus": fractional speed bonus for the solo seat only (GDD literal +15% => 0.15).</summary>
        public readonly float SoloSpeedBonus;

        /// <summary>GDD "dashCooldown": exact seconds between dashes, same for every seat (GDD literal 1.5).</summary>
        public readonly float DashCooldownSeconds;

        /// <summary>Instantaneous dash travel distance (logical units) for a trio seat.</summary>
        public readonly float TrioDashDistance;

        /// <summary>GDD "dashDistance": the solo seat's (longer-range) dash distance — the mobility-only compensation, alongside <see cref="SoloSpeedBonus"/>.</summary>
        public readonly float SoloDashDistance;

        /// <summary>[ASSUMED] Player collision circle radius — matches Esquiva's AvatarRadius default; identical for every seat (AC6).</summary>
        public readonly float AvatarRadius;

        /// <summary>[ASSUMED] Cover obstacle collision circle radius.</summary>
        public readonly float ObstacleRadius;

        /// <summary>Round length in seconds — must be within the TASK-030 validator's GDD §11.1 [3,8] bound.</summary>
        public readonly float DurationSeconds;

        public PersigueParams(
            int soloSeat,
            PersigueMode mode,
            PersigueArenaLayout arenaLayout,
            float baseAvatarSpeed,
            float soloSpeedBonus,
            float dashCooldownSeconds,
            float trioDashDistance,
            float soloDashDistance,
            float avatarRadius,
            float obstacleRadius,
            float durationSeconds)
        {
            if (soloSeat < 0 || soloSeat > 3)
                throw new ArgumentOutOfRangeException(nameof(soloSeat), "must be a seat index 0..3.");
            if (baseAvatarSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(baseAvatarSpeed), "must be positive.");
            if (soloSpeedBonus < 0f)
                throw new ArgumentOutOfRangeException(nameof(soloSpeedBonus), "must be non-negative.");
            if (dashCooldownSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(dashCooldownSeconds), "must be positive.");
            if (trioDashDistance <= 0f)
                throw new ArgumentOutOfRangeException(nameof(trioDashDistance), "must be positive.");
            if (soloDashDistance <= 0f)
                throw new ArgumentOutOfRangeException(nameof(soloDashDistance), "must be positive.");
            if (avatarRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(avatarRadius), "must be positive.");
            if (obstacleRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(obstacleRadius), "must be positive.");
            if (durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "must be positive.");

            SoloSeat = soloSeat;
            Mode = mode;
            ArenaLayout = arenaLayout;
            BaseAvatarSpeed = baseAvatarSpeed;
            SoloSpeedBonus = soloSpeedBonus;
            DashCooldownSeconds = dashCooldownSeconds;
            TrioDashDistance = trioDashDistance;
            SoloDashDistance = soloDashDistance;
            AvatarRadius = avatarRadius;
            ObstacleRadius = obstacleRadius;
            DurationSeconds = durationSeconds;
        }

        /// <summary>
        /// GDD-plausible baseline calibration: BaseAvatarSpeed matches Esquiva's
        /// AvatarSpeed (0.35 units/s); SoloSpeedBonus is the GDD literal +15%;
        /// DashCooldownSeconds is the GDD literal 1.5s; dash distances are
        /// engineering defaults with the solo's comfortably longer, per GDD's
        /// "dash de mayor alcance."
        /// </summary>
        public static PersigueParams GddDefaults(int soloSeat, PersigueMode mode) => new PersigueParams(
            soloSeat: soloSeat,
            mode: mode,
            arenaLayout: PersigueArenaLayout.Cross,
            baseAvatarSpeed: 0.35f,
            soloSpeedBonus: 0.15f,
            dashCooldownSeconds: 1.5f,
            trioDashDistance: 0.12f,
            soloDashDistance: 0.20f,
            avatarRadius: 0.03f,
            obstacleRadius: 0.08f,
            durationSeconds: 6.0f);
    }
}
