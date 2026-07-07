using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Scoring;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-109 — coverage for the §6 scoring systems (TASK-037): data-driven
    /// payout tables that never emit zero (§6.1), the compulsory FINAL_WAGER with
    /// exact 50/30/15/5 pot math and bounded ~1-star swing (§6.2/§5.6), the six
    /// bonus-star counters with the seeded 2-star draw and the ≥3-star exclusion
    /// rule (§6.3), the final tiebreak chain estrellas → monedas → victorias →
    /// declared shared podium, and replay determinism.
    ///
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class ScoringV2Tests
    {
        private const int AllSeats = 0b1111;

        private static int[] Arr(int a, int b, int c, int d) => new[] { a, b, c, d };

        // ── AC1: payout tables are data and never zero ───────────────────────

        [Test]
        public void DefaultPayouts_MatchGdd61()
        {
            Assert.That(PayoutRules.DefaultCompetitive, Is.EqualTo(Arr(6, 4, 2, 1)),
                "GDD 6.1: 1º=6, 2º=4, 3º=2, 4º=1");
            Assert.That(PayoutRules.DefaultCoopSuccess, Is.EqualTo(4));
            Assert.That(PayoutRules.DefaultCoopFail, Is.EqualTo(1));
        }

        [Test]
        public void Payout_RejectsZeroOrMissingEntries()
        {
            // §6.1: "el 0 absoluto desengancha" — a zero payout is a data bug.
            Assert.Throws<ArgumentException>(() => PayoutRules.ApplyCompetitive(
                new int[4], Arr(1, 2, 3, 4), Arr(6, 4, 2, 0)));
            Assert.Throws<ArgumentException>(() => PayoutRules.ApplyCompetitive(
                new int[4], Arr(1, 2, 3, 4), new[] { 6, 4, 2 }));
            Assert.Throws<ArgumentException>(() => PayoutRules.ApplyCoop(
                new int[4], true, 0, AllSeats));
        }

        [Test]
        public void Payout_Competitive_AddsCoinsByPlace_TiesSharePlacePayout()
        {
            int[] coins = Arr(10, 10, 10, 10);
            PayoutRules.ApplyCompetitive(coins, Arr(1, 1, 3, 4), PayoutRules.DefaultCompetitive);

            Assert.That(coins, Is.EqualTo(Arr(16, 16, 12, 11)),
                "tied players share the place and take that place's payout");
        }

        [Test]
        public void Payout_Competitive_AbsentSeatGetsNothing()
        {
            int[] coins = Arr(5, 5, 5, 5);
            PayoutRules.ApplyCompetitive(coins, Arr(1, 2, 3, 0), PayoutRules.DefaultCompetitive);

            Assert.That(coins[3], Is.EqualTo(5), "place 0 = absent seat, no payout");
        }

        [Test]
        public void Payout_Coop_PaysEveryActiveSeatTheSame()
        {
            int[] coins = new int[4];
            PayoutRules.ApplyCoop(coins, true, PayoutRules.DefaultCoopSuccess, 0b0111);
            Assert.That(coins, Is.EqualTo(Arr(4, 4, 4, 0)));

            PayoutRules.ApplyCoop(coins, false, PayoutRules.DefaultCoopFail, 0b0111);
            Assert.That(coins, Is.EqualTo(Arr(5, 5, 5, 0)), "coop fail still pays 1 — never zero");
        }

        // ── AC2: FINAL_WAGER ─────────────────────────────────────────────────

        [Test]
        public void Wager_TimeoutDefaultsToHalf_AndStakesAreCompulsory()
        {
            // [ASSUMED] the GDD gives no timeout default; Half (50%) is the middle
            // choice — flagged in the implementation for design calibration.
            var choices = new WagerChoice?[] { WagerChoice.Quarter, null, WagerChoice.ThreeQuarters, null };
            WagerResult r = FinalWager.Resolve(Arr(20, 20, 20, 20), choices, Arr(1, 2, 3, 4));

            Assert.That(r.Stakes, Is.EqualTo(Arr(5, 10, 15, 10)),
                "null choice = 5 s timeout = default 50%; everyone bets (compulsory)");
        }

        [Test]
        public void Wager_PotMathIsExact_50_30_15_5()
        {
            // Stakes: 25% of 40 = 10, 50% of 20 = 10, 75% of 40 = 30, 50% of 40 = 20. Pot = 70.
            var choices = new WagerChoice?[]
                { WagerChoice.Quarter, WagerChoice.Half, WagerChoice.ThreeQuarters, WagerChoice.Half };
            WagerResult r = FinalWager.Resolve(Arr(40, 20, 40, 40), choices, Arr(1, 2, 3, 4));

            Assert.That(r.Pot, Is.EqualTo(70));
            Assert.That(r.Shares[0], Is.EqualTo(35), "1st takes 50% of the pot");
            Assert.That(r.Shares[1], Is.EqualTo(21), "2nd takes 30%");
            Assert.That(r.Shares[2], Is.EqualTo(10), "3rd takes 15% (floor)");
            Assert.That(r.Shares[3], Is.EqualTo(4), "4th takes 5% (floor; rounding remainders are dealt from the bottom up — the anti-zero spirit of GDD 6.1)");
            int totalShares = 0;
            foreach (int s in r.Shares) totalShares += s;
            Assert.That(totalShares, Is.EqualTo(r.Pot), "every pot coin is redistributed — none vanish (GDD 5.3 invariant)");

            Assert.That(r.CoinsAfter, Is.EqualTo(Arr(40 - 10 + 35, 20 - 10 + 21, 40 - 30 + 10, 40 - 20 + 4)));
        }

        [Test]
        public void Wager_TiedPlaces_SplitTheirCombinedShares()
        {
            var choices = new WagerChoice?[]
                { WagerChoice.Half, WagerChoice.Half, WagerChoice.Half, WagerChoice.Half };
            WagerResult r = FinalWager.Resolve(Arr(20, 20, 20, 20), choices, Arr(1, 1, 3, 4));

            // Pot 40; places 1,1 pool 50%+30% = 80% => 32, split 16/16; 3rd 15% = 6; 4th 5% = 2.
            Assert.That(r.Shares[0], Is.EqualTo(16));
            Assert.That(r.Shares[1], Is.EqualTo(16));
            Assert.That(r.Shares[2], Is.EqualTo(6));
            Assert.That(r.Shares[3], Is.EqualTo(2));
        }

        [Test]
        public void Wager_AbsentSeats_NeitherStakeNorShare()
        {
            var choices = new WagerChoice?[] { WagerChoice.Half, null, WagerChoice.Half, null };
            WagerResult r = FinalWager.Resolve(Arr(20, 30, 20, 0), choices, Arr(1, 0, 2, 0));

            Assert.That(r.Stakes[1], Is.EqualTo(0), "absent seat (place 0) never stakes");
            Assert.That(r.Shares[1], Is.EqualTo(0));
            Assert.That(r.CoinsAfter[1], Is.EqualTo(30), "absent seat's coins untouched");
            int totalShares = 0;
            foreach (int s in r.Shares) totalShares += s;
            Assert.That(totalShares, Is.EqualTo(r.Pot));
        }

        [Test]
        public void Wager_SwingIsBoundedToRoughlyOneStar_UnderCalibratedEconomy()
        {
            // §5.6 envelope: end-of-session holdings after sinks ≈ up to ~35 coins.
            // §6.2/§5.6: the wager can flip ~1 star (15 coins) of value — never 3.
            // Exhaustive over choice combos and place permutations for corner holdings.
            int star = 15;
            int[][] holdingSets =
            {
                Arr(35, 25, 15, 5), Arr(35, 35, 35, 35), Arr(35, 0, 0, 0), Arr(20, 15, 10, 5),
            };
            var allChoices = new[] { WagerChoice.Quarter, WagerChoice.Half, WagerChoice.ThreeQuarters };
            int[][] placePerms = PlacePermutations();

            int worstGain = 0;
            foreach (int[] holdings in holdingSets)
                foreach (WagerChoice a in allChoices)
                    foreach (WagerChoice b in allChoices)
                        foreach (WagerChoice c in allChoices)
                            foreach (WagerChoice d in allChoices)
                                foreach (int[] places in placePerms)
                                {
                                    var choices = new WagerChoice?[] { a, b, c, d };
                                    WagerResult r = FinalWager.Resolve(holdings, choices, places);
                                    for (int s = 0; s < 4; s++)
                                        worstGain = Math.Max(worstGain, r.CoinsAfter[s] - holdings[s]);
                                }

            Assert.That(worstGain, Is.LessThanOrEqualTo(2 * star),
                "even the most extreme wager outcome stays in the ~1-star band, far from a 3-star (45-coin) flip");
            Assert.That(worstGain, Is.GreaterThanOrEqualTo(star / 2),
                "the wager must be able to move a meaningful fraction of a star, or the climax is stakes-free");
        }

        [Test]
        public void Wager_CompressesTheCoinDistribution_OnAverage()
        {
            // Rich players stake more coins in absolute terms, so across outcomes the
            // leader-vs-last gap shrinks in expectation (GDD 6.2 "comprime la
            // distribución") even though single outcomes can widen it.
            int[] holdings = Arr(35, 20, 10, 5);
            int preGap = 30;
            var allChoices = new[] { WagerChoice.Quarter, WagerChoice.Half, WagerChoice.ThreeQuarters };
            int[][] placePerms = PlacePermutations();

            long postGapSum = 0;
            int samples = 0;
            foreach (WagerChoice choice in allChoices)
                foreach (int[] places in placePerms)
                {
                    var choices = new WagerChoice?[] { choice, choice, choice, choice };
                    WagerResult r = FinalWager.Resolve(holdings, choices, places);
                    int max = int.MinValue, min = int.MaxValue;
                    foreach (int cAfter in r.CoinsAfter) { max = Math.Max(max, cAfter); min = Math.Min(min, cAfter); }
                    postGapSum += max - min;
                    samples++;
                }

            Assert.That(postGapSum / (double)samples, Is.LessThan(preGap),
                "average post-wager spread must be smaller than the pre-wager spread");
        }

        private static int[][] PlacePermutations()
        {
            var result = new List<int[]>();
            var seats = new[] { 0, 1, 2, 3 };
            Permute(seats, 0, result);
            return result.ToArray();
        }

        private static void Permute(int[] seats, int k, List<int[]> into)
        {
            if (k == seats.Length)
            {
                var places = new int[4];
                for (int i = 0; i < 4; i++) places[seats[i]] = i + 1;
                into.Add(places);
                return;
            }
            for (int i = k; i < seats.Length; i++)
            {
                (seats[k], seats[i]) = (seats[i], seats[k]);
                Permute(seats, k + 1, into);
                (seats[k], seats[i]) = (seats[i], seats[k]);
            }
        }

        // ── AC3: bonus-star counters + seeded draw + exclusion ───────────────

        private static SessionCounters CountersWhereSeatShines(int seat)
        {
            var c = new SessionCounters();
            c.RecordElimination(seat);
            c.RecordElimination(seat);
            c.RecordWeaponUse(seat);
            c.RecordZenMetric((seat + 1) & 3, 10f); // Zen is min-direction: rivals accumulate
            c.RecordZenMetric((seat + 2) & 3, 8f);
            c.RecordReaccionaLatency(seat, 120f);
            c.RecordReaccionaLatency((seat + 1) & 3, 200f);
            c.RecordInvestment(seat, 12);
            c.RecordRobbed((seat + 1) & 3, 4); // Fantasma is min-direction
            return c;
        }

        [Test]
        public void StarWinners_FollowEachCountersDirection()
        {
            var c = new SessionCounters();
            c.RecordElimination(1); c.RecordElimination(1); c.RecordElimination(2);
            c.RecordWeaponUse(3);
            c.RecordZenMetric(0, 5f); c.RecordZenMetric(1, 1f); c.RecordZenMetric(2, 9f); c.RecordZenMetric(3, 2f);
            c.RecordReaccionaLatency(0, 150f); c.RecordReaccionaLatency(0, 250f); // mean 200
            c.RecordReaccionaLatency(2, 180f);                                    // mean 180 — best
            c.RecordInvestment(0, 10); c.RecordInvestment(2, 25);
            c.RecordRobbed(0, 6); c.RecordRobbed(1, 2); c.RecordRobbed(2, 9); c.RecordRobbed(3, 4);

            Assert.That(c.WinnerOf(StarKind.Kamikaze, AllSeats), Is.EqualTo(1), "most eliminations");
            Assert.That(c.WinnerOf(StarKind.Cangreja, AllSeats), Is.EqualTo(3), "most weapons used");
            Assert.That(c.WinnerOf(StarKind.Zen, AllSeats), Is.EqualTo(1), "Zen is min-direction (GDD table truncation: 'Menor')");
            Assert.That(c.WinnerOf(StarKind.Gatillo, AllSeats), Is.EqualTo(2), "best (lowest) mean REACCIONA latency");
            Assert.That(c.WinnerOf(StarKind.Inversora, AllSeats), Is.EqualTo(2), "most coins invested");
            Assert.That(c.WinnerOf(StarKind.Fantasma, AllSeats), Is.EqualTo(1), "fewest coins robbed by rivals");
        }

        [Test]
        public void StarWinners_TieMeansNoAward_AndInactiveSeatsNeverWin()
        {
            var c = new SessionCounters();
            Assert.That(c.WinnerOf(StarKind.Kamikaze, AllSeats), Is.EqualTo(-1),
                "all-zero counters are a 4-way tie: a tied hidden objective has no owner [ASSUMED]");

            c.RecordRobbed(0, 3); c.RecordRobbed(1, 3);
            c.RecordRobbed(2, 9); c.RecordRobbed(3, 9);
            Assert.That(c.WinnerOf(StarKind.Fantasma, AllSeats), Is.EqualTo(-1), "two-way tie at the minimum");

            var c2 = new SessionCounters();
            c2.RecordRobbed(0, 5);
            // Seats 1-3 have 0 robbed (the minimum) but only seats 0-1 are active.
            Assert.That(c2.WinnerOf(StarKind.Fantasma, 0b0011), Is.EqualTo(1),
                "empty seats are excluded before direction is applied");

            Assert.That(c2.WinnerOf(StarKind.Gatillo, AllSeats), Is.EqualTo(-1),
                "no REACCIONA samples at all: nobody can hold the Gatillo star");
        }

        [Test]
        public void StarDraw_IsDeterministicInSessionSeed()
        {
            SessionCounters c = CountersWhereSeatShines(2);
            BonusStarResult a = BonusStarDraw.Draw(777, c, Arr(2, 1, 0, 1), Arr(20, 15, 10, 5), Arr(3, 2, 1, 1), AllSeats);
            BonusStarResult b = BonusStarDraw.Draw(777, c, Arr(2, 1, 0, 1), Arr(20, 15, 10, 5), Arr(3, 2, 1, 1), AllSeats);

            Assert.That(a.First, Is.EqualTo(b.First));
            Assert.That(a.Second, Is.EqualTo(b.Second));
            Assert.That(a.First, Is.Not.EqualTo(a.Second), "two DISTINCT stars are revealed (GDD 6.3)");
            Assert.That(a.StarsAfter, Is.EqualTo(b.StarsAfter));
        }

        [Test]
        public void StarDraw_ExclusionRule_FiltersFlipsBeyondTheThreshold()
        {
            // Constructed so seat 3 (2 stars behind, threshold set to 2 for the test)
            // wins every counter: any combo awards it up to 2 stars and flips the
            // podium via the coins tiebreak. With the exclusion threshold at 2 those
            // combos must be filtered; the draw still returns a valid combo.
            SessionCounters c = CountersWhereSeatShines(3);
            int[] baseStars = Arr(3, 1, 0, 1); // seat 0 leads; seat 3 trails by 2
            int[] coins = Arr(5, 5, 5, 50);    // seat 3 wins any star tie via coins

            for (int seed = 0; seed < 300; seed++)
            {
                BonusStarResult r = BonusStarDraw.Draw(seed, c, baseStars, coins, Arr(0, 0, 0, 0), AllSeats,
                    exclusionThreshold: 2);
                int[] places = FinalRanking.Rank(r.StarsAfter, coins, Arr(0, 0, 0, 0), AllSeats);
                Assert.That(places[3], Is.Not.EqualTo(1),
                    $"seed {seed}: a {2}-star-behind player won despite the exclusion threshold");
            }
        }

        [Test]
        public void StarDraw_AllowsTheJustifiableOneStarComeback()
        {
            // Seat 3 trails by exactly 1 star and shines at every counter: the GDD
            // WANTS this comeback ("la remontada absurda es el clímax") — with the
            // default threshold (3) these combos must NOT be filtered out.
            SessionCounters c = CountersWhereSeatShines(3);
            int[] baseStars = Arr(2, 1, 0, 1);
            int[] coins = Arr(5, 5, 5, 50);

            bool flipped = false;
            for (int seed = 0; seed < 300 && !flipped; seed++)
            {
                BonusStarResult r = BonusStarDraw.Draw(seed, c, baseStars, coins, Arr(0, 0, 0, 0), AllSeats);
                int[] places = FinalRanking.Rank(r.StarsAfter, coins, Arr(0, 0, 0, 0), AllSeats);
                flipped = places[3] == 1;
            }

            Assert.That(flipped, Is.True, "a 1-star comeback must be reachable through the bonus draw");
        }

        [Test]
        public void StarDraw_Fuzz1000Seeds_NeverCrownsAPlayerThreeOrMoreStarsBehind()
        {
            // GDD 6.3 acotación / §5.6 "voltear... 1 estrella — nunca uno de 3".
            // Structurally guaranteed at 1-star values AND enforced by the filter;
            // this fuzz pins the invariant end-to-end.
            var rng = new SeededRandom(2026);
            for (int trial = 0; trial < 1000; trial++)
            {
                var counters = new SessionCounters();
                for (int i = 0; i < 12; i++)
                {
                    int seat = rng.NextInt(0, 4);
                    switch (rng.NextInt(0, 6))
                    {
                        case 0: counters.RecordElimination(seat); break;
                        case 1: counters.RecordWeaponUse(seat); break;
                        case 2: counters.RecordZenMetric(seat, rng.NextInt(1, 20)); break;
                        case 3: counters.RecordReaccionaLatency(seat, rng.NextInt(90, 400)); break;
                        case 4: counters.RecordInvestment(seat, rng.NextInt(1, 15)); break;
                        default: counters.RecordRobbed(seat, rng.NextInt(1, 10)); break;
                    }
                }
                int[] baseStars = Arr(rng.NextInt(0, 6), rng.NextInt(0, 6), rng.NextInt(0, 6), rng.NextInt(0, 6));
                int[] coins = Arr(rng.NextInt(0, 36), rng.NextInt(0, 36), rng.NextInt(0, 36), rng.NextInt(0, 36));
                int[] wins = Arr(rng.NextInt(0, 8), rng.NextInt(0, 8), rng.NextInt(0, 8), rng.NextInt(0, 8));

                BonusStarResult r = BonusStarDraw.Draw(trial, counters, baseStars, coins, wins, AllSeats);
                int[] places = FinalRanking.Rank(r.StarsAfter, coins, wins, AllSeats);

                int maxBase = Math.Max(Math.Max(baseStars[0], baseStars[1]), Math.Max(baseStars[2], baseStars[3]));
                for (int seat = 0; seat < 4; seat++)
                    if (places[seat] == 1)
                        Assert.That(maxBase - baseStars[seat], Is.LessThan(3),
                            $"trial {trial}: winner was {maxBase - baseStars[seat]} stars behind before the bonus");
            }
        }

        // ── AC4: tiebreak chain ──────────────────────────────────────────────

        [Test]
        public void FinalRanking_TiebreakChain_StarsThenCoinsThenWins()
        {
            Assert.That(FinalRanking.Rank(Arr(3, 2, 2, 0), Arr(0, 9, 5, 99), Arr(0, 0, 0, 0), AllSeats),
                Is.EqualTo(Arr(1, 2, 3, 4)), "stars first; coins break the 2-star tie");

            Assert.That(FinalRanking.Rank(Arr(2, 2, 2, 2), Arr(7, 7, 7, 7), Arr(1, 3, 2, 0), AllSeats),
                Is.EqualTo(Arr(3, 1, 2, 4)), "equal stars and coins: microgame wins decide");
        }

        [Test]
        public void FinalRanking_FullTie_DeclaresSharedPodium()
        {
            int[] places = FinalRanking.Rank(Arr(2, 2, 1, 2), Arr(5, 5, 9, 5), Arr(1, 1, 0, 1), AllSeats);
            Assert.That(places, Is.EqualTo(Arr(1, 1, 4, 1)),
                "an unbreakable tie is DECLARED: tied players share the podium place (GDD 6.3)");
        }

        [Test]
        public void FinalRanking_AbsentSeats_AreUnplaced()
        {
            int[] places = FinalRanking.Rank(Arr(2, 0, 1, 0), Arr(5, 0, 9, 0), Arr(1, 0, 0, 0), 0b0101);
            Assert.That(places[0], Is.EqualTo(1));
            Assert.That(places[2], Is.EqualTo(2));
            Assert.That(places[1], Is.EqualTo(0), "empty seat gets place 0");
            Assert.That(places[3], Is.EqualTo(0));
        }
    }
}
