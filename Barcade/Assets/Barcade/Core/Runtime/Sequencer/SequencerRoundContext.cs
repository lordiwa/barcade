namespace Barcade.Core
{
    /// <summary>
    /// Read-only context describing the current round as computed by
    /// <see cref="SequencerDirector"/> after a <see cref="SequencerDirector.PickNext"/> call.
    ///
    /// The runtime adapter (MicrogameSequencerDriver MonoBehaviour) reads these values
    /// to populate the <c>IMicrogameContext</c> / <c>MicrogameContext</c> passed into
    /// each microgame's Prepare call.
    ///
    /// No UnityEngine dependency — safe for dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class SequencerRoundContext
    {
        /// <summary>Zero-based index of the current round (0 = first round of the session).</summary>
        public int RoundIndex { get; }

        /// <summary>The microgame selected for this round.</summary>
        public MicrogameDescriptor Descriptor { get; }

        /// <summary>
        /// Effective time limit for this round in seconds, after ramp is applied.
        /// Equals <c>RampSettings.EffectiveDuration(Descriptor.BaseDuration, RoundIndex)</c>.
        /// </summary>
        public float EffectiveDuration { get; }

        /// <summary>
        /// Speed multiplier for this round (>= 1.0).
        /// Equals <c>RampSettings.SpeedMultiplier(Descriptor.BaseDuration, RoundIndex)</c>.
        /// </summary>
        public float SpeedMultiplier { get; }

        internal SequencerRoundContext(
            int roundIndex,
            MicrogameDescriptor descriptor,
            float effectiveDuration,
            float speedMultiplier)
        {
            RoundIndex        = roundIndex;
            Descriptor        = descriptor;
            EffectiveDuration = effectiveDuration;
            SpeedMultiplier   = speedMultiplier;
        }
    }
}
