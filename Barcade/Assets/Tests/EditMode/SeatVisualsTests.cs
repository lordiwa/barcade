using NUnit.Framework;
using Barcade.Core;
using Barcade.Presentation;

namespace Barcade.Presentation.Tests
{
    /// <summary>
    /// TASK-027 (GDD T-104b) AC4/AC5 regression lock: the canonical seat-order
    /// shape+color mapping (DESIGN CONTRACT RATIFIED 2026-07-08) -- P1=Rojo/
    /// círculo, P2=Azul/cuadrado, P3=Amarillo/triángulo, P4=Verde/rombo -- and
    /// the exact GDD §1.3 reference hex palette. No Unity required -- pure C#.
    /// </summary>
    [TestFixture]
    public class SeatVisualsTests
    {
        [TestCase(PlayerSlot.Rojo, SeatShape.Circle)]
        [TestCase(PlayerSlot.Azul, SeatShape.Square)]
        [TestCase(PlayerSlot.Amarillo, SeatShape.Triangle)]
        [TestCase(PlayerSlot.Verde, SeatShape.Rhombus)]
        public void ShapeFor_MatchesCanonicalSeatOrder(PlayerSlot slot, SeatShape expected)
        {
            Assert.That(SeatVisuals.ShapeFor(slot), Is.EqualTo(expected));
        }

        [Test]
        public void AllFourSeats_HaveDistinctShapes()
        {
            var shapes = new[]
            {
                SeatVisuals.ShapeFor(PlayerSlot.Rojo),
                SeatVisuals.ShapeFor(PlayerSlot.Azul),
                SeatVisuals.ShapeFor(PlayerSlot.Amarillo),
                SeatVisuals.ShapeFor(PlayerSlot.Verde),
            };

            Assert.That(shapes, Is.Unique);
        }

        // ── GDD §1.3 reference hex palette, exactly ──────────────────────────────

        [Test]
        public void ColorFor_Rojo_MatchesHexE5484D()
        {
            AssertColor(SeatVisuals.ColorFor(PlayerSlot.Rojo), 0xE5, 0x48, 0x4D);
        }

        [Test]
        public void ColorFor_Azul_MatchesHex3B82F6()
        {
            AssertColor(SeatVisuals.ColorFor(PlayerSlot.Azul), 0x3B, 0x82, 0xF6);
        }

        [Test]
        public void ColorFor_Amarillo_MatchesHexFACC15()
        {
            AssertColor(SeatVisuals.ColorFor(PlayerSlot.Amarillo), 0xFA, 0xCC, 0x15);
        }

        [Test]
        public void ColorFor_Verde_MatchesHex22C55E()
        {
            AssertColor(SeatVisuals.ColorFor(PlayerSlot.Verde), 0x22, 0xC5, 0x5E);
        }

        private static void AssertColor(RgbColor color, byte r, byte g, byte b)
        {
            const float tolerance = 1f / 255f;
            Assert.That(color.R, Is.EqualTo(r / 255f).Within(tolerance));
            Assert.That(color.G, Is.EqualTo(g / 255f).Within(tolerance));
            Assert.That(color.B, Is.EqualTo(b / 255f).Within(tolerance));
        }
    }
}
