using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Constructor-injected tuning for <see cref="JefeMicrogame"/> (GDD §7.2,
    /// the asymmetric "Jefe" special phase). Plain engine-free POCO — same
    /// approach as <see cref="PersigueParams"/>/<see cref="BombardeaParams"/>.
    ///
    /// <para>
    /// <b><see cref="JefeSeat"/> is decided OUTSIDE this mechanic.</b> GDD:
    /// "un jugador (el líder del marcador)" — Core has no access to the real
    /// session ScoreModel (that lives at the Sequencer layer, out of this
    /// ticket's scope, same as GDD §7.2's own "castigo estructural suave al
    /// primero" framing and any tie-break rule among leaders). The caller
    /// resolves who the current score leader is and passes that seat in as
    /// data — the identical externalization
    /// <see cref="PersigueParams.SoloSeat"/> already established for MECH_06.
    /// </para>
    /// </summary>
    public readonly struct JefeParams
    {
        /// <summary>Seat index (0..3) that starts as Jefe (the score leader) — decided externally, must be an engaged (Human/Bot) seat.</summary>
        public readonly int JefeSeat;

        /// <summary>[ASSUMED] Uniform movement speed shared by every seat — GDD names no speed asymmetry for this phase (only "hitbox mayor" for the Jefe), so movement stays uniform.</summary>
        public readonly float AvatarSpeed;

        /// <summary>[ASSUMED] Baseline collision-circle radius (an attacker's hitbox; the Jefe's own is this times <see cref="JefeHitboxMultiplier"/>).</summary>
        public readonly float AvatarRadius;

        /// <summary>GDD literal: the Jefe's hitbox is +50% ("hitbox mayor... +50% vida" — the +50% figure is literally given for vida/hits (3 vs 2 base), reused here as the hitbox size multiplier per "hitbox mayor" — see class doc's [ASSUMED] note).</summary>
        public readonly float JefeHitboxMultiplier;

        /// <summary>[ASSUMED] Extra reach (beyond the two avatars' combined radii) an attacker's melee tap connects within.</summary>
        public readonly float MeleeRange;

        /// <summary>GDD literal: the Jefe's area attack telegraphs 0.6s before damage applies (P4).</summary>
        public readonly float TelegraphSeconds;

        /// <summary>[ASSUMED] Radius of the Jefe's area attack, centered on the Jefe's position at the moment it's triggered.</summary>
        public readonly float AoeBlastRadius;

        /// <summary>[ASSUMED] Minimum seconds between the Jefe's own area-attack triggers — GDD names no cadence number for this phase.</summary>
        public readonly float AoeCooldownSeconds;

        /// <summary>[ASSUMED] Seconds an attacker is stunned (frozen, no movement/attack) after being caught by the Jefe's area attack — GDD names the attack but not its consequence; modeled on CorreMicrogame's own stun-on-hit ("nunca elimina") precedent rather than any lives/elimination system.</summary>
        public readonly float AttackerStunSeconds;

        /// <summary>GDD literal: this phase runs exactly 75 seconds.</summary>
        public readonly float DurationSeconds;

        public JefeParams(
            int jefeSeat,
            float avatarSpeed,
            float avatarRadius,
            float jefeHitboxMultiplier,
            float meleeRange,
            float telegraphSeconds,
            float aoeBlastRadius,
            float aoeCooldownSeconds,
            float attackerStunSeconds,
            float durationSeconds)
        {
            if (jefeSeat < 0 || jefeSeat > 3)
                throw new ArgumentOutOfRangeException(nameof(jefeSeat), "must be a seat index 0..3.");
            if (avatarSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(avatarSpeed), "must be positive.");
            if (avatarRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(avatarRadius), "must be positive.");
            if (jefeHitboxMultiplier < 1f)
                throw new ArgumentOutOfRangeException(nameof(jefeHitboxMultiplier), "GDD: the Jefe's hitbox is bigger, never smaller.");
            if (meleeRange <= 0f)
                throw new ArgumentOutOfRangeException(nameof(meleeRange), "must be positive.");
            if (telegraphSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(telegraphSeconds), "must be positive.");
            if (aoeBlastRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(aoeBlastRadius), "must be positive.");
            if (aoeCooldownSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(aoeCooldownSeconds), "must be positive.");
            if (attackerStunSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(attackerStunSeconds), "must be positive.");
            if (durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "must be positive.");

            JefeSeat = jefeSeat;
            AvatarSpeed = avatarSpeed;
            AvatarRadius = avatarRadius;
            JefeHitboxMultiplier = jefeHitboxMultiplier;
            MeleeRange = meleeRange;
            TelegraphSeconds = telegraphSeconds;
            AoeBlastRadius = aoeBlastRadius;
            AoeCooldownSeconds = aoeCooldownSeconds;
            AttackerStunSeconds = attackerStunSeconds;
            DurationSeconds = durationSeconds;
        }

        /// <summary>
        /// GDD-literal baseline: JefeHitboxMultiplier (1.5), TelegraphSeconds
        /// (0.6), and DurationSeconds (75) are GDD literals. Everything else
        /// is an engineering default in the same gap-fill style as every
        /// other v2 mechanic's own GddDefaults.
        /// </summary>
        public static JefeParams GddDefaults(int jefeSeat) => new JefeParams(
            jefeSeat: jefeSeat,
            avatarSpeed: 0.35f,
            avatarRadius: 0.03f,
            jefeHitboxMultiplier: 1.5f,
            meleeRange: 0.05f,
            telegraphSeconds: 0.6f,
            aoeBlastRadius: 0.15f,
            aoeCooldownSeconds: 2.0f,
            attackerStunSeconds: 0.5f,
            durationSeconds: 75f);
    }
}
