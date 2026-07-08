using System;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
using Barcade.Core.Scoring;
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Iguala = Barcade.Core.Microgames.V2.IgualaMicrogame;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-077 (T-112) — coverage for <see cref="V2Iguala"/> (GDD §4 MECH_09,
    /// `colorRelay` coop mode): v2 IMicrogame contract shape, the no-3x
    /// consecutive generation property (AC2), the reactWindow(n) floor
    /// boundary at exactly 0.45s (AC3), wrong-owner −1-progress with correct
    /// attribution (AC4), owner-only advancement + the TASK-023 diagonal
    /// helper reuse + identical coop payout (AC5), difficultyScaling and the
    /// TASK-030 validator/schema pass (AC6), zero-alloc steady Tick with a
    /// mutation-proven sensor twin, and determinism (AC1).
    ///
    /// Every button-confirm test presses for 2 CONSECUTIVE ticks — the shared
    /// InputInterpreter's own hardware-debounce (2-tick confirm) applies here
    /// exactly as it does to every other v2 mechanic that reads a button; a
    /// single-tick press never reaches <c>ButtonPressedThisTick</c>.
    /// </summary>
    [TestFixture]
    public class IgualaMicrogameV2Tests
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
        public void IgualaMicrogame_Id_IsIguala()
        {
            V2Microgame mg = new V2Iguala(IgualaParams.GddDefaults);
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.Iguala));
        }

        // ── AC2: no-3-consecutive generation property ────────────────────────────

        [Test]
        public void GeneratedSequence_NeverRepeatsAColor3TimesConsecutively_AcrossManySeeds()
        {
            var p = new IgualaParams(sequenceLength: 12, reactWindow0: 0.9f, windowDecay: 0.05f, durationSeconds: 8f);

            for (int seed = 0; seed < 500; seed++)
            {
                var mg = new V2Iguala(p);
                mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                for (int k = 2; k < p.SequenceLength; k++)
                {
                    bool threeInARow = mg.GetSequenceColor(k) == mg.GetSequenceColor(k - 1)
                                     && mg.GetSequenceColor(k - 1) == mg.GetSequenceColor(k - 2);
                    Assert.That(threeInARow, Is.False, $"seed {seed}, index {k}: 3 consecutive identical colors");
                }
            }
        }

        [Test]
        public void GeneratedSequence_NeverRepeatsAColor3TimesConsecutively_WithOneAbandonedSeat()
        {
            // 3 engaged colors -- still enough room to satisfy the property.
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Human, SeatState.HumanIdle });
            var p = new IgualaParams(sequenceLength: 12, reactWindow0: 0.9f, windowDecay: 0.05f, durationSeconds: 8f);

            for (int seed = 0; seed < 300; seed++)
            {
                var mg = new V2Iguala(p);
                mg.Initialize(new SeededRandom(seed), roster, 1f);

                for (int k = 0; k < p.SequenceLength; k++)
                    Assert.That(mg.GetSequenceColor(k), Is.Not.EqualTo(3), $"seed {seed}, index {k}: abandoned Verde's color must never appear");

                for (int k = 2; k < p.SequenceLength; k++)
                {
                    bool threeInARow = mg.GetSequenceColor(k) == mg.GetSequenceColor(k - 1)
                                     && mg.GetSequenceColor(k - 1) == mg.GetSequenceColor(k - 2);
                    Assert.That(threeInARow, Is.False, $"seed {seed}, index {k}");
                }
            }
        }

        [Test]
        public void GeneratedSequence_SingleEngagedSeat_DoesNotCrash_DegenerateCase()
        {
            // Only 1 engaged seat: the no-3x rule is mathematically impossible
            // to satisfy (only one color exists) -- documented waiver, not a crash.
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Empty, SeatState.Empty, SeatState.Empty });
            var mg = new V2Iguala(IgualaParams.GddDefaults);
            mg.Initialize(new SeededRandom(1), roster, 1f);

            for (int k = 0; k < mg.SequenceLength; k++)
                Assert.That(mg.GetSequenceColor(k), Is.EqualTo(0), "the only engaged seat's color must be the only one drawn");
        }

        // ── AC3: reactWindow(n) floor boundary, exactly 0.45s ────────────────────

        [Test]
        public void ReactWindowSeconds_DecaysLinearly_ThenFloorsExactlyAt045()
        {
            // reactWindow0=0.9, windowDecay=0.05 -> floors at n=9 (0.9-0.45=0.45).
            Assert.That(V2Iguala.ReactWindowSeconds(0.9f, 0.05f, 0), Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(V2Iguala.ReactWindowSeconds(0.9f, 0.05f, 8), Is.EqualTo(0.5f).Within(1e-6f), "one step above the floor");
            Assert.That(V2Iguala.ReactWindowSeconds(0.9f, 0.05f, 9), Is.EqualTo(0.45f).Within(1e-6f), "the exact flooring n");
            Assert.That(V2Iguala.ReactWindowSeconds(0.9f, 0.05f, 10), Is.EqualTo(0.45f).Within(1e-6f), "past the flooring n -- still floored, not negative");
            Assert.That(V2Iguala.ReactWindowSeconds(0.9f, 0.05f, 100), Is.EqualTo(0.45f).Within(1e-6f), "far past the floor -- never goes below it");
        }

        // ── AC4: wrong-owner confirm subtracts exactly 1, correct attribution ────

        [Test]
        public void WrongOwnerConfirm_SubtractsExactlyOneProgress_AndAttributesTheCorrectErrer()
        {
            var mg = new V2Iguala(IgualaParams.GddDefaults);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            int t = 0;
            AdvanceCorrectly(mg, inputs, 2, ref t);
            Assert.That(mg.Progress, Is.EqualTo(2), "sensor setup: 2 correct confirms must have landed first");

            int expectedColor = mg.GetSequenceColor(mg.Progress);
            PlayerSlot erringSlot = AllSlots[(expectedColor + 1) % 4]; // any seat that is NOT the color's owner

            inputs.Set(erringSlot, ToStick(V2Iguala.ZoneForColor(expectedColor)), true);
            mg.Tick(inputs.Build(t)); t++;
            mg.Tick(inputs.Build(t)); t++; // confirms (foul) on this 2nd consecutive tick

            Assert.That(mg.Progress, Is.EqualTo(1), "a wrong-owner confirm must subtract exactly 1 from progress");
            Assert.That(mg.LastErringSeat, Is.EqualTo((int)erringSlot), "the erring seat must be attributed EXACTLY, not just 'someone'");
            Assert.That(mg.GetErrorCount(erringSlot), Is.EqualTo(1));

            RenderState rs = mg.GetRenderState();
            bool sawFeedback = false;
            for (int f = 0; f < rs.FeedbackCount; f++)
                if (rs.Feedback[f].Cue == V2Iguala.CueWrongOwner && rs.Feedback[f].Seat == (int)erringSlot)
                    sawFeedback = true;
            Assert.That(sawFeedback, Is.True, "the wrong-owner event must also be surfaced in the feedback event stream");
        }

        [Test]
        public void WrongOwnerConfirm_NeverDropsProgressBelowZero()
        {
            var mg = new V2Iguala(IgualaParams.GddDefaults);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            int expectedColor = mg.GetSequenceColor(0);
            PlayerSlot erringSlot = AllSlots[(expectedColor + 1) % 4];

            var inputs = new FakeInputs();
            inputs.Set(erringSlot, ToStick(V2Iguala.ZoneForColor(expectedColor)), true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1)); // confirms here

            Assert.That(mg.Progress, Is.EqualTo(0), "progress must floor at 0, never go negative");
        }

        // ── AC5: only the owner advances; non-owner confirm never advances ──────

        [Test]
        public void OnlyTheColorsOwner_AdvancesTheRelay()
        {
            var mg = new V2Iguala(IgualaParams.GddDefaults);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            int expectedColor = mg.GetSequenceColor(0);
            PlayerSlot owner = AllSlots[expectedColor];

            var inputs = new FakeInputs();
            inputs.Set(owner, ToStick(V2Iguala.ZoneForColor(expectedColor)), true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1)); // confirms here

            Assert.That(mg.Progress, Is.EqualTo(1), "the color's own owner confirming must advance the relay");
        }

        [Test]
        public void ConfirmOnAnInactiveZone_HasNoEffectAtAll_NotEvenAFoul()
        {
            var mg = new V2Iguala(IgualaParams.GddDefaults);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            int expectedColor = mg.GetSequenceColor(0);
            int inactiveColor = (expectedColor + 1) % 4; // some other, non-active zone
            PlayerSlot presser = AllSlots[inactiveColor]; // that zone's own owner, confirming the WRONG (inactive) zone

            var inputs = new FakeInputs();
            inputs.Set(presser, ToStick(V2Iguala.ZoneForColor(inactiveColor)), true);
            mg.Tick(inputs.Build(0));
            mg.Tick(inputs.Build(1)); // would confirm here, if it mattered

            Assert.That(mg.Progress, Is.EqualTo(0));
            Assert.That(mg.LastErringSeat, Is.EqualTo(-1), "aiming at an inactive zone is not a foul -- GDD's foul is specifically about confirming the SHOWN color");
            Assert.That(mg.GetErrorCount(presser), Is.EqualTo(0));
        }

        // ── TASK-023 diagonal-collapse helper reuse ──────────────────────────────

        /// <summary>
        /// rev-review LOW-3: parameterized across all 4 zones, not just
        /// whichever zone seed 1's sequence happened to start with. This
        /// matters because <c>CollapseDiagonal</c>'s COLD-START fallback
        /// (no prior dominant) already prefers the horizontal component --
        /// NE/SE both cold-start to Right, NW/SW both cold-start to Left. So
        /// for the Right/Left zones, even a COMPLETELY BROKEN hysteresis
        /// (one that ignored <c>previousDominant</c> entirely and always fell
        /// back cold) would coincidentally still resolve to the correct
        /// cardinal and pass. Only the Up/Down cases have no such coincidence
        /// -- NE/SE/NW/SW never cold-start to Up or Down, so an Up/Down
        /// confirm succeeding here is only explainable by the REAL hysteresis
        /// (the warmed-up <c>previousDominant</c> actually being honored).
        /// Running all 4 makes the "genuinely calls through the shared
        /// TASK-023 helper" proof non-vacuous, not just true-by-coincidence
        /// for 2 of the 4 zones.
        /// </summary>
        [TestCase(0)] // Up    -> Rojo
        [TestCase(1)] // Right -> Azul
        [TestCase(2)] // Down  -> Amarillo
        [TestCase(3)] // Left  -> Verde
        public void DiagonalStickInput_CollapsesViaTheExistingHelper_AndCanStillConfirm(int color)
        {
            CardinalDir ownZone = V2Iguala.ZoneForColor(color);
            PlayerSlot owner = AllSlots[color];

            var mg = new V2Iguala(IgualaParams.GddDefaults);
            int seed = FindSeedWithFirstColor(color);
            mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);
            Assert.That(mg.GetSequenceColor(0), Is.EqualTo(color), "sensor setup: the sequence's first symbol must be the zone under test");

            var inputs = new FakeInputs();

            // Warm up InputInterpreter's own "most-recently-dominant" hysteresis
            // memory (TASK-023) to this zone via one pure-cardinal tick (no
            // confirm yet -- button stays false).
            inputs.Set(owner, ToStick(ownZone), false);
            mg.Tick(inputs.Build(0));

            // Now aim via the DIAGONAL that straddles that same zone and confirm
            // -- CollapseDiagonal must resolve it back to ownZone via hysteresis
            // (not the diagonal's OTHER component, nor a coincidental cold-start
            // fallback -- see class doc above), and the mechanic must accept
            // that resolved cardinal as a valid owner confirm.
            Direction8 diagonal = DiagonalAdjacentTo(ownZone);
            inputs.Set(owner, diagonal, true);
            mg.Tick(inputs.Build(1));
            mg.Tick(inputs.Build(2)); // confirms here

            Assert.That(mg.Progress, Is.EqualTo(1), $"zone {ownZone}: a diagonal stick input must still collapse (via hysteresis) to the owner's own zone through the shared TASK-023 helper");
        }

        /// <summary>Deterministic search for a seed whose generated sequence starts with <paramref name="color"/> — avoids hand-picking a "lucky" seed.</summary>
        private static int FindSeedWithFirstColor(int color)
        {
            for (int seed = 0; seed < 1000; seed++)
            {
                var probe = new V2Iguala(IgualaParams.GddDefaults);
                probe.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);
                if (probe.GetSequenceColor(0) == color) return seed;
            }
            throw new InvalidOperationException($"no seed in [0,1000) produced color {color} as the first symbol -- sequence generation may be broken");
        }

        // ── AC5: identical coop payout, no internal ranking (P2) ─────────────────

        [Test]
        public void Success_YieldsCoopResultWithNoRanks_AndIdenticalPayoutToEveryEngagedSeat()
        {
            var p = new IgualaParams(sequenceLength: 3, reactWindow0: 0.9f, windowDecay: 0.05f, durationSeconds: 8f);
            var mg = new V2Iguala(p);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            int t = 0;
            AdvanceCorrectly(mg, inputs, 3, ref t);

            Assert.That(mg.IsFinished, Is.True);
            V2Result result = mg.GetResult();
            Assert.That(result.Kind, Is.EqualTo(ResultKind.CoopSuccess));
            Assert.That(result.Ranks, Is.Empty, "P2: a coop result must have NO per-seat ranking, structurally");

            var coins = new int[4];
            PayoutRules.ApplyCoop(coins, success: true, PayoutRules.DefaultCoopSuccess, activeSeatsMask: 0b1111);
            Assert.That(coins[0], Is.EqualTo(coins[1]));
            Assert.That(coins[1], Is.EqualTo(coins[2]));
            Assert.That(coins[2], Is.EqualTo(coins[3]));
        }

        [Test]
        public void Timeout_YieldsCoopFail_ButStillPaysEveryoneIdenticallyAndNeverZero()
        {
            var p = new IgualaParams(sequenceLength: 8, reactWindow0: 0.9f, windowDecay: 0.05f, durationSeconds: 3f);
            var mg = new V2Iguala(p);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs(); // nobody ever confirms -- guaranteed timeout failure
            int durationTicks = (int)MathF.Round(p.DurationSeconds * 60f);
            for (int t = 0; t < durationTicks && !mg.IsFinished; t++) mg.Tick(inputs.Build(t));

            Assert.That(mg.IsFinished, Is.True);
            V2Result result = mg.GetResult();
            Assert.That(result.Kind, Is.EqualTo(ResultKind.CoopFail));
            Assert.That(result.Ranks, Is.Empty);

            var coins = new int[4];
            PayoutRules.ApplyCoop(coins, success: false, PayoutRules.DefaultCoopFail, activeSeatsMask: 0b1111);
            Assert.That(coins[0], Is.EqualTo(coins[3]));
            Assert.That(coins[0], Is.GreaterThan(0), "GDD §6.1: even a coop failure must still pay -- the absolute zero is banned");
        }

        // ── AC6: difficultyScaling + TASK-030 validator/schema pass ──────────────

        [Test]
        public void DifficultyMult_ScalesTheEffectiveBaseReactWindow()
        {
            var p = IgualaParams.GddDefaults;

            var mgEasy = new V2Iguala(p);
            mgEasy.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            Assert.That(mgEasy.EffectiveReactWindow0, Is.EqualTo(p.ReactWindow0).Within(1e-6f));

            var mgHard = new V2Iguala(p);
            mgHard.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 2f);
            Assert.That(mgHard.EffectiveReactWindow0, Is.EqualTo(p.ReactWindow0 / 2f).Within(1e-6f), "difficultyMult must shrink the effective base window (harder = less margin)");
        }

        [Test]
        public void Definition_CoopWithPayoutTable_4_1_PassesValidator()
        {
            var def = new MicrogameDefinitionV2
            {
                Id = "mech_09_iguala",
                Mechanic = "MECH_09",
                DisplayVerb = "¡IGUALA!",
                Dynamics = MicrogameDynamics.Coop,
                Duration = 6f,
                PayoutTable = new[] { 4, 1 },
                MinPlayers = 4,
                Params = new System.Collections.Generic.Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["sequenceLength"] = 8.0,
                    ["reactWindow0"] = 0.9,
                    ["windowDecay"] = 0.05,
                    ["mode"] = "colorRelay",
                },
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);
            Assert.That(result.IsValid, Is.True, result.Message);
        }

        // ── AC1: zero heap allocation in steady-state Tick ───────────────────────

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            // Nobody ever confirms -- pure repeated-timeout-retry path (the
            // "reset window, never advance" branch) for the whole window,
            // guaranteed to stay alive since a long sequenceLength/duration
            // means it never reaches success within the measured ticks.
            var p = new IgualaParams(sequenceLength: 100, reactWindow0: 0.9f, windowDecay: 0.05f, durationSeconds: 60f);
            var mg = new V2Iguala(p);
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();

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
            var p = new IgualaParams(sequenceLength: 100, reactWindow0: 0.9f, windowDecay: 0.05f, durationSeconds: 60f);
            var mg = new V2Iguala(p);
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
                var mg = new V2Iguala(IgualaParams.GddDefaults);
                mg.Initialize(new SeededRandom(7), PlayerRoster.AllHuman, 1f);

                var inputs = new FakeInputs();
                int t = 0;
                while (!mg.IsFinished && t < 3000)
                {
                    foreach (PlayerSlot slot in AllSlots) inputs.Set(slot, Direction8.None, false);

                    int expected = mg.Progress < mg.SequenceLength ? mg.GetSequenceColor(mg.Progress) : 0;
                    bool pressNow = (t % 4) < 2; // 2 ticks true, 2 ticks false -- satisfies debounce every cycle
                    inputs.Set(AllSlots[expected], ToStick(V2Iguala.ZoneForColor(expected)), pressNow);

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

        // ── helpers ────────────────────────────────────────────────────────────────

        /// <summary>Drives <paramref name="steps"/> correct owner-confirms, each a proper 2-tick debounced press followed by a 2-tick release.</summary>
        private static void AdvanceCorrectly(V2Iguala mg, FakeInputs inputs, int steps, ref int t)
        {
            for (int s = 0; s < steps; s++)
            {
                int expected = mg.GetSequenceColor(mg.Progress);
                PlayerSlot owner = AllSlots[expected];

                inputs.Set(owner, ToStick(V2Iguala.ZoneForColor(expected)), true);
                mg.Tick(inputs.Build(t)); t++;
                mg.Tick(inputs.Build(t)); t++; // confirms here

                inputs.Set(owner, Direction8.None, false);
                mg.Tick(inputs.Build(t)); t++;
                mg.Tick(inputs.Build(t)); t++; // release confirmed -- ready for the next seat's own fresh edge
            }
        }

        private static Direction8 ToStick(CardinalDir zone)
        {
            switch (zone)
            {
                case CardinalDir.Up: return Direction8.N;
                case CardinalDir.Right: return Direction8.E;
                case CardinalDir.Down: return Direction8.S;
                default: return Direction8.W;
            }
        }

        /// <summary>
        /// A diagonal that has <paramref name="zone"/> as one of its two
        /// cardinal components, so once <paramref name="zone"/> is the
        /// "most-recently-dominant" cardinal (warmed up via a prior pure-cardinal
        /// tick), CollapseDiagonal's hysteresis resolves this diagonal BACK to
        /// <paramref name="zone"/> rather than its other component.
        /// </summary>
        private static Direction8 DiagonalAdjacentTo(CardinalDir zone)
        {
            switch (zone)
            {
                case CardinalDir.Up: return Direction8.NE;    // components: Up, Right
                case CardinalDir.Right: return Direction8.NE; // components: Up, Right
                case CardinalDir.Down: return Direction8.SE;  // components: Down, Right
                default: return Direction8.NW;                // Left; components: Up, Left
            }
        }
    }
}
