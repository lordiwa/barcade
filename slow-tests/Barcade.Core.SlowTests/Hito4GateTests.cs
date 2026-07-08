using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Board;
using Barcade.Core.Microgames.V2;
using Barcade.Core.Scoring;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Esquiva = Barcade.Core.Microgames.V2.EsquivaMicrogame;

namespace Barcade.SlowTests
{
    /// <summary>
    /// GDD §17 Hito 4 gate (T-114): "invariantes económicos en verde sobre 1 000
    /// semillas de sesión simulada por bots; sesión completa dentro del
    /// presupuesto temporal de §2.2." Drives the FULL <see cref="SessionStateMachine"/>
    /// — Join, every round's BOARD_MOVE/BOARD_RESOLVE (weapons + eventos),
    /// FINAL_WAGER, FinalMg, GameOver — with <see cref="BoardBotDriver"/> supplying
    /// every seat's input, for 1000 seeds.
    ///
    /// <para>
    /// <b>Real microgame content (TASK-071).</b> Each round's MgPlay drives a real
    /// <see cref="V2Esquiva"/> (GDD MECH_02) instead of the previous
    /// <c>NeverFinishingMicrogame</c> fake. That fake existed only because
    /// <c>PayoutRules</c> mutated <c>coins[seat]</c> directly with no
    /// <see cref="CoinDelta"/> leg (TASK-051/052) — mixing an un-reconcilable
    /// payout into this gate's economic-invariant check would have given a false
    /// failure. TASK-071 M1 closed that gap (<c>PayoutRules.ApplyCompetitive</c>/
    /// <c>ApplyCoop</c> now return a Bank-origin <see cref="CoinDelta"/> per paid
    /// seat), so this gate can now drive REAL payouts across all 1000 sessions and
    /// fold them into the same zero-violation check (see
    /// <see cref="CheckMicrogamePayoutInvariant"/>). <see cref="ShortEsquivaParams"/>
    /// sets <c>DurationSeconds</c> well under <see cref="MgPlaySeconds"/> so
    /// <see cref="IMicrogame.IsFinished"/> is guaranteed true (via Esquiva's own
    /// tick-ceiling, <c>_tick >= _durationTicks</c>, independent of hits) by the
    /// time <c>RoundPhaseMachine</c>'s elapsed-time ceiling ends MgPlay — a
    /// legitimate <c>Ranked</c> result every round regardless of bot skill (see
    /// <see cref="BoardBotDriver"/>, which supplies no MgPlay input at all: Esquiva
    /// does not need movement to finish, only to avoid guaranteed elimination).
    /// MgPlay still consumes its full configured duration via
    /// <c>RoundPhaseMachine</c>'s own elapsed-time ceiling (confirmed by reading
    /// <c>RoundPhaseMachine.DurationFor</c>: <c>PhaseKind.Play</c> is purely
    /// time-bounded, never short-circuited by <c>IsFinished</c>), so the TIME
    /// BUDGET half of this gate is unaffected by this change.
    /// </para>
    ///
    /// <para>
    /// <b>Economic-invariant surface.</b> SessionStateMachine getters this ticket
    /// (and TASK-042) add make the invariant externally verifiable without
    /// reaching into BoardModel internals: <see cref="SessionStateMachine.LastBoardResolution"/>
    /// (every CoinDelta — tile effects, weapon fire, and the evento-application
    /// LluviaDeMonedas Bank flow, all merged — for the round that just resolved),
    /// <see cref="SessionStateMachine.LastMicrogamePayout"/> (TASK-071: every
    /// Bank-origin CoinDelta the round's real microgame payout emitted),
    /// <see cref="SessionStateMachine.Coins"/> (sampled every round), and
    /// <see cref="SessionStateMachine.BoardSnapshot"/> (TASK-071 M2: BoardModel's
    /// own internal balances, reconciled against Coins at every resolve). Checked:
    /// every CoinDelta amount (board/weapon/evento AND microgame payout) is
    /// strictly positive (GDD §5.3: "nunca desaparecen 'al banco' sin
    /// representación visual" — a delta always names a real, visible movement);
    /// every microgame payout CoinDelta's origin is the Bank (payouts are income,
    /// never seat-to-seat); no seat's <see cref="SessionStateMachine.Coins"/> ever
    /// goes negative; <c>Coins == BoardSnapshot.Balances</c> exactly at every
    /// BOARD_RESOLVE exit (TASK-071 M2 — the strongest §5.3 reconciliation,
    /// previously only asserted by 3 single-session fast tests); FINAL_WAGER's
    /// stake/share redistribution conserves the pre-wager total exactly (it has no
    /// Bank leg — a closed system among the 4 seats, unlike board tile/weapon/
    /// evento/payout flows which legitimately create or sink coins via the visible
    /// Bank pot).
    /// </para>
    ///
    /// Pure C# — no UnityEngine, no Unity scene; runs in the dotnet test runner.
    /// </summary>
    [TestFixture]
    [Category("Slow")]
    public class Hito4GateTests
    {
        // TASK-071: GDD MECH_02 defaults but a short DurationSeconds so
        // IsFinished is guaranteed true (via Esquiva's own tick ceiling) well
        // before MgPlaySeconds elapses, regardless of bot skill or difficulty
        // ramp (DurationSeconds is fixed -- only HazardSpeed/SpawnRate scale with
        // difficultyMult). See class doc "Real microgame content".
        private static readonly EsquivaParams ShortEsquivaParams = new EsquivaParams(
            spawnRateBasePerSec: EsquivaParams.GddDefaults.SpawnRateBasePerSec,
            spawnRampCoef: EsquivaParams.GddDefaults.SpawnRampCoef,
            hazardSpeed: EsquivaParams.GddDefaults.HazardSpeed,
            hazardPattern: EsquivaParams.GddDefaults.HazardPattern,
            avatarSpeed: EsquivaParams.GddDefaults.AvatarSpeed,
            avatarRadius: EsquivaParams.GddDefaults.AvatarRadius,
            hazardRadius: EsquivaParams.GddDefaults.HazardRadius,
            durationSeconds: 2f,
            jumpEnabled: EsquivaParams.GddDefaults.JumpEnabled);

