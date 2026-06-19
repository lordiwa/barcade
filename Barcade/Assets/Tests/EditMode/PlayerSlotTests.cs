using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// AC1: PlayerSlot — exactly 4 fixed slots mapped to colors
    ///      Rojo=0, Azul=1, Amarillo=2, Verde=3.
    /// </summary>
    [TestFixture]
    public class PlayerSlotTests
    {
        // --- Enum values and index mapping --------------------------------

        [Test]
        public void PlayerSlot_Rojo_HasIndex0()
        {
            Assert.That((int)PlayerSlot.Rojo, Is.EqualTo(0));
        }

        [Test]
        public void PlayerSlot_Azul_HasIndex1()
        {
            Assert.That((int)PlayerSlot.Azul, Is.EqualTo(1));
        }

        [Test]
        public void PlayerSlot_Amarillo_HasIndex2()
        {
            Assert.That((int)PlayerSlot.Amarillo, Is.EqualTo(2));
        }

        [Test]
        public void PlayerSlot_Verde_HasIndex3()
        {
            Assert.That((int)PlayerSlot.Verde, Is.EqualTo(3));
        }

        [Test]
        public void PlayerSlotCount_IsExactlyFour()
        {
            var values = System.Enum.GetValues(typeof(PlayerSlot));
            Assert.That(values.Length, Is.EqualTo(4));
        }

        // --- Color lookup -------------------------------------------------

        [Test]
        public void GetColor_Rojo_ReturnsRedHex()
        {
            var color = PlayerSlotInfo.GetColor(PlayerSlot.Rojo);
            Assert.That(color.R, Is.EqualTo(0xE8));
            Assert.That(color.G, Is.EqualTo(0x21));
            Assert.That(color.B, Is.EqualTo(0x2B));
        }

        [Test]
        public void GetColor_Azul_ReturnsBlueHex()
        {
            var color = PlayerSlotInfo.GetColor(PlayerSlot.Azul);
            Assert.That(color.R, Is.EqualTo(0x1E));
            Assert.That(color.G, Is.EqualTo(0x5F));
            Assert.That(color.B, Is.EqualTo(0xBE));
        }

        [Test]
        public void GetColor_Amarillo_ReturnsYellowHex()
        {
            var color = PlayerSlotInfo.GetColor(PlayerSlot.Amarillo);
            Assert.That(color.R, Is.EqualTo(0xF5));
            Assert.That(color.G, Is.EqualTo(0xC4));
            Assert.That(color.B, Is.EqualTo(0x00));
        }

        [Test]
        public void GetColor_Verde_ReturnsGreenHex()
        {
            var color = PlayerSlotInfo.GetColor(PlayerSlot.Verde);
            Assert.That(color.R, Is.EqualTo(0x2B));
            Assert.That(color.G, Is.EqualTo(0xA8));
            Assert.That(color.B, Is.EqualTo(0x4A));
        }

        // --- Name lookup --------------------------------------------------

        [Test]
        public void GetName_EachSlot_ReturnsExpectedSpanishName()
        {
            Assert.That(PlayerSlotInfo.GetName(PlayerSlot.Rojo),    Is.EqualTo("Rojo"));
            Assert.That(PlayerSlotInfo.GetName(PlayerSlot.Azul),    Is.EqualTo("Azul"));
            Assert.That(PlayerSlotInfo.GetName(PlayerSlot.Amarillo),Is.EqualTo("Amarillo"));
            Assert.That(PlayerSlotInfo.GetName(PlayerSlot.Verde),   Is.EqualTo("Verde"));
        }

        // --- Round-trip index <-> slot ------------------------------------

        [Test]
        public void FromIndex_RoundTrips_AllFourSlots()
        {
            for (int i = 0; i < 4; i++)
            {
                var slot = PlayerSlotInfo.FromIndex(i);
                Assert.That((int)slot, Is.EqualTo(i));
            }
        }

        [Test]
        public void FromIndex_OutOfRange_ThrowsArgumentOutOfRange()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => PlayerSlotInfo.FromIndex(-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => PlayerSlotInfo.FromIndex(4));
        }
    }
}
