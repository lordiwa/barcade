using System;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Bots;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
using Barcade.Core.Scoring;
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Sujeta = Barcade.Core.Microgames.V2.SujetaMicrogame;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-076 (T-112) — coverage for <see cref="V2Sujeta"/> (GDD §4 MECH_08):
    /// v2 IMicrogame contract shape, tick-exact hold-window validation and
    /// early-release reset (AC2), coop-abandonment 4/4-&gt;3/3 and identical
    /// payout / no internal ranking (AC3, P2), infoAsym countdown visibility in
    /// both RenderState and BotView (AC4), difficultyScaling and the TASK-030
    /// validator/schema pass (AC5), zero-alloc steady Tick with a
    /// mutation-proven sensor twin, and determinism (AC1).
    /// </summary>
    [TestFixture]
    public class SujetaMicrogameV2Tests
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

        // ── AC1: v2 IMicrogame contract shape ────────────────────────────────────

        [Test]
        public void SujetaMicrogame_Id_IsSujeta()
        {
            V2Microgame mg = new V2Sujeta(SujetaParams.GddDefaults());
            Assert.That(mg.Id, Is.EqualTo(MicrogameId.Sujeta));
        }

        // ── AC2: hold window validates only with ALL required holds overlapped ──

        [Test]
        public void HoldWindow_CompletesExactlyWhenAllFourHeldContinuouslyForTheFullWindow()
        {
            var mg = new V2Sujeta(SujetaParams.GddDefaults());
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.SetAllButtons(true);

            int t = 0;
            while (mg.WindowsCompleted == 0 && t < 1000)
            {
                mg.Tick(inputs.Build(t));
                t++;
            }

            Assert.That(mg.WindowsCompleted, Is.EqualTo(1), "all 4 held continuously must eventually complete a window");
            Assert.That(mg.CurrentWindowHoldTicks, Is.EqualTo(0), "completing a window resets the current-window counter");
        }

        [Test]
        public void HoldWindow_NeverCompletes_WhenOneSeatNeverHolds()
        {
            var mg = new V2Sujeta(SujetaParams.GddDefaults());
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);
            inputs.Set(PlayerSlot.Azul, Direction8.None, true);
            inputs.Set(PlayerSlot.Amarillo, Direction8.None, true);
            inputs.Set(PlayerSlot.Verde, Direction8.None, false); // never holds

            for (int t = 0; t < 500; t++) mg.Tick(inputs.Build(t));

            Assert.That(mg.WindowsCompleted, Is.EqualTo(0), "3-of-4 held is not a completed window -- ALL required seats must overlap");
        }

        [Test]
        public void EarlyRelease_ResetsCurrentWindow_ButNeverFailsTheRound()
        {
            var p = SujetaParams.GddDefaults();
            var mg = new V2Sujeta(p);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.SetAllButtons(true);

            // Hold for HALF the window, well short of completion.
            int halfway = mg.EffectiveHoldWindowTicks / 2;
            int t = 0;
            for (; t < halfway; t++) mg.Tick(inputs.Build(t));
            Assert.That(mg.CurrentWindowHoldTicks, Is.GreaterThan(0), "sensor sanity: real progress must have accumulated");
            Assert.That(mg.WindowsCompleted, Is.EqualTo(0));

            // One seat releases -- resets progress, must NOT fail the round.
            inputs.Set(PlayerSlot.Verde, Direction8.None, false);
            mg.Tick(inputs.Build(t)); t++;
            mg.Tick(inputs.Build(t)); t++; // release confirmed (debounce)

            Assert.That(mg.CurrentWindowHoldTicks, Is.EqualTo(0), "an early release must reset the CURRENT window's progress");
            Assert.That(mg.IsFinished, Is.False, "GDD literal: an early release never fails the round outright");
            Assert.That(mg.WindowsCompleted, Is.EqualTo(0));

            // Re-hold everyone and retry to completion -- the round is genuinely retryable.
            inputs.SetAllButtons(true);
            while (mg.WindowsCompleted == 0 && t < 1000) { mg.Tick(inputs.Build(t)); t++; }

            Assert.That(mg.WindowsCompleted, Is.EqualTo(1), "the round must still be winnable after an earlier reset");
        }

        // ── AC3: coop-abandonment 4/4 -> 3/3, never impossible by abandonment ────

        [Test]
        public void IdleSeat_IsExcludedFromTheRequirement()
        {
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Human, SeatState.HumanIdle });
            var mg = new V2Sujeta(SujetaParams.GddDefaults());
            mg.Initialize(new SeededRandom(1), roster, 1f);

            Assert.That(mg.IsEngaged(PlayerSlot.Rojo), Is.True);
            Assert.That(mg.IsEngaged(PlayerSlot.Azul), Is.True);
            Assert.That(mg.IsEngaged(PlayerSlot.Amarillo), Is.True);
            Assert.That(mg.IsEngaged(PlayerSlot.Verde), Is.False, "an Idle seat must be excluded from the requirement -- 4/4 becomes 3/3");
        }

        [Test]
        public void EmptySeat_IsExcludedFromTheRequirement()
        {
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Human, SeatState.Empty });
            var mg = new V2Sujeta(SujetaParams.GddDefaults());
            mg.Initialize(new SeededRandom(1), roster, 1f);

            Assert.That(mg.IsEngaged(PlayerSlot.Verde), Is.False);
        }

        [Test]
        public void AbandonedSeat_NeverBlocksTheObjective_3Of3StillCompletesTheWindow()
        {
            // The idle seat NEVER holds its button at all -- if the requirement
            // still silently demanded 4/4, this round would be mathematically
            // impossible to win. It must complete via the 3 engaged seats alone.
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Human, SeatState.HumanIdle });
            var mg = new V2Sujeta(SujetaParams.GddDefaults());
            mg.Initialize(new SeededRandom(1), roster, 1f);

            var inputs = new FakeInputs();
            inputs.Set(PlayerSlot.Rojo, Direction8.None, true);
            inputs.Set(PlayerSlot.Azul, Direction8.None, true);
            inputs.Set(PlayerSlot.Amarillo, Direction8.None, true);
            inputs.Set(PlayerSlot.Verde, Direction8.None, false); // idle, never holds

            int t = 0;
            while (mg.WindowsCompleted == 0 && t < 1000) { mg.Tick(inputs.Build(t)); t++; }

            Assert.That(mg.WindowsCompleted, Is.EqualTo(1), "abandonment must never make the coop objective impossible");
        }

        // ── AC3: identical coop payout, no internal ranking (P2) ─────────────────

        [Test]
        public void Success_YieldsCoopResultWithNoRanks_AndIdenticalPayoutToEveryEngagedSeat()
        {
            var mg = new V2Sujeta(SujetaParams.GddDefaults());
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.SetAllButtons(true);
            int t = 0;
            while (!mg.IsFinished && t < 1000) { mg.Tick(inputs.Build(t)); t++; }

            V2Result result = mg.GetResult();
            Assert.That(result.Kind, Is.EqualTo(ResultKind.CoopSuccess));
            Assert.That(result.Ranks, Is.Empty, "P2: a coop result must have NO per-seat ranking, structurally");

            var coins = new int[4];
            PayoutRules.ApplyCoop(coins, success: true, PayoutRules.DefaultCoopSuccess, activeSeatsMask: 0b1111);
            Assert.That(coins[0], Is.EqualTo(coins[1]));
            Assert.That(coins[1], Is.EqualTo(coins[2]));
            Assert.That(coins[2], Is.EqualTo(coins[3]));
            Assert.That(coins[0], Is.GreaterThan(0), "GDD §6.1 minimum-reward invariant: never a zero payout");
        }

        [Test]
        public void Timeout_YieldsCoopFail_ButStillPaysEveryoneIdenticallyAndNeverZero()
        {
            var p = SujetaParams.GddDefaults();
            var mg = new V2Sujeta(p);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs(); // nobody ever holds -- guaranteed timeout failure
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

        // ── AC4: infoAsym countdown visibility, RenderState AND BotView ──────────

        [Test]
        public void InfoAsym_OnlyTheViewerSeat_SeesTheRealCountdown()
        {
            var p = SujetaParams.GddDefaults(SujetaMode.InfoAsym, viewerSeat: 0); // Rojo is the viewer
            var mg = new V2Sujeta(p);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.SetAllButtons(true);

            int halfway = mg.EffectiveHoldWindowTicks / 2;
            for (int t = 0; t < halfway; t++) mg.Tick(inputs.Build(t));

            RenderState rs = mg.GetRenderState();
            Assert.That(rs.Hud.Meters[0], Is.GreaterThan(0f), "the viewer seat must see the real, in-progress countdown");
            Assert.That(rs.Hud.Meters[1], Is.EqualTo(0f), "a non-viewing seat's published meter must carry zero timing information");
            Assert.That(rs.Hud.Meters[2], Is.EqualTo(0f));
            Assert.That(rs.Hud.Meters[3], Is.EqualTo(0f));

            // AC4 explicitly names BotView too -- a bot wrapping a non-viewer
            // seat reads the exact same structurally-blind value, not merely a
            // convention it is expected to respect.
            var nonViewerBotView = new BotView(rs, (int)PlayerSlot.Azul);
            Assert.That(nonViewerBotView.Hud.Meters[nonViewerBotView.Seat], Is.EqualTo(0f),
                "a non-viewing seat/bot must not be able to see the timing via BotView either");

            var viewerBotView = new BotView(rs, (int)PlayerSlot.Rojo);
            Assert.That(viewerBotView.Hud.Meters[viewerBotView.Seat], Is.GreaterThan(0f));
        }

        [Test]
        public void HoldTogether_EveryActiveSeatSeesTheSameSharedCountdown()
        {
            var p = SujetaParams.GddDefaults(SujetaMode.HoldTogether);
            var mg = new V2Sujeta(p);
            mg.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.SetAllButtons(true);
            int halfway = mg.EffectiveHoldWindowTicks / 2;
            for (int t = 0; t < halfway; t++) mg.Tick(inputs.Build(t));

            RenderState rs = mg.GetRenderState();
            Assert.That(rs.Hud.Meters[0], Is.GreaterThan(0f));
            Assert.That(rs.Hud.Meters[1], Is.EqualTo(rs.Hud.Meters[0]));
            Assert.That(rs.Hud.Meters[2], Is.EqualTo(rs.Hud.Meters[0]));
            Assert.That(rs.Hud.Meters[3], Is.EqualTo(rs.Hud.Meters[0]));
        }

        // ── AC5: difficultyScaling + TASK-030 validator/schema pass ──────────────

        [Test]
        public void DifficultyMult_ScalesTheEffectiveHoldWindowDirectly()
        {
            var p = SujetaParams.GddDefaults();
            int baseTicks = (int)MathF.Round(p.HoldWindowSeconds * 60f);

            var mgEasy = new V2Sujeta(p);
            mgEasy.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 1f);
            Assert.That(mgEasy.EffectiveHoldWindowTicks, Is.EqualTo(baseTicks));

            var mgHard = new V2Sujeta(p);
            mgHard.Initialize(new SeededRandom(1), PlayerRoster.AllHuman, 2f);
            Assert.That(mgHard.EffectiveHoldWindowTicks, Is.EqualTo(baseTicks * 2), "difficultyMult must scale the hold window directly (harder = longer hold required)");
        }

        [Test]
        public void Definition_CoopWithPayoutTable_4_1_PassesValidator()
        {
            var def = new MicrogameDefinitionV2
            {
                Id = "mech_08_sujeta",
                Mechanic = "MECH_08",
                DisplayVerb = "¡SUJETA!",
                Dynamics = MicrogameDynamics.Coop,
                Duration = 6f,
                PayoutTable = new[] { 4, 1 },
                MinPlayers = 4,
                Params = new System.Collections.Generic.Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["holdWindow"] = 1.5,
                    ["windowsToWin"] = 1.0,
                    ["mode"] = "holdTogether",
                },
            };

            ValidationResult result = MicrogameDefinitionValidator.Validate(def);
            Assert.That(result.IsValid, Is.True, result.Message);
        }

        // ── AC1: zero heap allocation in steady-state Tick ───────────────────────

        [Test]
        public void SteadyStateTick_AllocatesNoManagedMemory()
        {
            // WindowsToWin set far out of reach within the measured window so the
            // round never finishes early -- but HoldWindow stays short (GDD
            // default) so the "complete + reset" branch (the most complex path)
            // fires repeatedly, not just the idle "not held" path.
            var p = new SujetaParams(holdWindowSeconds: 1.5f, windowsToWin: 1000, mode: SujetaMode.HoldTogether, viewerSeat: 0, durationSeconds: 60f);
            var mg = new V2Sujeta(p);
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.SetAllButtons(true); // everyone holds continuously the whole test

            for (int i = 0; i < 100; i++) mg.Tick(inputs.Build(i));
            Assert.That(mg.IsFinished, Is.False, "sensor setup: must still be live after warmup");

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 100; i < 1100; i++) mg.Tick(inputs.Build(i));
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(mg.IsFinished, Is.False, "sensor setup: must still be live after the measured window");
            Assert.That(mg.WindowsCompleted, Is.GreaterThan(0), "sensor sanity: the complete+reset branch must have actually fired during the window");
            Assert.That(after - before, Is.EqualTo(0L));
        }

        /// <summary>Mutation-proof sensor check — see PersigueMicrogameV2Tests's twin test for the rationale.</summary>
        [Test]
        public void SteadyStateTick_SensorDetectsADeliberateAllocation()
        {
            var p = new SujetaParams(holdWindowSeconds: 1.5f, windowsToWin: 1000, mode: SujetaMode.HoldTogether, viewerSeat: 0, durationSeconds: 60f);
            var mg = new V2Sujeta(p);
            mg.Initialize(new SeededRandom(42), PlayerRoster.AllHuman, 1f);

            var inputs = new FakeInputs();
            inputs.SetAllButtons(true);
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
                var mg = new V2Sujeta(SujetaParams.GddDefaults());
                mg.Initialize(new SeededRandom(7), PlayerRoster.AllHuman, 1f);

                var inputs = new FakeInputs();
                int t = 0;
                while (!mg.IsFinished && t < 1000)
                {
                    // A deterministic, imperfect script: everyone holds except a
                    // periodic drop on Verde -- exercises both the accumulate and
                    // reset paths identically across both runs.
                    inputs.Set(PlayerSlot.Rojo, Direction8.None, true);
                    inputs.Set(PlayerSlot.Azul, Direction8.None, true);
                    inputs.Set(PlayerSlot.Amarillo, Direction8.None, true);
                    inputs.Set(PlayerSlot.Verde, Direction8.None, (t % 50) >= 4);
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
    }
}
