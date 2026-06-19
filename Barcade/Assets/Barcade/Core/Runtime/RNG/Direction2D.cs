namespace Barcade.Core
{
    /// <summary>
    /// A plain 2-D direction value (no UnityEngine dependency).
    /// Returned by <see cref="ISeededRandom.NextDirection"/>.
    /// Intended to be unit-length, but carries no invariant enforcement.
    /// </summary>
    public readonly struct Direction2D
    {
        public readonly float X;
        public readonly float Y;

        public Direction2D(float x, float y)
        {
            X = x;
            Y = y;
        }

        public override string ToString() => $"Direction2D({X:F4}, {Y:F4})";
    }
}
