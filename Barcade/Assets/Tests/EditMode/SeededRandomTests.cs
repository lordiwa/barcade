using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// AC3 + AC4: ISeededRandom / SeededRandom — deterministic RNG wrapper.
    ///            Same seed → same sequence; different seeds → different sequences.
    /// </summary>
    [TestFixture]
    public class SeededRandomTests
    {
        // --- Interface contract ------------------------------------------

        [Test]
        public void SeededRandom_ImplementsISeededRandom()
        {
            ISeededRandom rng = new SeededRandom(42);
            Assert.That(rng, Is.Not.Null);
        }

        // --- NextFloat ---------------------------------------------------

        [Test]
        public void NextFloat_SameSeed_ProducesIdenticalSequence()
        {
            var rng1 = new SeededRandom(12345);
            var rng2 = new SeededRandom(12345);

            for (int i = 0; i < 20; i++)
                Assert.That(rng1.NextFloat(), Is.EqualTo(rng2.NextFloat()),
                    $"Diverged at step {i}");
        }

        [Test]
        public void NextFloat_ReturnsBetweenZeroAndOne()
        {
            var rng = new SeededRandom(99);
            for (int i = 0; i < 100; i++)
            {
                float v = rng.NextFloat();
                Assert.That(v, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }
        }

        [Test]
        public void NextFloat_DifferentSeeds_ProduceDifferentSequences()
        {
            var rng1 = new SeededRandom(1);
            var rng2 = new SeededRandom(999999);

            bool anyDifferent = false;
            for (int i = 0; i < 20; i++)
                if (rng1.NextFloat() != rng2.NextFloat()) { anyDifferent = true; break; }

            Assert.That(anyDifferent, Is.True,
                "Two very different seeds produced an identical 20-float sequence — highly unlikely unless broken.");
        }

        // --- NextInt -----------------------------------------------------

        [Test]
        public void NextInt_SameSeed_ProducesIdenticalSequence()
        {
            var rng1 = new SeededRandom(7);
            var rng2 = new SeededRandom(7);

            for (int i = 0; i < 20; i++)
                Assert.That(rng1.NextInt(0, 100), Is.EqualTo(rng2.NextInt(0, 100)),
                    $"Diverged at step {i}");
        }

        [Test]
        public void NextInt_ReturnsWithinRange()
        {
            var rng = new SeededRandom(42);
            for (int i = 0; i < 100; i++)
            {
                int v = rng.NextInt(5, 15);
                Assert.That(v, Is.GreaterThanOrEqualTo(5).And.LessThan(15));
            }
        }

        [Test]
        public void NextInt_DifferentSeeds_ProduceDifferentSequences()
        {
            var rng1 = new SeededRandom(2);
            var rng2 = new SeededRandom(888888);

            bool anyDifferent = false;
            for (int i = 0; i < 20; i++)
                if (rng1.NextInt(0, 1000) != rng2.NextInt(0, 1000)) { anyDifferent = true; break; }

            Assert.That(anyDifferent, Is.True);
        }

        // --- NextDirection -----------------------------------------------

        [Test]
        public void NextDirection_SameSeed_ProducesIdenticalAngles()
        {
            var rng1 = new SeededRandom(55);
            var rng2 = new SeededRandom(55);

            for (int i = 0; i < 10; i++)
            {
                var d1 = rng1.NextDirection();
                var d2 = rng2.NextDirection();
                Assert.That(d1.X, Is.EqualTo(d2.X).Within(0.0001f), $"X diverged at step {i}");
                Assert.That(d1.Y, Is.EqualTo(d2.Y).Within(0.0001f), $"Y diverged at step {i}");
            }
        }

        [Test]
        public void NextDirection_ResultIsUnitVector()
        {
            var rng = new SeededRandom(123);
            for (int i = 0; i < 20; i++)
            {
                var d = rng.NextDirection();
                float mag = System.MathF.Sqrt(d.X * d.X + d.Y * d.Y);
                Assert.That(mag, Is.EqualTo(1f).Within(0.0001f), $"Not unit at step {i}");
            }
        }

        // --- Seed replay (cross-instance determinism) --------------------

        [Test]
        public void SameSeed_AfterMixedCalls_ReplayProducesSameValues()
        {
            // Drive rng1 with a mix of NextFloat and NextInt calls.
            var rng1 = new SeededRandom(314);
            float f1 = rng1.NextFloat();
            int   i1 = rng1.NextInt(0, 50);
            float f2 = rng1.NextFloat();

            // Replay from same seed.
            var rng2 = new SeededRandom(314);
            Assert.That(rng2.NextFloat(),  Is.EqualTo(f1));
            Assert.That(rng2.NextInt(0, 50), Is.EqualTo(i1));
            Assert.That(rng2.NextFloat(),  Is.EqualTo(f2));
        }
    }
}
