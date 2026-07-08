using System;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Microgames.V2;
using Barcade.Core.Scoring;
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2CoopPhase = Barcade.Core.Microgames.V2.CoopPhaseMicrogame;
using V2Sujeta = Barcade.Core.Microgames.V2.SujetaMicrogame;
using V2Iguala = Barcade.Core.Microgames.V2.IgualaMicrogame;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-078 (T-112) — coverage for <see cref="V2CoopPhase"/> (GDD §7.1
    /// special cooperative phase): v2 IMicrogame contract shape, zero-alloc
    /// steady Tick with a mutation-proven sensor twin, determinism (AC1);
    /// genuine composition of real MECH_08/09 sub-mechanic instances, never
    /// reimplemented; uniform avatar movement stats (AC2, structural); the 3
    /// toggleable ergonomic-violation elements (cuelloBotella,
    /// interseccionForzada, sueloDinamico) as composable level data, with
    /// inter-avatar collision proven to exist ONLY when interseccionForzada is
    /// on (AC3); and the bronze/plata/oro 2/4/6 payout tiers with a P4/P2
    /// identical no-ranking payout (AC4).
    /// </summary>
    [TestFixture]
    public class CoopPhaseMicrogameV2Tests
    {
        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(PlayerSlot slot, Direction8 stick = Direction8.None, bool button = false)
                => _players[(int)slot] = new PlayerInput(stick, button);

            public void SetAllButtons(bool button)
            {
                foreach (PlayerSlot slot in AllSlots) Set(slot, Direction8.None, button);
            }

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        private static CoopLevelData MinimalLevel(
            CoopObjective[] objectives,
            bool bottleneck = false,
            bool forcedIntersection = false,
            bool dynamicFloor = false,
            int bronze = 1, int plata = 2, int oro = 3) =>
            new CoopLevelData(
                objectives: objectives,
                avatarSpeed: 0.3f, avatarRadius: 0.03f, arrivalRadius: 0.06f,
                bottleneckEnabled: bottleneck, forcedIntersectionEnabled: forcedIntersection, dynamicFloorEnabled: dynamicFloor,
                bronzeThreshold: bronze, plataThreshold: plata, oroThreshold: oro,
                durationSeconds: 60f);

        // ── AC1: v2 IMicrogame contract shape ────────────────────────────────────

        [Test]
        public void CoopPhaseMicrogame_Id_IsCoopPhase()
        {
            V2Microgame mg = new V2CoopPhase(CoopLevelData.GddDefaults);
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.CoopPhase));
        }

        // ── Composition, not reimplementation ────────────────────────────────────

        [Test]
        public void SubMechanics_AreRealInstances_OfTheDelegatedMechanicTypes()
        {
            var mg = new V2CoopPhase(CoopLevelData.GddDefaults); // Sujeta, Iguala, Sujeta, Iguala
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            Assert.That(mg.GetSubMechanic(0), Is.InstanceOf<V2Sujeta>());
            Assert.That(mg.GetSubMechanic(1), Is.InstanceOf<V2Iguala>());
            Assert.That(mg.GetSubMechanic(2), Is.InstanceOf<V2Sujeta>());
            Assert.That(mg.GetSubMechanic(3), Is.InstanceOf<V2Iguala>());
        }

        // ── AC2: uniform avatar movement (structural) ────────────────────────────

        [Test]
        public void AllAvatars_ShareIdenticalMovementSpeed_StructuralProof()
        {
            var level = MinimalLevel(new[] { CoopObjective.Sujeta(0.9f, 0.9f, SujetaParams.GddDefaults(SujetaMode.HoldTogether, 0)) });
            var mg = new V2CoopPhase(level);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, Direction8.E, false);
            inputs.Set(PlayerSlot.Azul, Direction8.E, false);
            mg.Tick(inputs.Build(0));

            (float rx, float ry) = mg.GetAvatarPosition(PlayerSlot.Rojo);
            (float ax, float ay) = mg.GetAvatarPosition(PlayerSlot.Azul);
            Assert.That(rx, Is.EqualTo(ax), "identical input from identical starting positions must yield identical displacement -- one shared speed, no per-seat stat branch");
            Assert.That(ry, Is.EqualTo(ay));
        }

        // ── AC3: ergonomic-violation elements as composable data ─────────────────

        [Test]
        public void Bottleneck_PushesAvatarOutOfTheWall_WhenEnabled()
        {
            var level = MinimalLevel(new[] { CoopObjective.Sujeta(0.99f, 0.99f, SujetaParams.GddDefaults(SujetaMode.HoldTogether, 0)) }, bottleneck: true);
            var mg = new V2CoopPhase(level);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.35f), (0.05f, 0.05f), (0.95f, 0.05f), (0.05f, 0.95f) }); // Rojo exactly at the first wall's center
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            mg.Tick(new FakeInputs().Build(0));

            (float ax, float ay) = mg.GetAvatarPosition(PlayerSlot.Rojo);
            float dist = MathF.Sqrt((ax - 0.5f) * (ax - 0.5f) + (ay - 0.35f) * (ay - 0.35f));
            Assert.That(dist, Is.GreaterThan(0f), "must be pushed clear of the wall center when the bottleneck element is enabled");
        }

        [Test]
        public void Bottleneck_Disabled_NoPushAtAll()
        {
            var level = MinimalLevel(new[] { CoopObjective.Sujeta(0.99f, 0.99f, SujetaParams.GddDefaults(SujetaMode.HoldTogether, 0)) }, bottleneck: false);
            var mg = new V2CoopPhase(level);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.35f), (0.05f, 0.05f), (0.95f, 0.05f), (0.05f, 0.95f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            mg.Tick(new FakeInputs().Build(0));

            (float ax, float ay) = mg.GetAvatarPosition(PlayerSlot.Rojo);
            Assert.That(ax, Is.EqualTo(0.5f));
            Assert.That(ay, Is.EqualTo(0.35f));
        }

        [Test]
        public void ForcedIntersection_PushesOverlappingAvatarsApart_WhenEnabled()
        {
            var level = MinimalLevel(new[] { CoopObjective.Sujeta(0.99f, 0.99f, SujetaParams.GddDefaults(SujetaMode.HoldTogether, 0)) }, forcedIntersection: true);
            var mg = new V2CoopPhase(level);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.95f), (0.95f, 0.05f) }); // Rojo & Azul exactly overlapping
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            mg.Tick(new FakeInputs().Build(0));

            (float rx, float ry) = mg.GetAvatarPosition(PlayerSlot.Rojo);
            (float ax, float ay) = mg.GetAvatarPosition(PlayerSlot.Azul);
            float dist = MathF.Sqrt((rx - ax) * (rx - ax) + (ry - ay) * (ry - ay));
            Assert.That(dist, Is.GreaterThan(0f), "overlapping avatars must be pushed apart when interseccionForzada is enabled");
        }

        [Test]
        public void ForcedIntersection_Disabled_AvatarsFreelyOverlap_NoCollisionAtAll()
        {
            // GDD AC3 / this is the "verified off elsewhere" proof at the code level:
            // with the flag off, two avatars forced into the exact same point stay
            // there -- no collision response fires, exactly as every OTHER v2
            // mechanic already behaves (none of them has inter-avatar collision at all).
            var level = MinimalLevel(new[] { CoopObjective.Sujeta(0.99f, 0.99f, SujetaParams.GddDefaults(SujetaMode.HoldTogether, 0)) }, forcedIntersection: false);
            var mg = new V2CoopPhase(level);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.95f), (0.95f, 0.05f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            mg.Tick(new FakeInputs().Build(0));

            (float rx, float ry) = mg.GetAvatarPosition(PlayerSlot.Rojo);
            (float ax, float ay) = mg.GetAvatarPosition(PlayerSlot.Azul);
            Assert.That(rx, Is.EqualTo(ax));
            Assert.That(ry, Is.EqualTo(ay));
        }

        [Test]
        public void DynamicFloor_SwapsRemainingObjectivePositions_ExactlyAtTick1800_WhenEnabled()
        {
            var neverCompletes = new SujetaParams(holdWindowSeconds: 1000f, windowsToWin: 1, mode: SujetaMode.HoldTogether, viewerSeat: 0, durationSeconds: 1000f);
            var level = MinimalLevel(
                new[] { CoopObjective.Sujeta(0.2f, 0.8f, neverCompletes), CoopObjective.Iguala(0.3f, 0.7f, IgualaParams.GddDefaults) },
                dynamicFloor: true);
            var mg = new V2CoopPhase(level);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) }); // far from objective 0 -- never arrives
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs(); // nobody moves
            for (int t = 0; t < CoopLevelData.DynamicFloorTick; t++) mg.Tick(inputs.Build(t));

            (float xBefore, float yBefore) = mg.GetObjectivePosition(0);
            Assert.That((xBefore, yBefore), Is.EqualTo((0.2f, 0.8f)), "sensor sanity: unshifted right up to the boundary tick");

            mg.Tick(inputs.Build(CoopLevelData.DynamicFloorTick));

            (float xAfter, float yAfter) = mg.GetObjectivePosition(0);
            Assert.That((xAfter, yAfter), Is.EqualTo((0.8f, 0.2f)), "X/Y must swap exactly at t=30s (tick 1800)");
        }

        [Test]
        public void DynamicFloor_Disabled_ObjectivePositionsNeverChange()
        {
            var neverCompletes = new SujetaParams(holdWindowSeconds: 1000f, windowsToWin: 1, mode: SujetaMode.HoldTogether, viewerSeat: 0, durationSeconds: 1000f);
            var level = MinimalLevel(
                new[] { CoopObjective.Sujeta(0.2f, 0.8f, neverCompletes) },
                dynamicFloor: false);
            var mg = new V2CoopPhase(level);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            for (int t = 0; t < CoopLevelData.DynamicFloorTick + 10; t++) mg.Tick(inputs.Build(t));

            Assert.That(mg.GetObjectivePosition(0), Is.EqualTo((0.2f, 0.8f)));
        }

        // ── AC1: arrival triggers real sub-mechanic delegation, scores +1/-1 ─────

        [Test]
        public void ArrivingAtObjective_EngagesAndDelegates_AndScoresPlusOneOnSuccess()
        {
            var quickWin = new SujetaParams(holdWindowSeconds: 0.05f, windowsToWin: 1, mode: SujetaMode.HoldTogether, viewerSeat: 0, durationSeconds: 5f);
            var level = MinimalLevel(new[] { CoopObjective.Sujeta(0.5f, 0.5f, quickWin) });
            var mg = new V2CoopPhase(level);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) }); // already at the objective
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.SetAllButtons(true); // hold from the first tick engagement begins

            int t = 0;
            while (!mg.IsFinished && t < 500) { mg.Tick(inputs.Build(t)); t++; }

            Assert.That(mg.IsFinished, Is.True, "sensor setup: the single-objective level must finish");
            Assert.That(mg.TeamScore, Is.EqualTo(1));

            V2Result result = mg.GetResult();
            Assert.That(result.Kind, Is.EqualTo(ResultKind.CoopSuccess));
            Assert.That(result.Ranks, Is.Empty, "P2: no internal ranking, structurally");

            var coins = new int[4];
            PayoutRules.ApplyCoop(coins, success: true, mg.GetPayoutCoins(), activeSeatsMask: 0b1111);
            Assert.That(coins[0], Is.EqualTo(coins[1]));
            Assert.That(coins[1], Is.EqualTo(coins[2]));
            Assert.That(coins[2], Is.EqualTo(coins[3]));
        }

        [Test]
        public void ObjectiveTimeout_ScoresMinusOne()
        {
            var neverHeld = new SujetaParams(holdWindowSeconds: 0.5f, windowsToWin: 1, mode: SujetaMode.HoldTogether, viewerSeat: 0, durationSeconds: 1f);
            var level = MinimalLevel(new[] { CoopObjective.Sujeta(0.5f, 0.5f, neverHeld) });
            var mg = new V2CoopPhase(level);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs(); // nobody ever holds -- guaranteed objective timeout
            int t = 0;
            while (!mg.IsFinished && t < 500) { mg.Tick(inputs.Build(t)); t++; }

            Assert.That(mg.TeamScore, Is.EqualTo(-1));
            Assert.That(mg.GetResult().Kind, Is.EqualTo(ResultKind.CoopFail));
        }

        [Test]
        public void IdleSeat_DoesNotBlockArrival_AbandonmentNeverMakesTheObjectiveUnreachable()
        {
            var quickWin = new SujetaParams(holdWindowSeconds: 0.05f, windowsToWin: 1, mode: SujetaMode.HoldTogether, viewerSeat: 0, durationSeconds: 5f);
            var level = MinimalLevel(new[] { CoopObjective.Sujeta(0.5f, 0.5f, quickWin) });
            var mg = new V2CoopPhase(level);

            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Human, SeatState.HumanIdle });
            // Verde (idle) is left far away -- if arrival required ALL 4 active
            // seats (IsActive, which treats Idle as active) rather than only
            // ENGAGED (Human/Bot) seats, this objective would never trigger.
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.05f) });
            mg.Initialize(new SeededRandom(1), roster, 1f);

            var inputs = new FakeInputs();
            inputs.SetAllButtons(true);

            int t = 0;
            while (!mg.IsFinished && t < 500) { mg.Tick(inputs.Build(t)); t++; }

            Assert.That(mg.TeamScore, Is.EqualTo(1), "an idle seat must never block the team from arriving at / completing an objective");
        }

        // ── AC4: bronze/plata/oro pay exactly 2/4/6 ──────────────────────────────

        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 2)]
        [TestCase(4, 4)]
        [TestCase(5, 4)]
        [TestCase(6, 6)]
        [TestCase(100, 6)]
        [TestCase(-5, 1)]
        public void GetPayoutCoins_MapsTeamScoreToExact_2_4_6_Tiers(int score, int expectedCoins)
        {
            var mg = new V2CoopPhase(CoopLevelData.GddDefaults); // bronze=2, plata=4, oro=6
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            mg.SetTeamScoreForTest(score);

            Assert.That(mg.GetPayoutCoins(), Is.EqualTo(expectedCoins));
        }

        // ── AC1: zero heap allocation in steady-state Tick ───────────────────────

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            // GddDefaults (bottleneck+forcedIntersection+dynamicFloor all ON,
            // 75s/4500-tick duration): avatars parked at the arena center, far
            // from every corner objective and never moving -- stays in the
            // "approaching" branch (movement + both collision resolvers) for
            // the whole measured window without ever engaging a sub-mechanic
            // or finishing.
            var mg = new V2CoopPhase(CoopLevelData.GddDefaults);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();

            for (int i = 0; i < 100; i++) mg.Tick(inputs.Build(i));
            Assert.That(mg.IsFinished, Is.False, "sensor setup: must still be live after warmup");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 100; i < 1100; i++) mg.Tick(inputs.Build(i));
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(mg.IsFinished, Is.False, "sensor setup: must still be live after the measured window");
            Assert.That(mg.IsEngagedWithObjective, Is.False, "sensor sanity: must have stayed in the (more complex) approaching branch the whole window");
            Assert.That(after - before, Is.EqualTo(0L));
        }

        /// <summary>Mutation-proof sensor check — see PersigueMicrogameV2Tests's twin test for the rationale.</summary>
        [Test]
        public void SteadyStateTick_SensorDetectsADeliberateAllocation()
        {
            var mg = new V2CoopPhase(CoopLevelData.GddDefaults);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            for (int i = 0; i < 100; i++) mg.Tick(inputs.Build(i));

            long before = GC.GetAllocatedBytesForCurrentThread();
            object[] deliberate = null;
            for (int i = 100; i < 1100; i++)
            {
                mg.Tick(inputs.Build(i));
                deliberate = new object[4];
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(deliberate, Is.Not.Null);
            Assert.That(after - before, Is.GreaterThan(0L), "the sensor itself must be capable of detecting a real allocation");
        }

        // ── Determinism lock ───────────────────────────────────────────────────────

        [Test]
        public void Determinism_SameSeedAndScript_ProducesIdenticalResult()
        {
            V2Result Run()
            {
                var quickWin = new SujetaParams(holdWindowSeconds: 0.1f, windowsToWin: 1, mode: SujetaMode.HoldTogether, viewerSeat: 0, durationSeconds: 5f);
                var level = MinimalLevel(new[]
                {
                    CoopObjective.Sujeta(0.5f, 0.5f, quickWin),
                    CoopObjective.Iguala(0.5f, 0.5f, new IgualaParams(sequenceLength: 1, reactWindow0: 0.9f, windowDecay: 0.05f, durationSeconds: 5f)),
                });

                var mg = new V2CoopPhase(level);
                mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
                mg.Initialize(new SeededRandom(7), PlayerRoster.AllHuman, 1f);

                var inputs = new FakeInputs();
                int t = 0;
                while (!mg.IsFinished && t < 1000)
                {
                    foreach (PlayerSlot slot in AllSlots) inputs.Set(slot, Direction8.None, false);
                    inputs.SetAllButtons((t % 4) < 2); // debounced hold/release cycling
                    // Also aim every seat at every zone cyclically so whichever
                    // Iguala objective color comes up, SOME seat is aiming at it.
                    inputs.Set(AllSlots[t % 4], DirForZone(t % 4), (t % 4) < 2);
                    mg.Tick(inputs.Build(t));
                    t++;
                }
                return mg.GetResult();
            }

            V2Result a = Run();
            V2Result b = Run();

            Assert.That(a.Kind, Is.EqualTo(b.Kind));
            Assert.That(a.CoopScore, Is.EqualTo(b.CoopScore));
            Assert.That(a.Ranks.Length, Is.EqualTo(b.Ranks.Length));
        }

        private static Direction8 DirForZone(int color)
        {
            switch (color)
            {
                case 0: return Direction8.N;
                case 1: return Direction8.E;
                case 2: return Direction8.S;
                default: return Direction8.W;
            }
        }
    }
}
