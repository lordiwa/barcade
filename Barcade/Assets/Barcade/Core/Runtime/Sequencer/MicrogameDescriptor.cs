namespace Barcade.Core
{
    /// <summary>
    /// Pure-data descriptor that the sequencer holds for each microgame in the pool.
    /// Engine-free mirror of the Framework's MicrogameDefinition ScriptableObject.
    ///
    /// The runtime adapter (MicrogameSequencerDriver MonoBehaviour in Barcade.Framework)
    /// maps each MicrogameDefinition asset into one of these at startup and registers
    /// them with <see cref="SequencerDirector"/>.
    ///
    /// No UnityEngine dependency — safe for dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public sealed class MicrogameDescriptor
    {
        /// <summary>
        /// Unique string identifier for this microgame (e.g. "dodge_the_ball").
        /// Must be non-null and non-empty. Mirrors <c>MicrogameDefinition.id</c>.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The base round duration in seconds before any speed ramp is applied.
        /// Mirrors <c>MicrogameDefinition.baseDuration</c>.
        /// </summary>
        public float BaseDuration { get; }

        /// <summary>
        /// Designer-authored difficulty tier (1 = easy, 2 = medium, 3 = hard).
        /// Mirrors <c>MicrogameDefinition.difficulty</c>.
        /// Reserved for future pool-filtering by round threshold; not used by the
        /// sequencer director in milestone 1.
        /// </summary>
        public int Difficulty { get; }

        /// <summary>
        /// Creates an immutable microgame descriptor.
        /// </summary>
        /// <param name="id">Non-null, non-empty unique identifier.</param>
        /// <param name="baseDuration">Base round duration in seconds (> 0).</param>
        /// <param name="difficulty">Difficulty tier 1–3.</param>
        public MicrogameDescriptor(string id, float baseDuration, int difficulty)
        {
            if (id == null)         throw new System.ArgumentNullException("id");
            if (id.Length == 0)     throw new System.ArgumentException("id must not be empty", "id");
            if (baseDuration <= 0f) throw new System.ArgumentException("baseDuration must be positive", "baseDuration");

            Id           = id;
            BaseDuration = baseDuration;
            Difficulty   = difficulty;
        }
    }
}
