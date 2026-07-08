using System;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
// Barcade.Core.Tests is lexically nested inside Barcade.Core -- see the
// identical note in EsquivaMicrogameV2Tests.cs/ApuntaMicrogameV2Tests.cs.
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Persigue = Barcade.Core.Microgames.V2.PersigueMicrogame;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-040 (T-111) — coverage for <see cref="V2Persigue"/> (GDD §4 MECH_06):
    /// v2 IMicrogame contract shape, mobility-only balance (AC6, structural),
    /// dash cooldown exactness (AC3), both catch modes' early/timeout finishes,
    /// cover-obstacle collision, zero-alloc steady Tick, determinism, and the
    /// TASK-030 validator/schema pass. The 1000-seed corral sweep (AC3) lives in
    /// the slow suite alongside FairnessSweepTests.cs.
    /// </summary>
    [TestFixture]
    public class PersigueMicrogameV2Tests
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

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        // ── AC1: v2 IMicrogame contract shape ────────────────────────────────────

        [Test]
        public void PersigueMicrogame_Id_IsPersigue()
        {
            V2Microgame mg = new V2Persigue(PersigueParams.GddDefaults(0, PersigueMode.SoloHunts));
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.Persigue));
        }

        [Test]
        public void Initialize_SoloSeatMustBeActive_Throws()
        {
            var mg = new V2Persigue(PersigueParams.GddDefaults(3, PersigueMode.SoloHunts)); // Verde
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Human, SeatState.Empty });
            Assert.Throws<ArgumentException>(() => mg.Initialize(new SeededRandom(1), roster, 1f));
        }

        // ── AC3: dash cooldown, exact at tick resolution ─────────────────────────

        [Test]
        public void Dash_FiresOnceThenBlocksUntilCooldownExpires_ExactAtTickResolution()
        {
            var p = PersigueParams.GddDefaults(soloSeat: 0, PersigueMode.SoloHunts);
            var mg = new V2Persigue(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.1f), (0.05f, 0.95f), (0.95f, 0.95f), (0.95f, 0.05f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();

            // Establish facing East and dash (debounce needs 2 consecutive ticks).
            inputs.Set(PlayerSlot.Rojo, Direction8.E, true);
            mg.Tick(inputs.Build(0));
            (float xBeforeDash, float yBeforeDash) = mg.GetAvatarPosition(PlayerSlot.Rojo);
            mg.Tick(inputs.Build(1)); // press confirms here -> dash applies
            (float xAfterDash, float yAfterDash) = mg.GetAvatarPosition(PlayerSlot.Rojo);

            int expectedCooldownTicks = (int)MathF.Round(p.DashCooldownSeconds * 60f);
            Assert.That(mg.GetDashCooldownRemainingTicks(PlayerSlot.Rojo), Is.EqualTo(expectedCooldownTicks),
                "cooldown must be set to the exact tick count immediately after the dash fires");

            float dashJump = xAfterDash - xBeforeDash;
            Assert.That(dashJump, Is.GreaterThan(0.05f), "a real dash displacement (well beyond one tick's normal movement) must have occurred");

            // Release, then wait out the cooldown with neutral input (no button edges at all).
            inputs.Set(PlayerSlot.Rojo, Direction8.None, false);
            mg.Tick(inputs.Build(2));
            mg.Tick(inputs.Build(3)); // release confirmed

            int t = 4;
            while (mg.GetDashCooldownRemainingTicks(PlayerSlot.Rojo) > 1)
            {
                mg.Tick(inputs.Build(t));
                t++;
            }
            Assert.That(mg.GetDashCooldownRemainingTicks(PlayerSlot.Rojo), Is.EqualTo(1));

            (float xJustBeforeReady, _) = mg.GetAvatarPosition(PlayerSlot.Rojo);

            // First of a fresh 2-tick press: cooldown decrements to 0 this tick, but
            // the debounce edge itself doesn't confirm until the NEXT tick — dash
            // must not have applied yet.
            inputs.Set(PlayerSlot.Rojo, Direction8.E, true);
            mg.Tick(inputs.Build(t)); t++;
            Assert.That(mg.GetDashCooldownRemainingTicks(PlayerSlot.Rojo), Is.EqualTo(0));
            (float xStillNotDashed, _) = mg.GetAvatarPosition(PlayerSlot.Rojo);
            Assert.That(xStillNotDashed - xJustBeforeReady, Is.LessThan(0.02f), "the debounce edge has not confirmed yet -- no second dash yet");

            // Second consecutive true tick: the press edge confirms AND cooldown is already 0 -> dash fires again.
            mg.Tick(inputs.Build(t)); t++;
            (float xAfterSecondDash, _) = mg.GetAvatarPosition(PlayerSlot.Rojo);
            Assert.That(xAfterSecondDash - xStillNotDashed, Is.GreaterThan(0.05f), "the second dash must fire exactly once the cooldown reaches 0");
            Assert.That(mg.GetDashCooldownRemainingTicks(PlayerSlot.Rojo), Is.EqualTo(expectedCooldownTicks));
        }

        // ── Catch modes: soloHunts ────────────────────────────────────────────────

        [Test]
        public void SoloHunts_OverlapsOneTrioSeat_MarksCaught_ButRoundContinues()
        {
            var p = PersigueParams.GddDefaults(soloSeat: 0, PersigueMode.SoloHunts);
            var mg = new V2Persigue(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.05f), (0.95f, 0.95f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.Tick(inputs.Build(0));

            Assert.That(mg.IsCaught(PlayerSlot.Azul), Is.True);
            Assert.That(mg.IsCaught(PlayerSlot.Amarillo), Is.False);
            Assert.That(mg.IsCaught(PlayerSlot.Verde), Is.False);
            Assert.That(mg.IsFinished, Is.False, "only 1 of 3 trio seats caught -- round must continue");
        }

        [Test]
        public void SoloHunts_AllTrioCaught_FinishesEarly_SoloWins()
        {
            var p = PersigueParams.GddDefaults(soloSeat: 0, PersigueMode.SoloHunts);
            var mg = new V2Persigue(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.Tick(inputs.Build(0));

            Assert.That(mg.IsFinished, Is.True, "all 3 trio seats caught on the same tick -- must finish early");

            V2Result result = mg.GetResult();
            PlayerRank soloRank = FindRank(result, PlayerSlot.Rojo);
            Assert.That(soloRank.Place, Is.EqualTo(1));
            foreach (PlayerSlot trio in new[] { PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde })
                Assert.That(FindRank(result, trio).Place, Is.EqualTo(2), $"{trio} must share the losing place");
        }

        [Test]
        public void SoloHunts_Timeout_TrioWinsIfNotAllCaught()
        {
            var p = PersigueParams.GddDefaults(soloSeat: 0, PersigueMode.SoloHunts);
            var mg = new V2Persigue(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.1f), (0.9f, 0.9f), (0.9f, 0.85f), (0.85f, 0.9f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs(); // nobody moves -- solo never reaches the far corner
            int durationTicks = (int)MathF.Round(p.DurationSeconds * 60f);
            for (int t = 0; t < durationTicks && !mg.IsFinished; t++) mg.Tick(inputs.Build(t));

            Assert.That(mg.IsFinished, Is.True, "duration must have elapsed");
            V2Result result = mg.GetResult();
            Assert.That(FindRank(result, PlayerSlot.Rojo).Place, Is.EqualTo(2), "solo failed to catch anyone -- trio wins");
            Assert.That(FindRank(result, PlayerSlot.Azul).Place, Is.EqualTo(1));
        }

        // ── Catch modes: soloFlees ────────────────────────────────────────────────

        [Test]
        public void SoloFlees_TrioOverlapsSolo_FinishesEarly_TrioWins()
        {
            var p = PersigueParams.GddDefaults(soloSeat: 0, PersigueMode.SoloFlees);
            var mg = new V2Persigue(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.05f), (0.95f, 0.95f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.Tick(inputs.Build(0));

            Assert.That(mg.IsCaught(PlayerSlot.Rojo), Is.True);
            Assert.That(mg.IsFinished, Is.True, "solo caught -- round must finish immediately");

            V2Result result = mg.GetResult();
            Assert.That(FindRank(result, PlayerSlot.Rojo).Place, Is.EqualTo(2), "solo was caught -- solo loses");
            Assert.That(FindRank(result, PlayerSlot.Azul).Place, Is.EqualTo(1));
        }

        [Test]
        public void SoloFlees_Timeout_SoloWinsIfNeverCaught()
        {
            var p = PersigueParams.GddDefaults(soloSeat: 0, PersigueMode.SoloFlees);
            var mg = new V2Persigue(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.1f), (0.9f, 0.9f), (0.9f, 0.85f), (0.85f, 0.9f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs(); // nobody moves -- trio never reaches the solo
            int durationTicks = (int)MathF.Round(p.DurationSeconds * 60f);
            for (int t = 0; t < durationTicks && !mg.IsFinished; t++) mg.Tick(inputs.Build(t));

            Assert.That(mg.IsFinished, Is.True);
            V2Result result = mg.GetResult();
            Assert.That(FindRank(result, PlayerSlot.Rojo).Place, Is.EqualTo(1), "solo survived the full duration uncaught -- solo wins");
            Assert.That(FindRank(result, PlayerSlot.Azul).Place, Is.EqualTo(2));
        }

        // ── Cover obstacles ────────────────────────────────────────────────────────

        [Test]
        public void ObstacleOverlap_PushesAvatarOutToTheCombinedRadius()
        {
            var p = PersigueParams.GddDefaults(soloSeat: 0, PersigueMode.SoloHunts); // Cross layout: obstacle 0 at (0.5, 0.25)
            var mg = new V2Persigue(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.25f), (0.05f, 0.05f), (0.95f, 0.05f), (0.05f, 0.95f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.Tick(inputs.Build(0));

            (float ox, float oy) = mg.GetObstaclePosition(0);
            (float ax, float ay) = mg.GetAvatarPosition(PlayerSlot.Rojo);
            float dist = MathF.Sqrt((ax - ox) * (ax - ox) + (ay - oy) * (ay - oy));
            float minDist = p.AvatarRadius + p.ObstacleRadius;

            Assert.That(dist, Is.GreaterThanOrEqualTo(minDist - 1e-4f), "avatar must be pushed clear of the obstacle, never left overlapping");
        }

        // ── AC6: mobility-only balance (structural) ──────────────────────────────

        [Test]
        public void EffectiveSpeed_OnlySoloGetsTheBonus()
        {
            var p = PersigueParams.GddDefaults(soloSeat: 0, PersigueMode.SoloHunts);
            var mg = new V2Persigue(p);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            float soloSpeed = mg.EffectiveSpeed(PlayerSlot.Rojo);
            float trioSpeed = mg.EffectiveSpeed(PlayerSlot.Azul);

            Assert.That(soloSpeed, Is.EqualTo(p.BaseAvatarSpeed * (1f + p.SoloSpeedBonus)).Within(1e-6f));
            Assert.That(trioSpeed, Is.EqualTo(p.BaseAvatarSpeed).Within(1e-6f));
            Assert.That(soloSpeed, Is.GreaterThan(trioSpeed));
        }

        [Test]
        public void EffectiveDashDistance_OnlySoloGetsTheLongerRange()
        {
            var p = PersigueParams.GddDefaults(soloSeat: 0, PersigueMode.SoloHunts);
            var mg = new V2Persigue(p);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            Assert.That(mg.EffectiveDashDistance(PlayerSlot.Rojo), Is.EqualTo(p.SoloDashDistance));
            Assert.That(mg.EffectiveDashDistance(PlayerSlot.Azul), Is.EqualTo(p.TrioDashDistance));
            Assert.That(p.SoloDashDistance, Is.GreaterThan(p.TrioDashDistance));
        }

        [Test]
        public void TrioSeatMovement_IsIdenticalRegardlessOfWhichOtherSeatIsSolo()
        {
            // AC6 (structural, not a convention): the only thing SoloSeat changes
            // about a BYSTANDER trio seat's own movement/obstacle-resolution rules
            // is nothing at all -- a trio seat runs the exact same code whether
            // seat 1 or seat 2 currently holds the solo role. Prove it by diffing
            // seat 0's (always trio in both runs) full trajectory.
            var pA = PersigueParams.GddDefaults(soloSeat: 1, PersigueMode.SoloHunts);
            var pB = PersigueParams.GddDefaults(soloSeat: 2, PersigueMode.SoloHunts);
            var mgA = new V2Persigue(pA);
            var mgB = new V2Persigue(pB);

            // Keep every seat far apart the whole run so no catch ever fires and
            // interrupts the comparison window.
            var starts = new (float, float)[] { (0.5f, 0.5f), (0.05f, 0.05f), (0.95f, 0.05f), (0.05f, 0.95f) };
            mgA.SetForcedAvatarPositions(starts);
            mgB.SetForcedAvatarPositions(starts);
            mgA.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            mgB.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            var dirs = new[] { Direction8.N, Direction8.NE, Direction8.E, Direction8.SE, Direction8.S, Direction8.SW, Direction8.W, Direction8.NW };

            for (int t = 0; t < 60; t++)
            {
                inputs.Set(PlayerSlot.Rojo, dirs[t % dirs.Length], (t % 8) == 0);
                mgA.Tick(inputs.Build(t));
                mgB.Tick(inputs.Build(t));

                (float axA, float ayA) = mgA.GetAvatarPosition(PlayerSlot.Rojo);
                (float axB, float ayB) = mgB.GetAvatarPosition(PlayerSlot.Rojo);
                Assert.That(axA, Is.EqualTo(axB).Within(0f), $"tick {t}: X diverged based only on which OTHER seat is solo");
                Assert.That(ayA, Is.EqualTo(ayB).Within(0f), $"tick {t}: Y diverged based only on which OTHER seat is solo");
            }
        }

        // ── AC1: zero heap allocation in steady-state Tick ───────────────────────

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            // Long duration, all 4 seats parked far apart and in the arena's
            // corners (away from every obstacle in every layout), neutral input
            // throughout -- no catch, no dash, no finish inside the measured
            // window is mathematically guaranteed (nobody ever moves).
            var p = new PersigueParams(
                soloSeat: 0, mode: PersigueMode.SoloFlees, arenaLayout: PersigueArenaLayout.Central,
                baseAvatarSpeed: 0.35f, soloSpeedBonus: 0.15f, dashCooldownSeconds: 1.5f,
                trioDashDistance: 0.12f, soloDashDistance: 0.20f, avatarRadius: 0.03f,
                obstacleRadius: 0.08f, durationSeconds: 25f);

            var mg = new V2Persigue(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.05f, 0.05f), (0.95f, 0.05f), (0.95f, 0.95f), (0.05f, 0.95f) });
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs(); // every seat defaults to Direction8.None, button false -- nobody moves, nobody dashes

            for (int i = 0; i < 100; i++) mg.Tick(inputs.Build(i));
            Assert.That(mg.IsFinished, Is.False, "sensor setup: must still be live after warmup");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 100; i < 1100; i++) mg.Tick(inputs.Build(i));
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(mg.IsFinished, Is.False, "sensor setup: must still be live after the measured window");
            Assert.That(after - before, Is.EqualTo(0L));
        }

        /// <summary>
        /// Mutation-proof sensor check (rev-t058-style non-vacuity): if the
        /// zero-alloc test above were silently broken (e.g. by accidentally
        /// letting a bot/list-allocating helper drive input inside the measured
        /// window), this twin test proves the sensor CAN detect an allocation --
        /// it deliberately allocates inside the window and asserts the delta is
        /// non-zero, so a future regression that breaks the sensor itself (not
        /// just the mechanic) does not silently read 0.
        /// </summary>
        [Test]
        public void SteadyStateTick_SensorDetectsADeliberateAllocation()
        {
            var p = new PersigueParams(
                soloSeat: 0, mode: PersigueMode.SoloFlees, arenaLayout: PersigueArenaLayout.Central,
                baseAvatarSpeed: 0.35f, soloSpeedBonus: 0.15f, dashCooldownSeconds: 1.5f,
                trioDashDistance: 0.12f, soloDashDistance: 0.20f, avatarRadius: 0.03f,
                obstacleRadius: 0.08f, durationSeconds: 25f);

            var mg = new V2Persigue(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.05f, 0.05f), (0.95f, 0.05f), (0.95f, 0.95f), (0.05f, 0.95f) });
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            for (int i = 0; i < 100; i++) mg.Tick(inputs.Build(i));

            long before = GC.GetAllocatedBytesForCurrentThread();
            object[] deliberate = null;
            for (int i = 100; i < 1100; i++)
            {
                mg.Tick(inputs.Build(i));
                deliberate = new object[4]; // deliberate allocation the sensor must catch
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
                var p = PersigueParams.GddDefaults(soloSeat: 0, PersigueMode.SoloHunts);
                var mg = new V2Persigue(p);
                mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.1f), (0.9f, 0.9f), (0.9f, 0.85f), (0.85f, 0.9f) });
                mg.Initialize(new SeededRandom(7), PlayerRoster.AllHuman, 1f);

                var inputs = new FakeInputs();
                var dirs = new[] { Direction8.N, Direction8.NE, Direction8.E, Direction8.SE };
                int durationTicks = (int)MathF.Round(p.DurationSeconds * 60f);
                for (int t = 0; t < durationTicks && !mg.IsFinished; t++)
                {
                    inputs.Set(PlayerSlot.Rojo, dirs[t % dirs.Length], (t % 6) == 0);
                    mg.Tick(inputs.Build(t));
                }
                return mg.GetResult();
            }

            V2Result a = Run();
            V2Result b = Run();

            Assert.That(a.Ranks.Length, Is.EqualTo(b.Ranks.Length));
            for (int i = 0; i < a.Ranks.Length; i++)
            {
                Assert.That(a.Ranks[i].Seat, Is.EqualTo(b.Ranks[i].Seat));
                Assert.That(a.Ranks[i].Place, Is.EqualTo(b.Ranks[i].Place));
                Assert.That(a.Ranks[i].Metric, Is.EqualTo(b.Ranks[i].Metric));
            }
        }

        // ── TASK-030 validator/schema pass ────────────────────────────────────────

        [Test]
        public void Definition_Asym1v3WithPayoutTable_6_2_2_2_PassesValidator()
        {
            var def = new MicrogameDefinitionV2
            {
                Id = "mech_06_persigue",
                Mechanic = "MECH_06",
                DisplayVerb = "¡PERSIGUE!",
                Dynamics = MicrogameDynamics.Asym1v3,
                Duration = 6f,
                PayoutTable = new[] { 6, 2, 2, 2 },
                MinPlayers = 4,
                Params = new System.Collections.Generic.Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["soloSpeedBonus"] = 0.15,
                    ["dashCooldown"] = 1.5,
                    ["dashDistance"] = 0.20,
                    ["arenaLayout"] = "cross",
                    ["mode"] = "soloHunts",
                },
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);
            Assert.That(result.IsValid, Is.True, result.Message);
        }

        // ── helpers ────────────────────────────────────────────────────────────────

        private static PlayerRank FindRank(V2Result result, PlayerSlot slot)
        {
            foreach (PlayerRank r in result.Ranks)
                if (r.Seat == (int)slot) return r;
            throw new InvalidOperationException($"no rank found for {slot}");
        }
    }
}
