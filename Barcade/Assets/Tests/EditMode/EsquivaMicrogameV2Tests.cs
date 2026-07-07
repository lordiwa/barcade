using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Bots;
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

        // ── TASK-064: CueEliminated feedback-cue emission (MECH_02 juice) ────────
        // Mirrors ReaccionaMicrogameTests' feedback-trace locks (per-tick scan of
        // RenderState.Feedback for a specific Cue/Seat). EsquivaMicrogame declares
        // CueEliminated but historically never emitted it -- these pin that a real
        // elimination raises EXACTLY ONE CueEliminated naming the eliminated seat,
        // on the elimination tick, and never re-fires while the seat stays latched.

        [Test]
        public void PlayerElimination_EmitsExactlyOneCueEliminated_ForThatSeat()
        {
            // Rojo forced to the arena center with a hazard forced exactly on top of
            // it; the other three seats tucked into the far corners, out of reach of
            // the forced hazard. Rojo is eliminated on tick 0; the round then runs to
            // completion. Counting only Rojo's CueEliminated events across the WHOLE
            // round pins both halves of the guarantee at once: non-vacuous (>= 1, the
            // emit exists) and not re-emitted while latched (<= 1, the already-
            // eliminated seat is skipped every subsequent tick).
            var mg = new V2Esquiva(EsquivaParams.GddDefaults);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.02f, 0.02f), (0.98f, 0.02f), (0.02f, 0.98f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            mg.ForceSpawnHazardForTest(0.5f, 0.5f, dirX: 0f, dirY: 0f); // exactly on Rojo

            var inputs = new FakeInputs();
            int rojoCues = 0;
            int firstRojoCueTick = -1;
            int t = 0;
            while (!mg.IsFinished && t < 400)
            {
                mg.Tick(inputs.Build(t));

                RenderState rs = mg.GetRenderState();
                for (int i = 0; i < rs.FeedbackCount; i++)
                {
                    if (rs.Feedback[i].Cue == V2Esquiva.CueEliminated && rs.Feedback[i].Seat == (int)PlayerSlot.Rojo)
                    {
                        rojoCues++;
                        if (firstRojoCueTick < 0) firstRojoCueTick = rs.Tick;
                    }
                }
                t++;
            }

            Assert.That(mg.IsEliminated(PlayerSlot.Rojo), Is.True, "test setup sanity: Rojo must have been eliminated");
            Assert.That(rojoCues, Is.EqualTo(1),
                "a player elimination must emit EXACTLY ONE CueEliminated for that seat -- non-vacuous (>= 1) and never re-fired while latched (<= 1)");
            Assert.That(firstRojoCueTick, Is.EqualTo(mg.GetEliminationTick(PlayerSlot.Rojo)),
                "the CueEliminated must fire on the exact elimination tick");
        }

        [Test]
        public void EliminatingOneSeat_EmitsNoCueEliminatedForUnhitSeats()
        {
            // Only Rojo is hit (forced hazard on Rojo alone); the other seats sit far
            // away. Bounded to the pre-spawn window (GddDefaults' first natural spawn
            // is ~1.67s = ~tick 100) so the RNG spawner never fires and cannot
            // eliminate any other seat -- isolating the "correct seat" claim: exactly
            // one cue for Rojo, zero for everyone else. A mutation that named the
            // wrong seat (or a global -1) would drop Rojo's count to 0 and fail here.
            var mg = new V2Esquiva(EsquivaParams.GddDefaults);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.05f, 0.05f), (0.95f, 0.05f), (0.05f, 0.95f) });
            mg.Initialize(new SeededRandom(2), PlayerRoster.AllHuman, 1f);

            mg.ForceSpawnHazardForTest(0.5f, 0.5f, dirX: 0f, dirY: 0f); // exactly on Rojo, nobody else

            var inputs = new FakeInputs();
            var cuesBySeat = new int[4];
            for (int t = 0; t < 30 && !mg.IsFinished; t++)
            {
                mg.Tick(inputs.Build(t));

                RenderState rs = mg.GetRenderState();
                for (int i = 0; i < rs.FeedbackCount; i++)
                {
                    int seat = rs.Feedback[i].Seat;
                    if (rs.Feedback[i].Cue == V2Esquiva.CueEliminated && seat >= 0 && seat < 4)
                        cuesBySeat[seat]++;
                }
            }

            Assert.That(cuesBySeat[(int)PlayerSlot.Rojo], Is.EqualTo(1), "only Rojo was hit -> exactly one CueEliminated naming Rojo");
            Assert.That(cuesBySeat[(int)PlayerSlot.Azul], Is.EqualTo(0), "Azul was never hit -> no CueEliminated");
            Assert.That(cuesBySeat[(int)PlayerSlot.Amarillo], Is.EqualTo(0), "Amarillo was never hit -> no CueEliminated");
            Assert.That(cuesBySeat[(int)PlayerSlot.Verde], Is.EqualTo(0), "Verde was never hit -> no CueEliminated");
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
            // TASK-038 (GDD T-110): the reactive solver this test drives is now
            // the canonical Barcade.Core.Bots.EsquivaBotPolicy, calibrated as
            // Bot.Optimo — no more textually-duplicated EscapeBot copy (this
            // file's own private class used to have one, byte-identical to
            // Barcade.SlowTests.Oracles.EsquivaEscapeBot's, a parity-drift risk
            // TASK-038's hand-off flagged and resolved). One policy instance per
            // seat (see IBotPolicy's own doc — Decide() is seat-scoped via
            // BotView, not a parameter). Bot.Optimo's errorRate=0/stdDev=0 means
            // it never actually draws from the injected rng, so any SeededRandom
            // instance here is inert scaffolding, not a source of behavior.
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

                    var bots = NewOptimoBotsPerSeat();
                    var botRng = new SeededRandom(seed);
                    var inputs = new FakeInputs();
                    int t = 0;
                    while (!mg.IsFinished && t < 1000)
                    {
                        RenderState rs = mg.GetRenderState();
                        foreach (PlayerSlot slot in AllSlots)
                            inputs.Set(slot, bots[(int)slot].Decide(Bot.Optimo, new BotView(rs, (int)slot), botRng).Stick);
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

                var bots = NewOptimoBotsPerSeat();
                var botRng = new SeededRandom(seed);
                var inputs = new FakeInputs();
                int t = 0;
                while (!mg.IsFinished && t < 1000)
                {
                    RenderState rs = mg.GetRenderState();
                    foreach (PlayerSlot slot in AllSlots)
                        inputs.Set(slot, bots[(int)slot].Decide(Bot.Optimo, new BotView(rs, (int)slot), botRng).Stick);
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
            // Feedback-event traces), this compares a per-tick RenderState trace:
            // EntityCount plus every entity's Kind/OwnerSeat/X/Y, for a full round
            // including a HomingSoft pattern, plus the final GetResult() Ranks (the
            // CueEliminated feedback stream itself is pinned separately by the
            // TASK-064 feedback-trace tests above). Input is a FIXED
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
        /// TASK-038 (GDD T-110): the real reactive two-layer solver (strategic
        /// wall-hysteresis + tactical one-ply lookahead) this file used to keep
        /// as a private <c>EscapeBot</c> class now lives ONE place —
        /// <see cref="EsquivaBotPolicy"/> (calibrated <see cref="Bot.Optimo"/>
        /// for these ceiling tests) — rather than a byte-identical private copy
        /// here PLUS a second copy in <c>Barcade.SlowTests.Oracles.EsquivaEscapeBot</c>
        /// (the parity-drift risk TASK-038's hand-off flagged: two independently
        /// editable copies that only stayed identical by discipline). See
        /// <see cref="EsquivaBotPolicy"/>'s own class doc for the full per-seed
        /// diagnosis history behind its exact constants (the HomingSoft seed 4 /
        /// Sides seed 2 / Cross seed 16 cases this file used to document
        /// in-place). One policy instance per seat, since <see cref="IBotPolicy.Decide"/>
        /// is seat-scoped via <see cref="BotView"/>, not a parameter (unlike the
        /// old EscapeBot, which kept a <c>bool[4]</c> and served all 4 seats from
        /// one shared instance).
        /// </summary>
        private static EsquivaBotPolicy[] NewOptimoBotsPerSeat()
        {
            var bots = new EsquivaBotPolicy[4];
            for (int i = 0; i < 4; i++) bots[i] = new EsquivaBotPolicy();
            return bots;
        }
    }
}
