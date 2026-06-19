using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-011 — Apunta (Aim and Fire) microgame pure logic.
    ///
    /// Win condition: fire (Button.Pressed) when aim angle is within tolerance of target direction.
    /// Boundary pinned:
    ///   - Aim angle exactly equal to angleTolerance → WIN (boundary inclusive).
    ///   - Aim angle just above angleTolerance → LOSS.
    /// </summary>
    [TestFixture]
    public class ApuntaMicrogameTests
    {
        private static readonly PlayerSlot[] AllPlayers =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private static IMicrogameContext MakeContext(int seed = 0, float difficulty = 0f)
            => new ApuntaTestContext(new SeededRandom(seed), AllPlayers, difficulty);

        // Build inputs where the given player aims in direction (aimX, aimY) and fires (Button.Pressed).
        private static IReadOnlyPlayerInputs AimAndFire(PlayerSlot who, float aimX, float aimY)
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(
                    s == who ? aimX : 0f,
                    s == who ? aimY : 0f,
                    s == who ? ButtonState.Pressed : ButtonState.Released);
            return new ApuntaInputs(map);
        }

        private static IReadOnlyPlayerInputs AllAimAndFire(float aimX, float aimY)
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(aimX, aimY, ButtonState.Pressed);
            return new ApuntaInputs(map);
        }

        private static IReadOnlyPlayerInputs NoInput()
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(0f, 0f, ButtonState.Released);
            return new ApuntaInputs(map);
        }

        private static IReadOnlyPlayerInputs AllAim(float aimX, float aimY)
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(aimX, aimY, ButtonState.Released);
            return new ApuntaInputs(map);
        }

        // ── CommandVerb ──────────────────────────────────────────────────────────

        [Test]
        public void ApuntaMicrogame_CommandVerb_IsApunta()
        {
            var game = new ApuntaMicrogame(angleTolerance: 15f);
            Assert.That(game.CommandVerb, Is.EqualTo("¡APUNTA!"));
        }

        // ── Win: aim directly at target, fire → win ──────────────────────────────

        [Test]
        public void ApuntaMicrogame_AimDirectlyAtTarget_AllWin()
        {
            // Target forced to point RIGHT (1,0). Aim right → angle=0 < tolerance → WIN.
            const float tolerance = 15f;
            var game = new ApuntaMicrogame(angleTolerance: tolerance);
            game.SetForcedTargetDirections(new (float, float)[]
            {
                (1f, 0f), (1f, 0f), (1f, 0f), (1f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));

            // Aim right and fire.
            game.Tick(0.1f, AllAimAndFire(1f, 0f));

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True,  "Direct aim → win");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.True,  "Direct aim → win");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.True,  "Direct aim → win");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.True,  "Direct aim → win");
        }

        // ── Loss: no fire before time limit ─────────────────────────────────────

        [Test]
        public void ApuntaMicrogame_NoFireBeforeTimeLimit_AllLose()
        {
            var game = new ApuntaMicrogame(angleTolerance: 15f);
            game.SetForcedTargetDirections(new (float, float)[]
            {
                (1f, 0f), (1f, 0f), (1f, 0f), (1f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));

            // Only stick movement, no button press.
            game.Tick(0.1f, AllAim(1f, 0f));
            game.Tick(0.1f, NoInput());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.False, "No fire → loss");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False, "No fire → loss");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False, "No fire → loss");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False, "No fire → loss");
        }

        // ── Boundary: aim exactly at tolerance edge → WIN ────────────────────────

        [Test]
        public void ApuntaMicrogame_AimExactlyAtToleranceEdge_IsWin()
        {
            // Target direction: right (1, 0) = 0 degrees.
            // Aim at exactly angleTolerance degrees off.
            const float tolerance = 30f; // degrees
            float angleRad = tolerance * (float)Math.PI / 180f;
            float aimX = (float)Math.Cos(angleRad);
            float aimY = (float)Math.Sin(angleRad);

            var game = new ApuntaMicrogame(angleTolerance: tolerance);
            game.SetForcedTargetDirections(new (float, float)[]
            {
                (1f, 0f), (1f, 0f), (1f, 0f), (1f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));
            game.Tick(0.1f, AllAimAndFire(aimX, aimY));

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.True,
                "Aim angle exactly at tolerance boundary should be a HIT (<=)");
        }

        // ── Boundary: aim just ABOVE tolerance → MISS ────────────────────────────

        [Test]
        public void ApuntaMicrogame_AimSlightlyAboveTolerance_IsMiss()
        {
            const float tolerance = 30f;
            float angleRad = (tolerance + 1f) * (float)Math.PI / 180f; // 31 degrees
            float aimX = (float)Math.Cos(angleRad);
            float aimY = (float)Math.Sin(angleRad);

            var game = new ApuntaMicrogame(angleTolerance: tolerance);
            game.SetForcedTargetDirections(new (float, float)[]
            {
                (1f, 0f), (1f, 0f), (1f, 0f), (1f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));
            game.Tick(0.1f, AllAimAndFire(aimX, aimY));

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "Aim angle just above tolerance should be a MISS (boundary exclusive above)");
        }

        // ── Win is latched: second fire after win does not change outcome ─────────

        [Test]
        public void ApuntaMicrogame_WinLatched_StaysWinAfterMiss()
        {
            const float tolerance = 15f;
            var game = new ApuntaMicrogame(angleTolerance: tolerance);
            game.SetForcedTargetDirections(new (float, float)[]
            {
                (1f, 0f), (1f, 0f), (1f, 0f), (1f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));

            // First fire: direct hit → win latched.
            game.Tick(0.1f, AllAimAndFire(1f, 0f));

            // Second fire: bad aim → but win was already latched.
            game.Tick(0.1f, AllAimAndFire(-1f, 0f)); // 180 degrees off

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.True,
                "Win must be latched — a subsequent miss must not override it");
        }

        // ── Miss is latched: first miss, no second chance ─────────────────────────

        [Test]
        public void ApuntaMicrogame_FirstFireMisses_NoSecondChance()
        {
            // Fire once (miss), then fire again (hit) — miss should be final.
            const float tolerance = 15f;
            var game = new ApuntaMicrogame(angleTolerance: tolerance);
            game.SetForcedTargetDirections(new (float, float)[]
            {
                (1f, 0f), (1f, 0f), (1f, 0f), (1f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));

            // Miss: aim away from target (opposite direction).
            game.Tick(0.1f, AllAimAndFire(-1f, 0f)); // 180 deg off — miss latched

            // Would-be hit: too late.
            game.Tick(0.1f, AllAimAndFire(1f, 0f)); // would be a hit but already missed

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "First miss is latched — subsequent would-be hit must not override it");
        }

        // ── Independent per-player targets ──────────────────────────────────────

        [Test]
        public void ApuntaMicrogame_DifferentTargetPerPlayer_EachScoresIndependently()
        {
            // Rojo target: right (1,0). Azul target: up (0,1).
            // Both aim right → Rojo hits, Azul misses.
            const float tolerance = 15f;
            var game = new ApuntaMicrogame(angleTolerance: tolerance);
            game.SetForcedTargetDirections(new (float, float)[]
            {
                (1f, 0f),  // Rojo: right
                (0f, 1f),  // Azul: up
                (1f, 0f),  // Amarillo: right
                (1f, 0f),  // Verde: right
            });

            game.Prepare(MakeContext(seed: 0));

            // Everyone fires aiming right.
            game.Tick(0.1f, AllAimAndFire(1f, 0f));

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True,  "Rojo aimed at its target → win");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False, "Azul aimed away from its target → miss");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.True,  "Amarillo aimed at its target → win");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.True,  "Verde aimed at its target → win");
        }

        // ── Difficulty scaling ───────────────────────────────────────────────────

        [Test]
        public void ApuntaMicrogame_Difficulty0_HasBaseAngleTolerance()
        {
            const float baseTolerance = 30f;
            var game = new ApuntaMicrogame(angleTolerance: baseTolerance);
            game.SetForcedTargetDirections(new (float, float)[]
                { (1f, 0f), (1f, 0f), (1f, 0f), (1f, 0f) });
            game.Prepare(MakeContext(seed: 0, difficulty: 0f));

            // At difficulty=0: tolerance = base*(1-0*0.75) = base.
            Assert.That(game.EffectiveAngleTolerance, Is.EqualTo(baseTolerance).Within(0.01f),
                "Difficulty 0 must preserve the base angle tolerance");
        }

        [Test]
        public void ApuntaMicrogame_Difficulty1_NarrowsAngleTolerance()
        {
            const float baseTolerance = 40f;
            var game = new ApuntaMicrogame(angleTolerance: baseTolerance);
            game.SetForcedTargetDirections(new (float, float)[]
                { (1f, 0f), (1f, 0f), (1f, 0f), (1f, 0f) });
            game.Prepare(MakeContext(seed: 0, difficulty: 1f));

            // At difficulty=1: tolerance = base*(1-0.75) = base*0.25 = 10.
            float expected = baseTolerance * 0.25f;
            Assert.That(game.EffectiveAngleTolerance, Is.EqualTo(expected).Within(0.01f),
                "Difficulty 1 must narrow tolerance to 25% of base");
        }

        [Test]
        public void ApuntaMicrogame_LowDifficultyWins_HighDifficultyLoses()
        {
            // Target: right (1,0). Aim: 20 degrees off.
            // - Easy (difficulty=0): tolerance=30° → 20° is within → WIN.
            // - Hard (difficulty=1): tolerance=30*0.25=7.5° → 20° exceeds → LOSS.
            const float baseTolerance = 30f;
            float aimAngleDeg = 20f;
            float aimRad = aimAngleDeg * (float)Math.PI / 180f;
            float aimX = (float)Math.Cos(aimRad);
            float aimY = (float)Math.Sin(aimRad);

            var gameEasy = new ApuntaMicrogame(angleTolerance: baseTolerance);
            gameEasy.SetForcedTargetDirections(new (float, float)[]
                { (1f, 0f), (1f, 0f), (1f, 0f), (1f, 0f) });
            gameEasy.Prepare(MakeContext(seed: 0, difficulty: 0f));
            gameEasy.Tick(0.1f, AllAimAndFire(aimX, aimY));
            var easyResult = gameEasy.Evaluate();
            gameEasy.Cleanup();

            var gameHard = new ApuntaMicrogame(angleTolerance: baseTolerance);
            gameHard.SetForcedTargetDirections(new (float, float)[]
                { (1f, 0f), (1f, 0f), (1f, 0f), (1f, 0f) });
            gameHard.Prepare(MakeContext(seed: 0, difficulty: 1f));
            gameHard.Tick(0.1f, AllAimAndFire(aimX, aimY));
            var hardResult = gameHard.Evaluate();
            gameHard.Cleanup();

            Assert.That(easyResult.IsWin(PlayerSlot.Rojo), Is.True,
                "20° aim within 30° tolerance (difficulty 0) → WIN");
            Assert.That(hardResult.IsWin(PlayerSlot.Rojo), Is.False,
                "20° aim outside 7.5° tolerance (difficulty 1) → LOSS");
        }

        // ── Evaluate frozen ──────────────────────────────────────────────────────

        [Test]
        public void ApuntaMicrogame_Evaluate_ReturnsFrozenResult()
        {
            var game = new ApuntaMicrogame(angleTolerance: 45f);
            game.SetForcedTargetDirections(new (float, float)[]
            {
                (1f, 0f), (1f, 0f), (1f, 0f), (1f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));
            game.Tick(0.1f, AllAimAndFire(1f, 0f));

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsFrozen, Is.True);
        }
    }

    // ── Test-only helpers ────────────────────────────────────────────────────────

    internal sealed class ApuntaTestContext : IMicrogameContext
    {
        public ISeededRandom Rng { get; }
        public PlayerSlot[] Players { get; }
        public float Difficulty { get; }
        public ApuntaTestContext(ISeededRandom rng, PlayerSlot[] players, float difficulty = 0f)
        {
            Rng        = rng;
            Players    = players;
            Difficulty = difficulty;
        }
    }

    internal sealed class ApuntaInputs : IReadOnlyPlayerInputs
    {
        private readonly Dictionary<PlayerSlot, InputSnapshot> _map;
        public ApuntaInputs(Dictionary<PlayerSlot, InputSnapshot> map) { _map = map; }
        public InputSnapshot For(PlayerSlot slot) => _map[slot];
    }
}
