using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
// Barcade.Core.Tests is lexically nested inside Barcade.Core, so an unqualified
// name resolves against Barcade.Core's own members before "using"-imported ones —
// see the identical note in ReaccionaMicrogameTests.cs/ApuntaMicrogameV2Tests.cs.
// Renamed aliases sidestep the MicrogameResult/IMicrogame/InputSnapshot/
// EsquivaMicrogame collisions (the v1 Barcade.Core.EsquivaMicrogame from TASK-009
// shares this v2 mechanic's simple name).
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Esquiva = Barcade.Core.Microgames.V2.EsquivaMicrogame;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// GDD T-107 slice 1 — coverage for <see cref="V2Esquiva"/> (§4 MECH_02):
    /// v2 IMicrogame contract shape, forced-hazard collision boundary (migrated
    /// from the v1 EsquivaMicrogameTests' exact-radii cases), no-hazards timeout
    /// survival, no-overlap-at-spawn, escapability (a real reactive bot swept
    /// across seeds and all 4 patterns), ranking edge cases, difficulty scaling,
    /// and the TASK-030 validator/schema pass.
    ///
    /// The v1 Barcade.Core.EsquivaMicrogame and its own EsquivaMicrogameTests are
    /// untouched — conserved, not modified. Several tests here explicitly note
    /// which v1 scenario they migrate onto the v2 Ranked/elimination-tick shape.
    ///
    /// No Unity scene required — pure C#, runs in the dotnet fast-test runner.
    /// </summary>
    [TestFixture]
    public class EsquivaMicrogameV2Tests
    {
        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        // ── Test double: builds a v2 InputSnapshot from per-seat stick state ─────

        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(PlayerSlot slot, Direction8 stick = Direction8.None, bool button = false)
                => _players[(int)slot] = new PlayerInput(stick, button);

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        private static V2Snapshot NoInput(int tick)
        {
            var inputs = new FakeInputs();
            return inputs.Build(tick);
        }

        // ── AC1: v2 IMicrogame contract shape ────────────────────────────────────

        [Test]
        public void EsquivaMicrogame_Id_IsEsquiva()
        {
            V2Microgame mg = new V2Esquiva();
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.Esquiva));
        }

        [Test]
        public void EsquivaMicrogame_GetResult_BeforeFinished_Throws()
        {
            var mg = new V2Esquiva();
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            Assert.Throws<InvalidOperationException>(() => mg.GetResult());
        }

        // ── Migrated from v1 EsquivaMicrogameTests: forced-hazard collision boundary ──

        [Test]
        public void ForcedHazard_DistanceExactlyCombinedRadii_Eliminates()
        {
            // Migrates EsquivaMicrogame_DistanceExactlyCombinedRadii_IsCollision (v1):
            // combinedRadius = avatarRadius(0.03) + hazardRadius(0.03) = 0.06.
            // Hazard forced exactly 0.06 away from Rojo's forced start, both stationary.
            // Compute combinedRadius the same way production does (avatarRadius +
            // hazardRadius) so the float arithmetic matches exactly -- comparing
            // two independently-rounded literals at an exact float boundary is a
            // classic precision trap (v1's own boundary tests used clean integer
            // values for exactly this reason).
            float combined = EsquivaParams.GddDefaults.AvatarRadius + EsquivaParams.GddDefaults.HazardRadius;

            // Avatar X = combined, hazard X = 0 -- dx = combined - 0 = combined bit-
            // exact (no lossy add-then-subtract round trip through an unrelated
            // base coordinate like 0.5).
            var mg = new V2Esquiva(EsquivaParams.GddDefaults);
            mg.SetForcedAvatarPositions(new (float, float)[] { (combined, 0.5f), (0.1f, 0.1f), (0.9f, 0.1f), (0.1f, 0.9f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            bool spawned = mg.ForceSpawnHazardForTest(0f, 0.5f, dirX: 0f, dirY: 0f);
            Assert.That(spawned, Is.True, "test setup sanity");

            mg.Tick(NoInput(0));

            Assert.That(mg.IsEliminated(PlayerSlot.Rojo), Is.True,
                "distance exactly equal to combined radii must be a collision (boundary: <=)");
        }

        [Test]
        public void ForcedHazard_DistanceSlightlyAboveCombinedRadii_IsSafe()
        {
            // Migrates EsquivaMicrogame_DistanceSlightlyAboveCombinedRadii_IsSafe (v1).
            float combined = EsquivaParams.GddDefaults.AvatarRadius + EsquivaParams.GddDefaults.HazardRadius;

            var mg = new V2Esquiva(EsquivaParams.GddDefaults);
            mg.SetForcedAvatarPositions(new (float, float)[] { (combined + 0.001f, 0.5f), (0.1f, 0.1f), (0.9f, 0.1f), (0.1f, 0.9f) });
            mg.Initialize(new SeededRandom(2), PlayerRoster.AllHuman, 1f);

            mg.ForceSpawnHazardForTest(0f, 0.5f, dirX: 0f, dirY: 0f);

            mg.Tick(NoInput(0));

            Assert.That(mg.IsEliminated(PlayerSlot.Rojo), Is.False,
                "distance just above combined radii must be safe -- no collision");
        }

        [Test]
        public void ForcedHazard_CollisionLatched_StaysEliminatedAfterMoving()
        {
            // Migrates EsquivaMicrogame_CollisionLatched_LossStaysEvenIfAvatarMoves (v1).
            var mg = new V2Esquiva(EsquivaParams.GddDefaults);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.1f, 0.1f), (0.9f, 0.1f), (0.1f, 0.9f) });
            mg.Initialize(new SeededRandom(3), PlayerRoster.AllHuman, 1f);

            mg.ForceSpawnHazardForTest(0.5f, 0.5f, dirX: 0f, dirY: 0f); // exactly on Rojo -- dist = 0

            var inputs = new FakeInputs();
            mg.Tick(inputs.Build(0)); // tick 0: collision
            Assert.That(mg.IsEliminated(PlayerSlot.Rojo), Is.True, "test setup sanity");
            int eliminationTick = mg.GetEliminationTick(PlayerSlot.Rojo);

            // Rojo tries to move far away on every subsequent tick -- elimination must persist.
            for (int t = 1; t < 30; t++)
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.E);
                mg.Tick(inputs.Build(t));
            }

            Assert.That(mg.IsEliminated(PlayerSlot.Rojo), Is.True, "elimination must be latched, not cleared by moving away");
            Assert.That(mg.GetEliminationTick(PlayerSlot.Rojo), Is.EqualTo(eliminationTick),
                "elimination tick must not change after the fact");
        }

        // ── Migrated: AvatarClampedToPlayArea -> normalized [0,1]² clamp ─────────

        [Test]
        public void Avatar_ClampedToNormalizedArena()
        {
            // Migrates EsquivaMicrogame_AvatarClampedToPlayArea (v1): the arena is
            // now normalized [0,1]² instead of playAreaHalfExtent.
            var mg = new V2Esquiva(EsquivaParams.GddDefaults);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
            mg.Initialize(new SeededRandom(4), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            for (int t = 0; t < 600; t++) // 10s of simulated driving E, comfortably enough to hit the wall
            {
                inputs.Set(PlayerSlot.Rojo, Direction8.E);
                if (!mg.IsFinished) mg.Tick(inputs.Build(t));
            }

            RenderState rs = mg.GetRenderState();
            float rojoX = FindAvatarX(rs, (int)PlayerSlot.Rojo);
            Assert.That(rojoX, Is.LessThanOrEqualTo(1f), "avatar X must never exceed the normalized arena");
            Assert.That(rojoX, Is.GreaterThanOrEqualTo(0f), "avatar X must never go below the normalized arena");
        }

        // ── Difficulty scaling (GDD §9.1: "aplicado a velocidad/densidad") ──────

        [Test]
        public void DifficultyMult_ScalesHazardSpeedAndSpawnRateDirectly()
        {
            // difficultyMult is GDD's own D(r)=1+0.06(r-1)-shaped multiplier -- a
            // direct multiply, unlike v1 EsquivaMicrogame's [0,1] progress value.
            var paramsWithKnownRates = new EsquivaParams(
                spawnRateBasePerSec: 1f, spawnRampCoef: 0f, hazardSpeed: 0.2f,
                hazardPattern: EsquivaHazardPattern.Sides, avatarSpeed: 0.5f,
                avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 5f, jumpEnabled: false);

            var mg = new V2Esquiva(paramsWithKnownRates);
            mg.Initialize(new SeededRandom(5), PlayerRoster.AllHuman, difficultyMult: 2f);

            // At difficultyMult=2, effective spawn rate is 2/sec -> first spawn at 0.5s = 30 ticks.
            var inputs = new FakeInputs();
            int firstSpawnTick = -1;
            for (int t = 0; t < 40 && firstSpawnTick < 0; t++)
            {
                mg.Tick(inputs.Build(t));
                if (CountHazards(mg.GetRenderState()) > 0) firstSpawnTick = t;
            }

            Assert.That(firstSpawnTick, Is.InRange(28, 32),
                "difficultyMult=2 must double the effective spawn rate (first spawn ~30 ticks, not ~60)");
        }

        // ── No hazards ever spawn -> full-duration timeout survival ──────────────

        [Test]
        public void NoHazardsSpawnedYet_AllActiveSeatsSurviveToTimeout_ShareFirstPlace()
        {
            // Migrates EsquivaMicrogame_NoCollision_AllPlayersWin (v1) onto the v2
            // Ranked shape: a short-enough round that the spawner never fires even
            // once (GddDefaults' first spawn threshold is ~1.67s) means every
            // active seat survives to the timeout and GDD's own rule applies:
            // "supervivientes al agotar el tiempo comparten el 1er puesto."
            var shortParams = new EsquivaParams(
                spawnRateBasePerSec: 0.6f, spawnRampCoef: 0.15f, hazardSpeed: 0.15f,
                hazardPattern: EsquivaHazardPattern.Rain, avatarSpeed: 0.35f,
                avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 0.5f, jumpEnabled: false);

            var mg = new V2Esquiva(shortParams);
            mg.Initialize(new SeededRandom(6), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            int t = 0;
            while (!mg.IsFinished && t < 200) { mg.Tick(inputs.Build(t)); t++; }
            Assert.That(mg.IsFinished, Is.True, "test setup sanity");
            Assert.That(CountHazards(mg.GetRenderState()), Is.EqualTo(0), "test setup sanity: no hazard must ever have spawned");

            V2Result result = mg.GetResult();
            Assert.That(result.Kind, Is.EqualTo(ResultKind.Ranked));
            foreach (PlayerRank rank in result.Ranks)
                Assert.That(rank.Place, Is.EqualTo(1), $"seat {rank.Seat} must share Place 1 -- every seat survived to timeout");
        }

        // ── AC4: simultaneous eliminations share Place; no fabricated winners ────

        [Test]
        public void SimultaneousEliminations_SharePlace()
        {
            var mg = new V2Esquiva(EsquivaParams.GddDefaults);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.1f, 0.1f), (0.9f, 0.1f), (0.1f, 0.9f), (0.9f, 0.9f) });
            mg.Initialize(new SeededRandom(7), PlayerRoster.AllHuman, 1f);

            // Rojo and Azul each get a hazard forced right on top of them; Amarillo/Verde do not.
            mg.ForceSpawnHazardForTest(0.1f, 0.1f, 0f, 0f);
            mg.ForceSpawnHazardForTest(0.9f, 0.1f, 0f, 0f);

            var inputs = new FakeInputs();
            int t = 0;
            while (!mg.IsFinished && t < 400) { mg.Tick(inputs.Build(t)); t++; }

            V2Result result = mg.GetResult();
            var bySeat = new Dictionary<int, PlayerRank>();
            foreach (PlayerRank r in result.Ranks) bySeat[r.Seat] = r;

            Assert.That(bySeat[(int)PlayerSlot.Rojo].Place, Is.EqualTo(bySeat[(int)PlayerSlot.Azul].Place),
                "Rojo and Azul were eliminated on the exact same tick -- must share a place");
            Assert.That(bySeat[(int)PlayerSlot.Amarillo].Place, Is.EqualTo(bySeat[(int)PlayerSlot.Verde].Place),
                "Amarillo and Verde both survived to timeout -- must share a place");
            Assert.That(bySeat[(int)PlayerSlot.Amarillo].Place, Is.LessThan(bySeat[(int)PlayerSlot.Rojo].Place),
                "survivors must outrank the eliminated pair -- no fabricated winner among the eliminated seats");
        }

        [Test]
        public void MixedEliminationTicks_RankInSurvivalOrder_NoFabricatedWinner()
        {
            var mg = new V2Esquiva(EsquivaParams.GddDefaults);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.1f, 0.1f), (0.9f, 0.1f), (0.1f, 0.9f), (0.9f, 0.9f) });
            mg.Initialize(new SeededRandom(8), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();

            // Tick 0: Rojo hit immediately.
            mg.ForceSpawnHazardForTest(0.1f, 0.1f, 0f, 0f);
            mg.Tick(inputs.Build(0));
            Assert.That(mg.IsEliminated(PlayerSlot.Rojo), Is.True, "test setup sanity");

            // A few ticks later: Azul hit.
            for (int t = 1; t < 5; t++) mg.Tick(inputs.Build(t));
            mg.ForceSpawnHazardForTest(0.9f, 0.1f, 0f, 0f);
            mg.Tick(inputs.Build(5));
            Assert.That(mg.IsEliminated(PlayerSlot.Azul), Is.True, "test setup sanity");

            int t2 = 6;
            while (!mg.IsFinished && t2 < 400) { mg.Tick(inputs.Build(t2)); t2++; }

            V2Result result = mg.GetResult();
            var bySeat = new Dictionary<int, PlayerRank>();
            foreach (PlayerRank r in result.Ranks) bySeat[r.Seat] = r;

            Assert.That(bySeat[(int)PlayerSlot.Amarillo].Place, Is.EqualTo(bySeat[(int)PlayerSlot.Verde].Place),
                "the two survivors share Place 1");
            Assert.That(bySeat[(int)PlayerSlot.Azul].Place, Is.EqualTo(3),
                "the later-eliminated seat is 3rd -- the survivor pair's tie at 1st skips to 3rd, standard competition ranking");
            Assert.That(bySeat[(int)PlayerSlot.Rojo].Place, Is.EqualTo(4),
                "the earliest-eliminated seat is last -- no fabricated winner among ties");
        }

        // ── AC2: no-overlap-at-spawn (min separation 0.08) ───────────────────────

        [Test]
        public void EveryNewSpawn_RespectsMinimumSeparationFromAllAliveHazards()
        {
            // High spawn rate to force many closely-timed spawns across several
            // seeds/patterns. Tracks each pool SLOT's alive/dead transition
            // (stable identity, unlike RenderState's repacked/reordered entity
            // list) so this checks EXACTLY the guarantee TrySpawnHazard makes:
            // the JUST-spawned hazard's distance to every OTHER alive hazard at
            // the moment it spawns -- not every pairwise distance among hazards
            // that may have drifted close together long after their own,
            // individually-valid spawns (a materially different, stricter claim
            // the GDD text doesn't make).
            foreach (EsquivaHazardPattern pattern in new[]
                     {
                         EsquivaHazardPattern.Rain, EsquivaHazardPattern.Sides,
                         EsquivaHazardPattern.Cross, EsquivaHazardPattern.HomingSoft,
                     })
            {
                for (int seed = 0; seed < 10; seed++)
                {
                    var highRateParams = new EsquivaParams(
                        spawnRateBasePerSec: 8f, spawnRampCoef: 0f, hazardSpeed: 0.1f,
                        hazardPattern: pattern, avatarSpeed: 0.35f,
                        avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 8f, jumpEnabled: false);

                    var mg = new V2Esquiva(highRateParams);
                    mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                    bool[] wasAlive = new bool[mg.HazardSlotCapacity];

                    var inputs = new FakeInputs();
                    for (int t = 0; t < 480 && !mg.IsFinished; t++)
                    {
                        mg.Tick(inputs.Build(t));

                        for (int slot = 0; slot < mg.HazardSlotCapacity; slot++)
                        {
                            bool aliveNow = mg.IsHazardSlotAlive(slot);
                            if (aliveNow && !wasAlive[slot])
                                AssertSeparatedFromAllOtherAliveHazards(mg, slot, pattern, seed, t);
                            wasAlive[slot] = aliveNow;
                        }
                    }
                }
            }
        }

        // ── AC1/AC3: escapability -- the headline correctness pin ────────────────

        [Test]
        public void EscapabilityBot_SurvivesFullDuration_EveryPatternEverySeed()
        {
            // A REAL reactive solver (see EscapeBot's own class doc), not a
            // hardcoded path -- it reacts to whatever hazards the seed actually
            // produces. Proves each pattern (especially HomingSoft, whose
            // escapability is supposed to come from its 30 deg/s turn cap) is fair
            // by construction, not merely tuned for one lucky seed.
            foreach (EsquivaHazardPattern pattern in new[]
                     {
                         EsquivaHazardPattern.Rain, EsquivaHazardPattern.Sides,
                         EsquivaHazardPattern.Cross, EsquivaHazardPattern.HomingSoft,
                     })
            {
                for (int seed = 0; seed < 20; seed++)
                {
                    var mg = new V2Esquiva(new EsquivaParams(
                        spawnRateBasePerSec: 0.6f, spawnRampCoef: 0.15f, hazardSpeed: 0.15f,
                        hazardPattern: pattern, avatarSpeed: 0.35f,
                        avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 8f, jumpEnabled: false));
                    mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                    var bot = new EscapeBot();
                    var inputs = new FakeInputs();
                    int t = 0;
                    while (!mg.IsFinished && t < 1000)
                    {
                        RenderState rs = mg.GetRenderState();
                        foreach (PlayerSlot slot in AllSlots)
                            inputs.Set(slot, bot.Decide(rs, (int)slot));
                        mg.Tick(inputs.Build(t));
                        t++;
                    }

                    Assert.That(mg.IsEliminated(PlayerSlot.Rojo), Is.False,
                        $"pattern {pattern}, seed {seed}: the optimal bot must survive the full duration");
                }
            }
        }

        [Test]
        public void HomingSoft_HazardFasterThanAvatar_EscapeGuaranteedByTurnCapAlone()
        {
            // The main escapability sweep above uses AvatarSpeed(0.35) well
            // above HazardSpeed(0.15) -- under those numbers ANY hazard is
            // outrun by simply moving away, whether or not HomingSoft's turn
            // cap is even in effect (a faster evader always increases distance
            // from a slower pursuer regardless of the pursuer's steering). That
            // does not actually exercise GDD AC1's specific claim: "Homing_soft
            // limita giro a 30°/s para que siempre exista escape" -- i.e. the
            // CAP itself, not a mere speed advantage, is what should guarantee
            // escape. Tried HazardSpeed == AvatarSpeed first: still escapable
            // even with the cap removed (a same-speed direct pursuer can never
            // close a constant lead). This test instead makes HazardSpeed
            // (0.25) GREATER than AvatarSpeed (0.2), so a raw speed advantage
            // can no longer explain any escape -- only the 30°/s steering limit
            // can. Mutation-checked with these exact numbers: temporarily
            // removing the turn-rate cap (HomingTurnRateDegPerSec effectively
            // unbounded) makes this exact test fail at seed 0 ("Expected:
            // False, But was: True" on IsEliminated) -- confirmed and reverted
            // before this file's commit.
            for (int seed = 0; seed < 20; seed++)
            {
                var mg = new V2Esquiva(new EsquivaParams(
                    spawnRateBasePerSec: 0.3f, spawnRampCoef: 0.1f, hazardSpeed: 0.25f,
                    hazardPattern: EsquivaHazardPattern.HomingSoft, avatarSpeed: 0.2f,
                    avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 8f, jumpEnabled: false));
                mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                var bot = new EscapeBot();
                var inputs = new FakeInputs();
                int t = 0;
                while (!mg.IsFinished && t < 1000)
                {
                    RenderState rs = mg.GetRenderState();
                    foreach (PlayerSlot slot in AllSlots)
                        inputs.Set(slot, bot.Decide(rs, (int)slot));
                    mg.Tick(inputs.Build(t));
                    t++;
                }

                Assert.That(mg.IsEliminated(PlayerSlot.Rojo), Is.False,
                    $"seed {seed}: with HazardSpeed > AvatarSpeed, only the 30°/s turn cap can explain escape -- it must still hold");
            }
        }

        // ── AC1: zero heap allocation in steady-state Tick (rev-t058 M1) ─────────

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            // HomingSoft specifically, so BOTH SteerHoming (runs every tick any
            // HomingSoft hazard is alive) and the spawn-retry loop (TrySpawnHazard,
            // called from Tick's own while loop) execute inside the measured
            // window -- a Rain/Sides/Cross round would never touch SteerHoming at
            // all, the path most likely to hide an allocation.
            //
            // EscapeBot (see below) allocates per Decide() call (`new
            // List<(float,float)>()`), so it cannot drive input here without
            // corrupting the measurement with test-code allocations instead of
            // production-code ones. Instead, all 4 players stay stationary at the
            // arena center (0.5, 0.5) and never move (Direction8.None every tick
            // -- InputBridge.ToUnitVector maps that to (0,0), a genuine no-op
            // avatar-movement step, not a workaround). A stationary target needs
            // zero steering correction from InitialHomingDirection's
            // already-optimal initial aim, so the worst-case (closest possible)
            // spawn-to-target distance is EXACT, not approximate: the closest any
            // perimeter point can be to the center is 0.5 (an edge midpoint). At
            // hazardSpeed 0.05, covering 0.5 takes 10s -- longer than
            // HomingLifetimeTicks' 4s cap -- so every hazard despawns via its
            // lifetime cap before it can possibly reach a player (worst-case
            // remaining distance at expiry: 0.5 - 0.05*4 = 0.3, still comfortably
            // outside the 0.06 combined collision radius). That makes the round's
            // liveness (nobody ever eliminated, never all-eliminated, never
            // timed out inside the measured window) a mathematical guarantee, not
            // a probabilistic one tied to a lucky seed.
            var p = new EsquivaParams(
                spawnRateBasePerSec: 1f, spawnRampCoef: 0f, hazardSpeed: 0.05f,
                hazardPattern: EsquivaHazardPattern.HomingSoft, avatarSpeed: 0.35f,
                avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 25f, jumpEnabled: false);

            var mg = new V2Esquiva(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs(); // every seat defaults to Direction8.None -- nobody ever moves

            for (int i = 0; i < 100; i++) mg.Tick(inputs.Build(i));
            Assert.That(mg.IsFinished, Is.False, "sensor setup: the round must still be live after warmup");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 100; i < 1100; i++) mg.Tick(inputs.Build(i));
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(mg.IsFinished, Is.False, "sensor setup: the round must still be live after the measured window, or it wasn't measuring the live path");
            Assert.That(after - before, Is.EqualTo(0L));
        }

        // ── Replay determinism (rev-t058 L2; also needed by TASK-032's .bcrp replay work) ──

        [Test]
        public void Determinism_SameSeedAndInputScript_ProducesIdenticalTraceAndResult()
        {
            // Unlike ReaccionaMicrogame's own determinism lock (which compares
            // Feedback-event traces), CueEliminated is declared here but never
            // actually emitted via EmitFeedback anywhere in this mechanic, so
            // there is no feedback stream to compare. This compares a per-tick
            // RenderState trace instead: EntityCount plus every entity's
            // Kind/OwnerSeat/X/Y, for a full round including a HomingSoft
            // pattern, plus the final GetResult() Ranks. Input is a FIXED
            // per-seat cycling schedule -- a deterministic function of (seat,
            // tick) only, never read back from RenderState -- so nothing here is
            // reactive/adaptive; this isolates the SIM's own determinism
            // (SeededRandom + the fixed-tick model), not a bot's.
            var p = new EsquivaParams(
                spawnRateBasePerSec: 0.6f, spawnRampCoef: 0.15f, hazardSpeed: 0.15f,
                hazardPattern: EsquivaHazardPattern.HomingSoft, avatarSpeed: 0.35f,
                avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 8f, jumpEnabled: false);

            var runA = RunDeterminismTrace(p, seed: 17);
            var runB = RunDeterminismTrace(p, seed: 17);

            Assert.That(runB.Frames.Count, Is.EqualTo(runA.Frames.Count),
                "same seed + same input script must produce the same number of ticks/entities");
            for (int i = 0; i < runA.Frames.Count; i++)
                Assert.That(runB.Frames[i], Is.EqualTo(runA.Frames[i]),
                    $"per-tick RenderState trace diverged at frame {i} (tick {runA.Frames[i].Tick})");

            Assert.That(runB.Result.Kind, Is.EqualTo(runA.Result.Kind));
            Assert.That(runB.Result.Ranks.Length, Is.EqualTo(runA.Result.Ranks.Length));
            for (int i = 0; i < runA.Result.Ranks.Length; i++)
            {
                Assert.That(runB.Result.Ranks[i].Seat, Is.EqualTo(runA.Result.Ranks[i].Seat), $"rank[{i}] seat diverged");
                Assert.That(runB.Result.Ranks[i].Place, Is.EqualTo(runA.Result.Ranks[i].Place), $"rank[{i}] place diverged");
                Assert.That(runB.Result.Ranks[i].Metric, Is.EqualTo(runA.Result.Ranks[i].Metric), $"rank[{i}] metric diverged");
            }
        }

        // ── AC5: v2 definition + TASK-030 validator/schema pass ──────────────────

        [Test]
        public void V2Definition_ForEsquiva_PassesValidator()
        {
            var def = new MicrogameDefinitionV2
            {
                SchemaVersion = 2,
                Id = "mg_esquiva_lluvia_01",
                Mechanic = "MECH_02",
                DisplayVerb = "¡ESQUIVA!",
                Dynamics = MicrogameDynamics.Competitive,
                Duration = 5.0f,
                DifficultyScaling = new[] { "hazardSpeed", "spawnRatePerSec" },
                Params = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["spawnRatePerSec"] = 0.6,
                    ["hazardSpeed"] = 0.15,
                    ["hazardPattern"] = "Rain",
                    ["jumpEnabled"] = false,
                },
                PayoutTable = new[] { 6, 4, 2, 1 },
                MinPlayers = 2,
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);
            Assert.That(result.IsValid, Is.True,
                $"field '{result.OffendingField}' -- {result.Message}");
        }

        [Test]
        public void V2Definition_ForEsquiva_HazardSpeedOutOfRange_FailsValidator()
        {
            var def = new MicrogameDefinitionV2
            {
                SchemaVersion = 2,
                Id = "mg_esquiva_bad_01",
                Mechanic = "MECH_02",
                DisplayVerb = "¡ESQUIVA!",
                Dynamics = MicrogameDynamics.Competitive,
                Duration = 5.0f,
                DifficultyScaling = Array.Empty<string>(),
                Params = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["hazardSpeed"] = 999.0, // out of Mech02Esquiva's [0,2] range
                },
                PayoutTable = new[] { 6, 4, 2, 1 },
                MinPlayers = 2,
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);
            Assert.That(result.IsValid, Is.False,
                "an out-of-range hazardSpeed must fail validation -- proves the new MECH_02 schema entry actually gates something");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static (List<(int Tick, int EntityCount, EntityKind Kind, int OwnerSeat, float X, float Y)> Frames, V2Result Result)
            RunDeterminismTrace(EsquivaParams p, int seed)
        {
            var mg = new V2Esquiva(p);
            mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

            var frames = new List<(int, int, EntityKind, int, float, float)>();
            var inputs = new FakeInputs();
            int t = 0;
            while (!mg.IsFinished && t < 600)
            {
                foreach (PlayerSlot slot in AllSlots)
                    inputs.Set(slot, ScheduledDirection((int)slot, t));
                mg.Tick(inputs.Build(t));

                RenderState rs = mg.GetRenderState();
                for (int i = 0; i < rs.EntityCount; i++)
                    frames.Add((t, rs.EntityCount, rs.Entities[i].Kind, rs.Entities[i].OwnerSeat, rs.Entities[i].X, rs.Entities[i].Y));

                t++;
            }

            return (frames, mg.GetResult());
        }

        private static readonly Direction8[] ScheduleCycle =
        {
            Direction8.N, Direction8.NE, Direction8.E, Direction8.SE,
            Direction8.S, Direction8.SW, Direction8.W, Direction8.NW,
        };

        /// <summary>Fixed, non-reactive per-seat schedule -- a pure function of (seat, tick), never RenderState-derived.</summary>
        private static Direction8 ScheduledDirection(int seat, int tick)
            => ScheduleCycle[(tick + seat * 3) % ScheduleCycle.Length];

        private static float FindAvatarX(RenderState rs, int seat)
        {
            for (int i = 0; i < rs.EntityCount; i++)
                if (rs.Entities[i].Kind == EntityKind.PlayerAvatar && rs.Entities[i].OwnerSeat == seat)
                    return rs.Entities[i].X;
            throw new InvalidOperationException($"seat {seat} avatar not found in RenderState");
        }

        private static int CountHazards(RenderState rs)
        {
            int count = 0;
            for (int i = 0; i < rs.EntityCount; i++)
                if (rs.Entities[i].Kind == EntityKind.Hazard) count++;
            return count;
        }

        private static void AssertSeparatedFromAllOtherAliveHazards(
            V2Esquiva mg, int newSlot, EsquivaHazardPattern pattern, int seed, int tick)
        {
            (float nx, float ny) = mg.GetHazardSlotPosition(newSlot);

            for (int other = 0; other < mg.HazardSlotCapacity; other++)
            {
                if (other == newSlot || !mg.IsHazardSlotAlive(other)) continue;

                (float ox, float oy) = mg.GetHazardSlotPosition(other);
                float dx = nx - ox;
                float dy = ny - oy;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                // Small tolerance: the assertion runs after this tick's movement
                // step, one tick after TrySpawnHazard's own pre-movement check.
                Assert.That(dist, Is.GreaterThanOrEqualTo(V2Esquiva.MinSpawnSeparation - 0.01f),
                    $"pattern {pattern}, seed {seed}, tick {tick}: newly-spawned slot {newSlot} landed within " +
                    $"the {V2Esquiva.MinSpawnSeparation} minimum separation of slot {other} ({dist})");
            }
        }

        /// <summary>
        /// Real reactive solver (not a hardcoded path), two layers:
        ///
        /// <list type="number">
        /// <item><description>
        /// STRATEGIC (wall hysteresis, top priority): near a wall and no
        /// hazard immediately dangerous, head for the arena center. Prevents
        /// getting trapped hugging a wall/corner, which a purely tactical,
        /// short-horizon evaluation cannot see coming -- diagnosed on pattern
        /// HomingSoft, seed 4: a lone one-ply lookahead (see layer 2) correctly
        /// found that, given the hazard layout at that instant, staying put
        /// AT the Y=0 wall was the locally-safest choice (moving off the wall
        /// happened to step slightly TOWARD the nearest hazard that tick), so
        /// it never moved -- and a second hazard sliding along that same wall
        /// eventually caught up. A myopic tactical evaluation has no way to
        /// notice "this locally-safe spot is a trap" days in advance; only a
        /// standing strategic bias away from walls does. Hysteresis (enter
        /// under <see cref="EnterWallMargin"/>, exit only past the wider
        /// <see cref="ExitWallMargin"/>) prevents the mode itself from
        /// flip-flopping astride a single threshold (an earlier, single-margin
        /// version of exactly this rule oscillated on pattern Sides, seed 2).
        /// Suppressed when a hazard is within <see cref="ImmediateDangerRadius"/>
        /// -- an imminent hit always overrides the standing wall bias.
        /// </description></item>
        /// <item><description>
        /// TACTICAL (one-ply lookahead, otherwise/override): for each of the 8
        /// digital Direction8 stick values plus None (GDD §1.3: the cabinet
        /// stick is digital, not analog), evaluate the hypothetical resulting
        /// position (clamped to the arena) and pick whichever candidate
        /// maximizes the MINIMUM resulting distance to any currently-alive
        /// hazard. A summed inverse-distance repulsion vector, snapped to the
        /// nearest octant, was tried first and is wrong: whenever several
        /// threats are roughly opposite each other (several Cross hazards
        /// travelling the same diagonal, spawned from the same corner at
        /// different times, on either side of the bot), their pulls nearly
        /// cancel, burying the much smaller perpendicular escape signal in
        /// numerical noise and flipping the snapped octant between the two
        /// dominant, near-cancelling directions every tick instead of finding
        /// the way out (diagnosed on pattern Cross, seed 16). Evaluating each
        /// candidate's actual resulting distance sidesteps this: moving
        /// perpendicular to a hazard's line of travel visibly increases the
        /// minimum distance even when a naive vector sum would call it a wash.
        /// </description></item>
        /// </list>
        ///
        /// <see cref="ProbeStep"/> only needs to rank candidates consistently
        /// against each other, not match the sim's real per-tick displacement
        /// exactly -- it is a fixed evaluation step, not the actual move (the
        /// sim applies its own configured AvatarSpeed to whichever Direction8
        /// this returns). Stateful (hysteresis needs to remember the previous
        /// tick's mode per seat), unlike the tactical layer, which is a pure
        /// function of the current RenderState.
        /// </summary>
        private sealed class EscapeBot
        {
            private const float EnterWallMargin = 0.2f;
            private const float ExitWallMargin = 0.35f;
            private const float ImmediateDangerRadius = 0.12f;
            private const float ProbeStep = 0.05f;

            private static readonly Direction8[] AllChoices =
            {
                Direction8.None, Direction8.N, Direction8.NE, Direction8.E, Direction8.SE,
                Direction8.S, Direction8.SW, Direction8.W, Direction8.NW,
            };

            private readonly bool[] _inWallMode = new bool[4];

            public Direction8 Decide(RenderState rs, int mySeat)
            {
                float myX = 0f, myY = 0f;
                bool foundSelf = false;
                for (int i = 0; i < rs.EntityCount; i++)
                {
                    if (rs.Entities[i].Kind == EntityKind.PlayerAvatar && rs.Entities[i].OwnerSeat == mySeat)
                    {
                        myX = rs.Entities[i].X;
                        myY = rs.Entities[i].Y;
                        foundSelf = true;
                        break;
                    }
                }
                if (!foundSelf) return Direction8.None;

                var hazards = new List<(float x, float y)>();
                for (int i = 0; i < rs.EntityCount; i++)
                    if (rs.Entities[i].Kind == EntityKind.Hazard)
                        hazards.Add((rs.Entities[i].X, rs.Entities[i].Y));

                if (hazards.Count == 0) return Direction8.None;

                float nearestHazardDist = float.MaxValue;
                foreach ((float hx, float hy) in hazards)
                {
                    float ddx = myX - hx, ddy = myY - hy;
                    float d = MathF.Sqrt(ddx * ddx + ddy * ddy);
                    if (d < nearestHazardDist) nearestHazardDist = d;
                }

                float distToNearestWall = MathF.Min(MathF.Min(myX, 1f - myX), MathF.Min(myY, 1f - myY));
                if (_inWallMode[mySeat])
                {
                    if (distToNearestWall > ExitWallMargin) _inWallMode[mySeat] = false;
                }
                else if (distToNearestWall < EnterWallMargin)
                {
                    _inWallMode[mySeat] = true;
                }

                bool imminentDanger = nearestHazardDist < ImmediateDangerRadius;
                if (_inWallMode[mySeat] && !imminentDanger)
                    return TowardCenter(myX, myY, hazards);

                return BestLookaheadMove(myX, myY, hazards);
            }

            /// <summary>
            /// Strategic "head for center" move, itself picked via the same
            /// lookahead mechanism (maximize resulting distance to the arena
            /// center) rather than a raw vector snap -- keeps a single decision
            /// mechanism instead of two independently-tunable ones.
            /// </summary>
            private static Direction8 TowardCenter(float myX, float myY, List<(float x, float y)> hazards)
            {
                Direction8 best = Direction8.None;
                float bestDist = float.MaxValue;
                foreach (Direction8 candidate in AllChoices)
                {
                    (float dx, float dy) = InputBridge.ToUnitVector(candidate);
                    float nx = Clamp01(myX + dx * ProbeStep);
                    float ny = Clamp01(myY + dy * ProbeStep);
                    float d = MathF.Sqrt((nx - 0.5f) * (nx - 0.5f) + (ny - 0.5f) * (ny - 0.5f));
                    if (d < bestDist) { bestDist = d; best = candidate; }
                }
                return best;
            }

            private static Direction8 BestLookaheadMove(float myX, float myY, List<(float x, float y)> hazards)
            {
                Direction8 best = Direction8.None;
                float bestMinDist = float.MinValue;

                foreach (Direction8 candidate in AllChoices)
                {
                    (float dx, float dy) = InputBridge.ToUnitVector(candidate);
                    float nx = Clamp01(myX + dx * ProbeStep);
                    float ny = Clamp01(myY + dy * ProbeStep);

                    float minDist = float.MaxValue;
                    foreach ((float hx, float hy) in hazards)
                    {
                        float ddx = nx - hx, ddy = ny - hy;
                        float d = MathF.Sqrt(ddx * ddx + ddy * ddy);
                        if (d < minDist) minDist = d;
                    }

                    if (minDist > bestMinDist)
                    {
                        bestMinDist = minDist;
                        best = candidate;
                    }
                }

                return best;
            }

            private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
