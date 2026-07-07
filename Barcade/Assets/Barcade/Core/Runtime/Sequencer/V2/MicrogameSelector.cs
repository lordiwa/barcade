using System;
using System.Collections.Generic;
using Barcade.Core.Content;

namespace Barcade.Core.Sequencer.V2
{
    /// <summary>
    /// The GDD §9.2 microgame selector (T-108) — the v2 successor of
    /// <see cref="Barcade.Core.SequencerDirector"/>'s pick logic, operating on
    /// v2 <see cref="MicrogameDefinitionV2"/> pools (T-106). The v1 director and
    /// its consumers are untouched (same v1/v2 coexistence rationale as the v2
    /// <c>IMicrogame</c> contract); it retires under T-107 migration.
    ///
    /// <para>
    /// <b>Pure function.</b> Every method is static and deterministic in
    /// (pool contents+order, sessionSeed, round number, history) — no hidden
    /// state, no wall clock (GDD §9.2: "el sorteo es función pura de (seed,
    /// historial)"). Round RNG streams derive from the session seed (§13
    /// hierarchy) so consuming randomness in one round never shifts another.
    /// </para>
    ///
    /// <para>
    /// <b>One decision → at most one draw (GDD §6.4 / P3).</b> Constraints are
    /// applied by deterministically FILTERING the candidate set, then a single
    /// weighted draw picks the definition. The v1 director's re-roll loop is
    /// deliberately not reproduced. The two schedule decisions (which round
    /// hosts the coop special, which the asymmetric) are each their own single
    /// draw from a dedicated seed-derived stream.
    /// </para>
    ///
    /// <para>
    /// <b>Quota (§9.2, windows from §7).</b> Per 7-round session: exactly 1 coop
    /// special, scheduled in round 3 or 4 (§7.1); the asymmetric quota is
    /// satisfied by scheduling an Asym1v3 round in round 5 or 6 (the §7.2 "Jefe"
    /// window); every other round draws competitive ("resto competitivas"). For
    /// shorter sessions the specials are scheduled only while their windows
    /// exist (coop needs >= 4 rounds, asymmetric >= 5).
    /// </para>
    ///
    /// <para>
    /// <b>Anti-repetition (§9.2, hard).</b> Never the same mechanic two rounds
    /// in a row; never the same variant twice per session — enforced by
    /// filtering, including for the climax. If a small pool makes the
    /// constraints unsatisfiable, they relax in documented order (dynamics
    /// quota → mechanic-repeat → variant-repeat): liveness over constraints,
    /// the session never throws mid-play.
    /// </para>
    ///
    /// <para>
    /// <b>Anti-frustration bias (§6.4, soft).</b> If one seat placed exactly 4th
    /// in BOTH of the last two rounds, this round's draw multiplies the weight
    /// of that seat's best in-session mechanic (its best place across history;
    /// ties → most recent) by <see cref="BiasWeight"/> — restricted to mechanics
    /// present in the already-filtered candidate set, so the bias never
    /// overrides the hard constraints and never guarantees the outcome ("bias
    /// suave de selección, no de resultado").
    /// </para>
    ///
    /// No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// C# 9 compatible (Unity 6).
    /// </summary>
    public static class MicrogameSelector
    {
        /// <summary>
        /// Weight multiplier the §6.4 bias applies to the favored mechanic's
        /// variants in the single draw. [ASSUMED] 3× — the GDD says "bias suave"
        /// without a number; with a 4-mechanic pool this roughly doubles the
        /// favored mechanic's probability. GameTuning candidate (§11.3).
        /// </summary>
        public const int BiasWeight = 3;

        /// <summary>GDD §9.1: fixed difficulty multiplier for the climax microgame.</summary>
        public const float FinalDifficulty = 1.5f;

        // Dedicated derivation salts (§13: independent streams per decision).
        private const int CoopScheduleSalt = 101;
        private const int AsymScheduleSalt = 202;

        /// <summary>GDD §9.1: D(r) = 1 + 0.06(r-1), r 1-based.</summary>
        public static float DifficultyRamp(int roundNumber) => 1f + 0.06f * (roundNumber - 1);

