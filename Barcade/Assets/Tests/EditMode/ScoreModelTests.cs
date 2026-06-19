using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-003 — ScoreModel: variance-friendly per-player win/loss tracking.
    ///
    /// AC1: Records wins/losses per player for each microgame round (4 players via PlayerSlot).
    /// AC2: Exposes per-player totals (Wins/Losses/GamesPlayed), WinRate, and LeadingPlayer
    ///      with deterministic tie handling (GetLeaders returns ALL tied leaders;
    ///      GetSingleLeader returns a deterministic pick among ties).
    /// AC3: Transparent accumulation — raw counts only, no hidden multipliers, no randomness.
    /// AC4: Tests cover: accumulation across many rounds, leader selection, tie handling
    ///      (incl. all-tied and empty), WinRate math (incl. zero-games guard), initial state.
    /// </summary>
    [TestFixture]
    public class ScoreModelTests
    {
        // ── Helpers ─────────────────────────────────────────────────────────────

        // Record a single round with a specific bool[4] (indexed by (int)PlayerSlot).
        private static bool[] Wins(bool rojo, bool azul, bool amarillo, bool verde)
            => new bool[] { rojo, azul, amarillo, verde };

        // ── AC4 (initial / empty state) ─────────────────────────────────────────

        [Test]
        public void InitialState_AllWinsAndLossesAreZero()
        {
            var model = new ScoreModel();
            foreach (PlayerSlot slot in System.Enum.GetValues(typeof(PlayerSlot)))
            {
                Assert.That(model.GetWins(slot), Is.EqualTo(0),   $"{slot} wins should be 0");
                Assert.That(model.GetLosses(slot), Is.EqualTo(0), $"{slot} losses should be 0");
                Assert.That(model.GetGamesPlayed(slot), Is.EqualTo(0), $"{slot} games should be 0");
            }
        }

        [Test]
        public void InitialState_WinRate_IsZeroForAllPlayers()
        {
            var model = new ScoreModel();
            foreach (PlayerSlot slot in System.Enum.GetValues(typeof(PlayerSlot)))
                Assert.That(model.WinRate(slot), Is.EqualTo(0f), $"{slot} WinRate should be 0 when no games played");
        }

        [Test]
        public void InitialState_GetLeaders_ReturnsAllFourPlayers()
        {
            // With no rounds played all players are tied at 0 wins; all are leaders.
            var model = new ScoreModel();
            var leaders = model.GetLeaders();
            Assert.That(leaders.Count, Is.EqualTo(4));
        }

        [Test]
        public void InitialState_GetSingleLeader_ReturnsDeterministicSlot()
        {
            // Must not throw and must return a valid slot.
            var model = new ScoreModel();
            PlayerSlot leader = model.GetSingleLeader();
            Assert.That(System.Enum.IsDefined(typeof(PlayerSlot), leader), Is.True);
        }

        // ── AC1: basic record-a-round accumulation ───────────────────────────────

        [Test]
        public void RecordRound_OneWinner_IncrementsWinForThatPlayer()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true, false, false, false));
            Assert.That(model.GetWins(PlayerSlot.Rojo), Is.EqualTo(1));
        }

        [Test]
        public void RecordRound_OneWinner_IncrementsLossForNonWinners()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true, false, false, false));
            Assert.That(model.GetLosses(PlayerSlot.Azul),    Is.EqualTo(1));
            Assert.That(model.GetLosses(PlayerSlot.Amarillo), Is.EqualTo(1));
            Assert.That(model.GetLosses(PlayerSlot.Verde),   Is.EqualTo(1));
        }

        [Test]
        public void RecordRound_AllFourWin_IncrementsWinsForAll()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true, true, true, true));
            foreach (PlayerSlot slot in System.Enum.GetValues(typeof(PlayerSlot)))
            {
                Assert.That(model.GetWins(slot), Is.EqualTo(1), $"{slot} should have 1 win");
                Assert.That(model.GetLosses(slot), Is.EqualTo(0), $"{slot} should have 0 losses");
            }
        }

        [Test]
        public void RecordRound_AllFourLose_IncrementsLossesForAll()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(false, false, false, false));
            foreach (PlayerSlot slot in System.Enum.GetValues(typeof(PlayerSlot)))
            {
                Assert.That(model.GetWins(slot), Is.EqualTo(0), $"{slot} should have 0 wins");
                Assert.That(model.GetLosses(slot), Is.EqualTo(1), $"{slot} should have 1 loss");
            }
        }

        [Test]
        public void RecordRound_GamesPlayedIncrementsByOneForAllPlayers()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true, false, true, false));
            foreach (PlayerSlot slot in System.Enum.GetValues(typeof(PlayerSlot)))
                Assert.That(model.GetGamesPlayed(slot), Is.EqualTo(1), $"{slot} should have 1 game played");
        }

        [Test]
        public void RecordRound_WinsAndLossesSumToGamesPlayed()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true, false, true, false));
            model.RecordRound(Wins(false, true, false, true));
            model.RecordRound(Wins(true, true, false, false));

            foreach (PlayerSlot slot in System.Enum.GetValues(typeof(PlayerSlot)))
            {
                int games = model.GetGamesPlayed(slot);
                int wins  = model.GetWins(slot);
                int losses = model.GetLosses(slot);
                Assert.That(wins + losses, Is.EqualTo(games),
                    $"{slot}: wins({wins}) + losses({losses}) != gamesPlayed({games})");
            }
        }

        // ── AC3: transparent accumulation — no hidden multipliers ───────────────

        [Test]
        public void AccumulationAcrossManyRounds_RawCountsAreExact()
        {
            var model = new ScoreModel();

            // Drive 100 rounds where only Rojo wins
            for (int i = 0; i < 100; i++)
                model.RecordRound(Wins(true, false, false, false));

            Assert.That(model.GetWins(PlayerSlot.Rojo),         Is.EqualTo(100));
            Assert.That(model.GetLosses(PlayerSlot.Rojo),       Is.EqualTo(0));
            Assert.That(model.GetWins(PlayerSlot.Azul),         Is.EqualTo(0));
            Assert.That(model.GetLosses(PlayerSlot.Azul),       Is.EqualTo(100));
            Assert.That(model.GetGamesPlayed(PlayerSlot.Rojo),  Is.EqualTo(100));
        }

        [Test]
        public void AccumulationAcrossMixedRounds_CountsMatchManualTally()
        {
            var model = new ScoreModel();

            // Round sequence: Rojo wins 3, Azul wins 2, Amarillo wins 1, Verde wins 0
            model.RecordRound(Wins(true,  false, false, false));
            model.RecordRound(Wins(true,  false, false, false));
            model.RecordRound(Wins(true,  false, false, false));
            model.RecordRound(Wins(false, true,  false, false));
            model.RecordRound(Wins(false, true,  false, false));
            model.RecordRound(Wins(false, false, true,  false));

            Assert.That(model.GetWins(PlayerSlot.Rojo),     Is.EqualTo(3));
            Assert.That(model.GetWins(PlayerSlot.Azul),     Is.EqualTo(2));
            Assert.That(model.GetWins(PlayerSlot.Amarillo), Is.EqualTo(1));
            Assert.That(model.GetWins(PlayerSlot.Verde),    Is.EqualTo(0));

            Assert.That(model.GetLosses(PlayerSlot.Rojo),     Is.EqualTo(3));
            Assert.That(model.GetLosses(PlayerSlot.Azul),     Is.EqualTo(4));
            Assert.That(model.GetLosses(PlayerSlot.Amarillo), Is.EqualTo(5));
            Assert.That(model.GetLosses(PlayerSlot.Verde),    Is.EqualTo(6));

            Assert.That(model.GetGamesPlayed(PlayerSlot.Rojo), Is.EqualTo(6));
        }

        // ── AC2: WinRate math ────────────────────────────────────────────────────

        [Test]
        public void WinRate_ZeroGames_ReturnsZero_NoDivisionByZero()
        {
            var model = new ScoreModel();
            // Must not throw; must return 0f
            float rate = model.WinRate(PlayerSlot.Azul);
            Assert.That(rate, Is.EqualTo(0f));
        }

        [Test]
        public void WinRate_AllWins_ReturnsOne()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true, true, true, true));
            model.RecordRound(Wins(true, true, true, true));
            Assert.That(model.WinRate(PlayerSlot.Rojo), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void WinRate_HalfWins_ReturnsPointFive()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true,  false, false, false));
            model.RecordRound(Wins(false, false, false, false));

            Assert.That(model.WinRate(PlayerSlot.Rojo), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void WinRate_ThreeWinsInFourGames_IsPointSevenFive()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true,  false, false, false));
            model.RecordRound(Wins(true,  false, false, false));
            model.RecordRound(Wins(true,  false, false, false));
            model.RecordRound(Wins(false, false, false, false));

            Assert.That(model.WinRate(PlayerSlot.Rojo), Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void WinRate_AllLosses_ReturnsZero()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(false, false, false, false));
            model.RecordRound(Wins(false, false, false, false));
            Assert.That(model.WinRate(PlayerSlot.Amarillo), Is.EqualTo(0f).Within(0.0001f));
        }

        // ── AC2: leader selection ────────────────────────────────────────────────

        [Test]
        public void GetLeaders_SingleLeader_ReturnsThatPlayerOnly()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true, false, false, false));
            model.RecordRound(Wins(true, false, false, false));

            var leaders = model.GetLeaders();
            Assert.That(leaders.Count, Is.EqualTo(1));
            Assert.That(leaders[0], Is.EqualTo(PlayerSlot.Rojo));
        }

        [Test]
        public void GetSingleLeader_SingleLeader_ReturnsThatPlayer()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(false, true, false, false));

            Assert.That(model.GetSingleLeader(), Is.EqualTo(PlayerSlot.Azul));
        }

        [Test]
        public void GetLeaders_TwoTied_ReturnsBoth()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true,  true,  false, false));
            model.RecordRound(Wins(true,  true,  false, false));

            var leaders = model.GetLeaders();
            Assert.That(leaders.Count, Is.EqualTo(2));
            Assert.That(leaders, Does.Contain(PlayerSlot.Rojo));
            Assert.That(leaders, Does.Contain(PlayerSlot.Azul));
        }

        [Test]
        public void GetLeaders_AllTied_ReturnsFourPlayers()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true, true, true, true));
            model.RecordRound(Wins(true, true, true, true));

            var leaders = model.GetLeaders();
            Assert.That(leaders.Count, Is.EqualTo(4));
        }

        [Test]
        public void GetLeaders_AllTiedAtZero_ReturnsFourPlayers()
        {
            // No rounds played — all tied at 0
            var model = new ScoreModel();
            var leaders = model.GetLeaders();
            Assert.That(leaders.Count, Is.EqualTo(4));
        }

        [Test]
        public void GetLeaders_ThreeTied_ReturnsThree()
        {
            var model = new ScoreModel();
            // Rojo, Azul, Amarillo each win once; Verde never wins
            model.RecordRound(Wins(true,  false, false, false));
            model.RecordRound(Wins(false, true,  false, false));
            model.RecordRound(Wins(false, false, true,  false));

            var leaders = model.GetLeaders();
            Assert.That(leaders.Count, Is.EqualTo(3));
            Assert.That(leaders, Does.Not.Contain(PlayerSlot.Verde));
        }

        // ── AC2: deterministic single-leader tie-break ───────────────────────────

        [Test]
        public void GetSingleLeader_TwoTied_ReturnsDeterministicResult_SameEveryCall()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(true, true, false, false));

            // Must return the SAME slot on repeated calls (deterministic, not random).
            PlayerSlot first  = model.GetSingleLeader();
            PlayerSlot second = model.GetSingleLeader();
            PlayerSlot third  = model.GetSingleLeader();
            Assert.That(second, Is.EqualTo(first));
            Assert.That(third,  Is.EqualTo(first));
        }

        [Test]
        public void GetSingleLeader_AllTied_ReturnsValidAndDeterministicSlot()
        {
            var model = new ScoreModel();
            // All 0 wins — deterministic tie-break must pick a valid slot
            PlayerSlot pick = model.GetSingleLeader();
            Assert.That(System.Enum.IsDefined(typeof(PlayerSlot), pick), Is.True);

            // Repeated calls must agree
            Assert.That(model.GetSingleLeader(), Is.EqualTo(pick));
        }

        [Test]
        public void GetSingleLeader_TiedLeaders_IsAlwaysOneOfTheTiedPlayers()
        {
            var model = new ScoreModel();
            model.RecordRound(Wins(false, true,  true, false));
            model.RecordRound(Wins(false, true,  true, false));

            var leaders = model.GetLeaders();
            PlayerSlot single = model.GetSingleLeader();
            Assert.That(leaders, Does.Contain(single));
        }

        // ── AC3 cross-check: no hidden randomness — accumulation is deterministic ─

        [Test]
        public void TwoModelsWithSameRounds_ProduceIdenticalCounts()
        {
            var a = new ScoreModel();
            var b = new ScoreModel();

            bool[][] rounds = new bool[][]
            {
                Wins(true,  false, true,  false),
                Wins(false, true,  false, true),
                Wins(true,  true,  false, false),
                Wins(false, false, false, false),
            };

            foreach (var round in rounds)
            {
                a.RecordRound(round);
                b.RecordRound(round);
            }

            foreach (PlayerSlot slot in System.Enum.GetValues(typeof(PlayerSlot)))
            {
                Assert.That(a.GetWins(slot),       Is.EqualTo(b.GetWins(slot)),       $"{slot} wins mismatch");
                Assert.That(a.GetLosses(slot),     Is.EqualTo(b.GetLosses(slot)),     $"{slot} losses mismatch");
                Assert.That(a.GetGamesPlayed(slot),Is.EqualTo(b.GetGamesPlayed(slot)),$"{slot} gamesPlayed mismatch");
            }
        }
    }
}
