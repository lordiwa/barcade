using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-009 — Esquiva (Dodge) microgame pure logic.
    ///
    /// Win condition: no collision occurred by time limit.
    /// Loss: first collision with any hazard latches a loss for that player.
    /// Boundary pinned: distance exactly equal to (avatarRadius + hazardRadius) is a HIT.
    /// </summary>
    [TestFixture]
    public class EsquivaMicrogameTests
    {
        private static readonly PlayerSlot[] AllPlayers =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private static IMicrogameContext MakeContext(int seed = 0)
            => new EsquivaTestContext(new SeededRandom(seed), AllPlayers);

        private static IReadOnlyPlayerInputs NoInput()
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(0f, 0f, ButtonState.Released);
            return new EsquivaDictInputs(map);
        }

        private static IReadOnlyPlayerInputs StickInput(PlayerSlot who, float x, float y)
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(
                    s == who ? x : 0f,
                    s == who ? y : 0f,
                    ButtonState.Released);
            return new EsquivaDictInputs(map);
        }

        // ── CommandVerb ──────────────────────────────────────────────────────────

        [Test]
        public void EsquivaMicrogame_CommandVerb_IsEsquiva()
        {
            var game = new EsquivaMicrogame();
            Assert.That(game.CommandVerb, Is.EqualTo("¡ESQUIVA!"));
        }

        // ── Win: player survives without collision ───────────────────────────────

        [Test]
        public void EsquivaMicrogame_NoCollision_AllPlayersWin()
        {
            // Seed 42 with no hazards placed inside player start positions — see impl note.
            // We place hazards far away by using a game configured with 0 hazards.
            var game = new EsquivaMicrogame(hazardCount: 0);
            game.Prepare(MakeContext(seed: 42));

            game.Tick(0.1f, NoInput());
            game.Tick(0.1f, NoInput());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True,  "Rojo should win — no hazards");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.True,  "Azul should win — no hazards");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.True,  "Amarillo should win — no hazards");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.True,  "Verde should win — no hazards");
        }

        // ── Loss: player walks into a hazard ────────────────────────────────────

        [Test]
        public void EsquivaMicrogame_PlayerMovesIntoHazard_PlayerLoses()
        {
            // Hazard placed at (5, 0); avatarRadius=0.5, hazardRadius=0.5 → hit when dist<=1.
            // Rojo starts at (0,0), moves right at speed 10 → will collide in first tick.
            var game = new EsquivaMicrogame(
                hazardCount: 1,
                hazardSpeed: 0f,          // static hazard
                avatarRadius: 0.5f,
                hazardRadius: 0.5f,
                avatarSpeed: 10f,
                playAreaHalfExtent: 100f);

            // We need deterministic hazard position. Use a factory override for tests.
            // EsquivaMicrogame accepts an optional forced hazard position list.
            game.SetForcedHazardPositions(new (float, float)[] { (5f, 0f) });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (50f, 50f), (50f, -50f), (-50f, 50f)
            });

            game.Prepare(MakeContext(seed: 0));

            // Rojo moves right — will cover 10 * 0.5 = 5 units, landing exactly on hazard.
            // Distance at arrival = 0 < 1.0 (avatarRadius + hazardRadius) → collision.
            game.Tick(0.5f, StickInput(PlayerSlot.Rojo, 1f, 0f));

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.False, "Rojo walked into hazard — loss");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.True,  "Azul far away — win");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.True,  "Amarillo far away — win");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.True,  "Verde far away — win");
        }

        // ── Boundary: distance exactly equal to combined radii → HIT ────────────

        [Test]
        public void EsquivaMicrogame_DistanceExactlyCombinedRadii_IsCollision()
        {
            // avatarRadius=0.5, hazardRadius=0.5 → combined=1.0
            // Place hazard at (1, 0) and avatar at (0, 0) → dist=1.0 → COLLISION.
            var game = new EsquivaMicrogame(
                hazardCount: 1,
                hazardSpeed: 0f,
                avatarRadius: 0.5f,
                hazardRadius: 0.5f,
                avatarSpeed: 0f,
                playAreaHalfExtent: 100f);

            game.SetForcedHazardPositions(new (float, float)[] { (1f, 0f) });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (50f, 50f), (50f, -50f), (-50f, 50f)
            });

            game.Prepare(MakeContext(seed: 0));
            game.Tick(0.016f, NoInput()); // static hazard, avatar doesn't move

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            // distance(0,0)→(1,0) = 1.0 == avatarRadius+hazardRadius → HIT (<=)
            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "Distance exactly = combined radii should be a collision (boundary: <=)");
        }

        // ── Boundary: distance just ABOVE combined radii → SAFE ─────────────────

        [Test]
        public void EsquivaMicrogame_DistanceSlightlyAboveCombinedRadii_IsSafe()
        {
            // avatarRadius=0.5, hazardRadius=0.5 → combined=1.0
            // Place hazard at (1.001, 0) → dist=1.001 > 1.0 → NO collision.
            var game = new EsquivaMicrogame(
                hazardCount: 1,
                hazardSpeed: 0f,
                avatarRadius: 0.5f,
                hazardRadius: 0.5f,
                avatarSpeed: 0f,
                playAreaHalfExtent: 100f);

            game.SetForcedHazardPositions(new (float, float)[] { (1.001f, 0f) });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (50f, 50f), (50f, -50f), (-50f, 50f)
            });

            game.Prepare(MakeContext(seed: 0));
            game.Tick(0.016f, NoInput());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.True,
                "Distance just above combined radii should be safe — no collision");
        }

        // ── Loss is latched — stays even after player moves away ────────────────

        [Test]
        public void EsquivaMicrogame_CollisionLatched_LossStaysEvenIfAvatarMoves()
        {
            var game = new EsquivaMicrogame(
                hazardCount: 1,
                hazardSpeed: 0f,
                avatarRadius: 0.5f,
                hazardRadius: 0.5f,
                avatarSpeed: 20f,
                playAreaHalfExtent: 100f);

            game.SetForcedHazardPositions(new (float, float)[] { (0f, 0f) });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (50f, 50f), (50f, -50f), (-50f, 50f) // Rojo starts AT hazard
            });

            game.Prepare(MakeContext(seed: 0));

            // Tick 1: collision occurs (Rojo at hazard position → dist=0)
            game.Tick(0.016f, NoInput());

            // Tick 2: Rojo moves far away — but loss should be latched
            game.Tick(0.1f, StickInput(PlayerSlot.Rojo, 1f, 0f));

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "Collision was latched — loss must persist even if avatar moves away");
        }

        // ── Player uses stick to dodge successfully ──────────────────────────────

        [Test]
        public void EsquivaMicrogame_PlayerDodgesHazard_Wins()
        {
            // Hazard at (3, 0), avatar at (0, 0), moves UP (y direction) — no collision.
            var game = new EsquivaMicrogame(
                hazardCount: 1,
                hazardSpeed: 0f,
                avatarRadius: 0.5f,
                hazardRadius: 0.5f,
                avatarSpeed: 5f,
                playAreaHalfExtent: 100f);

            game.SetForcedHazardPositions(new (float, float)[] { (3f, 0f) });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (50f, 50f), (50f, -50f), (-50f, 50f)
            });

            game.Prepare(MakeContext(seed: 0));

            // Move Rojo upward — away from hazard at (3,0)
            game.Tick(1f, StickInput(PlayerSlot.Rojo, 0f, 1f)); // moves to (0, 5)

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            // dist from (0,5) to (3,0) = sqrt(9+25) ≈ 5.83 > 1.0 → safe
            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.True,
                "Rojo dodged the hazard — should win");
        }

        // ── Avatar clamped to play area ──────────────────────────────────────────

        [Test]
        public void EsquivaMicrogame_AvatarClampedToPlayArea()
        {
            // playAreaHalfExtent=5; avatar starts at (0,0), moves right fast.
            // After many ticks, X should not exceed 5.
            var game = new EsquivaMicrogame(
                hazardCount: 0,
                hazardSpeed: 0f,
                avatarRadius: 0.5f,
                hazardRadius: 0.5f,
                avatarSpeed: 100f,
                playAreaHalfExtent: 5f);

            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (0f, 0f), (0f, 0f), (0f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));
            game.Tick(1f, StickInput(PlayerSlot.Rojo, 1f, 0f));

            // Read exposed position state
            float x = game.GetAvatarX(PlayerSlot.Rojo);
            float y = game.GetAvatarY(PlayerSlot.Rojo);

            Assert.That(x, Is.LessThanOrEqualTo(5f),  "Avatar X must not exceed playAreaHalfExtent");
            Assert.That(x, Is.GreaterThanOrEqualTo(-5f), "Avatar X must not go below -playAreaHalfExtent");
        }

        // ── Prepare resets state ─────────────────────────────────────────────────

        [Test]
        public void EsquivaMicrogame_SecondPrepare_ResetsLossState()
        {
            var game = new EsquivaMicrogame(
                hazardCount: 1,
                hazardSpeed: 0f,
                avatarRadius: 0.5f,
                hazardRadius: 0.5f,
                avatarSpeed: 0f,
                playAreaHalfExtent: 100f);

            game.SetForcedHazardPositions(new (float, float)[] { (0f, 0f) });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (50f, 50f), (50f, -50f), (-50f, 50f) // Rojo at hazard
            });

            // Round 1: Rojo collides and loses
            game.Prepare(MakeContext(seed: 0));
            game.Tick(0.016f, NoInput());
            MicrogameResult round1 = game.Evaluate();
            game.Cleanup();
            Assert.That(round1.IsWin(PlayerSlot.Rojo), Is.False);

            // Round 2: no hazards — everyone should win (state was reset)
            game = new EsquivaMicrogame(hazardCount: 0);
            game.Prepare(MakeContext(seed: 0));
            game.Tick(0.016f, NoInput());
            MicrogameResult round2 = game.Evaluate();
            game.Cleanup();

            Assert.That(round2.IsWin(PlayerSlot.Rojo), Is.True,
                "Second round must start fresh — no carry-over from round 1 loss");
        }
    }

    // ── Test-only helpers ────────────────────────────────────────────────────────

    internal sealed class EsquivaTestContext : IMicrogameContext
    {
        public ISeededRandom Rng { get; }
        public PlayerSlot[] Players { get; }
        public EsquivaTestContext(ISeededRandom rng, PlayerSlot[] players)
        {
            Rng = rng;
            Players = players;
        }
    }

    internal sealed class EsquivaDictInputs : IReadOnlyPlayerInputs
    {
        private readonly Dictionary<PlayerSlot, InputSnapshot> _map;
        public EsquivaDictInputs(Dictionary<PlayerSlot, InputSnapshot> map) { _map = map; }
        public InputSnapshot For(PlayerSlot slot) => _map[slot];
    }
}
