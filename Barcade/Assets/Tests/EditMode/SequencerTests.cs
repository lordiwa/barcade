using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-005 — MicrogameSequencerDirector: selection, ramp, and scoring.
    ///
    /// AC1: Selects next microgame from pool using seeded RNG with anti-repeat.
    ///      Pool-size-1 edge case: single entry is always allowed (no infinite loop).
    /// AC2: Difficulty/speed ramps via effectiveDuration = baseDuration * pow(decay, round)
    ///      clamped to minDuration. Curve is explicit, deterministic, and testable.
    /// AC3: After each microgame, per-player results feed ScoreModel.RecordRound.
    /// AC4: Fixed seed yields a fixed, asserted selection sequence; anti-repeat holds;
    ///      ramp values match the formula; ScoreModel reflects fed results.
    ///
    /// TASK-003 carry-forward (MEDIUM): ScoreModel.RecordRound input validation.
    ///   - Throws ArgumentNullException when results is null.
    ///   - Throws ArgumentException when results.Length != 4.
    /// </summary>
    [TestFixture]
    public class SequencerTests
    {
        // ────────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────────

        private static MicrogameDescriptor Desc(string id, float baseDuration = 5f, int difficulty = 1)
            => new MicrogameDescriptor(id, baseDuration, difficulty);

        private static SequencerDirector MakeDirector(
            IReadOnlyList<MicrogameDescriptor> pool,
            int seed,
            RampSettings ramp = null)
        {
            return new SequencerDirector(
                pool,
                new SeededRandom(seed),
                ramp ?? RampSettings.Default);
        }

        /// <summary>
        /// TASK-057: wraps a real <see cref="SeededRandom"/> and counts
        /// <see cref="NextInt"/> calls, so a test can assert exactly how many
        /// RNG draws <see cref="SequencerDirector.PickNext"/> consumes per
        /// decision (GDD P3: "una decisión -&gt; como máximo una tirada").
        /// </summary>
        private sealed class CountingRandom : ISeededRandom
        {
            private readonly SeededRandom _inner;
            public int NextIntCallCount { get; private set; }

            public CountingRandom(int seed) => _inner = new SeededRandom(seed);

            public float NextFloat() => _inner.NextFloat();

            public int NextInt(int minInclusive, int maxExclusive)
            {
                NextIntCallCount++;
                return _inner.NextInt(minInclusive, maxExclusive);
            }

            public Direction2D NextDirection() => _inner.NextDirection();
        }

        // ────────────────────────────────────────────────────────────────────────
        // ScoreModel.RecordRound validation (TASK-003 carry-forward)
        // ────────────────────────────────────────────────────────────────────────

        [Test]
        public void ScoreModel_RecordRound_NullResults_ThrowsArgumentNullException()
        {
            var model = new ScoreModel();
            Assert.Throws<ArgumentNullException>(() => model.RecordRound(null));
        }

        [Test]
        public void ScoreModel_RecordRound_WrongLength_ThrowsArgumentException()
        {
            var model = new ScoreModel();
            Assert.Throws<ArgumentException>(() => model.RecordRound(new bool[] { true, false, true }));
        }

        [Test]
        public void ScoreModel_RecordRound_LengthFive_ThrowsArgumentException()
        {
            var model = new ScoreModel();
            Assert.Throws<ArgumentException>(() => model.RecordRound(new bool[] { true, false, true, false, true }));
        }

        [Test]
        public void ScoreModel_RecordRound_EmptyArray_ThrowsArgumentException()
        {
            var model = new ScoreModel();
            Assert.Throws<ArgumentException>(() => model.RecordRound(new bool[0]));
        }

        [Test]
        public void ScoreModel_RecordRound_ExactlyFourElements_DoesNotThrow()
        {
            var model = new ScoreModel();
            Assert.DoesNotThrow(() => model.RecordRound(new bool[] { true, false, true, false }));
        }

        // ────────────────────────────────────────────────────────────────────────
        // AC1: MicrogameDescriptor is pure data
        // ────────────────────────────────────────────────────────────────────────

        [Test]
        public void MicrogameDescriptor_StoresProperties()
        {
            var desc = new MicrogameDescriptor("dodge", 4.5f, 2);
            Assert.That(desc.Id,           Is.EqualTo("dodge"));
            Assert.That(desc.BaseDuration, Is.EqualTo(4.5f).Within(0.0001f));
            Assert.That(desc.Difficulty,   Is.EqualTo(2));
        }

        // ────────────────────────────────────────────────────────────────────────
        // AC1: Selection — fixed seed yields exact asserted sequence
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// With pool=["A","B","C"] and seed=42, the SeededRandom-driven
        /// anti-repeat selection must produce: C A B A C B A C A B
        /// (computed from System.Random(42) with anti-repeat loop).
        /// </summary>
        [Test]
        public void PickNext_FixedSeed_ProducesExactDeterministicSequence()
        {
            var pool = new[]
            {
                Desc("A"), Desc("B"), Desc("C")
            };
            var director = MakeDirector(pool, seed: 42);

            // Recalibrated for TASK-044: SeededRandom is now PCG32 (GDD 13), so the
            // seed-42 pick sequence changed once, permanently (algorithm-pinned).
            var expected = new[] { "A", "C", "B", "C", "A", "C", "A", "B", "A", "C" };
            for (int i = 0; i < expected.Length; i++)
            {
                string picked = director.PickNext().Id;
                Assert.That(picked, Is.EqualTo(expected[i]),
                    $"Round {i}: expected '{expected[i]}', got '{picked}'");
                director.AdvanceRound(new bool[] { true, false, true, false });
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // AC1: Anti-repeat — never same id twice in a row (multi-round run)
        // ────────────────────────────────────────────────────────────────────────

        [Test]
        public void PickNext_AntiRepeat_NeverSameIdTwiceInARow_LongRun()
        {
            var pool = new[]
            {
                Desc("A"), Desc("B"), Desc("C"), Desc("D")
            };
            var director = MakeDirector(pool, seed: 7);

            string lastId = null;
            for (int round = 0; round < 200; round++)
            {
                string id = director.PickNext().Id;
                if (lastId != null)
                    Assert.That(id, Is.Not.EqualTo(lastId),
                        $"Anti-repeat violated at round {round}: '{id}' repeated after '{lastId}'");
                lastId = id;
                director.AdvanceRound(new bool[] { false, false, false, false });
            }
        }

        [Test]
        public void PickNext_AntiRepeat_TwoItemPool_AlternatesCorrectly()
        {
            // With only 2 items the anti-repeat must force strict alternation.
            var pool = new[] { Desc("X"), Desc("Y") };
            var director = MakeDirector(pool, seed: 100);

            // Precomputed for PCG32 (TASK-044): seed=100 with [X,Y] → X Y X Y X Y X Y X Y
            var expected = new[] { "X", "Y", "X", "Y", "X", "Y", "X", "Y", "X", "Y" };
            for (int i = 0; i < expected.Length; i++)
            {
                string picked = director.PickNext().Id;
                Assert.That(picked, Is.EqualTo(expected[i]),
                    $"Round {i}: expected '{expected[i]}', got '{picked}'");
                director.AdvanceRound(new bool[] { true, true, true, true });
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // AC1: Single-item pool edge case
        // ────────────────────────────────────────────────────────────────────────

        [Test]
        public void PickNext_SingleItemPool_AlwaysReturnsThatItem_NoInfiniteLoop()
        {
            var pool = new[] { Desc("SOLO") };
            var director = MakeDirector(pool, seed: 999);

            for (int i = 0; i < 20; i++)
            {
                string picked = director.PickNext().Id;
                Assert.That(picked, Is.EqualTo("SOLO"),
                    $"Round {i}: single-item pool must always return 'SOLO'");
                director.AdvanceRound(new bool[] { true, false, false, false });
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // TASK-057 (GDD P3): PickNext consumes at most one RNG draw per decision
        // ────────────────────────────────────────────────────────────────────────

        [Test]
        public void PickNext_MultiItemPool_ConsumesExactlyOneRngDrawPerDecision()
        {
            // AC1 (GDD P3: "una decisión -> como máximo una tirada. Prohibido
            // 'objeto aleatorio que además falla aleatoriamente'"): PickNext()
            // must never consume more than one RNG draw per decision. A 2-item
            // pool over many rounds makes at least one repeat-collision under
            // the OLD re-roll algorithm a near statistical certainty (~50%
            // chance every round), so this genuinely exercises the re-roll
            // path rather than getting lucky and never re-rolling.
            var pool = new[] { Desc("X"), Desc("Y") };
            var rng = new CountingRandom(seed: 12345);
            var director = new SequencerDirector(pool, rng, RampSettings.Default);

            const int rounds = 100;
            for (int i = 0; i < rounds; i++)
            {
                director.PickNext();
                director.AdvanceRound(new bool[] { false, false, false, false });
            }

            Assert.That(rng.NextIntCallCount, Is.EqualTo(rounds),
                "each PickNext() decision must consume exactly one RNG draw -- a re-roll loop would exceed this");
        }

        [Test]
        public void PickNext_SingleItemPool_ConsumesNoRngDraws()
        {
            // AC2 (degenerate single-candidate case): a repeat is unavoidable
            // with only one microgame in the pool -- PickNext() must return it
            // directly without ever touching the RNG.
            var pool = new[] { Desc("SOLO") };
            var rng = new CountingRandom(seed: 999);
            var director = new SequencerDirector(pool, rng, RampSettings.Default);

            for (int i = 0; i < 20; i++)
            {
                director.PickNext();
                director.AdvanceRound(new bool[] { true, false, false, false });
            }

            Assert.That(rng.NextIntCallCount, Is.EqualTo(0),
                "a single-item pool must never consume an RNG draw -- the pick is forced regardless");
        }

        // ────────────────────────────────────────────────────────────────────────
        // AC2: Ramp — effective duration formula
        // effectiveDuration = Max(minDuration, baseDuration * pow(decayPerRound, round))
        // ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Ramp_RoundZero_EffectiveDurationEqualsBaseDuration()
        {
            var ramp = new RampSettings(decayPerRound: 0.95f, minDuration: 1.0f);
            float eff = ramp.EffectiveDuration(baseDuration: 5f, roundIndex: 0);
            Assert.That(eff, Is.EqualTo(5.0f).Within(0.0001f));
        }

        [Test]
        public void Ramp_RoundOne_AppliesOneDecayStep()
        {
            var ramp = new RampSettings(decayPerRound: 0.95f, minDuration: 1.0f);
            // 5 * 0.95^1 = 4.75
            float eff = ramp.EffectiveDuration(baseDuration: 5f, roundIndex: 1);
            Assert.That(eff, Is.EqualTo(4.75f).Within(0.0001f));
        }

        [Test]
        public void Ramp_Round5_MatchesFormula()
        {
            var ramp = new RampSettings(decayPerRound: 0.95f, minDuration: 1.0f);
            // 5 * 0.95^5 = 5 * 0.7737... = 3.8689...
            float expected = 5f * (float)Math.Pow(0.95, 5);
            float eff = ramp.EffectiveDuration(baseDuration: 5f, roundIndex: 5);
            Assert.That(eff, Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void Ramp_HighRound_ClampsToMinDuration()
        {
            var ramp = new RampSettings(decayPerRound: 0.95f, minDuration: 1.0f);
            // After enough rounds decay drives below minDuration=1
            float eff = ramp.EffectiveDuration(baseDuration: 5f, roundIndex: 100);
            Assert.That(eff, Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void Ramp_SpeedMultiplier_InverseOfDurationRatio()
        {
            var ramp = new RampSettings(decayPerRound: 0.95f, minDuration: 1.0f);
            float eff = ramp.EffectiveDuration(baseDuration: 5f, roundIndex: 3);
            float speed = ramp.SpeedMultiplier(baseDuration: 5f, roundIndex: 3);
            // speedMultiplier * effectiveDuration == baseDuration (when above floor)
            Assert.That(speed * eff, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void Ramp_Default_HasExpectedConstants()
        {
            var ramp = RampSettings.Default;
            Assert.That(ramp.DecayPerRound, Is.EqualTo(0.95f).Within(0.0001f));
            Assert.That(ramp.MinDuration,   Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void Ramp_DurationStrictlyDecreasesUntilFloor()
        {
            var ramp = new RampSettings(decayPerRound: 0.90f, minDuration: 2.0f);
            float prev = ramp.EffectiveDuration(10f, 0);
            for (int r = 1; r <= 50; r++)
            {
                float curr = ramp.EffectiveDuration(10f, r);
                // Duration must be <= previous (monotone non-increasing)
                Assert.That(curr, Is.LessThanOrEqualTo(prev + 0.0001f),
                    $"Duration increased at round {r}: {prev} -> {curr}");
                Assert.That(curr, Is.GreaterThanOrEqualTo(2.0f - 0.0001f),
                    $"Duration dropped below floor at round {r}: {curr}");
                prev = curr;
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // AC2: Director exposes ramp context after PickNext
        // ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Director_RoundContext_EffectiveDurationMatchesRamp()
        {
            var pool = new[] { Desc("A", baseDuration: 6f), Desc("B", baseDuration: 4f) };
            var ramp = new RampSettings(decayPerRound: 0.80f, minDuration: 1.0f);
            var director = MakeDirector(pool, seed: 1, ramp: ramp);

            // Round 0
            MicrogameDescriptor desc0 = director.PickNext();
            SequencerRoundContext ctx0 = director.CurrentRoundContext;
            float expected0 = ramp.EffectiveDuration(desc0.BaseDuration, roundIndex: 0);
            Assert.That(ctx0.EffectiveDuration, Is.EqualTo(expected0).Within(0.0001f));
            Assert.That(ctx0.RoundIndex,        Is.EqualTo(0));

            director.AdvanceRound(new bool[] { true, false, true, false });

            // Round 1
            MicrogameDescriptor desc1 = director.PickNext();
            SequencerRoundContext ctx1 = director.CurrentRoundContext;
            float expected1 = ramp.EffectiveDuration(desc1.BaseDuration, roundIndex: 1);
            Assert.That(ctx1.EffectiveDuration, Is.EqualTo(expected1).Within(0.0001f));
            Assert.That(ctx1.RoundIndex,        Is.EqualTo(1));
        }

        // ────────────────────────────────────────────────────────────────────────
        // AC3: AdvanceRound feeds ScoreModel with per-player results
        // ────────────────────────────────────────────────────────────────────────

        [Test]
        public void AdvanceRound_FeedsScoreModelCorrectly()
        {
            var pool = new[] { Desc("A"), Desc("B") };
            var director = MakeDirector(pool, seed: 0);

            director.PickNext();
            director.AdvanceRound(new bool[] { true, false, true, false });

            var score = director.Score;
            Assert.That(score.GetWins(PlayerSlot.Rojo),     Is.EqualTo(1));
            Assert.That(score.GetWins(PlayerSlot.Azul),     Is.EqualTo(0));
            Assert.That(score.GetWins(PlayerSlot.Amarillo), Is.EqualTo(1));
            Assert.That(score.GetWins(PlayerSlot.Verde),    Is.EqualTo(0));

            Assert.That(score.GetLosses(PlayerSlot.Rojo),   Is.EqualTo(0));
            Assert.That(score.GetLosses(PlayerSlot.Azul),   Is.EqualTo(1));
            Assert.That(score.GetLosses(PlayerSlot.Amarillo), Is.EqualTo(0));
            Assert.That(score.GetLosses(PlayerSlot.Verde),  Is.EqualTo(1));
        }

        [Test]
        public void AdvanceRound_MultipleRounds_AccumulatesScoreCorrectly()
        {
            var pool = new[] { Desc("A"), Desc("B"), Desc("C") };
            var director = MakeDirector(pool, seed: 5);

            director.PickNext();
            director.AdvanceRound(new bool[] { true, false, false, false });

            director.PickNext();
            director.AdvanceRound(new bool[] { true, false, false, false });

            director.PickNext();
            director.AdvanceRound(new bool[] { false, true, false, false });

            var score = director.Score;
            Assert.That(score.GetWins(PlayerSlot.Rojo),  Is.EqualTo(2));
            Assert.That(score.GetWins(PlayerSlot.Azul),  Is.EqualTo(1));
            Assert.That(score.GetGamesPlayed(PlayerSlot.Rojo), Is.EqualTo(3));
        }

        // ────────────────────────────────────────────────────────────────────────
        // AC4: Determinism — same seed, same pool → identical sequence across instances
        // ────────────────────────────────────────────────────────────────────────

        [Test]
        public void TwoDirectors_SameSeed_ProduceIdenticalSelectionSequences()
        {
            var pool = new[]
            {
                Desc("game1"), Desc("game2"), Desc("game3"), Desc("game4")
            };

            var dirA = MakeDirector(pool, seed: 12345);
            var dirB = MakeDirector(pool, seed: 12345);

            for (int i = 0; i < 30; i++)
            {
                string idA = dirA.PickNext().Id;
                string idB = dirB.PickNext().Id;
                Assert.That(idA, Is.EqualTo(idB), $"Round {i}: directors diverged");
                dirA.AdvanceRound(new bool[] { true, true, true, true });
                dirB.AdvanceRound(new bool[] { true, true, true, true });
            }
        }

        [Test]
        public void TwoDirectors_DifferentSeeds_ProduceDifferentSequences()
        {
            var pool = new[]
            {
                Desc("g1"), Desc("g2"), Desc("g3"), Desc("g4"), Desc("g5")
            };

            var dirA = MakeDirector(pool, seed: 1);
            var dirB = MakeDirector(pool, seed: 9999);

            var seqA = new List<string>();
            var seqB = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                seqA.Add(dirA.PickNext().Id);
                seqB.Add(dirB.PickNext().Id);
                dirA.AdvanceRound(new bool[] { false, false, false, false });
                dirB.AdvanceRound(new bool[] { false, false, false, false });
            }

            // With different seeds the 20-pick sequences should differ at some point.
            bool anyDiff = false;
            for (int i = 0; i < 20; i++)
                if (seqA[i] != seqB[i]) { anyDiff = true; break; }

            Assert.That(anyDiff, Is.True,
                "Different seeds must produce different sequences (statistically certain with 5-item pool over 20 picks)");
        }

        // ────────────────────────────────────────────────────────────────────────
        // AC3 + AC4: Score accuracy after deterministic sequence
        // ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Score_AfterKnownSequence_MatchesManualTally()
        {
            // 3 rounds, all Rojo wins, no one else does.
            var pool = new[] { Desc("A"), Desc("B") };
            var director = MakeDirector(pool, seed: 77);

            bool[] rojoWins = new bool[] { true, false, false, false };

            for (int r = 0; r < 5; r++)
            {
                director.PickNext();
                director.AdvanceRound(rojoWins);
            }

            var score = director.Score;
            Assert.That(score.GetWins(PlayerSlot.Rojo),   Is.EqualTo(5));
            Assert.That(score.GetLosses(PlayerSlot.Azul), Is.EqualTo(5));
            Assert.That(score.GetSingleLeader(),           Is.EqualTo(PlayerSlot.Rojo));
        }

        // ────────────────────────────────────────────────────────────────────────
        // Anti-repeat exhaustive: long run with many pool sizes
        // ────────────────────────────────────────────────────────────────────────

        [Test]
        public void AntiRepeat_FiveItemPool_NeverRepeats_50Rounds()
        {
            var pool = new[]
            {
                Desc("g1"), Desc("g2"), Desc("g3"), Desc("g4"), Desc("g5")
            };
            var director = MakeDirector(pool, seed: 314159);

            string last = null;
            for (int r = 0; r < 50; r++)
            {
                string id = director.PickNext().Id;
                Assert.That(id, Is.Not.EqualTo(last), $"Repeat at round {r}");
                last = id;
                director.AdvanceRound(new bool[] { true, false, true, false });
            }
        }
    }
}
