using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Microgames.V2;
// Same collision as ReaccionaMicrogameTests/ApuntaMicrogameV2Tests: this file's
// namespace (Barcade.Core.Tests) is lexically nested inside Barcade.Core, so an
// unqualified name resolves against Barcade.Core's own members before any
// "using"-imported namespace. Renamed aliases sidestep it.
using V2Apunta = Barcade.Core.Microgames.V2.ApuntaMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-046 — coverage for the shared <see cref="InputBridge"/>: the
    /// canonical (normalized) stick-vector convention, and the 9-value
    /// <see cref="Direction8"/> round-trip through <see cref="InputInterpreter"/>
    /// that both ReaccionaMicrogame and ApuntaMicrogame now rely on via the same
    /// implementation.
    ///
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class InputBridgeTests
    {
        private static readonly Direction8[] AllNine =
        {
            Direction8.None,
            Direction8.N, Direction8.NE, Direction8.E, Direction8.SE,
            Direction8.S, Direction8.SW, Direction8.W, Direction8.NW,
        };

        // ── AC2: canonical convention (normalized unit vectors) ─────────────────

        [Test]
        public void ToUnitVector_Cardinals_AreExactUnitLength()
        {
            AssertUnit(InputBridge.ToUnitVector(Direction8.N), 0f, 1f);
            AssertUnit(InputBridge.ToUnitVector(Direction8.S), 0f, -1f);
            AssertUnit(InputBridge.ToUnitVector(Direction8.E), 1f, 0f);
            AssertUnit(InputBridge.ToUnitVector(Direction8.W), -1f, 0f);
        }

        [Test]
        public void ToUnitVector_Diagonals_AreNormalized_NotSignPure()
        {
            // The whole point of TASK-046's convention choice: diagonals must be
            // (±0.70710678, ±0.70710678) -- magnitude 1 -- NOT (±1, ±1) (magnitude
            // sqrt(2)), which is what ReaccionaMicrogame's old private copy used.
            const float diag = 0.70710678f;
            AssertUnit(InputBridge.ToUnitVector(Direction8.NE), diag, diag);
            AssertUnit(InputBridge.ToUnitVector(Direction8.SE), diag, -diag);
            AssertUnit(InputBridge.ToUnitVector(Direction8.NW), -diag, diag);
            AssertUnit(InputBridge.ToUnitVector(Direction8.SW), -diag, -diag);
        }

        [Test]
        public void ToUnitVector_None_IsZero()
        {
            (float x, float y) = InputBridge.ToUnitVector(Direction8.None);
            Assert.That(x, Is.EqualTo(0f));
            Assert.That(y, Is.EqualTo(0f));
        }

        [Test]
        public void ToUnitVector_EveryPressedDirection_HasMagnitudeExactlyOne()
        {
            // GDD S1.3: the cabinet stick is digital 8-way, no analog gradation --
            // every pressed direction must read as "fully pressed" uniformly.
            foreach (Direction8 d in AllNine)
            {
                if (d == Direction8.None) continue;
                (float x, float y) = InputBridge.ToUnitVector(d);
                float magnitude = System.MathF.Sqrt(x * x + y * y);
                Assert.That(magnitude, Is.EqualTo(1f).Within(1e-6f), $"{d}: magnitude must be exactly 1, not sign-pure's inconsistent 1 vs sqrt(2)");
            }
        }

        private static void AssertUnit((float X, float Y) actual, float expectedX, float expectedY)
        {
            Assert.That(actual.X, Is.EqualTo(expectedX).Within(1e-6f));
            Assert.That(actual.Y, Is.EqualTo(expectedY).Within(1e-6f));
        }

        // ── AC2: round-trips through InputInterpreter identically ───────────────

        [Test]
        public void AllNineDirection8Values_RoundTripThroughTheBridgeIntoInputInterpreter()
        {
            // The bridge is what both mechanics now share -- pinning this once
            // against the bridge type directly covers "identically for both
            // mechanics" by construction (there is exactly one implementation).
            foreach (Direction8 original in AllNine)
            {
                var interpreter = new InputInterpreter(InputInterpreterConfig.GddDefaults);
                var bridge = new InputBridge();
                var players = new PlayerInput[4];
                players[(int)PlayerSlot.Rojo] = new PlayerInput(original, false);
                bridge.SetSource(players);

                interpreter.Tick(bridge);

                Assert.That(interpreter.RawDirection(PlayerSlot.Rojo), Is.EqualTo(original),
                    $"{original} must round-trip through the bridge's unit-vector conversion and " +
                    "InputInterpreter.ToDirection8's sign-based classification back to itself");
            }
        }

        [Test]
        public void ApuntaMicrogame_CurrentAim_MatchesTheSharedBridgeConvention()
        {
            // Cross-mechanic consistency check (AC2 "for both mechanics"): drive a
            // real ApuntaMicrogame -- which now consumes the exact same InputBridge
            // -- through all 8 non-None directions and confirm its own public
            // CurrentAim accessor reports each one back correctly.
            foreach (Direction8 original in AllNine)
            {
                if (original == Direction8.None) continue; // cold-start default aim, not a round-trip case

                var mg = new V2Apunta(ApuntaParams.GddDefaults);
                mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
                var players = new PlayerInput[4];
                players[(int)PlayerSlot.Rojo] = new PlayerInput(original, false);
                var snapshot = new V2Snapshot(0, players);

                mg.Tick(snapshot);

                Assert.That(mg.CurrentAim(PlayerSlot.Rojo), Is.EqualTo(original),
                    $"{original}: ApuntaMicrogame's own aim, read through the shared bridge, must match");
            }
        }
    }
}
