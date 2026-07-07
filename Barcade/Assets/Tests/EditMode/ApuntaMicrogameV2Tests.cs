using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Microgames.V2;
// Barcade.Core.Tests is lexically nested inside Barcade.Core, so an unqualified
// name resolves against Barcade.Core's own members before "using"-imported ones —
// see the identical note in ReaccionaMicrogameTests.cs. Renamed aliases sidestep
// the MicrogameResult/IMicrogame/InputSnapshot collisions.
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-104 — coverage for <see cref="ApuntaMicrogame"/> (§4 MECH_04): v2
    /// IMicrogame contract shape, charge-power determinism, ballistic landing
    /// determinism (+ wind), target reachability from all 4 corners (solver),
    /// timeout auto-fire, same-tick contested-target resolution, ranking, and
    /// zero allocation.
    ///
    /// Drives the mechanic through the v2 InputSnapshot/PlayerInput contract.
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class ApuntaMicrogameV2Tests
    {
        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        // ── Test double: builds a v2 InputSnapshot from per-seat stick+button state ──

        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(PlayerSlot slot, Direction8 stick, bool pressed)
                => _players[(int)slot] = new PlayerInput(stick, pressed);

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        private static ApuntaParams DefaultParams => ApuntaParams.GddDefaults;

        // ── AC1 — v2 contract shape ─────────────────────────────────────────────

        [Test]
        public void Contract_InitialState_IdAndShapeAreCorrect()
        {
            var mg = new ApuntaMicrogame(DefaultParams);
            Assert.That(mg, Is.InstanceOf<V2Microgame>());
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.Apunta));
            Assert.That(mg.IsFinished, Is.False);
        }

        [Test]
        public void Contract_GetResult_BeforeFinished_Throws()
        {
            var mg = new ApuntaMicrogame(DefaultParams);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            Assert.Throws<InvalidOperationException>(() => mg.GetResult());
        }

        [Test]
        public void Contract_RenderState_PublishesTurretsAndTargets()
        {
            var mg = new ApuntaMicrogame(DefaultParams);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();
            mg.Tick(inputs.Build(0));

            RenderState rs = mg.GetRenderState();
            int avatarCount = 0, targetCount = 0;
            for (int i = 0; i < rs.EntityCount; i++)
            {
                if (rs.Entities[i].Kind == EntityKind.PlayerAvatar) avatarCount++;
                if (rs.Entities[i].Kind == EntityKind.Target) targetCount++;
            }
            Assert.That(avatarCount, Is.EqualTo(4));
            Assert.That(targetCount, Is.EqualTo(DefaultParams.TargetCount));
        }

        // ── Turret corners / default aim ────────────────────────────────────────

        [Test]
        public void TurretCorner_MapsEachSeatToADistinctCornerOfUnitSquare()
        {
            var corners = new HashSet<(float, float)>();
            foreach (PlayerSlot slot in AllSlots)
            {
                (float x, float y) = ApuntaMicrogame.TurretCorner(slot);
                Assert.That(x, Is.EqualTo(0f).Or.EqualTo(1f));
                Assert.That(y, Is.EqualTo(0f).Or.EqualTo(1f));
                corners.Add((x, y));
            }
            Assert.That(corners.Count, Is.EqualTo(4), "all 4 seats must map to distinct corners");
        }

        [Test]
        public void CurrentAim_ColdStart_FacesFromCornerTowardCenter()
        {
            var mg = new ApuntaMicrogame(DefaultParams);
            mg.Initialize(new SeededRandom(2), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs(); // stick left at None for everyone
            mg.Tick(inputs.Build(0));

            // Each corner's toward-center vector is exactly one of the 4 diagonals:
            // Rojo (0,0) -> NE, Azul (1,0) -> NW, Amarillo (1,1) -> SW, Verde (0,1) -> SE.
            Assert.That(mg.CurrentAim(PlayerSlot.Rojo), Is.EqualTo(Direction8.NE));
            Assert.That(mg.CurrentAim(PlayerSlot.Azul), Is.EqualTo(Direction8.NW));
            Assert.That(mg.CurrentAim(PlayerSlot.Amarillo), Is.EqualTo(Direction8.SW));
            Assert.That(mg.CurrentAim(PlayerSlot.Verde), Is.EqualTo(Direction8.SE));
        }

        [Test]
        public void CurrentAim_StickReturnsToNone_KeepsLastAim()
        {
            var mg = new ApuntaMicrogame(DefaultParams);
            mg.Initialize(new SeededRandom(3), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            inputs.Set(PlayerSlot.Rojo, Direction8.N, false);
            mg.Tick(inputs.Build(0));
            Assert.That(mg.CurrentAim(PlayerSlot.Rojo), Is.EqualTo(Direction8.N));

            inputs.Set(PlayerSlot.Rojo, Direction8.None, false); // stick released to center
            mg.Tick(inputs.Build(1));
            Assert.That(mg.CurrentAim(PlayerSlot.Rojo), Is.EqualTo(Direction8.N), "aim must persist through a centered stick");
        }

        // ── AC2 — charge power determinism ──────────────────────────────────────

        [Test]
        public void ChargePower_IsPureFunctionOfHoldDuration()
        {
            float p1 = ApuntaMicrogame.ChargePower(holdTicks: 18, chargeCycleSeconds: 1.2f, ticksPerSecond: 60);
            float p2 = ApuntaMicrogame.ChargePower(holdTicks: 18, chargeCycleSeconds: 1.2f, ticksPerSecond: 60);
            Assert.That(p2, Is.EqualTo(p1));

            // p(t) = 0.5*(1 + sin(w*t)); at t_hold=0 the oscillating meter starts at 0.5.
            float p0 = ApuntaMicrogame.ChargePower(holdTicks: 0, chargeCycleSeconds: 1.2f, ticksPerSecond: 60);
            Assert.That(p0, Is.EqualTo(0.5f).Within(0.0001f));

            // Quarter of the cycle (0.3s = 18 ticks @60Hz) -> sin(pi/2) = 1 -> power = 1.
            float pQuarter = ApuntaMicrogame.ChargePower(holdTicks: 18, chargeCycleSeconds: 1.2f, ticksPerSecond: 60);
            Assert.That(pQuarter, Is.EqualTo(1f).Within(0.001f));

            // Always within [0,1].
            for (int t = 0; t < 200; t++)
            {
                float p = ApuntaMicrogame.ChargePower(t, 1.2f, 60);
                Assert.That(p, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void Determinism_HoldOfSameDuration_FiresAtSameDistanceEveryTime()
        {
            var mg1 = new ApuntaMicrogame(DefaultParams);
            mg1.Initialize(new SeededRandom(7), PlayerRoster.AllHuman, 1f);
            var mg2 = new ApuntaMicrogame(DefaultParams);
            mg2.Initialize(new SeededRandom(7), PlayerRoster.AllHuman, 1f);

            HoldAndRelease(mg1, PlayerSlot.Rojo, Direction8.E, chargeTicksAtRelease: 18);
            HoldAndRelease(mg2, PlayerSlot.Rojo, Direction8.E, chargeTicksAtRelease: 18);

            Assert.That(mg2.LastLandingX(PlayerSlot.Rojo), Is.EqualTo(mg1.LastLandingX(PlayerSlot.Rojo)).Within(0.0001f));
            Assert.That(mg2.LastLandingY(PlayerSlot.Rojo), Is.EqualTo(mg1.LastLandingY(PlayerSlot.Rojo)).Within(0.0001f));
        }

        /// <summary>
        /// Holds <paramref name="slot"/>'s button (aiming <paramref name="stick"/>)
        /// until <see cref="ApuntaMicrogame.CurrentChargeTicks"/> reaches exactly
        /// <paramref name="chargeTicksAtRelease"/>, then releases (2 ticks) to fire.
        /// Discovers debounce timing via the observable accessor rather than
        /// hand-computing tick offsets.
        /// </summary>
        private static void HoldAndRelease(ApuntaMicrogame mg, PlayerSlot slot, Direction8 stick, int chargeTicksAtRelease, int maxTicks = 2000)
        {
            var inputs = new FakeInputs();
            int t = 0;
            for (; t < maxTicks; t++)
            {
                inputs.Set(slot, stick, true);
                mg.Tick(inputs.Build(t));
                if (mg.CurrentChargeTicks(slot) >= chargeTicksAtRelease) { t++; break; }
            }
            inputs.Set(slot, stick, false);
            mg.Tick(inputs.Build(t)); t++;
            mg.Tick(inputs.Build(t));
        }

        // ── AC3 — ballistic determinism + wind ──────────────────────────────────

        [Test]
        public void Wind_NonZeroAccel_ShiftsLandingPointDeterministically()
        {
            var noWind = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 3, targetMovingEnabled: false, targetMovingSpeed: 0.15f,
                windAccel: 0f, projectileSpeedMin: 0.45f, projectileSpeedMax: 0.95f, projectileFlightSeconds: 0.3f,
                durationSeconds: 5f, hitRadius: 0.18f, centralZoneMin: 0.4f, centralZoneMax: 0.6f,
                inputConfig: InputInterpreterConfig.GddDefaults);
            var withWind = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 3, targetMovingEnabled: false, targetMovingSpeed: 0.15f,
                windAccel: 0.1f, projectileSpeedMin: 0.45f, projectileSpeedMax: 0.95f, projectileFlightSeconds: 0.3f,
                durationSeconds: 5f, hitRadius: 0.18f, centralZoneMin: 0.4f, centralZoneMax: 0.6f,
                inputConfig: InputInterpreterConfig.GddDefaults);

            var mgA = new ApuntaMicrogame(noWind);
            mgA.Initialize(new SeededRandom(9), PlayerRoster.AllHuman, 1f);
            var mgB = new ApuntaMicrogame(withWind);
            mgB.Initialize(new SeededRandom(9), PlayerRoster.AllHuman, 1f);

            HoldAndRelease(mgA, PlayerSlot.Rojo, Direction8.NE, 18);
            HoldAndRelease(mgB, PlayerSlot.Rojo, Direction8.NE, 18);

            Assert.That(mgB.LastLandingX(PlayerSlot.Rojo), Is.Not.EqualTo(mgA.LastLandingX(PlayerSlot.Rojo)).Within(0.0001f),
                "non-zero windAccel must shift the landing point");
            Assert.That(mgB.LastLandingY(PlayerSlot.Rojo), Is.EqualTo(mgA.LastLandingY(PlayerSlot.Rojo)).Within(0.0001f),
                "this implementation's wind model only shifts the X axis — see ApuntaMicrogame class doc");
        }

        // ── AC4 — reachability solver ───────────────────────────────────────────

        [Test]
        public void Reachability_EveryTargetReachableFromEveryCorner_AcrossManySeeds()
        {
            ApuntaParams p = DefaultParams;
            const int seedCount = 150;

            for (int seed = 0; seed < seedCount; seed++)
            {
                var mg = new ApuntaMicrogame(p);
                mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);
                var inputs = new FakeInputs();
                mg.Tick(inputs.Build(0)); // publish target positions into RenderState

                RenderState rs = mg.GetRenderState();
                var targetPositions = new List<(float X, float Y)>();
                for (int i = 0; i < rs.EntityCount; i++)
                    if (rs.Entities[i].Kind == EntityKind.Target)
                        targetPositions.Add((rs.Entities[i].X, rs.Entities[i].Y));

                Assert.That(targetPositions.Count, Is.EqualTo(p.TargetCount), $"seed {seed}");

                foreach (PlayerSlot slot in AllSlots)
                {
                    (float cx, float cy) = ApuntaMicrogame.TurretCorner(slot);
                    foreach ((float tx, float ty) in targetPositions)
                    {
                        bool reachable = CanReach(cx, cy, tx, ty, p);
                        Assert.That(reachable, Is.True,
                            $"seed {seed}: target ({tx:F3},{ty:F3}) unreachable from {slot} corner ({cx},{cy})");
                    }
                }
            }
        }

        /// <summary>
        /// Brute-force numerical solver using the exact same 8-direction/landing-
        /// distance formula as production firing (<see cref="ApuntaMicrogame.DirectionToUnit"/>
        /// + the linear power-to-distance mapping) — samples power finely enough
        /// that any gap versus continuous power is well inside <c>hitRadius</c>.
        /// </summary>
        private static bool CanReach(float turretX, float turretY, float targetX, float targetY, ApuntaParams p)
        {
            Direction8[] dirs =
            {
                Direction8.N, Direction8.NE, Direction8.E, Direction8.SE,
                Direction8.S, Direction8.SW, Direction8.W, Direction8.NW
            };

            const int powerSamples = 400;
            foreach (Direction8 d in dirs)
            {
                (float dx, float dy) = ApuntaMicrogame.DirectionToUnit(d);
                for (int i = 0; i <= powerSamples; i++)
                {
                    float power = (float)i / powerSamples;
                    float distance = p.ProjectileSpeedMin + power * (p.ProjectileSpeedMax - p.ProjectileSpeedMin);
                    float landX = turretX + dx * distance;
                    float landY = turretY + dy * distance;
                    float dist = MathF.Sqrt((landX - targetX) * (landX - targetX) + (landY - targetY) * (landY - targetY));
                    if (dist <= p.HitRadius) return true;
                }
            }
            return false;
        }

        // ── AC5 — timeout auto-fire ──────────────────────────────────────────────

        [Test]
        public void TimeoutAutoFire_StillHeldButtonFiresWithInstantaneousPower()
        {
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 3, targetMovingEnabled: false, targetMovingSpeed: 0.15f,
                windAccel: 0f, projectileSpeedMin: 0.45f, projectileSpeedMax: 0.95f, projectileFlightSeconds: 0.3f,
                durationSeconds: 0.2f /* short round for a fast test */, hitRadius: 0.18f,
                centralZoneMin: 0.4f, centralZoneMax: 0.6f, inputConfig: InputInterpreterConfig.GddDefaults);

            var mg = new ApuntaMicrogame(p);
            mg.Initialize(new SeededRandom(4), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            // Hold Rojo's button from tick 0 and never release manually.
            for (int t = 0; t < 400 && !mg.IsFinished; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.E, true);
                foreach (PlayerSlot slot in AllSlots)
                    if (slot != PlayerSlot.Rojo) inputs.Set(slot, Direction8.None, false);
                mg.Tick(inputs.Build(t));
            }

            Assert.That(mg.ShotsFired(PlayerSlot.Rojo), Is.EqualTo(1), "the still-held button must auto-fire exactly once at timeout");
        }

        // ── AC6 — same-tick contested target ────────────────────────────────────

        [Test]
        public void SameTickContestedTarget_MorePreciseWins_LoserPassesToNextRemainingTarget()
        {
            // Force a single-target, two-seat scenario: Rojo and Azul both aim at
            // the same target with the same flight time so their shots arrive on
            // the identical tick; Rojo is closer (more precise) and must win it.
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 1, targetMovingEnabled: false, targetMovingSpeed: 0f,
                windAccel: 0f, projectileSpeedMin: 0.3f, projectileSpeedMax: 0.3f, projectileFlightSeconds: 0.2f,
                durationSeconds: 5f, hitRadius: 0.5f /* generous: both must be candidates for the sole target */,
                centralZoneMin: 0.5f, centralZoneMax: 0.5f /* single fixed target position */,
                inputConfig: InputInterpreterConfig.GddDefaults);

            var mg = new ApuntaMicrogame(p);
            mg.Initialize(new SeededRandom(50), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            // Both fire on the very first tick with a fresh (untouched, count=0) charge
            // so both shots use the same power and therefore the same fixed distance
            // (projectileSpeedMin == projectileSpeedMax here), arriving the same tick.
            // Rojo (0,0) aims E: lands at (0.3, 0). Azul (1,0) aims W: lands at (0.7, 0).
            // Neither lands exactly on target (0.5,0.5) but both are within the
            // generous hitRadius, and Rojo's distance to target is smaller.
            inputs.Set(PlayerSlot.Rojo, Direction8.E, true);
            inputs.Set(PlayerSlot.Azul, Direction8.W, true);
            mg.Tick(inputs.Build(0));
            inputs.Set(PlayerSlot.Rojo, Direction8.E, false);
            inputs.Set(PlayerSlot.Azul, Direction8.W, false);
            mg.Tick(inputs.Build(1));
            mg.Tick(inputs.Build(2));

            // Run until both shots have resolved (flight time elapses).
            for (int t = 3; t < 200; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.None, false);
                inputs.Set(PlayerSlot.Azul, Direction8.None, false);
                mg.Tick(inputs.Build(t));
            }

            Assert.That(mg.HitCount(PlayerSlot.Rojo) + mg.HitCount(PlayerSlot.Azul), Is.EqualTo(1),
                "only one seat may score the sole, single target");
            Assert.That(mg.HitCount(PlayerSlot.Rojo), Is.EqualTo(1), "Rojo's shot lands closer to the target and must win the contest");
            Assert.That(mg.HitCount(PlayerSlot.Azul), Is.EqualTo(0), "Azul's shot loses the contest and has no other target to pass to");
        }

        // ── AC7 — ranking ────────────────────────────────────────────────────────

        [Test]
        public void Ranking_MoreHitsRanksBetter_TiedHitsBreakBySummedPrecision()
        {
            // A short, controlled scenario rather than seeding internal counters
            // directly (nothing exposes writable hit/precision state): Rojo fires
            // once at the sole target and scores; Azul never fires. Metric/Place
            // ordering must reflect that. The same-tick contested-target mechanics
            // (which drive the precision tiebreak) are covered separately by
            // SameTickContestedTarget_MorePreciseWins_LoserPassesToNextRemainingTarget.
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 1, targetMovingEnabled: false, targetMovingSpeed: 0f,
                windAccel: 0f, projectileSpeedMin: 0.3f, projectileSpeedMax: 0.3f, projectileFlightSeconds: 0.05f,
                durationSeconds: 0.3f, hitRadius: 0.5f, centralZoneMin: 0.5f, centralZoneMax: 0.5f,
                inputConfig: InputInterpreterConfig.GddDefaults);
            var mg = new ApuntaMicrogame(p);
            mg.Initialize(new SeededRandom(51), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            inputs.Set(PlayerSlot.Rojo, Direction8.E, true);
            mg.Tick(inputs.Build(0));
            inputs.Set(PlayerSlot.Rojo, Direction8.E, false);
            mg.Tick(inputs.Build(1));
            mg.Tick(inputs.Build(2));

            for (int t = 3; t < 200 && !mg.IsFinished; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.None, false);
                mg.Tick(inputs.Build(t));
            }

            V2Result result = mg.GetResult();
            PlayerRank rojoRank = FindRank(result, PlayerSlot.Rojo);
            PlayerRank azulRank = FindRank(result, PlayerSlot.Azul);

            Assert.That(rojoRank.Metric, Is.EqualTo(1), "Metric reports hit count");
            Assert.That(azulRank.Metric, Is.EqualTo(0));
            Assert.That(rojoRank.Place, Is.LessThan(azulRank.Place), "more hits must rank strictly better");
        }

        [Test]
        public void Ranking_AllZeroHits_TieAtPlaceOne()
        {
            var mg = new ApuntaMicrogame(DefaultParams);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();
            for (int t = 0; t < 320 && !mg.IsFinished; t++) // GddDefaults duration=5s=300 ticks, nobody fires
                mg.Tick(inputs.Build(t));

            V2Result result = mg.GetResult();
            foreach (PlayerRank r in result.Ranks)
            {
                Assert.That(r.Place, Is.EqualTo(1));
                Assert.That(r.Metric, Is.EqualTo(0));
            }
        }

        private static PlayerRank FindRank(V2Result result, PlayerSlot slot)
        {
            foreach (PlayerRank r in result.Ranks)
                if (r.Seat == (int)slot) return r;
            throw new InvalidOperationException("seat not found in result");
        }

        // ── AC8 — zero heap allocation in steady-state Tick ────────────────────

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            var mg = new ApuntaMicrogame(DefaultParams);
            mg.Initialize(new SeededRandom(123), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, Direction8.E, true);
            inputs.Set(PlayerSlot.Azul, Direction8.N, false);
            inputs.Set(PlayerSlot.Amarillo, Direction8.NW, true);
            inputs.Set(PlayerSlot.Verde, Direction8.None, false);

            for (int i = 0; i < 300; i++) mg.Tick(inputs.Build(i)); // warm up (JIT, first charge/fire cycles)

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 300; i < 1300; i++) mg.Tick(inputs.Build(i));
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L));
        }

        // ── MicrogameId ──────────────────────────────────────────────────────────

        [Test]
        public void MicrogameId_HasApuntaMember()
        {
            Assert.That(MicrogameId.Apunta, Is.Not.EqualTo(MicrogameId.Reacciona));
        }
    }
}
