using UnityEngine;
using Barcade.Core;
using Barcade.Presentation;

namespace Barcade.Framework.Stage
{
    using Barcade.Framework; // ShapeFactory — C# doesn't auto-import a parent namespace's members into a nested one
    /// <summary>
    /// Builds the overhead shape marker for a player avatar (GDD §8.1's shape
    /// channel, carried into 3D per §10.4: "círculo/cuadrado/triángulo/rombo
    /// como remate sobre la cabeza"). Reuses <see cref="ShapeFactory"/> for the
    /// actual shape geometry per the ticket's instruction to reuse it where it
    /// fits — <see cref="ShapeFactory.MakeCircle(PlayerSlot,Vector3,float,Transform)"/>/
    /// <see cref="ShapeFactory.MakeSquare(PlayerSlot,Vector3,float,Transform)"/>
    /// are used as-is; <see cref="ShapeFactory.MakeTriangle"/>/
    /// <see cref="ShapeFactory.MakeRhombus"/> were added there additively
    /// (ShapeFactory previously only had 2 of the 4 canonical shapes).
    ///
    /// ShapeFactory's shapes are built flat in the XY plane, camera-facing --
    /// correct for its existing 2D HUD callers but not for an "overhead"
    /// marker, which must lie flat over the avatar's head (the XZ plane) to
    /// read from the stage's overhead/low-angle cameras. This factory rotates
    /// whatever ShapeFactory returns onto that plane rather than teaching
    /// ShapeFactory a new orientation concept.
    /// </summary>
    public static class AvatarMarkerFactory
    {
        /// <summary>[ASSUMED] world-unit marker size, roughly head-sized relative to a ~1-unit avatar.</summary>
        public const float BaseSize = 0.4f;

        /// <summary>[ASSUMED] world units the marker floats above the avatar's local origin.</summary>
        public const float HeightAboveAvatar = 1.0f;

        /// <summary>[ASSUMED] how much HudState.Meters (a per-seat [0,1] value) pulses the marker's scale.</summary>
        public const float MeterPulseScale = 0.5f;

        public static GameObject Create(PlayerSlot slot, SeatShape shape, Transform parent)
        {
            GameObject marker;
            switch (shape)
            {
                case SeatShape.Circle:
                    marker = ShapeFactory.MakeCircle(slot, Vector3.zero, BaseSize * 0.5f, parent);
                    break;
                case SeatShape.Square:
                    marker = ShapeFactory.MakeSquare(slot, Vector3.zero, BaseSize, parent);
                    break;
                case SeatShape.Triangle:
                    marker = ShapeFactory.MakeTriangle(slot, Vector3.zero, BaseSize, parent);
                    break;
                case SeatShape.Rhombus:
                    marker = ShapeFactory.MakeRhombus(slot, Vector3.zero, BaseSize, parent);
                    break;
                default:
                    marker = ShapeFactory.MakeCircle(slot, Vector3.zero, BaseSize * 0.5f, parent);
                    break;
            }

            // Lie flat overhead (XZ plane) instead of ShapeFactory's default
            // camera-facing XY orientation.
            marker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            marker.transform.localPosition = new Vector3(0f, HeightAboveAvatar, 0f);
            marker.name = $"AvatarMarker_{slot}_{shape}";
            return marker;
        }
    }
}
