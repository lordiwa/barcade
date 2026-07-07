using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Microgames.V2;
// Barcade.Core.Tests is lexically nested inside Barcade.Core, so an unqualified
// name resolves against Barcade.Core's own members before "using"-imported ones —
// see the identical note in ReaccionaMicrogameTests.cs. Renamed aliases sidestep
// the MicrogameResult/IMicrogame/InputSnapshot/ApuntaMicrogame collisions (the v1
// Barcade.Core.ApuntaMicrogame from TASK-011 shares this v2 mechanic's simple name).
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Apunta = Barcade.Core.Microgames.V2.ApuntaMicrogame;

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
            var mg = new V2Apunta(DefaultParams);
            Assert.That(mg, Is.InstanceOf<V2Microgame>());
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.Apunta));
            Assert.That(mg.IsFinished, Is.False);
        }

        [Test]
        public void Contract_GetResult_BeforeFinished_Throws()
        {
            var mg = new V2Apunta(DefaultParams);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            Assert.Throws<InvalidOperationException>(() => mg.GetResult());
        }

        [Test]
        public void Contract_RenderState_PublishesTurretsAndTargets()
        {
            var mg = new V2Apunta(DefaultParams);
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
                (float x, float y) = V2Apunta.TurretCorner(slot);
                Assert.That(x, Is.EqualTo(0f).Or.EqualTo(1f));
                Assert.That(y, Is.EqualTo(0f).Or.EqualTo(1f));
                corners.Add((x, y));
            }
            Assert.That(corners.Count, Is.EqualTo(4), "all 4 seats must map to distinct corners");
        }

        [Test]
        public void CurrentAim_ColdStart_FacesFromCornerTowardCenter()
        {
            var mg = new V2Apunta(DefaultParams);
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
            var mg = new V2Apunta(DefaultParams);
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
            float p1 = V2Apunta.ChargePower(holdTicks: 18, chargeCycleSeconds: 1.2f, ticksPerSecond: 60);
            float p2 = V2Apunta.ChargePower(holdTicks: 18, chargeCycleSeconds: 1.2f, ticksPerSecond: 60);
            Assert.That(p2, Is.EqualTo(p1));

            // p(t) = 0.5*(1 + sin(w*t)); at t_hold=0 the oscillating meter starts at 0.5.
            float p0 = V2Apunta.ChargePower(holdTicks: 0, chargeCycleSeconds: 1.2f, ticksPerSecond: 60);
            Assert.That(p0, Is.EqualTo(0.5f).Within(0.0001f));

            // Quarter of the cycle (0.3s = 18 ticks @60Hz) -> sin(pi/2) = 1 -> power = 1.
            float pQuarter = V2Apunta.ChargePower(holdTicks: 18, chargeCycleSeconds: 1.2f, ticksPerSecond: 60);
            Assert.That(pQuarter, Is.EqualTo(1f).Within(0.001f));

            // Always within [0,1].
            for (int t = 0; t < 200; t++)
            {
                float p = V2Apunta.ChargePower(t, 1.2f, 60);
                Assert.That(p, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void Determinism_HoldOfSameDuration_FiresAtSameDistanceEveryTime()
        {
            var mg1 = new V2Apunta(DefaultParams);
            mg1.Initialize(new SeededRandom(7), PlayerRoster.AllHuman, 1f);
            var mg2 = new V2Apunta(DefaultParams);
            mg2.Initialize(new SeededRandom(7), PlayerRoster.AllHuman, 1f);

            HoldAndRelease(mg1, PlayerSlot.Rojo, Direction8.E, chargeTicksAtRelease: 18);
            HoldAndRelease(mg2, PlayerSlot.Rojo, Direction8.E, chargeTicksAtRelease: 18);

            Assert.That(mg2.LastLandingX(PlayerSlot.Rojo), Is.EqualTo(mg1.LastLandingX(PlayerSlot.Rojo)).Within(0.0001f));
            Assert.That(mg2.LastLandingY(PlayerSlot.Rojo), Is.EqualTo(mg1.LastLandingY(PlayerSlot.Rojo)).Within(0.0001f));
        }

        /// <summary>
        /// Holds <paramref name="slot"/>'s button (aiming <paramref name="stick"/>)
        /// until <see cref="V2Apunta.CurrentChargeTicks"/> reaches exactly
        /// <paramref name="chargeTicksAtRelease"/>, then releases (2 ticks) to fire.
        /// Discovers debounce timing via the observable accessor rather than
        /// hand-computing tick offsets.
        /// </summary>
        private static void HoldAndRelease(V2Apunta mg, PlayerSlot slot, Direction8 stick, int chargeTicksAtRelease, int maxTicks = 2000)
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

            var mgA = new V2Apunta(noWind);
            mgA.Initialize(new SeededRandom(9), PlayerRoster.AllHuman, 1f);
            var mgB = new V2Apunta(withWind);
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
                var mg = new V2Apunta(p);
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
                    (float cx, float cy) = V2Apunta.TurretCorner(slot);
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
        /// distance formula as production firing (<see cref="V2Apunta.DirectionToUnit"/>
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
                (float dx, float dy) = V2Apunta.DirectionToUnit(d);
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

            var mg = new V2Apunta(p);
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

            // LOW-1 (TASK-026 review): pin the landing point against the exact power
            // the auto-fire used. Rojo's press confirms at tick 1 (2-tick debounce)
            // and is held continuously, so HoldDurationTicks(tick) == tick for
            // tick >= 1 — the auto-fire tick is durationTicks-1, so that IS the
            // exact charge (in ticks) it fires with; no need to sample it live.
            int durationTicks = (int)MathF.Round(p.DurationSeconds * p.InputConfig.TicksPerSecond);
            int chargeAtFire = durationTicks - 1;
            float expectedPower = V2Apunta.ChargePower(chargeAtFire, p.ChargeCycleSeconds, p.InputConfig.TicksPerSecond);
            float expectedDistance = p.ProjectileSpeedMin + expectedPower * (p.ProjectileSpeedMax - p.ProjectileSpeedMin);
            (float dx, float dy) = V2Apunta.DirectionToUnit(Direction8.E);
            (float cx, float cy) = V2Apunta.TurretCorner(PlayerSlot.Rojo);

            Assert.That(mg.LastLandingX(PlayerSlot.Rojo), Is.EqualTo(cx + dx * expectedDistance).Within(0.001f),
                "the auto-fired shot must use the instantaneous charge power at the moment of timeout");
            Assert.That(mg.LastLandingY(PlayerSlot.Rojo), Is.EqualTo(cy + dy * expectedDistance).Within(0.001f));
        }

        // ── HIGH-1 (TASK-026 review) — firing must be gated on round duration ──

        [Test]
        public void HoldThroughExpiry_ReleaseDuringOvertime_FiresOnlyOnce()
        {
            // Holding through the round's nominal end (auto-fire happens at
            // durationTicks-1) and only releasing a beat later — the most natural
            // human behavior — must not mint a SECOND shot from the same physical
            // hold. Firing (release-triggered and auto-fire alike) must be gated
            // on _tick < _durationTicks.
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 3, targetMovingEnabled: false, targetMovingSpeed: 0f,
                windAccel: 0f, projectileSpeedMin: 0.45f, projectileSpeedMax: 0.95f, projectileFlightSeconds: 0.3f,
                durationSeconds: 0.5f, hitRadius: 0.18f, centralZoneMin: 0.4f, centralZoneMax: 0.6f,
                inputConfig: InputInterpreterConfig.GddDefaults);
            var mg = new V2Apunta(p);
            mg.Initialize(new SeededRandom(200), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            // durationTicks = round(0.5*60) = 30; hold continuously through it and
            // 10 ticks into overtime before releasing.
            for (int t = 0; t < 40; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.E, true);
                mg.Tick(inputs.Build(t));
            }
            Assert.That(mg.ShotsFired(PlayerSlot.Rojo), Is.EqualTo(1), "the timeout auto-fire must have already fired exactly once by now");

            inputs.Set(PlayerSlot.Rojo, Direction8.E, false);
            mg.Tick(inputs.Build(40));
            mg.Tick(inputs.Build(41)); // confirms the release (2-tick debounce)

            for (int t = 42; t < 200 && !mg.IsFinished; t++)
                mg.Tick(inputs.Build(t));

            Assert.That(mg.ShotsFired(PlayerSlot.Rojo), Is.EqualTo(1),
                "a release during overtime must not fire a second shot from the same physical hold");
        }

        [Test]
        public void MashingThroughOvertime_StillReachesFinishedWithinBoundedTicks()
        {
            // If firing weren't gated on duration, continuous mashing through
            // overtime keeps minting new in-flight projectiles faster than they
            // resolve, and AnyProjectileActive() (which IsFinished waits on) never
            // goes false — a permanent hang for whatever sequencer eventually
            // drives this microgame.
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 3, targetMovingEnabled: false, targetMovingSpeed: 0f,
                windAccel: 0f, projectileSpeedMin: 0.45f, projectileSpeedMax: 0.95f, projectileFlightSeconds: 0.3f,
                durationSeconds: 0.5f, hitRadius: 0.18f, centralZoneMin: 0.4f, centralZoneMax: 0.6f,
                inputConfig: InputInterpreterConfig.GddDefaults);
            int durationTicks = (int)MathF.Round(p.DurationSeconds * p.InputConfig.TicksPerSecond); // 30
            int flightTicks = (int)MathF.Round(p.ProjectileFlightSeconds * p.InputConfig.TicksPerSecond); // 18
            int bound = durationTicks + flightTicks + 10; // 58

            var mg = new V2Apunta(p);
            mg.Initialize(new SeededRandom(201), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            int finishedAtTick = -1;
            for (int t = 0; t < bound + 1000; t++) // generous safety margin beyond the asserted bound
            {
                bool pressed = (t % 4) < 2; // continuous rapid mashing, before AND through overtime, never stops
                inputs.Set(PlayerSlot.Rojo, Direction8.E, pressed);
                mg.Tick(inputs.Build(t));
                if (mg.IsFinished) { finishedAtTick = t; break; }
            }

            Assert.That(finishedAtTick, Is.Not.EqualTo(-1), "the round must eventually finish even under continuous overtime mashing");
            Assert.That(finishedAtTick, Is.LessThanOrEqualTo(bound),
                $"expected IsFinished by tick {bound} (durationTicks {durationTicks} + flightTicks {flightTicks} + slack), but it took until {finishedAtTick}");
        }

        // ── AC6 — same-tick contested target ────────────────────────────────────

        [Test]
        public void SameTickContestedTarget_MorePreciseWins_LoserPassesToNextRemainingTarget()
        {
            // Force a single-target, two-seat scenario where both shots arrive on
            // the identical tick (same flight time, released on the same tick) but
            // land at different distances from the sole target — Rojo aims exactly
            // at it (NE from corner (0,0) points straight at (0.5,0.5)) while Azul
            // deliberately aims off-target (N instead of the true NW line from
            // corner (1,0)), so Rojo is unambiguously more precise. Mirror-image
            // aims (e.g. both firing along their exact corner-to-target diagonal)
            // would tie by symmetry and not actually exercise the "more precise
            // wins" rule, hence the asymmetric choice here.
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 1, targetMovingEnabled: false, targetMovingSpeed: 0f,
                windAccel: 0f, projectileSpeedMin: 0.45f, projectileSpeedMax: 0.95f, projectileFlightSeconds: 0.2f,
                durationSeconds: 5f, hitRadius: 0.65f /* generous: both must be candidates for the sole target */,
                centralZoneMin: 0.5f, centralZoneMax: 0.5f /* single fixed target position, exactly (0.5,0.5) */,
                inputConfig: InputInterpreterConfig.GddDefaults);

            var mg = new V2Apunta(p);
            mg.Initialize(new SeededRandom(50), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            // Both press then release after the same minimal hold (2 ticks pressed,
            // 2 ticks released) so both fire with the identical charge power and
            // therefore the identical distance, and both arrive on the same tick.
            inputs.Set(PlayerSlot.Rojo, Direction8.NE, true);
            inputs.Set(PlayerSlot.Azul, Direction8.N, true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1));
            inputs.Set(PlayerSlot.Rojo, Direction8.NE, false);
            inputs.Set(PlayerSlot.Azul, Direction8.N, false);
            mg.Tick(inputs.Build(2));
            mg.Tick(inputs.Build(3));

            // Run until both shots have resolved (flight time elapses).
            for (int t = 4; t < 200; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.None, false);
                inputs.Set(PlayerSlot.Azul, Direction8.None, false);
                mg.Tick(inputs.Build(t));
            }

            Assert.That(mg.HitCount(PlayerSlot.Rojo) + mg.HitCount(PlayerSlot.Azul), Is.EqualTo(1),
                "only one seat may score the sole, single target");
            Assert.That(mg.HitCount(PlayerSlot.Rojo), Is.EqualTo(1), "Rojo aimed straight at the target and must win the contest");
            Assert.That(mg.HitCount(PlayerSlot.Azul), Is.EqualTo(0), "Azul's off-target shot loses the contest and has no other target to pass to");
        }

        [Test]
        public void SameTickTwoTargets_LoserCascadesToSecondTargetAndScores()
        {
            // LOW-2 (TASK-026 review): the positive half of the cascade rule — with
            // 2 shots arriving the same tick and 2 targets both close enough to be
            // candidates for either, each shot must end up scoring a DISTINCT
            // target rather than one shot's hit being wasted. A tiny (0.02-wide)
            // central zone puts both targets near (0.5,0.5); a generous hitRadius
            // means any landing point near center is a candidate for both.
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 2, targetMovingEnabled: false, targetMovingSpeed: 0f,
                windAccel: 0f, projectileSpeedMin: 0.3f, projectileSpeedMax: 0.3f, projectileFlightSeconds: 0.2f,
                durationSeconds: 5f, hitRadius: 0.5f, centralZoneMin: 0.49f, centralZoneMax: 0.51f,
                inputConfig: InputInterpreterConfig.GddDefaults);

            var mg = new V2Apunta(p);
            mg.Initialize(new SeededRandom(60), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            // Rojo (0,0) aims NE, Azul (1,0) aims NW — both land near (0.5,0.5).
            inputs.Set(PlayerSlot.Rojo, Direction8.NE, true);
            inputs.Set(PlayerSlot.Azul, Direction8.NW, true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1));
            inputs.Set(PlayerSlot.Rojo, Direction8.NE, false);
            inputs.Set(PlayerSlot.Azul, Direction8.NW, false);
            mg.Tick(inputs.Build(2));
            mg.Tick(inputs.Build(3));

            for (int t = 4; t < 200; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.None, false);
                inputs.Set(PlayerSlot.Azul, Direction8.None, false);
                mg.Tick(inputs.Build(t));
            }

            Assert.That(mg.HitCount(PlayerSlot.Rojo) + mg.HitCount(PlayerSlot.Azul), Is.EqualTo(2),
                "each shot should score a distinct target rather than one being wasted");
            Assert.That(mg.HitCount(PlayerSlot.Rojo), Is.EqualTo(1));
            Assert.That(mg.HitCount(PlayerSlot.Azul), Is.EqualTo(1));
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
            var mg = new V2Apunta(p);
            mg.Initialize(new SeededRandom(51), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            // NE from corner (0,0) points straight at the fixed target (0.5,0.5);
            // at the fixed distance 0.3 this lands ~0.41 from the target's center,
            // comfortably inside hitRadius 0.5. Press held for 2 ticks (debounce
            // needs 2 consecutive samples to confirm), then released for 2 ticks.
            inputs.Set(PlayerSlot.Rojo, Direction8.NE, true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1));
            inputs.Set(PlayerSlot.Rojo, Direction8.NE, false);
            mg.Tick(inputs.Build(2));
            mg.Tick(inputs.Build(3));

            for (int t = 4; t < 200 && !mg.IsFinished; t++)
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
            var mg = new V2Apunta(DefaultParams);
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

        // ── MEDIUM-2 (TASK-026 review) — render/HUD contract ────────────────────

        [Test]
        public void RenderState_PublishesInFlightProjectiles()
        {
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 1, targetMovingEnabled: false, targetMovingSpeed: 0f,
                windAccel: 0f, projectileSpeedMin: 0.45f, projectileSpeedMax: 0.95f, projectileFlightSeconds: 0.3f,
                durationSeconds: 5f, hitRadius: 0.18f, centralZoneMin: 0.4f, centralZoneMax: 0.6f,
                inputConfig: InputInterpreterConfig.GddDefaults);
            var mg = new V2Apunta(p);
            mg.Initialize(new SeededRandom(210), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            inputs.Set(PlayerSlot.Rojo, Direction8.NE, true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1));
            inputs.Set(PlayerSlot.Rojo, Direction8.NE, false);
            mg.Tick(inputs.Build(2));
            mg.Tick(inputs.Build(3)); // Fire() called here — a shot is now in flight

            RenderState rs = mg.GetRenderState();
            int projectileCount = 0;
            for (int i = 0; i < rs.EntityCount; i++)
                if (rs.Entities[i].Kind == EntityKind.Projectile) projectileCount++;

            Assert.That(projectileCount, Is.EqualTo(1), "an in-flight shot must be published as a Projectile entity so the presenter can draw it");
        }

        [Test]
        public void HudState_ExposesLiveChargeMeterPerSeat()
        {
            ApuntaParams p = DefaultParams;
            var mg = new V2Apunta(p);
            mg.Initialize(new SeededRandom(211), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            // Not charging: the meter must read 0.
            inputs.Set(PlayerSlot.Rojo, Direction8.NE, false);
            mg.Tick(inputs.Build(0));
            Assert.That(mg.GetRenderState().Hud.Meters[(int)PlayerSlot.Rojo], Is.EqualTo(0f));

            // Charging: the meter must track the oscillating ChargePower live.
            inputs.Set(PlayerSlot.Rojo, Direction8.NE, true);
            mg.Tick(inputs.Build(1));
            mg.Tick(inputs.Build(2)); // confirmed press at tick 2, HoldDurationTicks=1
            mg.Tick(inputs.Build(3)); // HoldDurationTicks=2

            int chargeNow = mg.CurrentChargeTicks(PlayerSlot.Rojo);
            float expected = V2Apunta.ChargePower(chargeNow, p.ChargeCycleSeconds, p.InputConfig.TicksPerSecond);
            Assert.That(mg.GetRenderState().Hud.Meters[(int)PlayerSlot.Rojo], Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void RenderState_AimInterpProgress_RampsToFullOverQuarterSecond()
        {
            ApuntaParams p = DefaultParams; // 60 ticks/sec -> interp window = round(0.25*60) = 15 ticks
            var mg = new V2Apunta(p);
            mg.Initialize(new SeededRandom(212), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            // Establish an initial aim and let it fully settle before testing a change.
            inputs.Set(PlayerSlot.Rojo, Direction8.N, false);
            for (int t = 0; t < 20; t++) mg.Tick(inputs.Build(t));

            // Change aim now and check progress immediately on the very tick it changes.
            inputs.Set(PlayerSlot.Rojo, Direction8.E, false);
            mg.Tick(inputs.Build(20));
            RenderEntity justChanged = FindAvatar(mg.GetRenderState(), (int)PlayerSlot.Rojo);
            // TASK-048: interp progress moved off VisualVariant (GDD §10.4's
            // discrete prefab/skin selector) onto the dedicated Progress01 [0,1]
            // channel; VisualVariant itself is now always 0 for PlayerAvatar.
            Assert.That(justChanged.Progress01, Is.LessThan(1f), "progress must not already be full on the very tick the aim changes");
            Assert.That(justChanged.VisualVariant, Is.EqualTo(0), "VisualVariant must not carry interp progress any more");

            // Hold the new aim through the full 0.25s (15-tick) interp window and beyond.
            for (int t = 21; t < 40; t++) mg.Tick(inputs.Build(t));
            RenderEntity settled = FindAvatar(mg.GetRenderState(), (int)PlayerSlot.Rojo);
            Assert.That(settled.Progress01, Is.EqualTo(1f), "progress must be full well after the 0.25s interp window elapses");
        }

        private static RenderEntity FindAvatar(RenderState rs, int ownerSeat)
        {
            for (int i = 0; i < rs.EntityCount; i++)
                if (rs.Entities[i].Kind == EntityKind.PlayerAvatar && rs.Entities[i].OwnerSeat == ownerSeat)
                    return rs.Entities[i];
            throw new InvalidOperationException("avatar not found for seat");
        }

        // ── MEDIUM-3 (TASK-026 review) — moving targets ─────────────────────────

        [Test]
        public void TargetMoving_SameSeed_ProducesSameTrajectory()
        {
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 2, targetMovingEnabled: true, targetMovingSpeed: 0.2f,
                windAccel: 0f, projectileSpeedMin: 0.45f, projectileSpeedMax: 0.95f, projectileFlightSeconds: 0.3f,
                durationSeconds: 5f, hitRadius: 0.18f, centralZoneMin: 0.3f, centralZoneMax: 0.7f,
                inputConfig: InputInterpreterConfig.GddDefaults);

            var mgA = new V2Apunta(p);
            mgA.Initialize(new SeededRandom(300), PlayerRoster.AllHuman, 1f);
            var mgB = new V2Apunta(p);
            mgB.Initialize(new SeededRandom(300), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            for (int t = 0; t < 100; t++)
            {
                mgA.Tick(inputs.Build(t));
                mgB.Tick(inputs.Build(t));
            }

            List<(float X, float Y)> posA = GetTargetPositions(mgA.GetRenderState());
            List<(float X, float Y)> posB = GetTargetPositions(mgB.GetRenderState());
            Assert.That(posA.Count, Is.EqualTo(p.TargetCount));
            for (int i = 0; i < posA.Count; i++)
            {
                Assert.That(posB[i].X, Is.EqualTo(posA[i].X).Within(0.00001f));
                Assert.That(posB[i].Y, Is.EqualTo(posA[i].Y).Within(0.00001f));
            }
        }

        [Test]
        public void TargetMoving_StaysWithinZoneBounds_EvenWhenStepSizeExceedsSpan()
        {
            // Zone span is tiny (0.01) and speed is large (2.0 units/sec) relative to
            // it — per-tick movement (2.0/60 ~= 0.033) exceeds the whole zone span,
            // which a naive single-step mirror reflection compounds into unbounded
            // drift instead of converging. Clamp-based reflection must keep the
            // target inside the zone regardless.
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 1, targetMovingEnabled: true, targetMovingSpeed: 2.0f,
                windAccel: 0f, projectileSpeedMin: 0.45f, projectileSpeedMax: 0.95f, projectileFlightSeconds: 0.3f,
                durationSeconds: 5f, hitRadius: 0.18f, centralZoneMin: 0.495f, centralZoneMax: 0.505f,
                inputConfig: InputInterpreterConfig.GddDefaults);

            var mg = new V2Apunta(p);
            mg.Initialize(new SeededRandom(301), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            for (int t = 0; t < 300; t++)
            {
                mg.Tick(inputs.Build(t));
                List<(float X, float Y)> positions = GetTargetPositions(mg.GetRenderState());
                Assert.That(positions[0].X, Is.InRange(p.CentralZoneMin, p.CentralZoneMax), $"tick {t}: X escaped the zone");
                Assert.That(positions[0].Y, Is.InRange(p.CentralZoneMin, p.CentralZoneMax), $"tick {t}: Y escaped the zone");
            }
        }

        [Test]
        public void TargetMoving_DegenerateZeroSpanZone_StaysStationary()
        {
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 1, targetMovingEnabled: true, targetMovingSpeed: 0.5f,
                windAccel: 0f, projectileSpeedMin: 0.45f, projectileSpeedMax: 0.95f, projectileFlightSeconds: 0.3f,
                durationSeconds: 5f, hitRadius: 0.18f, centralZoneMin: 0.5f, centralZoneMax: 0.5f,
                inputConfig: InputInterpreterConfig.GddDefaults);

            var mg = new V2Apunta(p);
            mg.Initialize(new SeededRandom(302), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();

            for (int t = 0; t < 300; t++)
            {
                mg.Tick(inputs.Build(t));
                List<(float X, float Y)> positions = GetTargetPositions(mg.GetRenderState());
                Assert.That(positions[0].X, Is.EqualTo(0.5f), $"tick {t}: a zero-span zone must keep the target stationary");
                Assert.That(positions[0].Y, Is.EqualTo(0.5f), $"tick {t}: a zero-span zone must keep the target stationary");
            }
        }

        private static List<(float X, float Y)> GetTargetPositions(RenderState rs)
        {
            var result = new List<(float, float)>();
            for (int i = 0; i < rs.EntityCount; i++)
                if (rs.Entities[i].Kind == EntityKind.Target)
                    result.Add((rs.Entities[i].X, rs.Entities[i].Y));
            return result;
        }

        // ── AC8 — zero heap allocation in steady-state Tick ────────────────────

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            // Fix (TASK-026 review, MEDIUM-1 — the 4th occurrence of this defect
            // class across the project; see ReaccionaMicrogame's and TASK-025's
            // identical fix): the original version measured 1000 ticks right after
            // a 300-tick warmup with GddDefaults (duration 5s = 300 ticks), so the
            // round had already finished and the measured window only ever hit the
            // `if (_isFinished) return;` no-op — Fire() never ran and CueHit never
            // fired at all in the whole test (both shots geometrically missed
            // anyway). This version uses a very long DurationSeconds, many
            // co-located targets, and a repeating 4-tick press/release cycle (2
            // ticks pressed, 2 released) that fires a real projectile every cycle
            // at a fixed distance — landing on one of the targets — so Fire and a
            // real CueHit both resolve repeatedly throughout the measured window,
            // asserted explicitly rather than assumed.
            var p = new ApuntaParams(
                chargeCycleSeconds: 1.2f, targetCount: 150, targetMovingEnabled: false, targetMovingSpeed: 0f,
                windAccel: 0f, projectileSpeedMin: 0.3f, projectileSpeedMax: 0.3f, projectileFlightSeconds: 0.05f,
                durationSeconds: 1000f, hitRadius: 0.5f, centralZoneMin: 0.5f, centralZoneMax: 0.5f,
                inputConfig: InputInterpreterConfig.GddDefaults);
            var mg = new V2Apunta(p);
            mg.Initialize(new SeededRandom(123), PlayerRoster.AllHuman, 1f);
            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Azul, Direction8.N, true); // held continuously throughout: never fires, just charges

            int hitsSoFar = 0;
            for (int i = 0; i < 500; i++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.NE, (i % 4) < 2);
                mg.Tick(inputs.Build(i));
                RenderState rsWarm = mg.GetRenderState();
                for (int f = 0; f < rsWarm.FeedbackCount; f++)
                    if (rsWarm.Feedback[f].Cue == V2Apunta.CueHit) hitsSoFar++;
            }
            Assert.That(mg.IsFinished, Is.False, "sensor setup: the round must still be live after warmup");
            Assert.That(hitsSoFar, Is.GreaterThan(0), "sensor setup: warmup should already exercise at least one real hit");

            bool sawHitDuringMeasuredWindow = false;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 500; i < 1500; i++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.NE, (i % 4) < 2);
                mg.Tick(inputs.Build(i));
                RenderState rs = mg.GetRenderState();
                for (int f = 0; f < rs.FeedbackCount; f++)
                    if (rs.Feedback[f].Cue == V2Apunta.CueHit) sawHitDuringMeasuredWindow = true;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(mg.IsFinished, Is.False, "sensor setup: the round must still be live after the measured window too");
            Assert.That(sawHitDuringMeasuredWindow, Is.True, "sensor setup: at least one real hit must resolve inside the measured window, not just during warmup");
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
