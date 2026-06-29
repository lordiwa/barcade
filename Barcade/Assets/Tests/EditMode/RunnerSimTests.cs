using System;
using NUnit.Framework;
using Barcade.Core.Runner;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="RunnerSim"/>: speed escalation, lane clamping, jump/slide
    /// collision rules, coin collection, no double-jump, slide auto-end, spawn fairness,
    /// and determinism.
    /// </summary>
    [TestFixture]
    public class RunnerSimTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a sim with constant speed (no escalation), large spawn spacing so
        /// RNG spawns never appear in the test distance window, and an explicit starting
        /// lane.  Always sets forced lane; caller sets forced spawns then calls Restart().
        /// </summary>
        private static RunnerSim MakeSim(
            int   startLane     = 1,
            float speed         = 10f,
            float jumpDuration  = 0.5f,
            float slideDuration = 0.4f,
            int   seed          = 0)
        {
            var sim = new RunnerSim(
                laneCount:        3,
                baseSpeed:        speed,
                speedAccel:       0f,    // constant speed simplifies timing
                maxSpeed:         speed,
                jumpDuration:     jumpDuration,
                slideDuration:    slideDuration,
                coinValue:        1,
                spawnLookAhead:   500f,
                baseSpawnSpacing: 500f,  // huge: no RNG spawns in test range
                minSpawnSpacing:  500f,
                seed:             seed);
            sim.SetForcedLane(startLane);
            // Do NOT call Restart() here — let the test set forced spawns first,
            // then call Restart() once to apply both forced lane and forced spawns.
            return sim;
        }

        // ── Initial state ─────────────────────────────────────────────────────────

        [Test]
        public void InitialState_IsPlayingWithDefaults()
        {
            var sim = new RunnerSim();

            Assert.That(sim.State,          Is.EqualTo(RunnerState.Playing));
            Assert.That(sim.LostReason,     Is.EqualTo(RunnerLostReason.None));
            Assert.That(sim.Distance,       Is.EqualTo(0f).Within(1e-6f));
            Assert.That(sim.CoinsCollected, Is.EqualTo(0));
            Assert.That(sim.IsAirborne,     Is.False);
            Assert.That(sim.IsSliding,      Is.False);
            Assert.That(sim.JumpProgress01, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(sim.SlideProgress01,Is.EqualTo(0f).Within(1e-6f));
            // Default laneCount=3, starting lane = laneCount/2 = 1 (middle).
            Assert.That(sim.Lane, Is.EqualTo(1));
        }

        // ── Speed escalation ──────────────────────────────────────────────────────

        [Test]
        public void Speed_EscalatesOverTime_CappedAtMaxSpeed()
        {
            // Large spawn spacing suppresses RNG obstacles so Tick() never enters Lost.
            var sim = new RunnerSim(
                baseSpeed:        5f,
                speedAccel:       1f,
                maxSpeed:         10f,
                baseSpawnSpacing: 99999f,
                minSpawnSpacing:  99999f,
                seed:             0);

            Assert.That(sim.Speed, Is.EqualTo(5f).Within(1e-4f), "initial speed = baseSpeed");

            // After 3 s: speed = min(10, 5 + 1×3) = 8.
            sim.Tick(3f);
            Assert.That(sim.Speed, Is.EqualTo(8f).Within(1e-3f),
                "speed = baseSpeed + speedAccel × t");
            Assert.That(sim.Speed, Is.GreaterThan(5f), "speed must increase");

            // After 10 more seconds: would be 5+13=18, capped at 10.
            sim.Tick(10f);
            Assert.That(sim.Speed, Is.EqualTo(10f).Within(1e-4f),
                "speed must not exceed maxSpeed");
        }

        [Test]
        public void Distance_GrowsFasterLaterThanEarlier()
        {
            // With speed escalation, each second covers more distance than the previous.
            // Large spawn spacing suppresses RNG obstacles so Tick() never enters Lost.
            var sim = new RunnerSim(
                baseSpeed:        5f,
                speedAccel:       2f,
                maxSpeed:         1000f,  // effectively uncapped
                baseSpawnSpacing: 99999f,
                minSpawnSpacing:  99999f,
                seed:             0);

            float d0 = sim.Distance;
            sim.Tick(1f);
            float earlyGain = sim.Distance - d0; // first second

            sim.Tick(1f); // second second (speed now ~9)
            float d1 = sim.Distance;
            sim.Tick(1f); // third second (speed now ~13)
            float lateGain = sim.Distance - d1;

            Assert.That(lateGain, Is.GreaterThan(earlyGain),
                "distance per second must increase as speed escalates");
        }

        // ── Lane change: snap and clamp ───────────────────────────────────────────

        [Test]
        public void Lane_MoveLaneUp_SnapsAndClamps()
        {
            var sim = MakeSim(startLane: 1);
            sim.Restart();

            Assert.That(sim.Lane, Is.EqualTo(1), "starts in middle lane (forced)");

            sim.MoveLaneUp();
            Assert.That(sim.Lane, Is.EqualTo(2), "moved up to lane 2");

            sim.MoveLaneUp(); // attempt past max
            Assert.That(sim.Lane, Is.EqualTo(2), "clamped at laneCount-1 = 2");
        }

        [Test]
        public void Lane_MoveLaneDown_SnapsAndClamps()
        {
            var sim = MakeSim(startLane: 1);
            sim.Restart();

            sim.MoveLaneDown();
            Assert.That(sim.Lane, Is.EqualTo(0), "moved down to lane 0");

            sim.MoveLaneDown(); // attempt below 0
            Assert.That(sim.Lane, Is.EqualTo(0), "clamped at lane 0");
        }

        [Test]
        public void Lane_RequestLane_AppliesDeltaAndClamps()
        {
            var sim = MakeSim(startLane: 1);
            sim.Restart();

            sim.RequestLane(1);  // 1 → 2
            Assert.That(sim.Lane, Is.EqualTo(2));

            sim.RequestLane(5);  // 2+5=7, clamped at 2
            Assert.That(sim.Lane, Is.EqualTo(2));

            sim.RequestLane(-3); // 2-3=-1, clamped at 0
            Assert.That(sim.Lane, Is.EqualTo(0));
        }

        // ── Jump + LOW obstacle collision rules ───────────────────────────────────

        [Test]
        public void Jump_ClearsLowObstacle_SameLane()
        {
            // Speed 10 → distance=5 at t=0.5s. Obstacle at distance=4: crossed while airborne.
            var sim = MakeSim(startLane: 1, speed: 10f, jumpDuration: 0.5f);
            sim.SetForcedSpawns(new SpawnInfo(4f, 1, SpawnKind.LowObstacle));
            sim.Restart();

            sim.RequestJump();
            sim.Tick(0.5f); // distance crosses 4 while wasAirborne=true

            Assert.That(sim.State, Is.EqualTo(RunnerState.Playing),
                "jump clears LOW obstacle — must not be Lost");
        }

        [Test]
        public void LowObstacle_GroundedSameLane_CausesLoss()
        {
            var sim = MakeSim(startLane: 1, speed: 10f);
            sim.SetForcedSpawns(new SpawnInfo(4f, 1, SpawnKind.LowObstacle));
            sim.Restart();

            // No jump: grounded when crossing.
            sim.Tick(0.5f);

            Assert.That(sim.State,      Is.EqualTo(RunnerState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(RunnerLostReason.HitObstacle));
        }

        // ── Slide + HIGH obstacle collision rules ─────────────────────────────────

        [Test]
        public void Slide_ClearsHighObstacle_SameLane()
        {
            var sim = MakeSim(startLane: 1, speed: 10f, slideDuration: 0.4f);
            sim.SetForcedSpawns(new SpawnInfo(4f, 1, SpawnKind.HighObstacle));
            sim.Restart();

            sim.RequestSlide();
            sim.Tick(0.5f); // distance crosses 4 while wasSliding=true

            Assert.That(sim.State, Is.EqualTo(RunnerState.Playing),
                "slide clears HIGH obstacle — must not be Lost");
        }

        [Test]
        public void HighObstacle_NotSlidingSameLane_CausesLoss()
        {
            var sim = MakeSim(startLane: 1, speed: 10f);
            sim.SetForcedSpawns(new SpawnInfo(4f, 1, SpawnKind.HighObstacle));
            sim.Restart();

            // No slide: standing upright when crossing.
            sim.Tick(0.5f);

            Assert.That(sim.State,      Is.EqualTo(RunnerState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(RunnerLostReason.HitObstacle));
        }

        [Test]
        public void HighObstacle_JumpingSameLane_CausesLoss()
        {
            // Jumping does NOT clear a HIGH obstacle — only sliding does.
            var sim = MakeSim(startLane: 1, speed: 10f);
            sim.SetForcedSpawns(new SpawnInfo(4f, 1, SpawnKind.HighObstacle));
            sim.Restart();

            sim.RequestJump();
            sim.Tick(0.5f); // airborne (wasAirborne=true), but wasSliding=false → Lost

            Assert.That(sim.State,      Is.EqualTo(RunnerState.Lost));
            Assert.That(sim.LostReason, Is.EqualTo(RunnerLostReason.HitObstacle),
                "jump does not clear HIGH: must be Lost");
        }

        // ── Lane change avoids obstacle ───────────────────────────────────────────

        [Test]
        public void LaneChange_AvoidsObstacleInDifferentLane()
        {
            // Start in lane 1, obstacle in lane 1. Switch to lane 0 → no collision.
            var sim = MakeSim(startLane: 1, speed: 10f);
            sim.SetForcedSpawns(new SpawnInfo(4f, 1, SpawnKind.LowObstacle));
            sim.Restart();

            sim.MoveLaneDown(); // now in lane 0; obstacle is in lane 1
            sim.Tick(0.5f);     // crosses distance 4 in lane 0 → no collision

            Assert.That(sim.State, Is.EqualTo(RunnerState.Playing),
                "obstacle in lane 1 must not affect runner in lane 0");
        }

        // ── Coin collection ───────────────────────────────────────────────────────

        [Test]
        public void Coin_SameLane_IncrementsCoinsCollected()
        {
            var sim = MakeSim(startLane: 1, speed: 10f);
            sim.SetForcedSpawns(new SpawnInfo(4f, 1, SpawnKind.Coin));
            sim.Restart();

            Assert.That(sim.CoinsCollected, Is.EqualTo(0));
            sim.Tick(0.5f); // cross the coin
            Assert.That(sim.CoinsCollected, Is.EqualTo(1), "coin in same lane must be collected");
        }

        [Test]
        public void Coin_DifferentLane_IsNotCollected()
        {
            // Coin in lane 0, runner in lane 1.
            var sim = MakeSim(startLane: 1, speed: 10f);
            sim.SetForcedSpawns(new SpawnInfo(4f, 0, SpawnKind.Coin));
            sim.Restart();

            sim.Tick(0.5f);
            Assert.That(sim.CoinsCollected, Is.EqualTo(0),
                "coin in other lane must not be collected");
        }

        // ── No double-jump ────────────────────────────────────────────────────────

        [Test]
        public void Jump_NoDoubleJump_SecondRequestIgnoredWhileAirborne()
        {
            var sim = MakeSim(startLane: 1, jumpDuration: 0.5f);
            sim.Restart();

            sim.RequestJump();
            sim.Tick(0.2f); // mid-air (0.2 < 0.5)

            Assert.That(sim.IsAirborne, Is.True, "still airborne after 0.2 s");

            sim.RequestJump(); // second request — must be ignored
            sim.Tick(0.31f);   // total elapsed = 0.51 s > jumpDuration = 0.5 → lands

            Assert.That(sim.IsAirborne,     Is.False, "landed; second jump did not extend flight");
            Assert.That(sim.JumpProgress01, Is.EqualTo(0f).Within(1e-5f),
                "grounded → JumpProgress01 = 0");
        }

        // ── Slide auto-end ────────────────────────────────────────────────────────

        [Test]
        public void Slide_AutoEnds_AfterSlideDuration()
        {
            var sim = MakeSim(startLane: 1, slideDuration: 0.4f);
            sim.Restart();

            sim.RequestSlide();
            sim.Tick(0.2f); // mid-slide (0.2 < 0.4)

            Assert.That(sim.IsSliding,       Is.True, "still sliding at 0.2 s");
            Assert.That(sim.SlideProgress01, Is.GreaterThan(0f), "slide animation in progress");

            sim.Tick(0.25f); // total = 0.45 s > slideDuration = 0.4 → upright

            Assert.That(sim.IsSliding,       Is.False, "slide must end after slideDuration");
            Assert.That(sim.SlideProgress01, Is.EqualTo(0f).Within(1e-5f),
                "upright → SlideProgress01 = 0");
        }

        // ── Jump / slide mutual exclusion ─────────────────────────────────────────

        [Test]
        public void Jump_WhileSliding_IsIgnored()
        {
            var sim = MakeSim(startLane: 1);
            sim.Restart();

            sim.RequestSlide();
            sim.Tick(0.1f); // mid-slide

            Assert.That(sim.IsSliding, Is.True, "sliding after 0.1 s");

            sim.RequestJump(); // attempt to jump while sliding
            sim.Tick(0.1f);

            Assert.That(sim.IsAirborne,     Is.False, "jump must be ignored while sliding");
            Assert.That(sim.JumpProgress01, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Slide_WhileAirborne_IsIgnored()
        {
            var sim = MakeSim(startLane: 1);
            sim.Restart();

            sim.RequestJump();
            sim.Tick(0.1f); // mid-air

            Assert.That(sim.IsAirborne, Is.True, "airborne after 0.1 s");

            sim.RequestSlide(); // attempt to slide while airborne
            sim.Tick(0.1f);

            Assert.That(sim.IsSliding, Is.False, "slide must be ignored while airborne");
        }

        // ── Tick no-op after terminal state ───────────────────────────────────────

        [Test]
        public void Tick_NoOp_AfterLost()
        {
            var sim = MakeSim(startLane: 1, speed: 10f);
            sim.SetForcedSpawns(new SpawnInfo(4f, 1, SpawnKind.LowObstacle));
            sim.Restart();

            sim.Tick(0.5f); // hit obstacle → Lost
            Assert.That(sim.State, Is.EqualTo(RunnerState.Lost));

            float d = sim.Distance;
            int   c = sim.CoinsCollected;
            sim.Tick(5f); // must be a no-op

            Assert.That(sim.Distance,       Is.EqualTo(d).Within(1e-6f),
                "distance must not advance after Lost");
            Assert.That(sim.CoinsCollected, Is.EqualTo(c),
                "coins must not change after Lost");
        }

        // ── Restart resets state ──────────────────────────────────────────────────

        [Test]
        public void Restart_AfterLoss_ResetsToPlaying()
        {
            var sim = MakeSim(startLane: 1, speed: 10f);
            sim.SetForcedSpawns(new SpawnInfo(4f, 1, SpawnKind.LowObstacle));
            sim.Restart();

            sim.Tick(0.5f); // Lost
            Assert.That(sim.State, Is.EqualTo(RunnerState.Lost));

            sim.Restart();

            Assert.That(sim.State,          Is.EqualTo(RunnerState.Playing));
            Assert.That(sim.LostReason,     Is.EqualTo(RunnerLostReason.None));
            Assert.That(sim.Distance,       Is.EqualTo(0f).Within(1e-6f));
            Assert.That(sim.CoinsCollected, Is.EqualTo(0));
            Assert.That(sim.IsAirborne,     Is.False);
            Assert.That(sim.IsSliding,      Is.False);
        }

        // ── Spawn fairness: never block all lanes simultaneously ──────────────────

        [Test]
        public void Spawner_Fairness_NeverBlocksAllLanesSynchronously()
        {
            // Generate many spawns and verify no distance window of
            // minSpawnSpacing * laneCount has obstacles covering ALL 3 lanes.
            const int   laneCount        = 3;
            const float minSpawnSpacing  = 2f;
            const float safetyWindow     = minSpawnSpacing * laneCount; // 6 units

            var sim = new RunnerSim(
                laneCount:        laneCount,
                baseSpeed:        10f,
                speedAccel:       0f,
                maxSpeed:         10f,
                baseSpawnSpacing: 4f,
                minSpawnSpacing:  minSpawnSpacing,
                spawnLookAhead:   500f,
                seed:             42);

            // Tick enough to generate several hundred spawns.
            for (int i = 0; i < 60; i++)
                sim.Tick(0.016f);

            var spawns = sim.ActiveSpawns;
            Assert.That(spawns.Count, Is.GreaterThan(0), "sanity: spawns must have been generated");

            for (int i = 0; i < spawns.Count; i++)
            {
                if (spawns[i].Kind == SpawnKind.Coin) continue;

                // For this obstacle spawn, find all obstacle spawns within safetyWindow ahead.
                bool[] blocked = new bool[laneCount];
                for (int j = 0; j < spawns.Count; j++)
                {
                    if (spawns[j].Kind == SpawnKind.Coin) continue;
                    float delta = spawns[j].DistancePos - spawns[i].DistancePos;
                    if (delta >= 0f && delta < safetyWindow)
                        blocked[spawns[j].Lane] = true;
                }

                bool allBlocked = true;
                for (int l = 0; l < laneCount; l++)
                    if (!blocked[l]) { allBlocked = false; break; }

                Assert.That(allBlocked, Is.False,
                    $"obstacle at distance {spawns[i].DistancePos:F1} lane {spawns[i].Lane} " +
                    "is part of a cluster that blocks all 3 lanes within " +
                    $"{safetyWindow:F1} units — fairness violated");
            }
        }

        // ── Determinism ───────────────────────────────────────────────────────────

        [Test]
        public void Determinism_SameSeedSameInput_IdenticalOutcome()
        {
            const int seed = 77;

            var sim1 = new RunnerSim(baseSpeed: 5f, speedAccel: 0.5f, maxSpeed: 20f, seed: seed);
            var sim2 = new RunnerSim(baseSpeed: 5f, speedAccel: 0.5f, maxSpeed: 20f, seed: seed);

            for (int i = 0; i < 120; i++)
            {
                // Identical input pattern.
                if (i % 7  == 0) { sim1.RequestJump();  sim2.RequestJump();  }
                if (i % 11 == 0) { sim1.MoveLaneUp();   sim2.MoveLaneUp();   }
                if (i % 13 == 0) { sim1.MoveLaneDown(); sim2.MoveLaneDown(); }
                if (i % 17 == 0) { sim1.RequestSlide(); sim2.RequestSlide(); }

                sim1.Tick(0.05f);
                sim2.Tick(0.05f);

                Assert.That(sim1.Distance,       Is.EqualTo(sim2.Distance).Within(1e-4f),
                    $"Distance mismatch at tick {i}");
                Assert.That(sim1.CoinsCollected,  Is.EqualTo(sim2.CoinsCollected),
                    $"CoinsCollected mismatch at tick {i}");
                Assert.That(sim1.State,           Is.EqualTo(sim2.State),
                    $"State mismatch at tick {i}");
                Assert.That(sim1.Lane,            Is.EqualTo(sim2.Lane),
                    $"Lane mismatch at tick {i}");

                if (sim1.State != RunnerState.Playing) break;
            }
        }

        // ── Multiple coins and obstacles in sequence ──────────────────────────────

        [Test]
        public void MultipleSpawns_SameLane_ResolvedInOrder()
        {
            // Two coins at distances 3 and 7; one LOW obstacle at distance 5.
            // Jump over the obstacle → collect both coins, survive.
            var sim = MakeSim(startLane: 1, speed: 10f, jumpDuration: 0.15f);
            sim.SetForcedSpawns(
                new SpawnInfo(3f, 1, SpawnKind.Coin),
                new SpawnInfo(5f, 1, SpawnKind.LowObstacle),
                new SpawnInfo(7f, 1, SpawnKind.Coin));
            sim.Restart();

            sim.Tick(0.3f); // cross coin at 3; distance=3, still before obstacle

            Assert.That(sim.CoinsCollected, Is.EqualTo(1), "first coin collected at distance 3");
            Assert.That(sim.State,          Is.EqualTo(RunnerState.Playing));

            // Jump before the LOW obstacle (distance 5 → needs to arrive while airborne).
            // jumpDuration=0.15s → covers 1.5 units. Cross obstacle at distance=5 while airborne.
            sim.RequestJump();
            sim.Tick(0.2f); // crosses distance 5 while wasAirborne=true → clear

            Assert.That(sim.State, Is.EqualTo(RunnerState.Playing),
                "jumped over LOW obstacle: still Playing");

            sim.Tick(0.25f); // crosses distance 7 → second coin
            Assert.That(sim.CoinsCollected, Is.EqualTo(2), "second coin collected at distance 7");
        }

        // ── JumpProgress01 arc ────────────────────────────────────────────────────

        [Test]
        public void JumpProgress01_StartsAt0_PeaksNearMidpoint_Returns0OnLanding()
        {
            var sim = MakeSim(startLane: 1, jumpDuration: 0.6f);
            sim.Restart();

            Assert.That(sim.JumpProgress01, Is.EqualTo(0f).Within(1e-6f), "grounded at start");

            sim.RequestJump();
            sim.Tick(0.001f); // just took off

            Assert.That(sim.JumpProgress01, Is.GreaterThan(0f), "progress > 0 after takeoff");
            Assert.That(sim.JumpProgress01, Is.LessThan(0.1f),  "progress near 0 just after takeoff");

            sim.Tick(0.3f); // ~mid-jump

            Assert.That(sim.JumpProgress01, Is.GreaterThan(0.4f), "progress > 0.4 at mid-jump");
            Assert.That(sim.JumpProgress01, Is.LessThan(1f),      "progress < 1 before landing");

            sim.Tick(0.31f); // total > 0.6 → lands

            Assert.That(sim.IsAirborne,     Is.False, "must have landed");
            Assert.That(sim.JumpProgress01, Is.EqualTo(0f).Within(1e-5f),
                "grounded → JumpProgress01 = 0");
        }
    }
}
