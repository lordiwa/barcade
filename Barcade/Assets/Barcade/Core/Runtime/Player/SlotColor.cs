namespace Barcade.Core
{
    /// <summary>
    /// A plain RGB color triplet — no UnityEngine dependency.
    /// Components are bytes (0–255) matching the bar-grade hex palette.
    /// </summary>
    public readonly struct SlotColor
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public SlotColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }
    }
}
