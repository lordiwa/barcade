using System;

namespace Barcade.Core
{
    /// <summary>
    /// Static helpers for <see cref="PlayerSlot"/>: color lookup, name lookup,
    /// and index-to-slot conversion.
    ///
    /// All palette values come from the project's bar-grade color spec:
    ///   Rojo    #E8212B   Azul    #1E5FBE
    ///   Amarillo#F5C400   Verde   #2BA84A
    /// </summary>
    public static class PlayerSlotInfo
    {
        // Palette indexed by (int)PlayerSlot
        private static readonly SlotColor[] Colors =
        {
            new SlotColor(0xE8, 0x21, 0x2B), // Rojo    = 0
            new SlotColor(0x1E, 0x5F, 0xBE), // Azul    = 1
            new SlotColor(0xF5, 0xC4, 0x00), // Amarillo= 2
            new SlotColor(0x2B, 0xA8, 0x4A), // Verde   = 3
        };

        private static readonly string[] Names =
        {
            "Rojo",
            "Azul",
            "Amarillo",
            "Verde",
        };

        /// <summary>Returns the RGB color for the given slot.</summary>
        public static SlotColor GetColor(PlayerSlot slot) => Colors[(int)slot];

        /// <summary>Returns the Spanish color name for the given slot.</summary>
        public static string GetName(PlayerSlot slot) => Names[(int)slot];

        /// <summary>
        /// Converts a zero-based player index to the corresponding
        /// <see cref="PlayerSlot"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="index"/> is outside 0–3.
        /// </exception>
        public static PlayerSlot FromIndex(int index)
        {
            if (index < 0 || index > 3)
                throw new ArgumentOutOfRangeException(nameof(index),
                    $"Player index must be 0–3, got {index}.");
            return (PlayerSlot)index;
        }
    }
}
