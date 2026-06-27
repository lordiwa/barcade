using NUnit.Framework;
using Barcade.Core.Dodge;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="DodgeSim"/>: state transitions, player fall detection,
    /// obstacle catch detection, obstacle fall, win-by-timer, win-by-all-obstacles-fallen.
    /// </summary>
    [TestFixture]
    public class DodgeSimTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a solid arena with all tiles solid (large grace delay so it never collapses
        /// during a test unless the test explicitly ticks the arena).
        /// </summary>
        private static GridArena SolidArena(int n = 9)
            => new GridArena(n: n, graceDelay: 9999f, collapseInterval: 9999f);

        /// <summary>
        /// Builds a sim positioned so player is at <paramref name="px"/>,<paramref name="pz"/>
        /// and one obstacle at <paramref name="ox"/>,<paramref name="oz"/>.
        /// </summary>
        private static DodgeSim OneObstacleSim(
            GridArena arena,
            float px, float pz,
            float ox, float oz,
            float contactRadius = 0.6f,
            float obstacleSpeed = 0f)
        {
            var sim = new DodgeSim(arena,
                obstacleCount:    1,
                playerSpeed:      0f,
                obstacleSpeed:    obstacleSpeed,
                contactRadius:    contactRadius,
                survivalDuration: 9999f);

            sim.SetForcedPlayerStart(px, pz);
            sim.SetForcedObstacleStarts(new[] { (ox, oz) });
            sim.Restart();
            return sim;
        }

        // ── Initial state ─────────────────────────────────────────────────────────

        [Test]
        public void InitialState_IsPlaying()
        {
            var arena = SolidArena();
            var sim   = new DodgeSim(arena);
            Assert.That(sim.State,      Is.EqualTo(DodgeState.Playing));
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.None));
        }

        // ── Player moves with input ───────────────────────────────────────────────

        [Test]
        public void Tick_WithInput_PlayerMoves()
        {
            var arena = SolidArena();
            var sim   = new DodgeSim(arena,
                obstacleCount:    0,
                playerSpeed:      4f,
                survivalDuration: 9999f);
            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.Restart();

            float startX = sim.PlayerX;
            sim.Tick(1f, 1f, 0f); // move right 4 units

            Assert.That(sim.PlayerX, Is.EqualTo(startX + 4f).Within(1e-4f));
        }

        // ── Player fall detection ─────────────────────────────────────────────────

        [Test]
        public void PlayerFall_WhenOnFallenTile_StateLost()
        {
            // Arena n=3, grace=0 so ring 0 falls immediately after first tick.
            var arena = new GridArena(n: 3, graceDelay: 0.5f, collapseInterval: 1f);
            var sim   = new DodgeSim(arena,
                obstacleCount:    0,
                playerSpeed:      0f,
                survivalDuration: 9999f);

            // Place player on a border tile (ring 0) at (0, 0).
            sim.SetForcedPlayerStart(0.5f, 0.5f); // centre of cell (0,0)
            sim.Restart();

            // Tick arena + sim past grace delay so ring 0 collapses.
            arena.Tick(1f);
            sim.Tick(1f, 0f, 0f);

            Assert.That(sim.State,      Is.EqualTo(DodgeState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.Fell));
        }

        [Test]
        public void PlayerFall_WhenOffGrid_StateLost()
        {
            var arena = SolidArena(n: 9);
            var sim   = new DodgeSim(arena,
                obstacleCount:    0,
                playerSpeed:      100f,
                survivalDuration: 9999f);

            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.Restart();

            // Move player far off-grid (no clamping — player can exit the grid and fall).
            sim.Tick(1f, 1f, 0f); // moves to ~104.5

            Assert.That(sim.State,      Is.EqualTo(DodgeState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.Fell));
        }

        // ── Caught detection ──────────────────────────────────────────────────────

        [Test]
        public void PlayerCaught_WhenObstacleTooClose_StateLost()
        {
            var arena = SolidArena();
            // Obstacle starts at (5.5, 4.5), player at (4.5, 4.5) → dist=1.0, contactRadius=1.1 → caught.
            var sim = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 5.5f, oz: 4.5f,
                contactRadius: 1.1f);

            sim.Tick(0.016f, 0f, 0f);

            Assert.That(sim.State,      Is.EqualTo(DodgeState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.Caught));
        }

        [Test]
        public void PlayerCaught_DistanceExactlyContactRadius_IsLost()
        {
            var arena = SolidArena();
            // Obstacle at (5.1, 4.5), player at (4.5, 4.5) → dist=0.6 == contactRadius=0.6 → caught.
            var sim = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 5.1f, oz: 4.5f,
                contactRadius: 0.6f);

            sim.Tick(0.016f, 0f, 0f);

            Assert.That(sim.State, Is.EqualTo(DodgeState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.Caught));
        }

        [Test]
        public void PlayerCaught_DistanceJustAboveContactRadius_StillPlaying()
        {
            var arena = SolidArena();
            // dist = 0.7 > contactRadius = 0.6 → still alive.
            var sim = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 5.2f, oz: 4.5f,
                contactRadius: 0.6f);

            sim.Tick(0.016f, 0f, 0f);

            Assert.That(sim.State, Is.EqualTo(DodgeState.Playing));
        }

        // ── Obstacle fall ─────────────────────────────────────────────────────────

        [Test]
        public void ObstacleFall_WhenOnFallenTile_ObstacleRemovedFromPlay()
        {
            // Arena n=3, grace=0.5s so ring 0 collapses after 0.5s.
            var arena = new GridArena(n: 3, graceDelay: 0.5f, collapseInterval: 9999f);
            // Place obstacle on a border tile (cell 0,0), player safely in centre.
            var sim = new DodgeSim(arena,
                obstacleCount:    1,
                playerSpeed:      0f,
                obstacleSpeed:    0f,   // static so it stays on the fallen tile
                contactRadius:    0.1f, // small so touching doesn't trigger
                survivalDuration: 9999f);

            sim.SetForcedPlayerStart(1.5f, 1.5f);                  // centre cell
            sim.SetForcedObstacleStarts(new[] { (0.5f, 0.5f) });   // border cell
            sim.Restart();

            arena.Tick(1f);  // collapses ring 0
            sim.Tick(1f, 0f, 0f);

            Assert.That(sim.IsObstacleAlive(0), Is.False, "obstacle on fallen tile must be removed");
            // All obstacles are now gone → game transitions to Won (player was on solid tile).
            Assert.That(sim.State, Is.EqualTo(DodgeState.Won),
                "all obstacles fell — win condition triggered");
        }

        // ── Win by timer ──────────────────────────────────────────────────────────

        [Test]
        public void Win_ByTimer_WhenSurvivalDurationReached()
        {
            var arena = SolidArena();
            // No obstacles; player stationary on a solid tile; short survivalDuration.
            var sim = new DodgeSim(arena,
                obstacleCount:    0,
                playerSpeed:      0f,
                survivalDuration: 5f);

            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.Restart();

            sim.Tick(4.99f, 0f, 0f);
            Assert.That(sim.State, Is.EqualTo(DodgeState.Playing), "just under duration — not yet");

            sim.Tick(0.02f, 0f, 0f);
            Assert.That(sim.State, Is.EqualTo(DodgeState.Won), "past survival duration — win");
        }

        // ── Win by all obstacles fallen ───────────────────────────────────────────

        [Test]
        public void Win_AllObstaclesFallen_WhenNoAliveThreat()
        {
            var arena = new GridArena(n: 3, graceDelay: 0.5f, collapseInterval: 9999f);
            var sim = new DodgeSim(arena,
                obstacleCount:    1,
                playerSpeed:      0f,
                obstacleSpeed:    0f,
                contactRadius:    0.1f,
                survivalDuration: 9999f);

            sim.SetForcedPlayerStart(1.5f, 1.5f);
            sim.SetForcedObstacleStarts(new[] { (0.5f, 0.5f) });
            sim.Restart();

            arena.Tick(1f); // ring 0 collapses — obstacle on (0.5, 0.5) falls
            sim.Tick(1f, 0f, 0f);

            Assert.That(sim.IsObstacleAlive(0), Is.False);
            Assert.That(sim.State, Is.EqualTo(DodgeState.Won), "all obstacles gone — win");
        }

        // ── Tick is a no-op in a terminal state ───────────────────────────────────

        [Test]
        public void Tick_NoOp_AfterLost()
        {
            var arena = SolidArena();
            var sim = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 4.5f, oz: 4.5f, // on top of player → immediate catch
                contactRadius: 0.6f);

            sim.Tick(0.016f, 0f, 0f);
            Assert.That(sim.State, Is.EqualTo(DodgeState.Lost));

            float elapsed = sim.SurvivalElapsed;
            sim.Tick(1f, 0f, 0f); // should be a no-op
            Assert.That(sim.SurvivalElapsed, Is.EqualTo(elapsed), "timer must not advance in terminal state");
        }

        [Test]
        public void Tick_NoOp_AfterWon()
        {
            var arena = SolidArena();
            var sim = new DodgeSim(arena,
                obstacleCount: 0, playerSpeed: 0f, survivalDuration: 1f);
            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.Restart();

            sim.Tick(2f, 0f, 0f); // win
            Assert.That(sim.State, Is.EqualTo(DodgeState.Won));

            float elapsed = sim.SurvivalElapsed;
            sim.Tick(1f, 0f, 0f);
            Assert.That(sim.SurvivalElapsed, Is.EqualTo(elapsed), "timer must not advance after win");
        }

        // ── Restart resets state ──────────────────────────────────────────────────

        [Test]
        public void Restart_AfterLoss_ResetsToPlaying()
        {
            var arena = SolidArena();
            var sim = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 4.5f, oz: 4.5f,
                contactRadius: 0.6f);

            sim.Tick(0.016f, 0f, 0f);
            Assert.That(sim.State, Is.EqualTo(DodgeState.Lost));

            sim.Restart();
            Assert.That(sim.State,      Is.EqualTo(DodgeState.Playing));
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.None));
            Assert.That(sim.SurvivalElapsed, Is.EqualTo(0f).Within(1e-6f));
        }

        // ── Obstacle chase: moves toward player ───────────────────────────────────

        [Test]
        public void Obstacle_ChasesMoveTowardPlayer()
        {
            var arena = SolidArena();
            var sim = new DodgeSim(arena,
                obstacleCount:    1,
                playerSpeed:      0f,
                obstacleSpeed:    2f,
                contactRadius:    0.01f, // tiny — don't trigger catch during this test
                survivalDuration: 9999f);

            // Player at (6, 4.5), obstacle at (2, 4.5) — obstacle should move right (+X).
            sim.SetForcedPlayerStart(6f, 4.5f);
            sim.SetForcedObstacleStarts(new[] { (2f, 4.5f) });
            sim.Restart();

            float startX = sim.GetObstacleX(0);
            sim.Tick(1f, 0f, 0f);
            float endX = sim.GetObstacleX(0);

            Assert.That(endX, Is.GreaterThan(startX), "obstacle must move toward player (positive X)");
        }

        // ── Fell takes priority over Caught in the same tick ─────────────────────

        [Test]
        public void PlayerFell_CheckedBeforeCaught()
        {
            // Player off-grid AND obstacle at same position — Fell should be the reason.
            var arena = SolidArena(n: 3); // grid 0..2
            var sim = new DodgeSim(arena,
                obstacleCount:    1,
                playerSpeed:      0f,
                obstacleSpeed:    0f,
                contactRadius:    0.6f,
                survivalDuration: 9999f);

            // Place player off-grid at (-0.5, 1.5) and obstacle right next to it.
            sim.SetForcedPlayerStart(-0.5f, 1.5f);
            sim.SetForcedObstacleStarts(new[] { (-0.5f, 1.5f) });
            sim.Restart();

            sim.Tick(0.016f, 0f, 0f);

            Assert.That(sim.State,      Is.EqualTo(DodgeState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.Fell));
        }

        // ── Zero obstacles → win immediately once timer fires ─────────────────────

        [Test]
        public void ZeroObstacles_WinOnceTimerElapses()
        {
            var arena = SolidArena();
            var sim = new DodgeSim(arena,
                obstacleCount: 0, playerSpeed: 0f, survivalDuration: 2f);
            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.Restart();

            sim.Tick(2.01f, 0f, 0f);
            Assert.That(sim.State, Is.EqualTo(DodgeState.Won));
        }

        // ── Jump: airborne fall-immunity ──────────────────────────────────────────

        [Test]
        public void Jump_AirborneFallImmunity_GroundedFallsAirborneDoesNot()
        {
            // Baseline: grounded player on a void cell → Lost(Fell).
            var arenaA = new GridArena(n: 9, graceDelay: 0.1f, collapseInterval: 9999f);
            arenaA.Tick(0.5f); // ring 0 collapses

            var simA = new DodgeSim(arenaA,
                obstacleCount: 0, playerSpeed: 0f, survivalDuration: 9999f);
            simA.SetForcedPlayerStart(0.5f, 0.5f); // cell (0,0) — ring 0, void after collapse
            simA.Restart();

            simA.Tick(0.016f, 0f, 0f);
            Assert.That(simA.State, Is.EqualTo(DodgeState.Lost), "grounded on void tile → Lost(Fell)");

            // Same setup, but RequestJump() before the tick → airborne → immune to fall.
            var arenaB = new GridArena(n: 9, graceDelay: 0.1f, collapseInterval: 9999f);
            arenaB.Tick(0.5f);

            var simB = new DodgeSim(arenaB,
                obstacleCount: 0, playerSpeed: 0f, survivalDuration: 9999f);
            simB.SetForcedPlayerStart(0.5f, 0.5f);
            simB.Restart();

            simB.RequestJump();
            simB.Tick(0.016f, 0f, 0f); // 0.016s < 0.6s → still mid-air
            Assert.That(simB.State,          Is.EqualTo(DodgeState.Playing),
                "airborne over void → immune, still Playing");
            Assert.That(simB.IsAirborne,     Is.True);
            Assert.That(simB.JumpProgress01, Is.GreaterThan(0f).And.LessThan(1f));
        }

        // ── Jump: recover back inside — land on solid after steering over void ────

        [Test]
        public void Jump_RecoverBackInside_LandOnSolidSurvives()
        {
            var arena = new GridArena(n: 9, graceDelay: 0.1f, collapseInterval: 9999f);
            arena.Tick(0.5f); // ring 0 collapses

            // Player on ring-0 void cell; speed high enough to reach ring-2 in 0.6 s.
            var sim = new DodgeSim(arena,
                obstacleCount: 0, playerSpeed: 4f, survivalDuration: 9999f);
            sim.SetForcedPlayerStart(0.5f, 0.5f);
            sim.Restart();

            sim.RequestJump();
            // Steer diagonally toward centre; 4 * 0.6 / √2 ≈ 1.7 units each axis
            // → lands at ~(2.2, 2.2), cell (2, 2), ring 2 for n=9 — solid.
            sim.Tick(0.6f, 1f, 1f);

            Assert.That(sim.State, Is.EqualTo(DodgeState.Playing),
                "land on solid after flying over void edge → survive");
        }

        // ── Jump: landing on void still triggers fall ─────────────────────────────

        [Test]
        public void Jump_LandOnVoid_LostFell()
        {
            var arena = new GridArena(n: 9, graceDelay: 0.1f, collapseInterval: 9999f);
            arena.Tick(0.5f); // ring 0 collapses

            // Player on void cell, no movement → still on void at the landing frame.
            var sim = new DodgeSim(arena,
                obstacleCount: 0, playerSpeed: 0f, survivalDuration: 9999f);
            sim.SetForcedPlayerStart(0.5f, 0.5f);
            sim.Restart();

            sim.RequestJump();
            sim.Tick(0.6f, 0f, 0f); // full jump duration → landing frame on void cell

            Assert.That(sim.State,      Is.EqualTo(DodgeState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.Fell));
        }

        // ── Jump: hop over enemy while airborne ───────────────────────────────────

        [Test]
        public void Jump_HopOverEnemy_AirborneImmuneToCatch()
        {
            // Baseline: grounded at contactRadius distance → Lost(Caught).
            var arena = SolidArena();
            var sim1 = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 5.1f, oz: 4.5f,   // dist = 0.6 = contactRadius
                contactRadius: 0.6f);

            sim1.Tick(0.016f, 0f, 0f);
            Assert.That(sim1.State,      Is.EqualTo(DodgeState.Lost));
            Assert.That(sim1.LostReason, Is.EqualTo(LostReason.Caught));

            // Airborne: same setup, RequestJump → not caught.
            var sim2 = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 5.1f, oz: 4.5f,
                contactRadius: 0.6f);

            sim2.RequestJump();
            sim2.Tick(0.016f, 0f, 0f); // 0.016s < 0.6s → still mid-air

            Assert.That(sim2.State, Is.EqualTo(DodgeState.Playing),
                "airborne → hop over enemy, not caught");
        }

        // ── Jump: catch on the landing frame ─────────────────────────────────────

        [Test]
        public void Jump_CatchOnLanding_LostCaught()
        {
            var arena = SolidArena();
            // Enemy at exact contactRadius; player stationary → within radius on landing.
            var sim = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 5.1f, oz: 4.5f,   // dist = 0.6 = contactRadius
                contactRadius: 0.6f);

            sim.RequestJump();
            sim.Tick(0.6f, 0f, 0f); // full jump → landing frame triggers catch check

            Assert.That(sim.State,      Is.EqualTo(DodgeState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.Caught));
        }

        // ── Jump: no double-jump while airborne ───────────────────────────────────

        [Test]
        public void Jump_NoDoubleJump_SecondRequestIgnored()
        {
            var arena = SolidArena();
            var sim = new DodgeSim(arena,
                obstacleCount: 0, playerSpeed: 0f, survivalDuration: 9999f);
            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.Restart();

            sim.RequestJump();
            // Pending flag set but not consumed yet — not airborne before first Tick.
            Assert.That(sim.IsAirborne, Is.False,
                "pending jump not yet consumed — not airborne until Tick");

            sim.Tick(0.3f, 0f, 0f); // timer: 0.6 → 0.3, mid-air
            Assert.That(sim.IsAirborne, Is.True, "mid-jump — still airborne");

            sim.RequestJump(); // second request while airborne — must be ignored

            sim.Tick(0.3f, 0f, 0f); // timer: 0.3 → 0.0 — landing frame
            Assert.That(sim.IsAirborne, Is.False,
                "landed after exactly 0.6 s; second jump did NOT extend flight time");
            Assert.That(sim.JumpProgress01, Is.EqualTo(0f).Within(1e-5f),
                "grounded → JumpProgress01 returns 0");
            Assert.That(sim.State, Is.EqualTo(DodgeState.Playing));
        }
    }
}
