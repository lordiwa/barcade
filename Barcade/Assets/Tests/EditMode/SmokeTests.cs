using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// Smoke tests — verify the project compiles, the test pipeline runs,
    /// and the Core assembly is reachable from the test assembly.
    /// </summary>
    [TestFixture]
    public class SmokeTests
    {
        [Test]
        public void Arithmetic_TwoPlusTwo_EqualsFour()
        {
            Assert.That(2 + 2, Is.EqualTo(4));
        }

        [Test]
        public void GameVersion_Value_IsNotEmpty()
        {
            // References Barcade.Core to confirm the test assembly dependency is wired.
            Assert.That(GameVersion.Value, Is.Not.Null.And.Not.Empty);
        }
    }
}
