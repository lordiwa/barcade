using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Constructor-injected tuning for <see cref="MantenMicrogame"/> (GDD §4
    /// MECH_01, "Parámetros (dato): gravityFactor, perturbAmplitude0,
    /// perturbRamp, torqueGain, thetaMax, duration"). Plain engine-free POCO
    /// standing in for the not-yet-migrated v2 <c>MicrogameDefinition.params</c>
    /// block (T-106) -- same approach as <see cref="ReaccionaParams"/>/
    /// <see cref="ApuntaParams"/>/<see cref="EsquivaParams"/>.
    ///
    /// <see cref="ThetaMaxDegrees"/> is stored in DEGREES (matching the GDD's own
    /// literal "default 35°" framing and how a definition author would naturally
    /// write it), the same convention <see cref="EsquivaMicrogame.HomingTurnRateDegPerSec"/>
    /// already established for its own degree-valued GDD literal -- converted to
    /// radians once, at <see cref="MantenMicrogame.Initialize"/> time (see
    /// <see cref="MantenMicrogame.DegreesToRadians"/>).
    ///
    /// <see cref="Damping"/> is an [ASSUMED] deviation from the GDD's literal,
    /// undamped ODE (θ'' = (g/L)·sin(θ) + perturbación(t) − k·input_palanca has
    /// no damping term) -- human-ruled 2026-07-07: kept small for Euler
    /// integration stability, the same role <see cref="Barcade.Core.Alcocheck.AlcocheckSim"/>'s
    /// own damping term already plays for the identical inverted-pendulum
    /// integration pattern. See <see cref="MantenMicrogame"/>'s class doc for the
    /// full ruling.
    /// </summary>
    public readonly struct MantenParams
    {
        /// <summary>GDD: gravityFactor (g/L) -- the inverted-pendulum instability coefficient.</summary>
        public readonly float GravityFactor;

        /// <summary>GDD: perturbAmplitude0 (A0) -- the perturbation amplitude at t=0.</summary>
        public readonly float PerturbAmplitude0;

        /// <summary>GDD: perturbRamp -- amplitude growth per second, A(t) = A0 + perturbRamp·t.</summary>
        public readonly float PerturbRamp;

        /// <summary>GDD: torqueGain (k) -- corrective torque per unit of input_palanca.</summary>
        public readonly float TorqueGain;

        /// <summary>GDD: thetaMax, in degrees (GDD literal default: 35). Fail threshold is strict |theta| &gt; thetaMax.</summary>
        public readonly float ThetaMaxDegrees;

        /// <summary>[ASSUMED] Euler-integration-stability damping -- see class doc.</summary>
        public readonly float Damping;

        /// <summary>GDD: duration, in seconds (GDD's own note for this mechanic: 5-8s; falls within the general §11.1 [3,8] validator range).</summary>
        public readonly float DurationSeconds;

        /// <summary>
        /// GDD names "botón sin uso (o dash de recuperación con cooldown 2 s,
        /// variante)" as a per-definition VARIANT. [ASSUMED SCOPE] out of scope
        /// for this ticket -- no AC names dash/recovery behavior, and this
        /// mechanic does not read <see cref="PlayerInput.Button"/> at all.
        /// Declared for definition/schema completeness only, mirroring
        /// <see cref="EsquivaParams.JumpEnabled"/>'s identical status.
        /// </summary>
        public readonly bool DashRecoveryEnabled;

        public MantenParams(
            float gravityFactor,
            float perturbAmplitude0,
            float perturbRamp,
            float torqueGain,
            float thetaMaxDegrees,
            float damping,
            float durationSeconds,
            bool dashRecoveryEnabled)
        {
            if (gravityFactor < 0f)
                throw new ArgumentOutOfRangeException(nameof(gravityFactor), "must be non-negative.");
            if (perturbAmplitude0 < 0f)
                throw new ArgumentOutOfRangeException(nameof(perturbAmplitude0), "must be non-negative.");
            if (perturbRamp < 0f)
                throw new ArgumentOutOfRangeException(nameof(perturbRamp), "must be non-negative.");
            if (torqueGain < 0f)
                throw new ArgumentOutOfRangeException(nameof(torqueGain), "must be non-negative.");
            if (thetaMaxDegrees <= 0f)
                throw new ArgumentOutOfRangeException(nameof(thetaMaxDegrees), "must be positive.");
            if (damping < 0f)
                throw new ArgumentOutOfRangeException(nameof(damping), "must be non-negative.");
            if (durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "must be positive.");

            GravityFactor = gravityFactor;
            PerturbAmplitude0 = perturbAmplitude0;
            PerturbRamp = perturbRamp;
            TorqueGain = torqueGain;
            ThetaMaxDegrees = thetaMaxDegrees;
            Damping = damping;
            DurationSeconds = durationSeconds;
            DashRecoveryEnabled = dashRecoveryEnabled;
        }

        /// <summary>
        /// GDD-gap engineering defaults. Calibrated (see
        /// <c>MantenMicrogameV2Tests</c>'s AC sweeps) so that, across many
        /// seeds: (a) with null input every pendulum reliably falls well before
        /// <see cref="DurationSeconds"/>, and (b) a reactive PD-style
        /// full-authority correction oracle reliably survives the full duration
        /// with a comfortable torque-authority margin over the worst-case
        /// combined gravity+perturbation disturbance at t=DurationSeconds.
        /// DurationSeconds sits at the top of GDD's own "5-8s" note for this
        /// mechanic specifically -- the extra time (vs. a shorter round) is what
        /// makes the null-input-always-falls guarantee reliable across seeds
        /// (an unstable equilibrium kicked by a small early noise draw still
        /// needs enough time to visibly diverge).
        /// </summary>
        public static MantenParams GddDefaults => new MantenParams(
            gravityFactor: 3.0f,
            perturbAmplitude0: 1.2f,
            perturbRamp: 0.3f,
            torqueGain: 10.0f,
            thetaMaxDegrees: 35.0f,
            damping: 1.2f,
            durationSeconds: 8.0f,
            dashRecoveryEnabled: false);
    }
}