        /// <summary>
        /// Draws round <paramref name="roundNumber"/> (1-based, within
        /// <paramref name="roundsTotal"/>) for the session.
        /// </summary>
        /// <param name="pool">Active v2 pool. Order matters for replay: keep it stable.</param>
        /// <param name="sessionSeed">Session seed (§13 root).</param>
        /// <param name="roundNumber">1-based round to draw.</param>
        /// <param name="roundsTotal">Rounds before the climax (GDD §9.3 reference: 7).</param>
        /// <param name="history">Rounds already played, oldest first.</param>
        public static RoundSelection SelectRound(
            IReadOnlyList<MicrogameDefinitionV2> pool,
            int sessionSeed,
            int roundNumber,
            int roundsTotal,
            IReadOnlyList<RoundRecord> history)
        {
            Validate(pool, history);
            if (roundsTotal < 1) throw new ArgumentOutOfRangeException(nameof(roundsTotal));
            if (roundNumber < 1 || roundNumber > roundsTotal) throw new ArgumentOutOfRangeException(nameof(roundNumber));

            MicrogameDynamics desired = ScheduledDynamics(sessionSeed, roundNumber, roundsTotal);
            return Select(pool, sessionSeed, roundNumber, history, desired, DifficultyRamp(roundNumber));
        }

        /// <summary>
        /// Draws the climax microgame (after FINAL_WAGER). Competitive by design
        /// — the climax is the ranked showdown the wager rides on — at the fixed
        /// <see cref="FinalDifficulty"/>. Anti-repetition still applies against
        /// the full session history.
        /// </summary>
        public static RoundSelection SelectFinalRound(
            IReadOnlyList<MicrogameDefinitionV2> pool,
            int sessionSeed,
            int roundsTotal,
            IReadOnlyList<RoundRecord> history)
        {
            Validate(pool, history);
            if (roundsTotal < 1) throw new ArgumentOutOfRangeException(nameof(roundsTotal));

            return Select(pool, sessionSeed, roundsTotal + 1, history, MicrogameDynamics.Competitive, FinalDifficulty);
        }

        // ── Scheduling (one decision, one draw, dedicated streams) ──────────────

        private static MicrogameDynamics ScheduledDynamics(int sessionSeed, int roundNumber, int roundsTotal)
        {
            if (roundsTotal >= 4 && roundNumber == CoopRound(sessionSeed))
                return MicrogameDynamics.Coop;
            if (roundsTotal >= 5 && roundNumber == AsymRound(sessionSeed, roundsTotal))
                return MicrogameDynamics.Asym1v3;
            return MicrogameDynamics.Competitive;
        }

        /// <summary>GDD §7.1: the coop special lands in round 3 or 4.</summary>
        private static int CoopRound(int sessionSeed)
            => 3 + new SeededRandom(Combine(sessionSeed, CoopScheduleSalt)).NextInt(0, 2);

        /// <summary>GDD §7.2 window: the asymmetric round lands in round 5 or 6 (5 when the session is that short).</summary>
        private static int AsymRound(int sessionSeed, int roundsTotal)
        {
            if (roundsTotal < 6) return 5;
            return 5 + new SeededRandom(Combine(sessionSeed, AsymScheduleSalt)).NextInt(0, 2);
        }

        // ── The draw ─────────────────────────────────────────────────────────────

        private static RoundSelection Select(
            IReadOnlyList<MicrogameDefinitionV2> pool,
            int sessionSeed,
            int roundNumber,
            IReadOnlyList<RoundRecord> history,
            MicrogameDynamics desired,
            float difficulty)
        {
            string lastMechanic = history.Count > 0 ? history[history.Count - 1].Mechanic : null;
            var usedVariants = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < history.Count; i++)
                usedVariants.Add(history[i].VariantId);

            // Constraint filtering — deterministic, consumes no randomness.
            // Relaxation order on unsatisfiable pools: quota, then mechanic-repeat,
            // then variant-repeat (liveness over constraints).
            var candidates = new List<MicrogameDefinitionV2>(pool.Count);
            Filter(pool, candidates, desired, lastMechanic, usedVariants);
            if (candidates.Count == 0 && desired != MicrogameDynamics.Competitive)
                Filter(pool, candidates, MicrogameDynamics.Competitive, lastMechanic, usedVariants);
            if (candidates.Count == 0)
                Filter(pool, candidates, null, lastMechanic, usedVariants);
            if (candidates.Count == 0)
                Filter(pool, candidates, null, null, usedVariants);
            if (candidates.Count == 0)
                Filter(pool, candidates, null, null, null);

