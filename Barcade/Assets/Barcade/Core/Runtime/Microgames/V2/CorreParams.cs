using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Constructor-injected tuning for <see cref="CorreMicrogame"/> (GDD §4
    /// MECH_03, "Parámetros (dato): vBase, vGain, obstacleDensity, stunSeconds,
    /// raceToFinish (bool), rubberBandPct, duration"). Plain engine-free POCO
    /// standing in for the not-yet-migrated v2 <c>MicrogameDefinition.params</c>
    /// block (T-106) -- same approach as <see cref="EsquivaParams"/>/
    /// <see cref="MantenParams"/>/<see cref="ApuntaParams"/>.
    ///
    /// <para>
    /// <b>[ASSUMED] Engineering fields the GDD does not name.</b>
    /// <see cref="JumpAirtimeSeconds"/>, <see cref="MinObstacleGap"/> and
    /// <see cref="FirstObstacleDistance"/> are gap-fills: GDD §4 specifies the
    /// jump/stun/obstacle mechanic but not the jump's duration, a spacing floor, or
    /// where the track's first obstacle sits. See <see cref="CorreMicrogame"/>'s
    /// class doc for the jumpability rationale (<see cref="MinObstacleGap"/> is set
    /// above the worst-case airborne span so a perfectly-timed single jump always
    /// clears each obstacle with grounded room to re-time before the next).
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED SCOPE] <see cref="RaceToFinish"/> is declared, not read.</b> GDD
    /// §4 names "primero en cruzar meta (variante raceToFinish)" as a per-definition
    /// VARIANT. This T-107 Core slice implements only the baseline "mayor distancia
    /// al agotar el tiempo" race; the finish-line variant is out of scope (no AC
    /// names it). The field exists for definition/schema completeness -- the same
    /// declared-but-unread status as <see cref="EsquivaParams.JumpEnabled"/> and
    /// <see cref="MantenParams.DashRecoveryEnabled"/> -- and the mechanic does not
    /// read it yet.
    /// </para>
    /// </summary>
    public readonly struct CorreParams
    {
        /// <summary>GDD: vBase -- base run speed (logical units/second) at mashNorm 0.</summary>
        public readonly float VBase;

        /// <summary>GDD: vGain -- extra speed per unit of mashNorm, v = vBase + vGain·mashNorm.</summary>
        public readonly float VGain;

        /// <summary>GDD: obstacleDensity -- average obstacles per logical distance unit (floored by <see cref="MinObstacleGap"/>).</summary>
        public readonly float ObstacleDensity;

        /// <summary>GDD: stunSeconds -- stun duration on a grounded obstacle hit (GDD literal 0.6). No elimination.</summary>
        public readonly float StunSeconds;

        /// <summary>GDD: rubberBandPct -- fraction added to the last-place seat's vBase (GDD literal 0.08 = +8%).</summary>
        public readonly float RubberBandPct;

        /// <summary>GDD: duration, in seconds.</summary>
        public readonly float DurationSeconds;

        /// <summary>
        /// GDD: raceToFinish. [ASSUMED SCOPE] declared for schema completeness only
        /// -- not read by this Core slice (see class doc), mirroring
        /// <see cref="EsquivaParams.JumpEnabled"/>.
        /// </summary>
        public readonly bool RaceToFinish;

        /// <summary>[ASSUMED] Airborne duration of one jump, seconds -- GDD gap.</summary>
        public readonly float JumpAirtimeSeconds;

        /// <summary>[ASSUMED] Minimum distance between consecutive obstacles, the jumpability floor -- GDD gap. See <see cref="CorreMicrogame"/>.</summary>
        public readonly float MinObstacleGap;

        /// <summary>[ASSUMED] Distance of the track's first obstacle, giving the mash window room to ramp before it matters -- GDD gap.</summary>
        public readonly float FirstObstacleDistance;

        /// <summary>Tuning for the internal <see cref="InputInterpreter"/> used for the §3.3 mash curve and stick reads.</summary>
        public readonly InputInterpreterConfig InputConfig;

        public CorreParams(
            float vBase,
            float vGain,
            float obstacleDensity,
            float stunSeconds,
            float rubberBandPct,
            float durationSeconds,
            bool raceToFinish,
            float jumpAirtimeSeconds,
            float minObstacleGap,
            float firstObstacleDistance,
            InputInterpreterConfig inputConfig)
        {
            if (vBase <= 0f) throw new ArgumentOutOfRangeException(nameof(vBase), "must be positive.");
            if (vGain < 0f) throw new ArgumentOutOfRangeException(nameof(vGain), "must be non-negative.");
            if (obstacleDensity <= 0f) throw new ArgumentOutOfRangeException(nameof(obstacleDensity), "must be positive.");
            if (stunSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(stunSeconds), "must be non-negative.");
            if (rubberBandPct < 0f) throw new ArgumentOutOfRangeException(nameof(rubberBandPct), "must be non-negative.");
            if (durationSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(durationSeconds), "must be positive.");
            if (jumpAirtimeSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(jumpAirtimeSeconds), "must be positive.");
            if (minObstacleGap <= 0f) throw new ArgumentOutOfRangeException(nameof(minObstacleGap), "must be positive.");
            if (firstObstacleDistance < 0f) throw new ArgumentOutOfRangeException(nameof(firstObstacleDistance), "must be non-negative.");

            VBase = vBase;
            VGain = vGain;
            ObstacleDensity = obstacleDensity;
            StunSeconds = stunSeconds;
            RubberBandPct = rubberBandPct;
            DurationSeconds = durationSeconds;
            RaceToFinish = raceToFinish;
            JumpAirtimeSeconds = jumpAirtimeSeconds;
            MinObstacleGap = minObstacleGap;
            FirstObstacleDistance = firstObstacleDistance;
            InputConfig = inputConfig;
        }

        /// <summary>
        /// GDD-plausible baseline calibration. <see cref="MinObstacleGap"/> (4.0) is
        /// set above the worst-case airborne span -- max velocity
        /// vBase·(1+rubberBandPct)+vGain = 7.24 units/s times
        /// <see cref="JumpAirtimeSeconds"/> 0.5 s ≈ 3.62 units -- so a single
        /// well-timed jump clears any one obstacle with grounded room to re-time
        /// before the next (see <c>CorreMicrogameV2Tests</c>'s perfect-jump sweep).
        /// <see cref="StunSeconds"/>/<see cref="RubberBandPct"/> are the GDD literals
        /// (0.6 s, +8%). Duration sits at the top of the validator's [3,8] range for
        /// a longer, better-sampled track.
        /// </summary>
        public static CorreParams GddDefaults => new CorreParams(
            vBase: 3.0f,
            vGain: 4.0f,
            obstacleDensity: 0.2f,
            stunSeconds: 0.6f,
            rubberBandPct: 0.08f,
            durationSeconds: 8.0f,
            raceToFinish: false,
            jumpAirtimeSeconds: 0.5f,
            minObstacleGap: 4.0f,
            firstObstacleDistance: 5.0f,
            inputConfig: InputInterpreterConfig.GddDefaults);
    }
}
