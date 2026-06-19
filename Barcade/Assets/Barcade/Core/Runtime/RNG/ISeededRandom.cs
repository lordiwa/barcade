namespace Barcade.Core
{
    /// <summary>
    /// A deterministic random-number generator seeded at construction time.
    /// The same seed must yield the same sequence of values across instances
    /// and across sessions.
    ///
    /// Inject this interface into microgame logic classes via constructor
    /// injection; never call UnityEngine.Random directly from logic code.
    /// </summary>
    public interface ISeededRandom
    {
        /// <summary>
        /// Returns the next pseudo-random float in the half-open range [0, 1).
        /// </summary>
        float NextFloat();

        /// <summary>
        /// Returns the next pseudo-random integer in the half-open range
        /// [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).
        /// </summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>
        /// Returns a random unit-length 2-D direction vector (uniformly
        /// distributed on the unit circle) as a plain (X, Y) pair.
        /// </summary>
        Direction2D NextDirection();
    }
}
