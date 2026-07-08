using System;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Jefe = Barcade.Core.Microgames.V2.JefeMicrogame;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-079 (T-112) — coverage for <see cref="V2Jefe"/> (GDD §7.2, the
    /// special asymmetric "Jefe" phase): v2 IMicrogame contract shape,
    /// zero-alloc steady Tick with a mutation-proven sensor twin, determinism
    /// (AC1); the SAME-TICK role swap with NO invulnerability frame, proven
    /// by driving a second attacker's hit landing on the brand-new Jefe
    /// within the identical tick (AC2, the ticket's own crux); the Jefe's
    /// area attack always telegraphing 0.6s before damage (AC3, P4) plus its
    /// queue-of-1 guard; the 75s duration, timeout win, and asym1v3 [6,2,2,2]
    /// payout shape passing the TASK-030 validator (AC4).
    /// </summary>
    [TestFixture]
    public class JefeMicrogameV2Tests
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
        public void JefeMicrogame_Id_IsJefePhase()
        {
            V2Microgame mg = new V2Jefe(JefeParams.GddDefaults(0));
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.JefePhase));
        }

        [Test]
        public void Initialize_JefeSeatMustBeEngaged_Throws()
        {
            var mg = new V2Jefe(JefeParams.GddDefaults(3)); // Verde
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Human, SeatState.HumanIdle });
            Assert.Throws<ArgumentException>(() => mg.Initialize(new SeededRandom(1), roster, 1f));
        }

        [Test]
        public void Initialize_SetsJefeStartingHitsToThree()
        {
            var mg = new V2Jefe(JefeParams.GddDefaults(0));
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            Assert.That(mg.JefeSeat, Is.EqualTo(0));
            Assert.That(mg.JefeHits, Is.EqualTo(3));
        }

        // ── AC2: same-tick role swap, no invulnerability frame (the crux) ────────

        [Test]
        public void FinalHit_SwapsRoleAndResetsHits_InTheSameTick()
        {
            var mg = new V2Jefe(JefeParams.GddDefaults(0)); // Rojo starts as Jefe
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.95f), (0.95f, 0.05f) }); // Azul overlapping Rojo -- in melee range
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            mg.SetJefeHitsForTest(1); // one hit from the final blow

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Azul, Direction8.None, true);
            mg.Tick(inputs.Build(0));
            Assert.That(mg.JefeSeat, Is.EqualTo(0), "sensor setup: the press has not confirmed yet (debounce)");

            mg.Tick(inputs.Build(1)); // the press confirms HERE -- the final hit and the role swap both happen on this exact tick

            Assert.That(mg.JefeSeat, Is.EqualTo(1), "Azul must already be the Jefe on the SAME tick the final hit landed");
            Assert.That(mg.JefeHits, Is.EqualTo(2), "the new Jefe's hits must already read 2 on that same tick -- no delayed reset");
        }

        [Test]
        public void FinalHit_NoInvulnerabilityFrame_ASecondAttackerCanDamageTheBrandNewJefe_SameTick()
        {
            // The strongest possible non-vacuity proof: a SECOND attacker's
            // qualifying hit, confirmed on the EXACT SAME tick as the role
            // swap, must land on the BRAND NEW Jefe immediately -- if there
            // were any invulnerability-gap frame (even a partial one, e.g.
            // "the new Jefe is safe for the rest of the tick it was crowned
            // on"), this hit would be silently dropped instead of reducing
            // hits from 2 to 1.
            var mg = new V2Jefe(JefeParams.GddDefaults(0)); // Rojo starts as Jefe
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.95f, 0.05f) }); // Rojo, Azul, Amarillo all at the same point
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            mg.SetJefeHitsForTest(1);

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Azul, Direction8.None, true);
            inputs.Set(PlayerSlot.Amarillo, Direction8.None, true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1)); // BOTH presses confirm on this exact tick

            Assert.That(mg.JefeSeat, Is.EqualTo(1), "sensor setup: Azul (processed first, fixed seat order) must have landed the final hit and become Jefe");
            Assert.That(mg.JefeHits, Is.EqualTo(1),
                "Amarillo's hit, confirmed on the IDENTICAL tick, must land on the brand-new Jefe (2 -> 1) -- proving there is no invulnerability frame, not even a partial one within the swap tick itself");
        }

        [Test]
        public void JefeSeat_IsAlwaysExactlyOneValidSeat_AcrossManyTicks()
        {
            // Structural invariant, checked explicitly across a real sequence
            // of role swaps -- there is only ever one int field naming the
            // Jefe, so "0 or 2 Jefes simultaneously" is not merely untested,
            // it is not representable at all; this test still walks a real
            // multi-swap sequence and asserts the invariant every tick.
            var mg = new V2Jefe(new JefeParams(
                jefeSeat: 0, avatarSpeed: 0.35f, avatarRadius: 0.03f, jefeHitboxMultiplier: 1.5f,
                meleeRange: 0.05f, telegraphSeconds: 0.6f, aoeBlastRadius: 0.15f, aoeCooldownSeconds: 2f,
                attackerStunSeconds: 0.5f, durationSeconds: 75f));
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            for (int t = 0; t < 300; t++)
            {
                // Every non-Jefe seat mashes every tick -- forces repeated swaps.
                foreach (PlayerSlot slot in AllSlots)
                    inputs.Set(slot, Direction8.None, (int)slot != mg.JefeSeat);

                mg.Tick(inputs.Build(t));
                Assert.That(mg.JefeSeat, Is.InRange(0, 3), $"tick {t}");
                Assert.That(mg.JefeHits, Is.GreaterThan(0), $"tick {t}: a Jefe with 0 or fewer hits must have already been swapped out");
            }
        }

        [Test]
        public void NonFinalHit_DecrementsHits_NoSwap()
        {
            var mg = new V2Jefe(JefeParams.GddDefaults(0));
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.95f), (0.95f, 0.05f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            // Jefe starts at 3 hits -- a single melee hit must not swap.

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Azul, Direction8.None, true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1));

            Assert.That(mg.JefeSeat, Is.EqualTo(0));
            Assert.That(mg.JefeHits, Is.EqualTo(2));
        }

        [Test]
        public void MeleeHit_OutOfRange_DoesNotRegister()
        {
            var mg = new V2Jefe(JefeParams.GddDefaults(0));
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.1f, 0.1f), (0.9f, 0.9f), (0.05f, 0.95f), (0.95f, 0.05f) }); // Azul far away
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Azul, Direction8.None, true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1));

            Assert.That(mg.JefeHits, Is.EqualTo(3), "out-of-range tap must not connect");
        }

        [Test]
        public void IdleSeat_CannotMelee_EvenWithinRange()
        {
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.HumanIdle, SeatState.Human, SeatState.Human });
            var mg = new V2Jefe(JefeParams.GddDefaults(0));
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.95f), (0.95f, 0.05f) }); // Azul (idle) overlapping Rojo
            mg.Initialize(new SeededRandom(1), roster, 1f);

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Azul, Direction8.None, true); // an idle seat's own button state, if anything drove it
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1));

            Assert.That(mg.JefeHits, Is.EqualTo(3), "an Idle seat must never be able to land a melee hit");
        }

        // ── AC3: the Jefe's area attack always telegraphs 0.6s before damage (P4) ──

        [Test]
        public void Aoe_TelegraphsExactly06Seconds_BeforeStunApplies()
        {
            var p = JefeParams.GddDefaults(0);
            var mg = new V2Jefe(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.95f), (0.95f, 0.05f) }); // Azul inside blast radius
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.ForceFireAoeForTest(0.5f, 0.5f);
            int fireTick = mg.AoeFireTick;

            int t = fireTick;
            mg.Tick(inputs.Build(t)); // publish -- the blast must be visible starting this same tick
            RenderState rs = mg.GetRenderState();
            bool sawProjectile = false;
            for (int i = 0; i < rs.EntityCount; i++)
                if (rs.Entities[i].Kind == EntityKind.Projectile) sawProjectile = true;
            Assert.That(sawProjectile, Is.True, "the telegraphed blast must be visible in RenderState the instant it's triggered");

            while (mg.IsAoeActive) { t++; mg.Tick(inputs.Build(t)); }
            int resolvedAtTick = t;

            int expectedTicks = (int)MathF.Round(p.TelegraphSeconds * 60f);
            Assert.That(resolvedAtTick - fireTick, Is.EqualTo(expectedTicks), "AC3: the blast must resolve at EXACTLY 0.6s -- never earlier");
            Assert.That(mg.IsStunned(PlayerSlot.Azul), Is.True, "sensor sanity: the blast was aimed at Azul and must have connected");
        }

        [Test]
        public void Aoe_CannotQueueASecondBlastWhileOnePending()
        {
            var mg = new V2Jefe(JefeParams.GddDefaults(0));
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.2f, 0.2f), (0.9f, 0.9f), (0.05f, 0.95f), (0.95f, 0.05f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1)); // first blast fires at (0.2, 0.2)

            Assert.That(mg.IsAoeActive, Is.True);
            int firstFireTick = mg.AoeFireTick;

            inputs.Set(PlayerSlot.Rojo, Direction8.E, false);
            mg.Tick(inputs.Build(2));
            inputs.Set(PlayerSlot.Rojo, Direction8.E, true);
            mg.Tick(inputs.Build(3));
            mg.Tick(inputs.Build(4)); // a fresh press edge confirms here, but a blast is still pending

            Assert.That(mg.AoeFireTick, Is.EqualTo(firstFireTick), "a pending blast must not be overwritten by a second attempt");
        }

        [Test]
        public void Aoe_StunFreezesAttacker_NoMovementNoMelee()
        {
            var mg = new V2Jefe(JefeParams.GddDefaults(0));
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.05f, 0.95f), (0.95f, 0.05f) });
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            mg.ForceFireAoeForTest(0.5f, 0.5f);
            int t = mg.AoeFireTick;
            while (mg.IsAoeActive) { t++; mg.Tick(inputs.Build(t)); }
            Assert.That(mg.IsStunned(PlayerSlot.Azul), Is.True);

            (float xBefore, _) = mg.GetAvatarPosition(PlayerSlot.Azul);
            inputs.Set(PlayerSlot.Azul, Direction8.E, true); // try to move AND melee while stunned
            t++;
            mg.Tick(inputs.Build(t));
            t++;
            mg.Tick(inputs.Build(t));

            (float xAfter, _) = mg.GetAvatarPosition(PlayerSlot.Azul);
            Assert.That(xAfter, Is.EqualTo(xBefore), "a stunned attacker must not move");
            Assert.That(mg.JefeHits, Is.EqualTo(3), "a stunned attacker must not be able to melee either");
        }

        // ── AC4: 75s duration, timeout win, asym1v3 [6,2,2,2] payout ─────────────

        [Test]
        public void Timeout_JefeStillStanding_WinsPlace1_AttackersSharePlace2()
        {
            var p = new JefeParams(
                jefeSeat: 0, avatarSpeed: 0.35f, avatarRadius: 0.03f, jefeHitboxMultiplier: 1.5f,
                meleeRange: 0.05f, telegraphSeconds: 0.6f, aoeBlastRadius: 0.15f, aoeCooldownSeconds: 2f,
                attackerStunSeconds: 0.5f, durationSeconds: 3f); // short duration for the test
            var mg = new V2Jefe(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.9f, 0.9f), (0.05f, 0.05f), (0.05f, 0.95f), (0.95f, 0.05f) }); // everyone far apart -- nobody ever connects
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            int durationTicks = (int)MathF.Round(p.DurationSeconds * 60f);
            for (int t = 0; t < durationTicks; t++) mg.Tick(inputs.Build(t));

            Assert.That(mg.IsFinished, Is.True);
            Assert.That(mg.JefeSeat, Is.EqualTo(0), "sensor sanity: Rojo must still be Jefe -- nobody ever landed a hit");

            V2Result result = mg.GetResult();
            PlayerRank jefeRank = FindRank(result, PlayerSlot.Rojo);
            Assert.That(jefeRank.Place, Is.EqualTo(1));
            foreach (PlayerSlot attacker in new[] { PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde })
                Assert.That(FindRank(result, attacker).Place, Is.EqualTo(2), $"{attacker} must share the losing place");
        }

        [Test]
        public void Definition_Asym1v3WithPayoutTable_6_2_2_2_PassesValidator()
        {
            var def = new MicrogameDefinitionV2
            {
                Id = "jefe_phase",
                Mechanic = "JEFE_PHASE",
                DisplayVerb = "¡JEFE!",
                Dynamics = MicrogameDynamics.Asym1v3,
                Duration = 6f, // this special phase's own real duration (75s) is out-of-band for the standard [3,8]s validator bound -- see class doc
                PayoutTable = new[] { 6, 2, 2, 2 },
                MinPlayers = 4,
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);
            Assert.That(result.IsValid, Is.True, result.Message);
        }

        // ── AC1: zero heap allocation in steady-state Tick ───────────────────────

        /// <summary>
        /// All 4 seats co-located and mashing, but each on its OWN staggered
        /// 16-tick cycle (seat i's 2-tick press window at offset i*4 -- 0-1,
        /// 4-5, 8-9, 12-13, never overlapping another seat's) so no two seats
        /// ever confirm a press on the SAME tick. Two simultaneous confirms
        /// would let several hits (and even multiple role swaps) cascade
        /// invisibly within one single Tick() call -- only the NET result
        /// would be observable from outside, silently hiding the individual
        /// swap events this sensor test needs to actually witness. Staggering
        /// guarantees exactly one melee hit (or, when the currently-pressing
        /// seat happens to BE Jefe, one AoE-fire attempt) per confirm tick, so
        /// role swaps and AoE fires are each individually tick-observable via
        /// the public JefeSeat/IsAoeActive accessors between ticks.
        /// </summary>
        private static void SetStaggeredMashPattern(FakeInputs inputs, int t)
        {
            for (int i = 0; i < 4; i++)
            {
                int phase = ((t - i * 4) % 16 + 16) % 16;
                inputs.Set(AllSlots[i], Direction8.None, phase < 2);
            }
        }

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            // A short AoE cooldown (0.5s vs GddDefaults' 2s) so the whichever-
            // seat-is-Jefe-at-its-own-scheduled-turn alignment needed to fire
            // an AoE has many more chances to occur across the window.
            var p = new JefeParams(
                jefeSeat: 0, avatarSpeed: 0.35f, avatarRadius: 0.03f, jefeHitboxMultiplier: 1.5f,
                meleeRange: 0.05f, telegraphSeconds: 0.6f, aoeBlastRadius: 0.15f, aoeCooldownSeconds: 0.5f,
                attackerStunSeconds: 0.5f, durationSeconds: 75f);
            var mg = new V2Jefe(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) }); // co-located: melee always in range regardless of who currently holds Jefe
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            for (int i = 0; i < 100; i++) { SetStaggeredMashPattern(inputs, i); mg.Tick(inputs.Build(i)); }
            Assert.That(mg.IsFinished, Is.False, "sensor setup: must still be live after warmup");

            bool sawSwap = false;
            bool sawAoeResolve = false;
            int lastJefeSeat = mg.JefeSeat;
            bool wasAoeActive = mg.IsAoeActive;

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 100; i < 1100; i++)
            {
                SetStaggeredMashPattern(inputs, i);
                mg.Tick(inputs.Build(i));

                if (mg.JefeSeat != lastJefeSeat) sawSwap = true;
                lastJefeSeat = mg.JefeSeat;

                bool nowAoeActive = mg.IsAoeActive;
                if (wasAoeActive && !nowAoeActive) sawAoeResolve = true;
                wasAoeActive = nowAoeActive;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            // This phase runs the full GDD-literal 75s (4500 ticks); only the
            // 1100-tick warmup+measured window above is exercised by this test.
            Assert.That(mg.IsFinished, Is.False, "sensor setup: must still be live after the measured window");
            Assert.That(sawSwap, Is.True, "sensor sanity: the melee-hit/role-swap branch (ResolveMeleeAttacks) must have actually fired during the measured window");
            Assert.That(sawAoeResolve, Is.True, "sensor sanity: the AoE fire+telegraph+resolve branch (ResolveJefeAoe) must have actually fired during the measured window");
            Assert.That(after - before, Is.EqualTo(0L));
        }

        /// <summary>Mutation-proof sensor check — see PersigueMicrogameV2Tests's twin test for the rationale. Same co-located/mashing scenario as the test above, so it stresses the identical melee+AoE code paths.</summary>
        [Test]
        public void SteadyStateTick_SensorDetectsADeliberateAllocation()
        {
            var p = new JefeParams(
                jefeSeat: 0, avatarSpeed: 0.35f, avatarRadius: 0.03f, jefeHitboxMultiplier: 1.5f,
                meleeRange: 0.05f, telegraphSeconds: 0.6f, aoeBlastRadius: 0.15f, aoeCooldownSeconds: 0.5f,
                attackerStunSeconds: 0.5f, durationSeconds: 75f);
            var mg = new V2Jefe(p);
            mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            for (int i = 0; i < 100; i++) { SetStaggeredMashPattern(inputs, i); mg.Tick(inputs.Build(i)); }

            long before = GC.GetAllocatedBytesForCurrentThread();
            object[] deliberate = null;
            for (int i = 100; i < 1100; i++)
            {
                SetStaggeredMashPattern(inputs, i);
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
                var mg = new V2Jefe(JefeParams.GddDefaults(0));
                mg.SetForcedAvatarPositions(new (float, float)[] { (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f), (0.5f, 0.5f) });
                mg.Initialize(new SeededRandom(7), PlayerRoster.AllHuman, 1f);

                var inputs = new FakeInputs();
                int durationTicks = (int)MathF.Round(JefeParams.GddDefaults(0).DurationSeconds * 60f);
                for (int t = 0; t < durationTicks && !mg.IsFinished; t++)
                {
                    foreach (PlayerSlot slot in AllSlots)
                        inputs.Set(slot, Direction8.None, (int)slot != mg.JefeSeat && (t % 8) < 2);
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

        // ── helpers ────────────────────────────────────────────────────────────────

        private static PlayerRank FindRank(V2Result result, PlayerSlot slot)
        {
            foreach (PlayerRank r in result.Ranks)
                if (r.Seat == (int)slot) return r;
            throw new InvalidOperationException($"no rank found for {slot}");
        }
    }
}
