using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Constructor-injected tuning for <see cref="ReaccionaMicrogame"/> (GDD §4
    /// MECH_05). Plain engine-free POCO — the orchestrator's note for this ticket
    /// is that the v2 <c>MicrogameDefinition</c> data migration is a separate
    /// future ticket (T-106); this struct stands in for the eventual
    /// <c>definition.params</c> block with GDD defaults, no ScriptableObject
    /// involved.
    /// </summary>
    public readonly struct ReaccionaParams
    {
        /// <summary>Maximum number of visual fakeouts the mechanic can schedule per tanda (GDD: 0..2).</summary>
        public const int MaxFakeouts = 2;

        /// <summary>0..2 visual fakeouts shown before the real signal (GDD §4 "amagos visuales").</summary>
        public readonly int Fakeouts;

        /// <summary>Best-of-1 or best-of-3 tandas within the microgame (GDD §4 "rounds").</summary>
        public readonly int Rounds;

        /// <summary>Lower bound of the uniform signal-delay distribution, in seconds (GDD §4: 1.5).</summary>
        public readonly float SignalDelayMinSeconds;

        /// <summary>Upper bound of the uniform signal-delay distribution, in seconds (GDD §4: 4.5).</summary>
        public readonly float SignalDelayMaxSeconds;

        /// <summary>
        /// Latencies strictly below this many ticks after the signal are anticipation,
        /// not a genuine reaction (GDD §4: 90 ms ⇒ 5 ticks @ 60 Hz) and are converted
        /// to a false start.
        /// </summary>
        public readonly int AnticipationThresholdTicks;

        /// <summary>
        /// Per-seat cutoff, in seconds after the true signal, beyond which a seat
        /// that never pressed is resolved as "did not react" so the tanda can always
        /// conclude. Not specified numerically by GDD §4 (which only bounds the
        /// *pre*-signal wait) — an engineering addition documented as an assumption
        /// for the reviewer; see ReaccionaMicrogame's class doc.
        /// </summary>
        public readonly float ReactionTimeoutSeconds;

        /// <summary>Tuning for the internal <see cref="InputInterpreter"/> instance used for press-edge detection.</summary>
        public readonly InputInterpreterConfig InputConfig;

        public ReaccionaParams(
            int fakeouts,
            int rounds,
            float signalDelayMinSeconds,
            float signalDelayMaxSeconds,
            int anticipationThresholdTicks,
            float reactionTimeoutSeconds,
            InputInterpreterConfig inputConfig)
        {
            if (fakeouts < 0 || fakeouts > MaxFakeouts)
                throw new ArgumentOutOfRangeException(nameof(fakeouts), "must be in [0, " + MaxFakeouts + "].");
            if (rounds != 1 && rounds != 3)
                throw new ArgumentOutOfRangeException(nameof(rounds), "must be 1 or 3 (best-of-1 or best-of-3).");
            if (signalDelayMinSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(signalDelayMinSeconds), "must be positive.");
            if (signalDelayMaxSeconds <= signalDelayMinSeconds)
                throw new ArgumentOutOfRangeException(nameof(signalDelayMaxSeconds), "must be greater than signalDelayMinSeconds.");
            if (anticipationThresholdTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(anticipationThresholdTicks), "must be non-negative.");
            if (reactionTimeoutSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(reactionTimeoutSeconds), "must be positive.");

            Fakeouts = fakeouts;
            Rounds = rounds;
            SignalDelayMinSeconds = signalDelayMinSeconds;
            SignalDelayMaxSeconds = signalDelayMaxSeconds;
            AnticipationThresholdTicks = anticipationThresholdTicks;
            ReactionTimeoutSeconds = reactionTimeoutSeconds;
            InputConfig = inputConfig;
        }

        /// <summary>GDD §4 default calibration: no fakeouts, best-of-1, 1.5-4.5s signal window, 5-tick (90ms) anticipation threshold, 3s per-seat reaction timeout.</summary>
        public static ReaccionaParams GddDefaults => new ReaccionaParams(
            fakeouts: 0,
            rounds: 1,
            signalDelayMinSeconds: 1.5f,
            signalDelayMaxSeconds: 4.5f,
            anticipationThresholdTicks: 5,
            reactionTimeoutSeconds: 3.0f,
            inputConfig: InputInterpreterConfig.GddDefaults);
    }
}
