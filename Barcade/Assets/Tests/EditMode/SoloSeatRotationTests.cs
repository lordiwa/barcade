using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-040 (T-111) — coverage for <see cref="SoloSeatRotation"/>: GDD §4
    /// MECH_06 AC2 ("rotación de solista verificada round-robin en test de
    /// sesión completa; nunca lo elige el juego dos veces seguidas para el
    /// mismo puesto"). Simulates a full session (many consecutive rounds) and
    /// asserts the no-consecutive-repeat and even-coverage properties, plus the
    /// "system-forced, never chosen" structural guarantee (a pure function of
    /// round index and roster — no RNG, no player/bot input feeds it at all).
    /// </summary>
    [TestFixture]
    public class SoloSeatRotationTests
    {
        [Test]
        public void NextSoloSeat_FullSession_NeverRepeatsConsecutively()
        {
            int previous = -1;
            for (int round = 0; round < 500; round++)
            {
                int seat = SoloSeatRotation.NextSoloSeat(round, PlayerRoster.AllHuman);
                Assert.That(seat, Is.InRange(0, 3), $"round {round}");
                if (round > 0)
                    Assert.That(seat, Is.Not.EqualTo(previous), $"round {round}: solo seat repeated the immediately previous round");
                previous = seat;
            }
        }

        [Test]
        public void NextSoloSeat_FullSession_CyclesEveryActiveSeatEqually()
        {
            var counts = new Dictionary<int, int>();
            const int rounds = 400; // multiple of 4
            for (int round = 0; round < rounds; round++)
            {
                int seat = SoloSeatRotation.NextSoloSeat(round, PlayerRoster.AllHuman);
                counts[seat] = counts.TryGetValue(seat, out int c) ? c + 1 : 1;
            }

            Assert.That(counts.Count, Is.EqualTo(4), "every seat must get a turn");
            foreach (KeyValuePair<int, int> kv in counts)
                Assert.That(kv.Value, Is.EqualTo(rounds / 4), $"seat {kv.Key}: round-robin must distribute evenly");
        }

        [Test]
        public void NextSoloSeat_ExactRoundRobinSequence_ForAllHuman()
        {
            // GDD literal: round-robin -- the exact expected cycle for 4 active seats.
            int[] expected = { 0, 1, 2, 3, 0, 1, 2, 3, 0 };
            for (int round = 0; round < expected.Length; round++)
                Assert.That(SoloSeatRotation.NextSoloSeat(round, PlayerRoster.AllHuman), Is.EqualTo(expected[round]), $"round {round}");
        }

        [Test]
        public void NextSoloSeat_IsPureFunction_SameInputsAlwaysAgree()
        {
            // "System-forced, never chosen": no RNG/input parameter exists on this
            // method at all, so determinism is structural -- this test merely
            // confirms two independent calls with identical inputs never diverge.
            for (int round = 0; round < 50; round++)
            {
                int a = SoloSeatRotation.NextSoloSeat(round, PlayerRoster.AllHuman);
                int b = SoloSeatRotation.NextSoloSeat(round, PlayerRoster.AllHuman);
                Assert.That(a, Is.EqualTo(b), $"round {round}");
            }
        }

        [Test]
        public void NextSoloSeat_PartialRoster_SkipsEmptySeatsAndNeverRepeats()
        {
            // Verde (index 3) is Empty -- rotation must only ever land on the 3 active seats.
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Human, SeatState.Empty });

            int previous = -1;
            for (int round = 0; round < 300; round++)
            {
                int seat = SoloSeatRotation.NextSoloSeat(round, roster);
                Assert.That(seat, Is.Not.EqualTo(3), $"round {round}: rotation must never land on an empty seat");
                if (round > 0) Assert.That(seat, Is.Not.EqualTo(previous), $"round {round}");
                previous = seat;
            }
        }

        [Test]
        public void NextSoloSeat_NoActiveSeats_Throws()
        {
            var roster = new PlayerRoster(new[] { SeatState.Empty, SeatState.Empty, SeatState.Empty, SeatState.Empty });
            Assert.Throws<ArgumentException>(() => SoloSeatRotation.NextSoloSeat(0, roster));
        }

        [Test]
        public void NextSoloSeat_NegativeRoundIndex_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SoloSeatRotation.NextSoloSeat(-1, PlayerRoster.AllHuman));
        }
    }
}
