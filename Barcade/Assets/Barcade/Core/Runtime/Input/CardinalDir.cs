namespace Barcade.Core
{
    /// <summary>
    /// The four cardinal directions plus a neutral (None) state.
    /// Used as the result of <see cref="InputSnapshot.CardinalDirection"/>.
    /// </summary>
    public enum CardinalDir
    {
        None  = 0,
        Up    = 1,
        Down  = 2,
        Left  = 3,
        Right = 4,
    }
}