            // §6.4 bias — evaluated against the filtered set only.
            int biasSeat = FindBiasSeat(history);
            string biasMechanic = biasSeat >= 0 ? BestMechanicAmong(biasSeat, history, candidates) : null;
            if (biasMechanic == null) biasSeat = -1;

            // Single weighted draw (one decision → one tirada).
            int totalWeight = 0;
            for (int i = 0; i < candidates.Count; i++)
                totalWeight += WeightOf(candidates[i], biasMechanic);

            var rng = new SeededRandom(Combine(sessionSeed, roundNumber));
            int roll = rng.NextInt(0, totalWeight);

            MicrogameDefinitionV2 picked = candidates[candidates.Count - 1];
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= WeightOf(candidates[i], biasMechanic);
                if (roll < 0) { picked = candidates[i]; break; }
            }

            return new RoundSelection(picked, difficulty, biasSeat, biasMechanic);
        }

        private static int WeightOf(MicrogameDefinitionV2 def, string biasMechanic)
            => biasMechanic != null && string.Equals(def.Mechanic, biasMechanic, StringComparison.Ordinal)
                ? BiasWeight
                : 1;

        private static void Filter(
            IReadOnlyList<MicrogameDefinitionV2> pool,
            List<MicrogameDefinitionV2> into,
            MicrogameDynamics? dynamics,
            string excludedMechanic,
            HashSet<string> excludedVariants)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                MicrogameDefinitionV2 def = pool[i];
                if (dynamics.HasValue && def.Dynamics != dynamics.Value) continue;
                if (excludedMechanic != null && string.Equals(def.Mechanic, excludedMechanic, StringComparison.Ordinal)) continue;
                if (excludedVariants != null && excludedVariants.Contains(def.Id)) continue;
                into.Add(def);
            }
        }

        // ── §6.4 trigger + best-history lookup ───────────────────────────────────

        /// <summary>
        /// The seat that placed exactly 4th in BOTH of the last two rounds, or -1.
        /// (With shared-place competition ranking only a sole last-place scores 4,
        /// so at most one seat can trigger.)
        /// </summary>
        private static int FindBiasSeat(IReadOnlyList<RoundRecord> history)
        {
            if (history.Count < 2) return -1;
            RoundRecord last = history[history.Count - 1];
            RoundRecord prev = history[history.Count - 2];
            for (int seat = 0; seat < 4; seat++)
                if (last.Places[seat] == 4 && prev.Places[seat] == 4)
                    return seat;
            return -1;
        }

        /// <summary>
        /// The seat's best in-session mechanic restricted to mechanics present in
        /// the candidate set (so the bias can never resurrect a filtered-out
        /// mechanic). Best = lowest place ever achieved; ties → most recent.
        /// Null when the seat has no history inside the candidate set.
        /// </summary>
        private static string BestMechanicAmong(
            int seat,
            IReadOnlyList<RoundRecord> history,
            List<MicrogameDefinitionV2> candidates)
        {
            string best = null;
            int bestPlace = int.MaxValue;

            for (int i = history.Count - 1; i >= 0; i--)
            {
                RoundRecord rec = history[i];
                int place = rec.Places[seat];
                if (place < 1) continue; // seat absent that round
                if (place >= bestPlace) continue; // most-recent-first: strict improvement keeps recency on ties
                if (!MechanicInCandidates(rec.Mechanic, candidates)) continue;
                best = rec.Mechanic;
                bestPlace = place;
            }

            return best;
        }

        private static bool MechanicInCandidates(string mechanic, List<MicrogameDefinitionV2> candidates)
        {
            for (int i = 0; i < candidates.Count; i++)
                if (string.Equals(candidates[i].Mechanic, mechanic, StringComparison.Ordinal))
                    return true;
            return false;
        }

        // ── Plumbing ─────────────────────────────────────────────────────────────

        private static void Validate(IReadOnlyList<MicrogameDefinitionV2> pool, IReadOnlyList<RoundRecord> history)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (pool.Count == 0) throw new ArgumentException("pool must not be empty", nameof(pool));
            if (history == null) throw new ArgumentNullException(nameof(history));
        }

        /// <summary>§13 stream derivation: sessionSeed → per-decision seed. Mixed so nearby rounds/salts diverge.</summary>
        private static int Combine(int sessionSeed, int stream)
        {
            unchecked
            {
                int h = sessionSeed * 486187739;
                h ^= stream * 1000003;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