        // GDD §2.2 "MG_PLAY: 3-5s según definición" -- a representative duration so
        // the time-budget check exercises realistic per-round cost.
        private const float MgPlaySeconds = 4f;

        // GDD §2.2 "Total ronda: máximo 40s" -- the per-round ceiling this gate's
        // aggregate whole-session budget is derived from (Join/GameOver excluded --
        // those are lobby/decompression states, not part of §2.2's round table).
        private const float RoundMaxSeconds = 40f;

        [Test]
        public void FullBotDrivenSessions_1000Seeds_ZeroEconomicInvariantViolations_WithinTimeBudget()
        {
            const int SessionCount = 1000;
            var config = SessionStateMachineConfig.GddDefaults;
            float ceilingSeconds = config.TotalRounds * RoundMaxSeconds + config.FinalWagerSeconds + MgPlaySeconds;

            var failures = new List<string>();
            var stopwatch = Stopwatch.StartNew();
            double maxObservedSeconds = 0;

            for (int seed = 0; seed < SessionCount; seed++)
                RunOneSession(seed, config, ceilingSeconds, failures, ref maxObservedSeconds);

            stopwatch.Stop();
            TestContext.Progress.WriteLine(
                $"[SLOW-SWEEP] Hito4 gate: {SessionCount} full bot-driven sessions in {stopwatch.Elapsed} " +
                $"(max observed session length {maxObservedSeconds:F1}s, budget ceiling {ceilingSeconds:F1}s)");

            Assert.That(failures, Is.Empty,
                $"AC5 Hito4 gate violations ({failures.Count}/{SessionCount} seeds):\n" + string.Join("\n", failures.GetRange(0, Math.Min(30, failures.Count))));
        }

