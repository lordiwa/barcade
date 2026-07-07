using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Content;
using Barcade.Core.Sequencer.V2;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-108 — coverage for <see cref="MicrogameSelector"/> (§9.2 + §6.4):
    /// pure-function determinism, the per-7-round-session quota (exactly 1 coop
    /// special in round 3/4 per §7.1, >= 1 asymmetric in the §7.2 window, rest
    /// competitive), anti-repetition (never the same mechanic twice in a row,
    /// never the same variant twice per session), the §6.4 anti-frustration
    /// selection bias (statistical, never a guarantee), the §9.1 difficulty ramp
    /// D(r) = 1 + 0.06(r-1) with D_final = 1.5, and graceful degradation on
    /// pools that cannot satisfy the constraints.
    ///
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class MicrogameSelectorTests
    {
        private const int RoundsTotal = 7;

        // ── Pool builders ────────────────────────────────────────────────────

        private static MicrogameDefinitionV2 Def(string id, string mechanic, MicrogameDynamics dynamics)
            => new MicrogameDefinitionV2
            {
                Id = id,
                Mechanic = mechanic,
                DisplayVerb = "¡" + mechanic.ToUpperInvariant() + "!",
                Dynamics = dynamics,
                Duration = 5f,
                MinPlayers = 2,
            };

        /// <summary>
        /// A GDD-shaped pool: 4 competitive mechanics x 3 variants, 1 asymmetric
        /// mechanic x 2 variants, 1 coop mechanic x 2 variants — enough breadth
        /// that a 7-round session never exhausts any constraint.
        /// </summary>
        private static List<MicrogameDefinitionV2> FullPool()
        {
            var pool = new List<MicrogameDefinitionV2>();
            foreach (string mech in new[] { "esquiva", "apunta", "reacciona", "aporrea" })
                for (int v = 1; v <= 3; v++)
                    pool.Add(Def($"{mech}_v{v}", mech, MicrogameDynamics.Competitive));
            for (int v = 1; v <= 2; v++)
                pool.Add(Def($"persigue_v{v}", "persigue", MicrogameDynamics.Asym1v3));
            for (int v = 1; v <= 2; v++)
                pool.Add(Def($"sujeta_v{v}", "sujeta", MicrogameDynamics.Coop));
            return pool;
        }

        private static int[] Places(int p0, int p1, int p2, int p3) => new[] { p0, p1, p2, p3 };

        /// <summary>
        /// Competitive-only pool (no Coop/Asym1v3 definitions at all) for the two
        /// bias trigger-exactness tests below (TASK-035 review fix round, LOW-1 +
        /// LOW-3). With <see cref="FullPool"/>, the session-level schedule
        /// (CoopRound/AsymRound, both seed-derived) can coincidentally land Coop
        /// or Asym1v3 as the DESIRED dynamics for the exact round under test —
        /// silently filtering the candidate set down to sujeta/persigue only and
        /// excluding "esquiva" (the mechanic these tests bias toward) for
        /// whichever seeds happen to collide that way. That made the tests'
        /// correctness quietly depend on a specific seed's schedule NOT colliding
        /// (e.g. the original seed=7 relied on CoopRound(7)!=4, undocumented) —
        /// and it approximately halved the measured bias-shift statistical
        /// margin, since draws where the round was force-filtered to
        /// Coop-only could never hit "esquiva" regardless of bias. An
        /// all-competitive pool makes the schedule irrelevant: even if
        /// Coop/Asym1v3 is "desired" for the tested round, filtering for it
        /// yields zero candidates and falls back to Competitive immediately (see
        /// MicrogameSelector.Select's fallback chain), so these tests are
        /// decoupled from the scheduling RNG entirely — including from TASK-044's
        /// Derive migration this same fix round folds in.
        /// </summary>
        private static List<MicrogameDefinitionV2> CompetitiveOnlyPool()
        {
            var pool = new List<MicrogameDefinitionV2>();
            foreach (string mech in new[] { "esquiva", "apunta", "reacciona", "aporrea" })
                for (int v = 1; v <= 3; v++)
                    pool.Add(Def($"{mech}_v{v}", mech, MicrogameDynamics.Competitive));
            return pool;
        }

        /// <summary>
        /// Plays a full 7-round session for one seed: selects each round, appends
        /// a synthetic history record, and returns every selection including the
        /// final.
        ///
        /// MEDIUM-1(a) (TASK-035 review fix round): the rotation period was
        /// previously 1 (<c>rot=(seed+round)&amp;3</c>), which changes every round
        /// -- since consecutive rounds then NEVER share a rotation, the same seat
        /// can never land in 4th place twice in a row, so the GDD 6.4
        /// two-consecutive-4th bias trigger could NEVER fire from this corpus
        /// (the old doc comment's "bias occasionally engages" claim was
        /// provably false). Period 2 (<c>round/2</c>, integer division) makes
        /// several adjacent round pairs share the same rotation across the
        /// 1000-seed corpus, so the same seat's place repeats and the trigger
        /// genuinely engages for at least some seeds -- making the claim true.
        /// </summary>
        private static List<RoundSelection> PlaySession(
            IReadOnlyList<MicrogameDefinitionV2> pool, int seed, out List<RoundRecord> history)
        {
            history = new List<RoundRecord>();
            var selections = new List<RoundSelection>();

            for (int round = 1; round <= RoundsTotal; round++)
            {
                RoundSelection sel = MicrogameSelector.SelectRound(pool, seed, round, RoundsTotal, history);
                selections.Add(sel);

                int rot = (seed + round / 2) & 3;
                history.Add(new RoundRecord(
                    sel.Definition.Mechanic,
                    sel.Definition.Id,
                    Places(
                        1 + ((0 + rot) & 3),
                        1 + ((1 + rot) & 3),
                        1 + ((2 + rot) & 3),
                        1 + ((3 + rot) & 3))));
            }

            selections.Add(MicrogameSelector.SelectFinalRound(pool, seed, RoundsTotal, history));
            return selections;
        }

        // ── AC1: pure function of (seed, history) ────────────────────────────

        [Test]
        public void SelectRound_IsAPureFunction_SameInputsSameDraw()
        {
            var history = new List<RoundRecord>
            {
                new RoundRecord("esquiva", "esquiva_v1", Places(1, 2, 3, 4)),
            };

            RoundSelection a = MicrogameSelector.SelectRound(FullPool(), 42, 2, RoundsTotal, history);
            // Fresh pool instance + repeated calls: nothing may depend on hidden state.
            RoundSelection b = MicrogameSelector.SelectRound(FullPool(), 42, 2, RoundsTotal, history);
            RoundSelection c = MicrogameSelector.SelectRound(FullPool(), 42, 2, RoundsTotal, history);

            Assert.That(b.Definition.Id, Is.EqualTo(a.Definition.Id));
            Assert.That(c.Definition.Id, Is.EqualTo(a.Definition.Id));
            Assert.That(b.DifficultyMultiplier, Is.EqualTo(a.DifficultyMultiplier));
        }

        [Test]
        public void SelectRound_DifferentSeeds_ProduceDifferentSessions()
        {
            var pool = FullPool();
            var idsA = new List<string>();
            var idsB = new List<string>();
            foreach (RoundSelection s in PlaySession(pool, 1, out _)) idsA.Add(s.Definition.Id);
            foreach (RoundSelection s in PlaySession(pool, 2, out _)) idsB.Add(s.Definition.Id);

            Assert.That(idsA, Is.Not.EqualTo(idsB),
                "two seeds agreeing on all 8 draws means the seed is not feeding the draw");
        }

        // ── AC2 + AC3: quota and anti-repetition over a 1000-session corpus ──

        [Test]
        public void Corpus1000Sessions_QuotaAndAntiRepetitionHoldInEverySession()
        {
            var pool = FullPool();

            for (int seed = 0; seed < 1000; seed++)
            {
                List<RoundSelection> session = PlaySession(pool, seed, out _);

                int coop = 0, asym = 0, competitive = 0;
                var variantsSeen = new HashSet<string>(StringComparer.Ordinal);
                string prevMechanic = null;

                for (int i = 0; i < session.Count; i++)
                {
                    MicrogameDefinitionV2 def = session[i].Definition;

                    Assert.That(def.Mechanic, Is.Not.EqualTo(prevMechanic),
                        $"seed {seed}: mechanic '{def.Mechanic}' repeated in consecutive rounds ({i} and {i + 1})");
                    Assert.That(variantsSeen.Add(def.Id), Is.True,
                        $"seed {seed}: variant '{def.Id}' drawn twice in one session");
                    prevMechanic = def.Mechanic;

                    if (i >= RoundsTotal) continue; // the climax is outside the 7-round quota
                    switch (def.Dynamics)
                    {
                        case MicrogameDynamics.Coop: coop++;
                            Assert.That(i + 1, Is.EqualTo(3).Or.EqualTo(4),
                                $"seed {seed}: coop special landed in round {i + 1}; GDD 7.1 says round 3 or 4");
                            break;
                        case MicrogameDynamics.Asym1v3: asym++;
                            Assert.That(i + 1, Is.EqualTo(5).Or.EqualTo(6),
                                $"seed {seed}: asymmetric round landed in round {i + 1}; GDD 7.2 says round 5 or 6");
                            break;
                        default: competitive++; break;
                    }
                }

                // MEDIUM-1(b)/(c) (TASK-035 review fix round): the old "remainder
                // competitive" assert was tautological -- competitive is computed
                // as everything the switch didn't count as coop/asym, so
                // competitive==RoundsTotal-coop-asym held by construction, not as
                // a real invariant check. GDD 7.2 says the asymmetric round is
                // "1 por sesion" (exactly 1, not merely >=1, closing the AC's own
                // looser wording) -- mirrors the coop assert immediately above.
                Assert.That(coop, Is.EqualTo(1), $"seed {seed}: exactly 1 coop special per session (GDD 9.2)");
                Assert.That(asym, Is.EqualTo(1), $"seed {seed}: exactly 1 asymmetric round per session (GDD 7.2: '1 por sesion')");
            }
        }

        // ── AC4: §6.4 anti-frustration bias ──────────────────────────────────

        /// <summary>History where seat 2 shined at 'esquiva' (round 1) and then placed 4th twice (rounds 2-3).</summary>
        private static List<RoundRecord> TriggeredHistory() => new List<RoundRecord>
        {
            new RoundRecord("esquiva", "esquiva_v1", Places(2, 3, 1, 4)),
            new RoundRecord("aporrea", "aporrea_v1", Places(1, 2, 4, 3)),
            new RoundRecord("reacciona", "reacciona_v1", Places(1, 2, 4, 3)),
        };

        /// <summary>Same shape but seat 2 was 3rd in round 2 — no consecutive-4th trigger.</summary>
        private static List<RoundRecord> UntriggeredHistory() => new List<RoundRecord>
        {
            new RoundRecord("esquiva", "esquiva_v1", Places(2, 3, 1, 4)),
            new RoundRecord("aporrea", "aporrea_v1", Places(1, 2, 3, 4)),
            new RoundRecord("reacciona", "reacciona_v1", Places(1, 2, 4, 3)),
        };

        [Test]
        public void Bias_EngagesExactlyOnTwoConsecutiveFourthPlaces()
        {
            var pool = CompetitiveOnlyPool();

            RoundSelection triggered = MicrogameSelector.SelectRound(pool, 7, 4, RoundsTotal, TriggeredHistory());
            Assert.That(triggered.BiasSeat, Is.EqualTo(2), "seat 2 placed 4th in the last two rounds");
            Assert.That(triggered.BiasMechanic, Is.EqualTo("esquiva"),
                "the bias targets the seat's best in-session mechanic (place 1 at esquiva)");

            RoundSelection untriggered = MicrogameSelector.SelectRound(pool, 7, 4, RoundsTotal, UntriggeredHistory());
            Assert.That(untriggered.BiasSeat, Is.EqualTo(-1), "one 4th place is not enough (GDD 6.4: two consecutive)");
            Assert.That(untriggered.BiasMechanic, Is.Null);
        }

        [Test]
        public void Bias_ShiftsSelectionProbability_WithoutGuaranteeingTheMechanic()
        {
            var pool = CompetitiveOnlyPool();
            int biasedHits = 0, unbiasedHits = 0;
            const int trials = 2000;

            for (int seed = 0; seed < trials; seed++)
            {
                if (MicrogameSelector.SelectRound(pool, seed, 4, RoundsTotal, TriggeredHistory())
                        .Definition.Mechanic == "esquiva")
                    biasedHits++;
                if (MicrogameSelector.SelectRound(pool, seed, 4, RoundsTotal, UntriggeredHistory())
                        .Definition.Mechanic == "esquiva")
                    unbiasedHits++;
            }

            double biased = biasedHits / (double)trials;
            double unbiased = unbiasedHits / (double)trials;

            Assert.That(biased, Is.GreaterThan(unbiased + 0.10),
                $"bias must measurably shift the draw toward the best-history mechanic (biased {biased:P1} vs unbiased {unbiased:P1})");
            Assert.That(biased, Is.LessThan(0.95),
                "GDD 6.4: a soft selection bias, never a guaranteed outcome");
        }

        [Test]
        public void Bias_NeverOverridesAntiRepetition()
        {
            var pool = FullPool();
            // Seat 2's best mechanic IS last round's mechanic: anti-repeat is a hard
            // constraint (GDD 9.2), so the bias may not resurrect it.
            var history = new List<RoundRecord>
            {
                new RoundRecord("aporrea", "aporrea_v1", Places(1, 2, 4, 3)),
                new RoundRecord("esquiva", "esquiva_v1", Places(2, 3, 4, 1)),
            };
            // seat 2: 4th twice; best history = esquiva? no — 4th at both. Best place 4
            // ties both mechanics; last round's esquiva is excluded, so the bias (if
            // any) must fall on 'aporrea' — and 'esquiva' must never be drawn.
            for (int seed = 0; seed < 200; seed++)
            {
                RoundSelection sel = MicrogameSelector.SelectRound(pool, seed, 3, RoundsTotal, history);
                Assert.That(sel.Definition.Mechanic, Is.Not.EqualTo("esquiva"),
                    $"seed {seed}: anti-repetition is hard; bias never overrides it");
            }
        }

        // ── AC5: difficulty ramp ─────────────────────────────────────────────

        [Test]
        public void DifficultyRamp_FollowsGdd91_AndFinalIsFixed()
        {
            var pool = FullPool();
            var history = new List<RoundRecord>();

            for (int round = 1; round <= RoundsTotal; round++)
            {
                RoundSelection sel = MicrogameSelector.SelectRound(pool, 11, round, RoundsTotal, history);
                Assert.That(sel.DifficultyMultiplier, Is.EqualTo(1f + 0.06f * (round - 1)).Within(1e-5f),
                    $"GDD 9.1: D(r) = 1 + 0.06(r-1) at round {round}");
                history.Add(new RoundRecord(sel.Definition.Mechanic, sel.Definition.Id, Places(1, 2, 3, 4)));
            }

            RoundSelection final = MicrogameSelector.SelectFinalRound(pool, 11, RoundsTotal, history);
            Assert.That(final.DifficultyMultiplier, Is.EqualTo(1.5f),
                "GDD 9.1: the climax microgame uses D_final = 1.5 fixed");
        }

        // ── Graceful degradation (documented fallbacks, Hito 1 pools) ────────

        [Test]
        public void PoolWithoutSpecialDynamics_FallsBackToCompetitive_NeverThrows()
        {
            // Today's real pool has only competitive v2 mechanics (REACCIONA, APUNTA).
            var pool = new List<MicrogameDefinitionV2>();
            foreach (string mech in new[] { "reacciona", "apunta", "esquiva" })
                for (int v = 1; v <= 3; v++)
                    pool.Add(Def($"{mech}_v{v}", mech, MicrogameDynamics.Competitive));

            List<RoundSelection> session = PlaySession(pool, 99, out _);

            Assert.That(session.Count, Is.EqualTo(RoundsTotal + 1));
            foreach (RoundSelection sel in session)
                Assert.That(sel.Definition.Dynamics, Is.EqualTo(MicrogameDynamics.Competitive));
        }

        [Test]
        public void ExhaustedPool_RelaxesConstraintsInsteadOfThrowing()
        {
            // One mechanic, one variant: both anti-repetition rules are unsatisfiable
            // from round 2 on. Liveness wins over constraints (documented relaxation).
            var pool = new List<MicrogameDefinitionV2> { Def("solo_v1", "solo", MicrogameDynamics.Competitive) };

            List<RoundSelection> session = null;
            Assert.DoesNotThrow(() => session = PlaySession(pool, 5, out _));
            foreach (RoundSelection sel in session)
                Assert.That(sel.Definition.Id, Is.EqualTo("solo_v1"));
        }

        // ── Input validation ─────────────────────────────────────────────────

        [Test]
        public void SelectRound_RejectsBadArguments()
        {
            var pool = FullPool();
            var empty = new List<MicrogameDefinitionV2>();
            var history = new List<RoundRecord>();

            Assert.Throws<ArgumentNullException>(
                () => MicrogameSelector.SelectRound(null, 1, 1, RoundsTotal, history));
            Assert.Throws<ArgumentException>(
                () => MicrogameSelector.SelectRound(empty, 1, 1, RoundsTotal, history));
            Assert.Throws<ArgumentNullException>(
                () => MicrogameSelector.SelectRound(pool, 1, 1, RoundsTotal, null));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MicrogameSelector.SelectRound(pool, 1, 0, RoundsTotal, history));
        }

        [Test]
        public void RoundRecord_RequiresFourPlaces()
        {
            Assert.Throws<ArgumentNullException>(() => new RoundRecord("m", "v", null));
            Assert.Throws<ArgumentException>(() => new RoundRecord("m", "v", new[] { 1, 2, 3 }));
        }

        [Test]
        public void RoundRecord_ClonesThePlacesArray_MutatingTheCallersArrayDoesNotAffectIt()
        {
            // LOW-2 (TASK-035 review fix round, replay purity): RoundRecord used to
            // alias the caller's array directly. Session history is long-lived and
            // fed back into future SelectRound calls -- if a caller mutated the
            // array it originally passed in (e.g. reusing a scratch buffer across
            // rounds), every already-recorded RoundRecord would silently change
            // too, corrupting replay determinism.
            var places = new[] { 1, 2, 3, 4 };
            var record = new RoundRecord("esquiva", "esquiva_v1", places);

            places[0] = 99;

            Assert.That(record.Places[0], Is.EqualTo(1), "RoundRecord must not alias the caller's array");
            Assert.That(record.Places, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }
    }
}
