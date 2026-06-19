using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-013 — Recolecta (Collect) microgame pure logic.
    ///
    /// Win condition: net collected (good - bad) >= quota before time limit.
    /// Good collectible: overlap → score +1 (once each).
    /// Bad collectible: overlap → score -1 (once each).
    /// Boundary pinned: exactly quota net score wins; quota-1 loses.
    /// </summary>
    [TestFixture]
    public class RecolectaMicrogameTests
    {
        private static readonly PlayerSlot[] AllPlayers =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private static IMicrogameContext MakeContext(int seed = 0)
            => new RecolectaTestContext(new SeededRandom(seed), AllPlayers);

        private static IReadOnlyPlayerInputs NoInput()
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(0f, 0f, ButtonState.Released);
            return new RecolectaInputs(map);
        }

        private static IReadOnlyPlayerInputs StickInput(PlayerSlot who, float x, float y)
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(
                    s == who ? x : 0f,
                    s == who ? y : 0f,
                    ButtonState.Released);
            return new RecolectaInputs(map);
        }

        private static IReadOnlyPlayerInputs AllStick(float x, float y)
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(x, y, ButtonState.Released);
            return new RecolectaInputs(map);
        }

        // ── CommandVerb ──────────────────────────────────────────────────────────

        [Test]
        public void RecolectaMicrogame_CommandVerb_IsRecolecta()
        {
            var game = new RecolectaMicrogame(quota: 3, collectRadius: 0.5f, avatarSpeed: 5f);
            Assert.That(game.CommandVerb, Is.EqualTo("¡RECOLECTA!"));
        }

        // ── Win: collect exactly quota good items ─────────────────────────────────

        [Test]
        public void RecolectaMicrogame_CollectExactlyQuota_AllWin()
        {
            // 3 good collectibles placed at (1,0), (2,0), (3,0).
            // Avatar starts at (0,0), moves right at speed 5 → collects all.
            const int quota = 3;
            var game = new RecolectaMicrogame(quota: quota, collectRadius: 0.6f, avatarSpeed: 5f, playAreaHalfExtent: 100f);

            game.SetForcedCollectibles(new RecolectaCollectible[]
            {
                new RecolectaCollectible(1f, 0f, isGood: true),
                new RecolectaCollectible(2f, 0f, isGood: true),
                new RecolectaCollectible(3f, 0f, isGood: true),
            });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (0f, 0f), (0f, 0f), (0f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));

            // 3 ticks moving right — each tick advances 5 * dt units.
            // Tick 1: move 5*0.2=1 → lands on (1,0) → collect #1.
            game.Tick(0.2f, AllStick(1f, 0f));
            // Tick 2: at x=1, move 5*0.2=1 → (2,0) → collect #2.
            game.Tick(0.2f, AllStick(1f, 0f));
            // Tick 3: at x=2, move 5*0.2=1 → (3,0) → collect #3. Quota met.
            game.Tick(0.2f, AllStick(1f, 0f));

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True,  "Rojo collected quota → win");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.True,  "Azul collected quota → win");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.True,  "Amarillo collected quota → win");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.True,  "Verde collected quota → win");
        }

        // ── Boundary: exactly quota-1 net score → LOSS ───────────────────────────

        [Test]
        public void RecolectaMicrogame_QuotaMinusOneNet_AllLose()
        {
            // quota=3; only 2 good collectibles available → max score=2 < 3 → loss.
            const int quota = 3;
            var game = new RecolectaMicrogame(quota: quota, collectRadius: 0.6f, avatarSpeed: 5f, playAreaHalfExtent: 100f);

            game.SetForcedCollectibles(new RecolectaCollectible[]
            {
                new RecolectaCollectible(1f, 0f, isGood: true),
                new RecolectaCollectible(2f, 0f, isGood: true),
                // Only 2 good items → max net = 2 = quota-1
            });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (0f, 0f), (0f, 0f), (0f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.2f, AllStick(1f, 0f)); // collect first
            game.Tick(0.2f, AllStick(1f, 0f)); // collect second
            game.Tick(0.2f, AllStick(1f, 0f)); // nothing left to collect

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "Net score = quota-1 should be a loss");
            Assert.That(result.IsWin(PlayerSlot.Azul), Is.False);
        }

        // ── Bad collectible decrements score ──────────────────────────────────────

        [Test]
        public void RecolectaMicrogame_CollectBadItem_DecrementsScore()
        {
            // 3 good + 1 bad. quota=3.
            // Collect 3 good (+3) then 1 bad (-1) → net=2 < 3 → loss.
            // But collect only 3 good and skip the bad → win.
            // Test: collect 2 good and 1 bad → net=1 < 3 → loss.
            const int quota = 3;
            var game = new RecolectaMicrogame(quota: quota, collectRadius: 0.6f, avatarSpeed: 5f, playAreaHalfExtent: 100f);

            game.SetForcedCollectibles(new RecolectaCollectible[]
            {
                new RecolectaCollectible(1f, 0f, isGood: true),
                new RecolectaCollectible(2f, 0f, isGood: false), // BAD
                new RecolectaCollectible(3f, 0f, isGood: true),
            });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (50f, 50f), (50f, -50f), (-50f, 50f) // only Rojo close to collectibles
            });

            game.Prepare(MakeContext(seed: 0));

            // Rojo moves right: collects 1 good (+1), 1 bad (-1), 1 good (+1) → net=1 < 3.
            game.Tick(0.2f, StickInput(PlayerSlot.Rojo, 1f, 0f));
            game.Tick(0.2f, StickInput(PlayerSlot.Rojo, 1f, 0f));
            game.Tick(0.2f, StickInput(PlayerSlot.Rojo, 1f, 0f));

            MicrogameResult result = game.Evaluate();
            int netScore = game.GetNetScore(PlayerSlot.Rojo); // read before Cleanup
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "Collecting bad items reduces net score — net=1 < quota=3 → loss");
            Assert.That(netScore, Is.EqualTo(1),
                "Net score should be good(2) - bad(1) = 1");
        }

        // ── Each collectible is collected once ────────────────────────────────────

        [Test]
        public void RecolectaMicrogame_OverlapSameCollectibleTwice_OnlyCountedOnce()
        {
            // One good collectible at (1,0). Avatar stays on it for 2 ticks → counts only once.
            const int quota = 2;
            var game = new RecolectaMicrogame(quota: quota, collectRadius: 0.6f, avatarSpeed: 0f, playAreaHalfExtent: 100f);

            game.SetForcedCollectibles(new RecolectaCollectible[]
            {
                new RecolectaCollectible(0f, 0f, isGood: true), // at same position as avatar
            });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (0f, 0f), (0f, 0f), (0f, 0f) // all at same spot as collectible
            });

            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.1f, NoInput()); // overlap tick 1 — collect
            game.Tick(0.1f, NoInput()); // overlap tick 2 — must NOT count again

            MicrogameResult result = game.Evaluate();
            int netScore = game.GetNetScore(PlayerSlot.Rojo); // read before Cleanup
            game.Cleanup();

            // net score = 1, quota = 2 → loss (confirms counted only once).
            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "A collectible may only be collected once — staying on it must not double-count");
            Assert.That(netScore, Is.EqualTo(1),
                "Score must be 1 even after 2 ticks on the same collectible");
        }

        // ── Independent per-player state ─────────────────────────────────────────

        [Test]
        public void RecolectaMicrogame_OnlyRojoCollectsQuota_OnlyRojoWins()
        {
            // Collectibles near (0,0) where only Rojo starts.
            const int quota = 2;
            var game = new RecolectaMicrogame(quota: quota, collectRadius: 0.6f, avatarSpeed: 5f, playAreaHalfExtent: 100f);

            game.SetForcedCollectibles(new RecolectaCollectible[]
            {
                new RecolectaCollectible(1f, 0f, isGood: true),
                new RecolectaCollectible(2f, 0f, isGood: true),
            });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (50f, 50f), (50f, -50f), (-50f, 50f) // only Rojo near collectibles
            });

            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.2f, StickInput(PlayerSlot.Rojo, 1f, 0f));
            game.Tick(0.2f, StickInput(PlayerSlot.Rojo, 1f, 0f));

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True,  "Rojo collected quota → win");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False, "Azul collected nothing → loss");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False, "Amarillo collected nothing → loss");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False, "Verde collected nothing → loss");
        }

        // ── Score exposed for view ────────────────────────────────────────────────

        [Test]
        public void RecolectaMicrogame_GetNetScore_ReflectsCollections()
        {
            const int quota = 5;
            var game = new RecolectaMicrogame(quota: quota, collectRadius: 0.6f, avatarSpeed: 5f, playAreaHalfExtent: 100f);

            game.SetForcedCollectibles(new RecolectaCollectible[]
            {
                new RecolectaCollectible(1f, 0f, isGood: true),
                new RecolectaCollectible(2f, 0f, isGood: true),
            });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (50f, 50f), (50f, -50f), (-50f, 50f)
            });

            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.2f, StickInput(PlayerSlot.Rojo, 1f, 0f));
            game.Tick(0.2f, StickInput(PlayerSlot.Rojo, 1f, 0f));

            Assert.That(game.GetNetScore(PlayerSlot.Rojo), Is.EqualTo(2),
                "GetNetScore must reflect collected-good minus collected-bad");
            Assert.That(game.GetNetScore(PlayerSlot.Azul), Is.EqualTo(0),
                "Other players must have net score 0");

            game.Evaluate();
            game.Cleanup();
        }

        // ── Avatar clamped to play area ──────────────────────────────────────────

        [Test]
        public void RecolectaMicrogame_AvatarClampedToPlayArea()
        {
            const float halfExtent = 5f;
            var game = new RecolectaMicrogame(quota: 10, collectRadius: 0.5f, avatarSpeed: 100f, playAreaHalfExtent: halfExtent);
            game.SetForcedCollectibles(new RecolectaCollectible[0]);
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (0f, 0f), (0f, 0f), (0f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));
            game.Tick(1f, StickInput(PlayerSlot.Rojo, 1f, 0f)); // try to move far right

            float x = game.GetAvatarX(PlayerSlot.Rojo);
            Assert.That(x, Is.LessThanOrEqualTo(halfExtent),
                "Avatar X must be clamped to playAreaHalfExtent");

            game.Evaluate();
            game.Cleanup();
        }

        // ── Evaluate returns frozen result ───────────────────────────────────────

        [Test]
        public void RecolectaMicrogame_Evaluate_ReturnsFrozenResult()
        {
            var game = new RecolectaMicrogame(quota: 1, collectRadius: 0.6f, avatarSpeed: 5f, playAreaHalfExtent: 100f);
            game.SetForcedCollectibles(new RecolectaCollectible[]
            {
                new RecolectaCollectible(1f, 0f, isGood: true),
            });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (0f, 0f), (0f, 0f), (0f, 0f)
            });

            game.Prepare(MakeContext(seed: 0));
            game.Tick(0.2f, AllStick(1f, 0f));

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsFrozen, Is.True);
        }

        // ── Prepare resets state ─────────────────────────────────────────────────

        [Test]
        public void RecolectaMicrogame_SecondPrepare_ResetsScore()
        {
            const int quota = 2;
            var game = new RecolectaMicrogame(quota: quota, collectRadius: 0.6f, avatarSpeed: 5f, playAreaHalfExtent: 100f);
            var ctx = MakeContext(seed: 0);

            // Round 1: everyone collects quota and wins.
            game.SetForcedCollectibles(new RecolectaCollectible[]
            {
                new RecolectaCollectible(1f, 0f, isGood: true),
                new RecolectaCollectible(2f, 0f, isGood: true),
            });
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (0f, 0f), (0f, 0f), (0f, 0f)
            });

            game.Prepare(ctx);
            game.Tick(0.2f, AllStick(1f, 0f));
            game.Tick(0.2f, AllStick(1f, 0f));
            MicrogameResult round1 = game.Evaluate();
            game.Cleanup();
            Assert.That(round1.IsWin(PlayerSlot.Rojo), Is.True, "Round 1 should win");

            // Round 2: no collectibles → score stays 0 → loss.
            game.SetForcedCollectibles(new RecolectaCollectible[0]);
            game.SetForcedAvatarPositions(new (float, float)[]
            {
                (0f, 0f), (0f, 0f), (0f, 0f), (0f, 0f)
            });

            game.Prepare(ctx);
            game.Tick(0.2f, NoInput());
            MicrogameResult round2 = game.Evaluate();
            game.Cleanup();

            Assert.That(round2.IsWin(PlayerSlot.Rojo), Is.False,
                "Score must be reset between Prepare calls");
        }
    }

    // ── Test-only helpers ────────────────────────────────────────────────────────

    internal sealed class RecolectaTestContext : IMicrogameContext
    {
        public ISeededRandom Rng { get; }
        public PlayerSlot[] Players { get; }
        public RecolectaTestContext(ISeededRandom rng, PlayerSlot[] players)
        {
            Rng = rng;
            Players = players;
        }
    }

    internal sealed class RecolectaInputs : IReadOnlyPlayerInputs
    {
        private readonly Dictionary<PlayerSlot, InputSnapshot> _map;
        public RecolectaInputs(Dictionary<PlayerSlot, InputSnapshot> map) { _map = map; }
        public InputSnapshot For(PlayerSlot slot) => _map[slot];
    }
}