        private static void RunOneSession(
            int seed, SessionStateMachineConfig config, float ceilingSeconds, List<string> failures, ref double maxObservedSeconds)
        {
            var fsm = new SessionStateMachine(new SeededRandom(seed), config);
            var driver = new BoardBotDriver(SeededRandom.Derive(seed, roundNumber: 0, RngStream.Bots));
            var mg = new V2Esquiva(ShortEsquivaParams);
            fsm.SetActiveMicrogame(mg, "GATE", playDurationSeconds: MgPlaySeconds);

            fsm.InsertCredit();

            const int MaxTicks = 200_000; // defensive bound -- a real stall surfaces as a failed assertion below, not a hang
            int t = 0;
            int joinCompleteTick = -1;
            int[] coinsBeforeWager = null;

            for (; t < MaxTicks && fsm.CurrentPhase != SessionPhase.GameOver; t++)
            {
                if (joinCompleteTick < 0 && fsm.CurrentPhase != SessionPhase.Join) joinCompleteTick = t;

                SessionPhase beforeTick = fsm.CurrentPhase;
                fsm.Tick(driver.Decide(fsm, t));

                if (beforeTick == SessionPhase.BoardResolve && fsm.CurrentPhase != SessionPhase.BoardResolve)
                    CheckRoundInvariant(seed, fsm, failures);

                // TASK-071 M1: fold the round's REAL microgame payout (now that
                // NeverFinishingMicrogame is gone) into the same zero-violation
                // sweep, at the edge LastMicrogamePayout is populated (MgResult
                // entry) -- same "just arrived" edge-detection idiom as the
                // FinalWager baseline capture just below.
                if (beforeTick != SessionPhase.MgResult && fsm.CurrentPhase == SessionPhase.MgResult)
                    CheckMicrogamePayoutInvariant(seed, fsm, failures);

                if (beforeTick != SessionPhase.FinalWager && fsm.CurrentPhase == SessionPhase.FinalWager)
                    coinsBeforeWager = (int[])fsm.Coins.Clone();

                foreach (int c in fsm.Coins)
                    if (c < 0) failures.Add($"seed {seed} tick {t}: negative balance {c}");
            }

            if (fsm.CurrentPhase != SessionPhase.GameOver)
            {
                failures.Add($"seed {seed}: did not reach GameOver within {MaxTicks} ticks (stalled in {fsm.CurrentPhase})");
                return;
            }

            if (coinsBeforeWager != null && fsm.LastWagerResult != null)
            {
                int before = Sum(coinsBeforeWager);
                int after = Sum(fsm.LastWagerResult.CoinsAfter);
                if (before != after)
                    failures.Add($"seed {seed}: FINAL_WAGER did not conserve coins (before={before}, after={after})");
            }

            double sessionSeconds = (t - joinCompleteTick) / (double)config.TicksPerSecond;
            if (sessionSeconds > maxObservedSeconds) maxObservedSeconds = sessionSeconds;
            if (sessionSeconds > ceilingSeconds)
                failures.Add($"seed {seed}: session length {sessionSeconds:F1}s exceeds the §2.2-derived budget ({ceilingSeconds:F1}s)");
        }

        private static void CheckRoundInvariant(int seed, SessionStateMachine fsm, List<string> failures)
        {
            BoardResolution? resolution = fsm.LastBoardResolution;
            if (!resolution.HasValue) return;

            foreach (CoinDelta flow in resolution.Value.CoinFlows)
                if (flow.Amount <= 0)
                    failures.Add($"seed {seed}: non-positive CoinDelta amount {flow.Amount} ({flow.Origin}->{flow.Destination})");

            // TASK-071 M2: the strongest §5.3 reconciliation check -- Coins must
            // mirror BoardSnapshot.Balances exactly at every resolve, not merely
            // have positive deltas. Previously only asserted by BoardSnapshot's
            // own doc comment and 3 single-session fast tests; this repeats it
            // across all 1000 seeds and every round.
            BoardSnapshot snapshot = fsm.BoardSnapshot;
            int[] coins = fsm.Coins;
            for (int seat = 0; seat < coins.Length; seat++)
                if (coins[seat] != snapshot.Balances[seat])
                    failures.Add(
                        $"seed {seed}: seat {seat} Coins ({coins[seat]}) != BoardSnapshot.Balances ({snapshot.Balances[seat]}) at BOARD_RESOLVE exit");
        }

        /// <summary>
        /// TASK-071 M1: the real microgame payout's own Bank-origin CoinDeltas
        /// (<see cref="SessionStateMachine.LastMicrogamePayout"/>) folded into the
        /// same zero-violation sweep <see cref="CheckRoundInvariant"/> already
        /// runs for board/weapon/evento flows -- every amount strictly positive,
        /// every origin the Bank (payouts are income, never seat-to-seat).
        /// </summary>
        private static void CheckMicrogamePayoutInvariant(int seed, SessionStateMachine fsm, List<string> failures)
        {
            foreach (CoinDelta flow in fsm.LastMicrogamePayout)
            {
                if (flow.Amount <= 0)
                    failures.Add($"seed {seed}: non-positive microgame payout CoinDelta amount {flow.Amount} ({flow.Origin}->{flow.Destination})");
                if (flow.Origin != CoinDelta.Bank)
                    failures.Add($"seed {seed}: microgame payout CoinDelta origin {flow.Origin} is not Bank -- payouts must be Bank income, never seat-to-seat");
            }
        }

        private static int Sum(int[] values)
        {
            int total = 0;
            foreach (int v in values) total += v;
            return total;
        }
    }
}
