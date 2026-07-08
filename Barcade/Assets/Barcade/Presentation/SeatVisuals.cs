using Barcade.Core;

namespace Barcade.Presentation
{
    /// <summary>Overhead shape marker per seat (GDD §8.1 shape channel, carried into 3D per §10.4).</summary>
    public enum SeatShape
    {
        Circle,
        Square,
        Triangle,
        Rhombus,
    }

    /// <summary>
    /// Plain [0,1] RGB — UnityEngine-free stand-in for <c>UnityEngine.Color</c>
    /// so this stays in the fast-testable Presentation module. The Framework
    /// converts to a real <c>Color</c> at the point of use.
    /// </summary>
    public readonly struct RgbColor
    {
        public readonly float R, G, B;

        public RgbColor(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    /// <summary>
    /// Canonical seat-order color+shape mapping (GDD §1.3/§8.1, DESIGN CONTRACT
    /// RATIFIED 2026-07-08): P1=Rojo/círculo, P2=Azul/cuadrado,
    /// P3=Amarillo/triángulo, P4=Verde/rombo — fixed everywhere. Both channels
    /// are always carried together so the deuteranopia/protanopia-safe redundant
    /// coding §8.1 requires ("el color nunca es el único canal") holds in 3D too.
    ///
    /// <see cref="ColorFor"/> uses the GDD §1.3 reference hex palette exactly
    /// (Rojo #E5484D, Azul #3B82F6, Amarillo #FACC15, Verde #22C55E) — these are
    /// NOT the same constants as <c>Barcade.Framework.Views.ShapeFactory</c>'s
    /// older 2D HUD colors, which predate this ratified contract and are out of
    /// this ticket's scope to change.
    /// </summary>
    public static class SeatVisuals
    {
        public static SeatShape ShapeFor(PlayerSlot slot)
        {
            switch (slot)
            {
                case PlayerSlot.Rojo: return SeatShape.Circle;
                case PlayerSlot.Azul: return SeatShape.Square;
                case PlayerSlot.Amarillo: return SeatShape.Triangle;
                case PlayerSlot.Verde: return SeatShape.Rhombus;
                default: return SeatShape.Circle;
            }
        }

        public static RgbColor ColorFor(PlayerSlot slot)
        {
            switch (slot)
            {
                case PlayerSlot.Rojo: return new RgbColor(0.898f, 0.282f, 0.302f);     // #E5484D
                case PlayerSlot.Azul: return new RgbColor(0.231f, 0.510f, 0.965f);     // #3B82F6
                case PlayerSlot.Amarillo: return new RgbColor(0.980f, 0.800f, 0.082f); // #FACC15
                case PlayerSlot.Verde: return new RgbColor(0.133f, 0.773f, 0.369f);    // #22C55E
                default: return new RgbColor(0.6f, 0.6f, 0.6f);                        // neutral grey (unowned entity)
            }
        }
    }
}
