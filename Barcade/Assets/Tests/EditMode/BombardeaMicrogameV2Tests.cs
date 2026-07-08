using System;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Bombardea = Barcade.Core.Microgames.V2.BombardeaMicrogame;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-040 (T-111) — coverage for <see cref="V2Bombardea"/> (GDD §4
    /// MECH_07): v2 IMicrogame contract shape, the P4 telegraph guarantee
    /// (AC4, including the hard-floored params invariant), the queue-of-1 rule
    /// (AC4), fire cadence, hit resolution and the [ASSUMED] win condition,
    /// zero-alloc steady Tick, determinism, and the TASK-030 validator/schema
    /// pass. The 1000-seed winrate-band sweep (AC5) lives in the slow suite
    /// alongside FairnessSweepTests.cs.
    /// </summary>
    [TestFixture]
    public class BombardeaMicrogameV2Tests
    {
        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(PlayerSlot slot, Direction8 stick = Direction8.None, bool button = false)
                => _players[(int)slot] = new PlayerInput(stick, button);

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        // ── AC1: v2 IMicrogame contract shape ────────────────────────────────────

        [Test]
        public void BombardeaMicrogame_Id_IsBombardea()
        {
            V2Microgame mg = new V2Bombardea(BombardeaParams.GddDefaults(0));
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.Bombardea));
        }

        [Test]
        public void Initialize_SoloSeatMustBeActive_Throws()
        {
            var mg = new V2Bombardea(BombardeaParams.GddDefaults(3));
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Human, SeatState.Empty });
            Assert.Throws<ArgumentException>(() => mg.Initialize(new SeededRandom(1), roster, 1f));
        }

        // ── P4 guarantee: hard floor on TelegraphSeconds ─────────────────────────

        [Test]
        public void Params_TelegraphBelowHalfSecond_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BombardeaParams(
                soloSeat: 0, fireCooldownSeconds: 0.9f, telegraphSeconds: 0.49f, blastRadius: 0.12f,
                soloScorePerHit: 1, trioAvatarSpeed: 0.4f, reticleSpeed: 0.6f, avatarRadius: 0.03f, durationSeconds: 6f));
        }

        // ── AC4: telegraph -> damage timeline (>= 0.5s guaranteed) ───────────────

        [Test]
        public void Telegraph_VisibleAtLeastHalfASecondBeforeDamageApplies()
        {
            var p = BombardeaParams.GddDefaults(soloSeat: 0);
            var mg = new V2Bombardea(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.05f), (0.95f, 0.05f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.ForceFireShotForTest(0.5f, 0.5f); // same spot as Azul (trio) -- guarantees a hit at resolution
            int fireTick = mg.ShotFireTick;

            // Publish once at the fire tick itself -- PublishRenderState only runs
            // inside Tick(), same as a real button-driven Fire() becoming visible
            // starting the same tick it's confirmed.
            int t = fireTick;
            mg.Tick(inputs.Build(t));
            RenderState rsRightAfterFire = mg.GetRenderState();
            int telegraphTickSeen = FindProjectileTick(rsRightAfterFire, mg);
            Assert.That(telegraphTickSeen, Is.Not.EqualTo(-1), "the shot must be visible in RenderState the instant it's fired -- not hidden");

            while (mg.IsShotActive)
            {
                t++;
                mg.Tick(inputs.Build(t));
            }
            int resolvedAtTick = t;

            float elapsedSeconds = (resolvedAtTick - fireTick) / 60f;
            Assert.That(elapsedSeconds, Is.GreaterThanOrEqualTo(0.5f), "AC4: telegraph must guarantee >= 0.5s of visible warning before damage");
            Assert.That(mg.GetHitCount(PlayerSlot.Azul), Is.EqualTo(1), "sensor sanity: the shot was aimed at Azul and must have landed");
        }

        // ── AC4: solo cannot queue more than 1 pending shot ───────────────────────

        [Test]
        public void Solo_CannotQueueASecondShotWhileOnePending()
        {
            var p = BombardeaParams.GddDefaults(soloSeat: 0);
            var mg = new V2Bombardea(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.2f, 0.2f), (0.9f, 0.9f), (0.9f, 0.85f), (0.85f, 0.9f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1)); // first press confirms -> shot fired at (0.2, 0.2)

            Assert.That(mg.IsShotActive, Is.True);
            int firstFireTick = mg.ShotFireTick;
            (float firstX, float firstY) = mg.GetShotPosition();

            // Move the reticle and try to fire again while the first shot is still pending.
            inputs.Set(PlayerSlot.Rojo, Direction8.E, false);
            mg.Tick(inputs.Build(2));
            inputs.Set(PlayerSlot.Rojo, Direction8.E, true);
            mg.Tick(inputs.Build(3));
            mg.Tick(inputs.Build(4)); // a fresh press edge confirms here, but a shot is still pending

            Assert.That(mg.IsShotActive, Is.True, "the original shot must still be the only pending one");
            Assert.That(mg.ShotFireTick, Is.EqualTo(firstFireTick), "the second press must not have overwritten the pending shot");
            (float stillX, float stillY) = mg.GetShotPosition();
            Assert.That(stillX, Is.EqualTo(firstX));
            Assert.That(stillY, Is.EqualTo(firstY));
        }

        /// <summary>
        /// rev-<review>: MEDIUM-1 fix. The test above fires its retry attempt
        /// while BOTH gates would block it under GDD defaults
        /// (fireCooldown 0.9s=54t &gt; telegraph 0.7s=42t, so the cooldown alone
        /// is still unexpired at the retry tick) -- it never independently
        /// exercises the <c>!_shotActive</c> pending guard the class doc names
        /// as the AC4 mechanism ("the guard does not rely on that" ordering).
        /// This test configures <c>fireCooldownSeconds &lt; telegraphSeconds</c>
        /// specifically to isolate it: the cooldown gate opens (tick 7, 6
        /// ticks after firing) well BEFORE the first shot resolves (tick 43,
        /// 42 ticks after firing), so a retry attempted in that window can only
        /// be blocked by the pending-shot guard -- the cooldown gate itself
        /// would allow it. A companion positive assertion (fire succeeds once
        /// the shot has actually resolved) proves the retry attempt's shape was
        /// otherwise valid, so the earlier block is attributable to the pending
        /// guard specifically, not some other unrelated rejection.
        /// </summary>
        [Test]
        public void Solo_PendingShotGuardBlocksRefire_IndependentlyOfCooldownAlreadyElapsed()
        {
            var p = new BombardeaParams(
                soloSeat: 0, fireCooldownSeconds: 0.1f, telegraphSeconds: 0.7f, blastRadius: 0.12f,
                soloScorePerHit: 1, trioAvatarSpeed: 0.4f, reticleSpeed: 0.6f, avatarRadius: 0.03f, durationSeconds: 6f);
            var mg = new V2Bombardea(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.2f, 0.2f), (0.9f, 0.9f), (0.9f, 0.85f), (0.85f, 0.9f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            int fireCooldownTicks = (int)MathF.Round(p.FireCooldownSeconds * 60f);
            int telegraphTicks = (int)MathF.Round(p.TelegraphSeconds * 60f);
            Assert.That(fireCooldownTicks, Is.LessThan(telegraphTicks), "sensor sanity: this test's whole point is a cooldown shorter than the telegraph");

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1)); // first press confirms -> shot fired

            Assert.That(mg.IsShotActive, Is.True);
            int firstFireTick = mg.ShotFireTick;

            inputs.Set(PlayerSlot.Rojo, Direction8.None, false);
            mg.Tick(inputs.Build(2));
            mg.Tick(inputs.Build(3)); // release confirmed

            // Advance well past the (short) cooldown window but still short of
            // the (longer) telegraph resolution -- the cooldown gate is open
            // here; only the pending-shot guard can still be blocking a retry.
            int retryAttemptTick = firstFireTick + fireCooldownTicks + 4;
            Assert.That(retryAttemptTick, Is.LessThan(firstFireTick + telegraphTicks), "sensor sanity: retry must land before the first shot resolves");

            int t = 4;
            while (t < retryAttemptTick - 2) { mg.Tick(inputs.Build(t)); t++; }

            inputs.Set(PlayerSlot.Rojo, Direction8.E, true);
            mg.Tick(inputs.Build(t)); t++;
            mg.Tick(inputs.Build(t)); t++; // fresh press edge confirms here, cooldown already elapsed

            Assert.That(mg.IsShotActive, Is.True, "pending guard: the original shot must still be the only pending one");
            Assert.That(mg.ShotFireTick, Is.EqualTo(firstFireTick), "pending guard: cooldown had already elapsed -- only !_shotActive can explain the block");

            // Companion positive check: once the pending shot actually
            // resolves, a fresh fire attempt must succeed.
            inputs.Set(PlayerSlot.Rojo, Direction8.None, false);
            while (mg.IsShotActive) { t++; mg.Tick(inputs.Build(t)); }

            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);
            mg.Tick(inputs.Build(t + 1)); t++;
            mg.Tick(inputs.Build(t + 1)); t++;

            Assert.That(mg.IsShotActive, Is.True, "once resolved and cooldown is (still) elapsed, a fresh fire must succeed");
            Assert.That(mg.ShotFireTick, Is.Not.EqualTo(firstFireTick), "the new shot must be a genuinely new one, not the stale first fire tick");
        }

        // ── Fire cadence (cooldown) ───────────────────────────────────────────────

        [Test]
        public void Fire_RespectsCooldown_BlocksImmediateRefireAfterResolution()
        {
            var p = BombardeaParams.GddDefaults(soloSeat: 0); // fireCooldownSeconds 0.9 > telegraphSeconds 0.7
            var mg = new V2Bombardea(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.9f, 0.9f), (0.9f, 0.85f), (0.85f, 0.9f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.ForceFireShotForTest(0.5f, 0.5f);
            int fireTick = mg.ShotFireTick;

            int t = fireTick;
            while (mg.IsShotActive) { t++; mg.Tick(inputs.Build(t)); }
            int resolvedTick = t;

            // Immediately after resolution, cooldown has NOT elapsed (0.9s = 54 ticks
            // from fireTick; resolution happens at fireTick + telegraphTicks = 42).
            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);
            mg.Tick(inputs.Build(t + 1)); t++;
            mg.Tick(inputs.Build(t + 1)); t++; // press edge would confirm here
            Assert.That(mg.IsShotActive, Is.False, "still on cooldown -- the refire attempt must be ignored");

            // Wait out the remainder of the cooldown, then try again.
            int fireCooldownTicks = (int)MathF.Round(p.FireCooldownSeconds * 60f);
            inputs.Set(PlayerSlot.Rojo, Direction8.None, false);
            while (t < fireTick + fireCooldownTicks) { t++; mg.Tick(inputs.Build(t)); }

            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);
            mg.Tick(inputs.Build(t + 1)); t++;
            mg.Tick(inputs.Build(t + 1)); t++;
            Assert.That(mg.IsShotActive, Is.True, "cooldown has elapsed -- a fresh fire must now succeed");
            Assert.That(resolvedTick, Is.LessThan(fireTick + fireCooldownTicks), "sensor sanity: telegraph resolves before cooldown elapses (0.7s < 0.9s)");
        }

        // ── Hit resolution ─────────────────────────────────────────────────────────

        [Test]
        public void Shot_HitsAnyTrioSeatWithinBlastRadius()
        {
            var p = BombardeaParams.GddDefaults(soloSeat: 0);
            var mg = new V2Bombardea(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.05f), (0.95f, 0.05f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.ForceFireShotForTest(0.5f, 0.5f);
            int t = mg.ShotFireTick;
            while (mg.IsShotActive) { t++; mg.Tick(inputs.Build(t)); }

            Assert.That(mg.GetHitCount(PlayerSlot.Azul), Is.EqualTo(1));
            Assert.That(mg.GetHitCount(PlayerSlot.Amarillo), Is.EqualTo(0));
            Assert.That(mg.SoloHitsLanded, Is.EqualTo(1));
        }

        [Test]
        public void Shot_MissesWhenNoTrioSeatIsInRange()
        {
            var p = BombardeaParams.GddDefaults(soloSeat: 0);
            var mg = new V2Bombardea(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.05f, 0.05f), (0.95f, 0.05f), (0.05f, 0.95f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.ForceFireShotForTest(0.5f, 0.5f); // nobody is near arena center
            int t = mg.ShotFireTick;
            while (mg.IsShotActive) { t++; mg.Tick(inputs.Build(t)); }

            Assert.That(mg.SoloHitsLanded, Is.EqualTo(0));
        }

        // ── [ASSUMED] win condition ────────────────────────────────────────────────

        [Test]
        public void AnyLandedHit_MakesSoloWin_AtDurationEnd()
        {
            var p = BombardeaParams.GddDefaults(soloSeat: 0);
            var mg = new V2Bombardea(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.05f), (0.95f, 0.05f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.ForceFireShotForTest(0.5f, 0.5f);
            while (!mg.IsFinished) mg.Tick(inputs.Build(0));

            V2Result result = mg.GetResult();
            Assert.That(FindRank(result, PlayerSlot.Rojo).Place, Is.EqualTo(1));
            Assert.That(FindRank(result, PlayerSlot.Azul).Place, Is.EqualTo(2));
        }

        [Test]
        public void ZeroHits_MakesTrioWin_AtDurationEnd()
        {
            var p = BombardeaParams.GddDefaults(soloSeat: 0);
            var mg = new V2Bombardea(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.05f, 0.05f), (0.95f, 0.05f), (0.05f, 0.95f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs(); // never fires, nobody ever gets hit
            while (!mg.IsFinished) mg.Tick(inputs.Build(0));

            V2Result result = mg.GetResult();
            Assert.That(FindRank(result, PlayerSlot.Rojo).Place, Is.EqualTo(2));
            Assert.That(FindRank(result, PlayerSlot.Azul).Place, Is.EqualTo(1));
        }

        // ── AC1: zero heap allocation in steady-state Tick ───────────────────────

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            // Long duration (well past the 1100-tick measured+warmup window) --
            // GddDefaults' 6s/360-tick round would finish mid-window otherwise.
            var p = new BombardeaParams(
                soloSeat: 0, fireCooldownSeconds: 0.9f, telegraphSeconds: 0.7f, blastRadius: 0.12f,
                soloScorePerHit: 1, trioAvatarSpeed: 0.4f, reticleSpeed: 0.6f, avatarRadius: 0.03f, durationSeconds: 25f);
            var mg = new V2Bombardea(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.05f, 0.05f), (0.95f, 0.05f), (0.95f, 0.95f), (0.05f, 0.95f) });
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs(); // nobody moves, nobody fires -- no shot ever pending

            for (int i = 0; i < 100; i++) mg.Tick(inputs.Build(i));
            Assert.That(mg.IsFinished, Is.False, "sensor setup: must still be live after warmup");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 100; i < 1100; i++) mg.Tick(inputs.Build(i));
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(mg.IsFinished, Is.False, "sensor setup: must still be live after the measured window");
            Assert.That(after - before, Is.EqualTo(0L));
        }

        /// <summary>Mutation-proof sensor check — see PersigueMicrogameV2Tests's twin test for the rationale.</summary>
        [Test]
        public void SteadyStateTick_SensorDetectsADeliberateAllocation()
        {
            var p = new BombardeaParams(
                soloSeat: 0, fireCooldownSeconds: 0.9f, telegraphSeconds: 0.7f, blastRadius: 0.12f,
                soloScorePerHit: 1, trioAvatarSpeed: 0.4f, reticleSpeed: 0.6f, avatarRadius: 0.03f, durationSeconds: 25f);
            var mg = new V2Bombardea(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.05f, 0.05f), (0.95f, 0.05f), (0.95f, 0.95f), (0.05f, 0.95f) });
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
                var p = BombardeaParams.GddDefaults(soloSeat: 0);
                var mg = new V2Bombardea(p);
                mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.05f), (0.95f, 0.95f) });
                mg.Initialize(new SeededRandom(7), PlayerRoster.AllHuman, 1f);

                var inputs = new FakeInputs();
                int durationTicks = (int)MathF.Round(p.DurationSeconds * 60f);
                for (int t = 0; t < durationTicks && !mg.IsFinished; t++)
                {
                    inputs.Set(PlayerSlot.Rojo, Direction8.None, (t % 200) == 0);
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
                Id = "mech_07_bombardea",
                Mechanic = "MECH_07",
                DisplayVerb = "¡BOMBARDEA!",
                Dynamics = MicrogameDynamics.Asym1v3,
                Duration = 6f,
                PayoutTable = new[] { 6, 2, 2, 2 },
                MinPlayers = 4,
                Params = new System.Collections.Generic.Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["fireCooldown"] = 0.9,
                    ["telegraphSec"] = 0.7,
                    ["blastRadius"] = 0.12,
                    ["soloScorePerHit"] = 1,
                },
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);
            Assert.That(result.IsValid, Is.True, result.Message);
        }

        // ── helpers ────────────────────────────────────────────────────────────────

        private static int FindProjectileTick(RenderState rs, V2Bombardea mg)
        {
            for (int i = 0; i < rs.EntityCount; i++)
                if (rs.Entities[i].Kind == EntityKind.Projectile) return mg.ShotFireTick;
            return -1;
        }

        private static PlayerRank FindRank(V2Result result, PlayerSlot slot)
        {
            foreach (PlayerRank r in result.Ranks)
                if (r.Seat == (int)slot) return r;
            throw new InvalidOperationException($"no rank found for {slot}");
        }
    }
}
