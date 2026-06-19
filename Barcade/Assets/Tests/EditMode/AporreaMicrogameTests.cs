using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-010 — Aporrea (Mash) microgame pure logic.
    ///
    /// Win condition: count of ButtonState.Pressed rising-edges >= threshold before time limit.
    /// Boundary pinned: exactly threshold presses wins, threshold-1 loses.
    /// </summary>
    [TestFixture]
    public class AporreaMicrogameTests
    {
        private static readonly PlayerSlot[] AllPlayers =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private static IMicrogameContext MakeContext(int seed = 0)
            => new ApporreaTestContext(new SeededRandom(seed), AllPlayers);

        private static IReadOnlyPlayerInputs AllPressed()
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(0f, 0f, ButtonState.Pressed);
            return new ApporreaInputs(map);
        }

        private static IReadOnlyPlayerInputs AllReleased()
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(0f, 0f, ButtonState.Released);
            return new ApporreaInputs(map);
        }

        private static IReadOnlyPlayerInputs AllHeld()
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(0f, 0f, ButtonState.Held);
            return new ApporreaInputs(map);
        }

        private static IReadOnlyPlayerInputs OnlyOnePressed(PlayerSlot who)
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(0f, 0f,
                    s == who ? ButtonState.Pressed : ButtonState.Released);
            return new ApporreaInputs(map);
        }

        // ── CommandVerb ──────────────────────────────────────────────────────────

        [Test]
        public void AporreaMicrogame_CommandVerb_IsAporrea()
        {
            var game = new AporreaMicrogame(threshold: 5, timeLimit: 5f);
            Assert.That(game.CommandVerb, Is.EqualTo("¡APORREA!"));
        }

        // ── Win: exactly threshold presses ───────────────────────────────────────

        [Test]
        public void AporreaMicrogame_ExactlyThresholdPresses_AllWin()
        {
            const int threshold = 5;
            var game = new AporreaMicrogame(threshold: threshold, timeLimit: 10f);
            game.Prepare(MakeContext(seed: 0));

            // Feed exactly `threshold` Pressed ticks (each tick is a rising edge).
            for (int i = 0; i < threshold; i++)
                game.Tick(0.1f, AllPressed());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True,  "Rojo: exactly threshold presses → win");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.True,  "Azul: exactly threshold presses → win");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.True,  "Amarillo: exactly threshold presses → win");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.True,  "Verde: exactly threshold presses → win");
        }

        // ── Boundary: threshold-1 presses → loss ────────────────────────────────

        [Test]
        public void AporreaMicrogame_ThresholdMinusOnePresses_AllLose()
        {
            const int threshold = 5;
            var game = new AporreaMicrogame(threshold: threshold, timeLimit: 10f);
            game.Prepare(MakeContext(seed: 0));

            // Feed threshold-1 Pressed ticks.
            for (int i = 0; i < threshold - 1; i++)
                game.Tick(0.1f, AllPressed());

            // Timer expires.
            game.Tick(50f, AllReleased());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.False, "threshold-1 presses → loss");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False, "threshold-1 presses → loss");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False, "threshold-1 presses → loss");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False, "threshold-1 presses → loss");
        }

        // ── Held state does NOT count as a rising edge ───────────────────────────

        [Test]
        public void AporreaMicrogame_HeldState_DoesNotCountAsPress()
        {
            const int threshold = 3;
            var game = new AporreaMicrogame(threshold: threshold, timeLimit: 10f);
            game.Prepare(MakeContext(seed: 0));

            // One real press, then many Held ticks — Held must NOT increment count.
            game.Tick(0.1f, AllPressed());  // count = 1
            for (int i = 0; i < 20; i++)
                game.Tick(0.1f, AllHeld()); // must NOT increment count

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            // Only 1 press out of 3 threshold → loss.
            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "Held state must not count as a rising edge press");
        }

        // ── Independent per-player counting ─────────────────────────────────────

        [Test]
        public void AporreaMicrogame_OnlyRojoReachesThreshold_OnlyRojoWins()
        {
            const int threshold = 3;
            var game = new AporreaMicrogame(threshold: threshold, timeLimit: 10f);
            game.Prepare(MakeContext(seed: 0));

            // Only Rojo presses enough times.
            for (int i = 0; i < threshold; i++)
                game.Tick(0.1f, OnlyOnePressed(PlayerSlot.Rojo));

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True,  "Rojo reached threshold → win");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False, "Azul never pressed → loss");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False, "Amarillo never pressed → loss");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False, "Verde never pressed → loss");
        }

        // ── No presses = all lose ────────────────────────────────────────────────

        [Test]
        public void AporreaMicrogame_NoPresses_AllLose()
        {
            var game = new AporreaMicrogame(threshold: 3, timeLimit: 2f);
            game.Prepare(MakeContext(seed: 0));

            game.Tick(5f, AllReleased()); // past time limit, no presses

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.False);
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False);
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False);
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False);
        }

        // ── Presses after time limit do not count ────────────────────────────────

        [Test]
        public void AporreaMicrogame_PressAfterTimeLimit_DoesNotCountAsWin()
        {
            const int threshold = 3;
            const float limit = 1f;
            var game = new AporreaMicrogame(threshold: threshold, timeLimit: limit);
            game.Prepare(MakeContext(seed: 0));

            // Advance past time limit with no presses.
            game.Tick(limit + 0.1f, AllReleased());

            // Press after expiry — must not count.
            game.Tick(0.1f, AllPressed());
            game.Tick(0.1f, AllPressed());
            game.Tick(0.1f, AllPressed());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "Presses after time limit must not count toward threshold");
        }

        // ── Evaluate returns a frozen result ─────────────────────────────────────

        [Test]
        public void AporreaMicrogame_Evaluate_ReturnsFrozenResult()
        {
            var game = new AporreaMicrogame(threshold: 1, timeLimit: 5f);
            game.Prepare(MakeContext(seed: 0));
            game.Tick(0.1f, AllPressed());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsFrozen, Is.True, "Evaluate must freeze result before returning");
        }

        // ── Prepare resets state ─────────────────────────────────────────────────

        [Test]
        public void AporreaMicrogame_SecondPrepare_ResetsCount()
        {
            const int threshold = 3;
            var game = new AporreaMicrogame(threshold: threshold, timeLimit: 10f);
            var ctx = MakeContext(seed: 0);

            // Round 1: everyone wins.
            game.Prepare(ctx);
            for (int i = 0; i < threshold; i++)
                game.Tick(0.1f, AllPressed());
            MicrogameResult round1 = game.Evaluate();
            game.Cleanup();
            Assert.That(round1.IsWin(PlayerSlot.Rojo), Is.True);

            // Round 2: no presses — should lose (count was reset).
            game.Prepare(ctx);
            game.Tick(20f, AllReleased());
            MicrogameResult round2 = game.Evaluate();
            game.Cleanup();

            Assert.That(round2.IsWin(PlayerSlot.Rojo), Is.False,
                "Press count must reset on second Prepare");
        }

        // ── Expose per-player press count for view rendering ─────────────────────

        [Test]
        public void AporreaMicrogame_GetPressCount_ReflectsRisingEdges()
        {
            const int threshold = 10;
            var game = new AporreaMicrogame(threshold: threshold, timeLimit: 30f);
            game.Prepare(MakeContext(seed: 0));

            // 3 presses for Rojo only.
            for (int i = 0; i < 3; i++)
                game.Tick(0.1f, OnlyOnePressed(PlayerSlot.Rojo));

            Assert.That(game.GetPressCount(PlayerSlot.Rojo), Is.EqualTo(3),
                "PressCount must reflect the number of rising-edge Pressed events");
            Assert.That(game.GetPressCount(PlayerSlot.Azul), Is.EqualTo(0),
                "Other players must have count 0");

            game.Evaluate();
            game.Cleanup();
        }
    }

    // ── Test-only helpers ────────────────────────────────────────────────────────

    internal sealed class ApporreaTestContext : IMicrogameContext
    {
        public ISeededRandom Rng { get; }
        public PlayerSlot[] Players { get; }
        public ApporreaTestContext(ISeededRandom rng, PlayerSlot[] players)
        {
            Rng = rng;
            Players = players;
        }
    }

    internal sealed class ApporreaInputs : IReadOnlyPlayerInputs
    {
        private readonly Dictionary<PlayerSlot, InputSnapshot> _map;
        public ApporreaInputs(Dictionary<PlayerSlot, InputSnapshot> map) { _map = map; }
        public InputSnapshot For(PlayerSlot slot) => _map[slot];
    }
}
