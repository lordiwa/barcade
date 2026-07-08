using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Constructor-injected tuning for <see cref="IgualaMicrogame"/> (GDD §4
    /// MECH_09, `colorRelay` coop mode only — see that class's doc for why the
    /// `simonSolo` competitive variant is out of scope for this ticket). Plain
    /// engine-free POCO — same approach as <see cref="SujetaParams"/>.
    ///
    /// <para>
    /// <b><see cref="ReactWindowFloorSeconds"/> is a fixed GDD constant, not a
    /// per-definition field.</b> GDD names it as a physiological reaction-time
    /// floor ("límite de reacción humana con margen") — the same treatment
    /// <see cref="BombardeaParams.MinTelegraphSeconds"/> already gives a
    /// fairness/safety floor: a hard-coded guarantee, not something remote
    /// recalibration should be able to loosen. <see cref="ReactWindow0"/> and
    /// <see cref="WindowDecay"/> ARE data (GDD names both as `Parámetros`); the
    /// floor they decay toward is not.
    /// </para>
    /// </summary>
    public readonly struct IgualaParams
    {
        /// <summary>GDD "sequenceLength": number of colored symbols in the relay.</summary>
        public readonly int SequenceLength;

        /// <summary>GDD "reactWindow0": the base reaction window in seconds at the relay's start (GDD literal 0.9).</summary>
        public readonly float ReactWindow0;

        /// <summary>GDD "windowDecay": seconds subtracted from the window per relay step (GDD literal 0.05).</summary>
        public readonly float WindowDecay;

        /// <summary>GDD literal: the reaction window never drops below this, regardless of decay ("límite de reacción humana con margen").</summary>
        public const float ReactWindowFloorSeconds = 0.45f;

        /// <summary>Round length in seconds — must be within the TASK-030 validator's GDD §11.1 [3,8] bound.</summary>
        public readonly float DurationSeconds;

        public IgualaParams(
            int sequenceLength,
            float reactWindow0,
            float windowDecay,
            float durationSeconds)
        {
            if (sequenceLength < 1)
                throw new ArgumentOutOfRangeException(nameof(sequenceLength), "must be at least 1.");
            if (reactWindow0 <= 0f)
                throw new ArgumentOutOfRangeException(nameof(reactWindow0), "must be positive.");
            if (windowDecay < 0f)
                throw new ArgumentOutOfRangeException(nameof(windowDecay), "must be non-negative.");
            if (durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "must be positive.");

            SequenceLength = sequenceLength;
            ReactWindow0 = reactWindow0;
            WindowDecay = windowDecay;
            DurationSeconds = durationSeconds;
        }

        /// <summary>
        /// GDD-literal baseline calibration: ReactWindow0/WindowDecay are the
        /// GDD literals (0.9/0.05). SequenceLength is [ASSUMED] — GDD names the
        /// parameter but gives no default for colorRelay (the "3-5 símbolos"
        /// figure it DOES give is for the separate simonSolo variant).
        ///
        /// rev-review LOW-1 correction: with SequenceLength=8, the per-symbol
        /// index n used by <see cref="IgualaMicrogame.ReactWindowSeconds"/> only
        /// ever ranges 0..7 (n = Progress, and Progress never reaches 8 while a
        /// window is still being computed) — the window at the LAST symbol is
        /// reactWindow(7) = 0.9 - 0.05*7 = 0.55s. This default calibration
        /// never actually decays down to the 0.45s floor; the floor is first
        /// reached at n=9, which requires SequenceLength >= 10. 8 was chosen
        /// only to fit the validator's [3,8]s duration bound at a reasonable
        /// pace, not because it exercises the floor — see
        /// <c>ReactWindowSeconds_DecaysLinearly_ThenFloorsExactlyAt045</c>
        /// (which tests the pure formula directly, at longer explicit n values)
        /// for the floor-boundary proof itself.
        /// </summary>
        public static IgualaParams GddDefaults => new IgualaParams(
            sequenceLength: 8,
            reactWindow0: 0.9f,
            windowDecay: 0.05f,
            durationSeconds: 8.0f);
    }
}
