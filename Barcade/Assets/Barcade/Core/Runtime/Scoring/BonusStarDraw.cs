using System;
using System.Collections.Generic;

namespace Barcade.Core.Scoring
{
    /// <summary>
    /// The Game Over bonus-star reveal (GDD §6.3): 2 distinct stars drawn from
    /// the 6-kind pool with the session seed, subject to the acotación rule —
    /// the draw excludes combinations that would hand final victory to a player
    /// trailing the star leader by <see cref="DefaultExclusionThreshold"/> or
    /// more stars BEFORE the bonus ("la remontada absurda es el clímax; la
    /// imposible de justificar rompe la percepción de justicia").
    ///
    /// One decision → one draw (GDD §6.4/P3): each combination's outcome is
    /// computed deterministically (star holders from <see cref="SessionCounters"/>,
    /// podium from <see cref="FinalRanking"/>), ineligible combinations are
    /// filtered, and a single seeded draw picks among the survivors (stream:
    /// <see cref="RngStream.Draws"/>).
    ///
    /// At the GDD's fixed 1-star bonus values a ≥3-star flip is structurally
    /// impossible (two bonus stars can close at most 2), so the default filter
    /// is a defensive invariant — it becomes live if star values or bonus count
    /// ever change, and the threshold is parameterized so tests prove the filter
    /// works. If filtering ever left nothing (unreachable today: a combination
    /// that keeps the pre-bonus leader on top is never excluded), the draw falls
    /// back to the LEAST-HARMFUL combo(s) — whichever minimize the worst crowned
    /// gap — rather than an unfiltered pick (MEDIUM-2, TASK-037 review fix
    /// round): silently drawing an arbitrarily-bad outcome is the worst option
    /// on a live cabinet, but so is throwing mid-reveal, so this keeps the
    /// "liveness first" spirit (never crashes) while still honoring the
    /// invariant as closely as the situation allows.
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public static class BonusStarDraw
    {
        /// <summary>GDD §6.3: exclude combos that would crown a player >= 3 stars behind.</summary>
        public const int DefaultExclusionThreshold = 3;

        private const int KindCount = 6;

        /// <summary>
        /// Draws the 2 bonus stars for this session.
        /// </summary>
        /// <param name="sessionSeed">Session seed (§13); the draw uses the Draws stream.</param>
        /// <param name="counters">The tracked hidden-objective counters.</param>
        /// <param name="baseStars">Per-seat stars BEFORE the bonus reveal.</param>
        /// <param name="coins">Per-seat coins (post-wager) — feeds the tiebreak chain.</param>
        /// <param name="microgameWins">Per-seat microgame wins — feeds the tiebreak chain.</param>
        /// <param name="activeSeatsMask">Bit i = seat i participated in the session.</param>
        /// <param name="exclusionThreshold">Acotación threshold; default per GDD §6.3.</param>
        public static BonusStarResult Draw(
            int sessionSeed,
            SessionCounters counters,
            int[] baseStars,
            int[] coins,
            int[] microgameWins,
            int activeSeatsMask,
            int exclusionThreshold = DefaultExclusionThreshold)
        {
            if (counters == null) throw new ArgumentNullException(nameof(counters));
            if (baseStars == null) throw new ArgumentNullException(nameof(baseStars));
            if (coins == null) throw new ArgumentNullException(nameof(coins));
            if (microgameWins == null) throw new ArgumentNullException(nameof(microgameWins));
            if (baseStars.Length != 4 || coins.Length != 4 || microgameWins.Length != 4)
                throw new ArgumentException("baseStars, coins and microgameWins must be length 4");

            int maxBaseStars = 0;
            for (int seat = 0; seat < 4; seat++)
                if ((activeSeatsMask & (1 << seat)) != 0 && baseStars[seat] > maxBaseStars)
                    maxBaseStars = baseStars[seat];

            // Star holders are combination-independent — resolve once.
            var holders = new int[KindCount];
            for (int kind = 0; kind < KindCount; kind++)
                holders[kind] = counters.WinnerOf((StarKind)kind, activeSeatsMask);

            // Enumerate the 15 combinations in canonical order and filter.
            var eligible = new List<(int First, int Second)>(15);
            var all = new List<(int First, int Second)>(15);
            var starsScratch = new int[4];
            for (int first = 0; first < KindCount; first++)
                for (int second = first + 1; second < KindCount; second++)
                {
                    all.Add((first, second));
                    if (!CrownsADistantTrailer(
                            holders[first], holders[second], baseStars, coins, microgameWins,
                            activeSeatsMask, maxBaseStars, exclusionThreshold, starsScratch))
                        eligible.Add((first, second));
                }

            List<(int First, int Second)> drawPool;
            if (eligible.Count > 0)
            {
                drawPool = eligible;
            }
            else
            {
                // MEDIUM-2 fallback: every combo crowns a distant trailer. Prefer
                // whichever combo(s) minimize the worst crowned gap instead of an
                // unfiltered pick (see class doc).
                int bestGap = int.MaxValue;
                var leastHarmful = new List<(int First, int Second)>(all.Count);
                for (int i = 0; i < all.Count; i++)
                {
                    (int First, int Second) combo = all[i];
                    int gap = WorstCrownedGap(
                        holders[combo.First], holders[combo.Second], baseStars, coins, microgameWins,
                        activeSeatsMask, maxBaseStars, starsScratch);
                    if (gap < bestGap)
                    {
                        bestGap = gap;
                        leastHarmful.Clear();
                        leastHarmful.Add(combo);
                    }
                    else if (gap == bestGap)
                    {
                        leastHarmful.Add(combo);
                    }
                }
                drawPool = leastHarmful;
            }

            var rng = SeededRandom.Derive(sessionSeed, 0, RngStream.Draws);
            (int First, int Second) picked = drawPool[rng.NextInt(0, drawPool.Count)];

            var starsAfter = new int[4];
            ApplyCombo(holders[picked.First], holders[picked.Second], baseStars, starsAfter);

            return new BonusStarResult(
                (StarKind)picked.First, (StarKind)picked.Second,
                holders[picked.First], holders[picked.Second],
                starsAfter);
        }

        /// <summary>True when awarding this combo would crown a seat whose pre-bonus star deficit meets the threshold.</summary>
        private static bool CrownsADistantTrailer(
            int firstHolder, int secondHolder, int[] baseStars, int[] coins, int[] wins,
            int activeSeatsMask, int maxBaseStars, int threshold, int[] starsScratch)
            => WorstCrownedGap(firstHolder, secondHolder, baseStars, coins, wins, activeSeatsMask, maxBaseStars, starsScratch) >= threshold;

        /// <summary>
        /// The largest pre-bonus star deficit among seats this combo would crown
        /// (place == 1 after applying it); 0 if the pre-bonus leader (or a
        /// zero-deficit seat) stays on top. Used both by the threshold check above
        /// and by the MEDIUM-2 least-harmful fallback, which needs the actual
        /// value rather than just a boolean.
        /// </summary>
        private static int WorstCrownedGap(
            int firstHolder, int secondHolder, int[] baseStars, int[] coins, int[] wins,
            int activeSeatsMask, int maxBaseStars, int[] starsScratch)
        {
            ApplyCombo(firstHolder, secondHolder, baseStars, starsScratch);
            int[] places = FinalRanking.Rank(starsScratch, coins, wins, activeSeatsMask);
            int worst = 0;
            for (int seat = 0; seat < 4; seat++)
                if (places[seat] == 1)
                    worst = Math.Max(worst, maxBaseStars - baseStars[seat]);
            return worst;
        }

        private static void ApplyCombo(int firstHolder, int secondHolder, int[] baseStars, int[] into)
        {
            for (int seat = 0; seat < 4; seat++) into[seat] = baseStars[seat];
            if (firstHolder >= 0) into[firstHolder]++;
            if (secondHolder >= 0) into[secondHolder]++;
        }
    }
}
