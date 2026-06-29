using NUnit.Framework;
using Barcade.Core.Dodge;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="DodgeSim"/>: state transitions, player fall detection,
    /// knockback (replaces catch), obstacle spawn schedule, win conditions.
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
        /// Uses spawnStartDelay=0 so the obstacle activates on the first Tick.
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
                survivalDuration: 9999f,
                spawnStartDelay:  0f);

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

        // ── Contact → knockback (replaces catch-as-loss) ──────────────────────────
        //
        // Caught is no longer a loss condition.  Contact with an active obstacle
        // while grounded applies a knockback impulse that pushes the player away.
        // LostReason.Caught is kept as an enum value to avoid churn but must never
        // be produced by the simulation.

        [Test]
        public void PlayerContact_WhenObstacleTooClose_IsKnockedBack()
        {
            var arena = SolidArena();
            // Obstacle at (5.5, 4.5), player at (4.5, 4.5) → dist=1.0, contactRadius=1.1 → within range.
            var sim = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 5.5f, oz: 4.5f,
                contactRadius: 1.1f);

            sim.Tick(0.016f, 0f, 0f);

            // Contact must not set Lost; Caught must never be produced.
            Assert.That(sim.State,      Is.EqualTo(DodgeState.Playing),
                "contact within radius → knockback, not Lost");
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.None),
                "LostReason.Caught must never be produced");
        }

        [Test]
        public void PlayerContact_AtContactRadius_IsKnockedBack()
        {
            var arena = SolidArena();
            // dist = 0.6 == contactRadius = 0.6 → at the edge, in contact.
            var sim = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 5.1f, oz: 4.5f,
                contactRadius: 0.6f);

            sim.Tick(0.016f, 0f, 0f);

            Assert.That(sim.State, Is.EqualTo(DodgeState.Playing),
                "contact at exact radius → knockback, not Lost");
        }

        [Test]
        public void PlayerCaught_DistanceJustAboveContactRadius_StillPlaying()
        {
            var arena = SolidArena();
            // dist = 0.7 > contactRadius = 0.6 → outside range, no knockback.
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
                contactRadius:    0.1f, // small so touching doesn't trigger knockback
                survivalDuration: 9999f,
                spawnStartDelay:  0f);

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
                survivalDuration: 9999f,
                spawnStartDelay:  0f);

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
            // Use off-grid fall to reach Lost state (contact no longer causes Lost).
            var arena = SolidArena(n: 3);
            var sim = new DodgeSim(arena,
                obstacleCount:    0,
                playerSpeed:      100f,
                survivalDuration: 9999f);
            sim.SetForcedPlayerStart(1.5f, 1.5f);
            sim.Restart();

            sim.Tick(1f, 1f, 0f); // shoots off right edge → Lost(Fell)
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
            // Use off-grid fall to reach Lost state (contact no longer causes Lost).
            var arena = SolidArena(n: 3);
            var sim = new DodgeSim(arena,
                obstacleCount:    0,
                playerSpeed:      100f,
                survivalDuration: 9999f);
            sim.SetForcedPlayerStart(1.5f, 1.5f);
            sim.Restart();

            sim.Tick(1f, 1f, 0f); // shoots off right edge → Lost(Fell)
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
                contactRadius:    0.01f, // tiny — don't trigger knockback during this test
                survivalDuration: 9999f,
                spawnStartDelay:  0f);

            // Player at (6, 4.5), obstacle at (2, 4.5) — obstacle should move right (+X).
            sim.SetForcedPlayerStart(6f, 4.5f);
            sim.SetForcedObstacleStarts(new[] { (2f, 4.5f) });
            sim.Restart();

            float startX = sim.GetObstacleX(0);
            sim.Tick(1f, 0f, 0f);
            float endX = sim.GetObstacleX(0);

            Assert.That(endX, Is.GreaterThan(startX), "obstacle must move toward player (positive X)");
        }

        // ── Fell takes priority over knockback in the same tick ──────────────────

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
                survivalDuration: 9999f,
                spawnStartDelay:  0f);

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

        // ── Jump: hop over enemy — airborne = no knockback ────────────────────────

        [Test]
        public void Jump_HopOverEnemy_AirborneNoKnockback()
        {
            var arena = SolidArena();

            // Grounded baseline: contact triggers knockback, state stays Playing.
            var sim1 = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 5.1f, oz: 4.5f,   // dist = 0.6 = contactRadius
                contactRadius: 0.6f);

            sim1.Tick(0.016f, 0f, 0f); // contact detected → knockback velocity set
            Assert.That(sim1.State, Is.EqualTo(DodgeState.Playing),
                "grounded contact → knockback, not Caught (no longer a loss condition)");

            float sim1StartX = sim1.PlayerX;
            sim1.Tick(0.1f, 0f, 0f); // knockback velocity applied → player moves left
            Assert.That(sim1.PlayerX, Is.LessThan(sim1StartX),
                "second tick: knockback pushes player away from obstacle (+X side)");

            // Airborne: no knockback at all — position unaffected.
            var sim2 = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 5.1f, oz: 4.5f,
                contactRadius: 0.6f);

            float sim2StartX = sim2.PlayerX;
            sim2.RequestJump();
            sim2.Tick(0.016f, 0f, 0f); // mid-air (0.016 < 0.6) → contact check skipped
            Assert.That(sim2.State,    Is.EqualTo(DodgeState.Playing));
            Assert.That(sim2.IsAirborne, Is.True, "still mid-air");

            sim2.Tick(0.016f, 0f, 0f); // still mid-air
            Assert.That(sim2.PlayerX, Is.EqualTo(sim2StartX).Within(1e-4f),
                "airborne → no knockback displacement");
        }

        // ── Jump: landing next to enemy triggers knockback, not Caught ────────────

        [Test]
        public void Jump_LandNextToEnemy_ReceivesKnockbackNotCaught()
        {
            var arena = SolidArena();
            // Enemy at exact contactRadius; player stationary → within radius on landing.
            var sim = OneObstacleSim(arena,
                px: 4.5f, pz: 4.5f,
                ox: 5.1f, oz: 4.5f,   // dist = 0.6 = contactRadius
                contactRadius: 0.6f);

            sim.RequestJump();
            sim.Tick(0.6f, 0f, 0f); // full jump → landing frame; contact check runs

            // With new rules: contact on landing triggers knockback, NOT Caught.
            Assert.That(sim.State,      Is.EqualTo(DodgeState.Playing),
                "landing next to enemy → knockback, Caught must never be produced");
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.None));
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

        // ── Obstacle spawn state: escalating cadence ─────────────────────────────

        [Test]
        public void Obstacle_NotYetSpawned_DoesNotMove()
        {
            // spawnStartDelay=99s so obstacle never activates in this test window.
            var arena = SolidArena();
            var sim = new DodgeSim(arena,
                obstacleCount:    1,
                playerSpeed:      0f,
                obstacleSpeed:    5f,
                contactRadius:    0.01f,
                survivalDuration: 9999f,
                spawnStartDelay:  99f);

            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.SetForcedObstacleStarts(new[] { (1f, 1f) });
            sim.Restart();

            float startX = sim.GetObstacleX(0);
            float startZ = sim.GetObstacleZ(0);

            sim.Tick(1f, 0f, 0f);

            Assert.That(sim.IsObstacleSpawned(0), Is.False, "not yet spawned");
            Assert.That(sim.GetObstacleX(0), Is.EqualTo(startX).Within(1e-4f),
                "not-spawned obstacle must not move X");
            Assert.That(sim.GetObstacleZ(0), Is.EqualTo(startZ).Within(1e-4f),
                "not-spawned obstacle must not move Z");
        }

        [Test]
        public void Obstacle_NotYetSpawned_NoKnockbackOnPlayer()
        {
            // Obstacle within contact range but not yet spawned — player must not be knocked back.
            var arena = SolidArena();
            var sim = new DodgeSim(arena,
                obstacleCount:    1,
                playerSpeed:      0f,
                obstacleSpeed:    0f,
                contactRadius:    2f,    // generous radius
                survivalDuration: 9999f,
                spawnStartDelay:  99f,   // never spawns in this test window
                knockbackSpeed:   10f);

            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.SetForcedObstacleStarts(new[] { (4.6f, 4.5f) }); // dist ≈ 0.1, within radius
            sim.Restart();

            float startX = sim.PlayerX;

            sim.Tick(0.1f, 0f, 0f);

            Assert.That(sim.State,   Is.EqualTo(DodgeState.Playing));
            Assert.That(sim.PlayerX, Is.EqualTo(startX).Within(1e-4f),
                "not-spawned obstacle must not knock back the player");
        }

        [Test]
        public void Obstacles_ActivateOneAtATime_InOrder()
        {
            // gap=3s fixed (gapDecay=0), spawnStartDelay=2s:
            //   obstacle 0 spawns at t=2, obstacle 1 at t=5, obstacle 2 at t=8.
            var arena = SolidArena();
            var sim = new DodgeSim(arena,
                obstacleCount:    3,
                playerSpeed:      0f,
                obstacleSpeed:    0f,
                contactRadius:    0.01f,
                survivalDuration: 9999f,
                spawnStartDelay:  2f,
                baseGap:          3f,
                gapDecay:         0f,   // no escalation for ordering test
                minGap:           3f);

            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.SetForcedObstacleStarts(new[] { (1f, 1f), (2f, 2f), (3f, 3f) });
            sim.Restart();

            // Before any spawn (t < 2).
            sim.Tick(1.99f, 0f, 0f);
            Assert.That(sim.IsObstacleSpawned(0), Is.False, "before spawnStartDelay, none spawned");

            // Obstacle 0 spawns at t=2.
            sim.Tick(0.02f, 0f, 0f); // t ≈ 2.01
            Assert.That(sim.IsObstacleSpawned(0), Is.True,  "obstacle 0 spawned at t=2");
            Assert.That(sim.IsObstacleSpawned(1), Is.False, "obstacle 1 not yet spawned");
            Assert.That(sim.IsObstacleSpawned(2), Is.False, "obstacle 2 not yet spawned");

            // Obstacle 1 spawns at t=5.
            sim.Tick(3.0f, 0f, 0f); // t ≈ 5.01
            Assert.That(sim.IsObstacleSpawned(0), Is.True,  "obstacle 0 still spawned");
            Assert.That(sim.IsObstacleSpawned(1), Is.True,  "obstacle 1 spawned at t=5");
            Assert.That(sim.IsObstacleSpawned(2), Is.False, "obstacle 2 not yet spawned at t=5");

            // Obstacle 2 spawns at t=8.
            sim.Tick(3.0f, 0f, 0f); // t ≈ 8.01
            Assert.That(sim.IsObstacleSpawned(2), Is.True, "obstacle 2 spawned at t=8");
        }

        [Test]
        public void Obstacles_LaterGapsShorterThanEarlierGaps()
        {
            // baseGap=5, gapDecay=0.6, minGap=1.5, spawnStartDelay=0:
            //   obstacle 0 at t=0
            //   gap_0 = max(1.5, 5 - 0*0.6) = 5.0 → obstacle 1 at t=5.0
            //   gap_1 = max(1.5, 5 - 1*0.6) = 4.4 → obstacle 2 at t=9.4
            // Later gap (4.4 s) < earlier gap (5.0 s) — escalating pressure.
            var arena = SolidArena();
            var sim = new DodgeSim(arena,
                obstacleCount:    3,
                playerSpeed:      0f,
                obstacleSpeed:    0f,
                contactRadius:    0.01f,
                survivalDuration: 9999f,
                spawnStartDelay:  0f,
                baseGap:          5f,
                gapDecay:         0.6f,
                minGap:           1.5f);

            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.SetForcedObstacleStarts(new[] { (1f, 1f), (2f, 2f), (3f, 3f) });
            sim.Restart();

            // At t=4.9s obstacle 1 must NOT yet be spawned (gap_0 = 5.0 s).
            sim.Tick(4.9f, 0f, 0f);
            Assert.That(sim.IsObstacleSpawned(1), Is.False,
                "at 4.9 s obstacle 1 not yet spawned (first gap = 5 s)");

            // At t=5.05s obstacle 1 IS spawned; obstacle 2 is not yet.
            sim.Tick(0.15f, 0f, 0f); // t ≈ 5.05
            Assert.That(sim.IsObstacleSpawned(1), Is.True,  "obstacle 1 spawns at t=5.0 s");
            Assert.That(sim.IsObstacleSpawned(2), Is.False, "obstacle 2 not yet spawned at t=5.05 s");

            // At t=9.3s obstacle 2 must NOT yet be spawned (second gap = 4.4 s, spawn at 9.4 s).
            sim.Tick(4.25f, 0f, 0f); // t ≈ 9.3
            Assert.That(sim.IsObstacleSpawned(2), Is.False,
                "at 9.3 s obstacle 2 not yet spawned (spawns at 9.4 s)");

            // At t=9.45s obstacle 2 IS spawned — gap_1 (4.4 s) was shorter than gap_0 (5 s).
            sim.Tick(0.15f, 0f, 0f); // t ≈ 9.45
            Assert.That(sim.IsObstacleSpawned(2), Is.True,
                "obstacle 2 spawns at t=9.4 s (second gap=4.4 s < first gap=5 s — escalation confirmed)");
        }

        [Test]
        public void NotSpawnedObstacle_DoesNotCount_ForAllFallenWin()
        {
            // Two obstacles: huge gap so only obstacle 0 spawns within the test window.
            // Even after obstacle 0 falls off the grid, obstacle 1 is still NotSpawned
            // → the all-fallen win condition must NOT trigger.
            var arena = new GridArena(n: 3, graceDelay: 0.5f, collapseInterval: 9999f);
            var sim = new DodgeSim(arena,
                obstacleCount:    2,
                playerSpeed:      0f,
                obstacleSpeed:    0f,
                contactRadius:    0.01f,
                survivalDuration: 9999f,
                spawnStartDelay:  0f,
                baseGap:          9999f); // obstacle 1 spawns way past test window

            sim.SetForcedPlayerStart(1.5f, 1.5f);
            sim.SetForcedObstacleStarts(new[] { (0.5f, 0.5f), (1.4f, 1.4f) });
            sim.Restart();

            arena.Tick(1f); // ring 0 collapses — obstacle 0 falls
            sim.Tick(1f, 0f, 0f);

            Assert.That(sim.IsObstacleAlive(0),   Is.False, "obstacle 0 fell");
            Assert.That(sim.IsObstacleSpawned(1), Is.False, "obstacle 1 not yet spawned");
            Assert.That(sim.State, Is.EqualTo(DodgeState.Playing),
                "obstacle 1 never spawned → all-fallen win must not trigger");
        }

        // ── Knockback mechanics ───────────────────────────────────────────────────

        [Test]
        public void PlayerContact_ReceivesKnockback_PositionMovesAway()
        {
            // Obstacle to the RIGHT of the player at exactly contactRadius.
            // After contact frame: knockback velocity set (pointing left).
            // After second tick: knockback applied → player moves LEFT (away from obstacle).
            var arena = SolidArena();
            var sim = new DodgeSim(arena,
                obstacleCount:    1,
                playerSpeed:      0f,
                obstacleSpeed:    0f,
                contactRadius:    0.6f,
                survivalDuration: 9999f,
                spawnStartDelay:  0f,
                knockbackSpeed:   10f,
                knockbackDamping: 0f); // no damping so velocity persists

            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.SetForcedObstacleStarts(new[] { (5.1f, 4.5f) }); // dist=0.6, within contactRadius
            sim.Restart();

            // First tick: spawn + contact detected → knockback velocity set, not applied yet.
            sim.Tick(0.016f, 0f, 0f);
            Assert.That(sim.State, Is.EqualTo(DodgeState.Playing), "contact must not cause Lost");

            float startX = sim.PlayerX;
            // Second tick: knockback velocity applied → player displaced left (−X).
            sim.Tick(0.1f, 0f, 0f);
            Assert.That(sim.PlayerX, Is.LessThan(startX),
                "player must be pushed left (away from obstacle on the +X side)");
        }

        [Test]
        public void PlayerContact_DoesNotSetLostOrCaught()
        {
            // Obstacle well within contact range; verify no Lost/Caught after contact.
            var arena = SolidArena();
            var sim = new DodgeSim(arena,
                obstacleCount:    1,
                playerSpeed:      0f,
                obstacleSpeed:    0f,
                contactRadius:    1.5f,  // large radius, definitely in contact
                survivalDuration: 9999f,
                spawnStartDelay:  0f);

            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.SetForcedObstacleStarts(new[] { (5f, 4.5f) }); // dist=0.5, within radius
            sim.Restart();

            sim.Tick(0.1f, 0f, 0f);
            Assert.That(sim.State,      Is.EqualTo(DodgeState.Playing),
                "contact must not trigger Lost");
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.None),
                "LostReason.Caught must never be produced");
        }

        [Test]
        public void PlayerKnockedOffEdge_LostFell()
        {
            // Player near left edge. Obstacle to the right pushes player leftward off the grid.
            var arena = SolidArena(n: 9);
            var sim = new DodgeSim(arena,
                obstacleCount:    1,
                playerSpeed:      0f,
                obstacleSpeed:    0f,
                contactRadius:    1f,
                survivalDuration: 9999f,
                spawnStartDelay:  0f,
                knockbackSpeed:   50f,   // strong enough to push player off in one tick
                knockbackDamping: 0f);

            // Player at (0.5, 4.5) — left edge (x=0 is solid). Obstacle at (1.3, 4.5).
            // dist = 0.8 < contactRadius=1 → contact → knockback points left (−X).
            sim.SetForcedPlayerStart(0.5f, 4.5f);
            sim.SetForcedObstacleStarts(new[] { (1.3f, 4.5f) });
            sim.Restart();

            // First tick: contact detected → knockback velocity set (pointing −X).
            sim.Tick(0.016f, 0f, 0f);
            Assert.That(sim.State, Is.EqualTo(DodgeState.Playing),
                "first tick: knockback set, not yet applied far enough");

            // Second tick: knockback moves player 50*0.5 = 25 units left → well off grid → Fell.
            sim.Tick(0.5f, 0f, 0f);
            Assert.That(sim.State,      Is.EqualTo(DodgeState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(LostReason.Fell),
                "knocked off the grid edge → Lost(Fell)");
        }

        [Test]
        public void AirborneContact_NoKnockback_PositionUnchanged()
        {
            // While airborne the contact check is entirely skipped — no knockback set.
            var arena = SolidArena();
            var sim = new DodgeSim(arena,
                obstacleCount:    1,
                playerSpeed:      0f,
                obstacleSpeed:    0f,
                contactRadius:    2f,    // very large radius
                survivalDuration: 9999f,
                spawnStartDelay:  0f,
                knockbackSpeed:   50f);

            sim.SetForcedPlayerStart(4.5f, 4.5f);
            sim.SetForcedObstacleStarts(new[] { (5f, 4.5f) }); // dist=0.5, well within radius

            sim.Restart();

            sim.RequestJump();
            float startX = sim.PlayerX;

            // Two mid-air ticks (0.016 + 0.016 = 0.032 < jumpDuration=0.6).
            sim.Tick(0.016f, 0f, 0f);
            Assert.That(sim.IsAirborne, Is.True);
            Assert.That(sim.State,      Is.EqualTo(DodgeState.Playing));

            sim.Tick(0.016f, 0f, 0f);
            Assert.That(sim.PlayerX, Is.EqualTo(startX).Within(1e-4f),
                "airborne player must not be displaced by knockback");
            Assert.That(sim.State, Is.EqualTo(DodgeState.Playing));
        }
    }
}
