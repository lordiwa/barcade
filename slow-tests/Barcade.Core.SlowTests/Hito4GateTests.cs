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
    /// <b>[ASSUMED] no microgame content injected.</b> The real sequencer/content
    /// pool (GDD T-108) is out of scope for this ticket (SessionStateMachine's own
    /// class doc: "the real sequencer/pool is explicitly out of scope"), and
    /// microgame payouts are NOT expressed as visible <see cref="CoinDelta"/> flows
    /// (a pre-existing, TASK-051/052 characteristic of <c>PayoutRules</c> — a
    /// direct <c>coins[seat] += ...</c>, no Bank leg) — mixing that into THIS
    /// gate's economic-invariant check would either give a false failure (no
    /// CoinDelta to reconcile against a real coin increase) or silently mask real
    /// Board/weapon/evento bugs behind an unrelated, already-covered concern. So
    /// each round's MgPlay uses <see cref="NeverFinishingMicrogame"/> — a fake that
    /// never reports <see cref="IMicrogame.IsFinished"/>, so <c>CaptureResult</c>
    /// sees no legitimate finish and applies no payout (see
    /// <c>SessionStateMachine.CaptureResult</c>: "ceiling cutoff without a
    /// legitimate finish: no counters, no payout, no wins credit") — MgPlay still
    /// consumes its full configured duration via <c>RoundPhaseMachine</c>'s own
    /// elapsed-time ceiling (independent of <c>IsFinished</c>, confirmed by
    /// reading <c>RoundPhaseMachine.DurationFor</c>), so the TIME BUDGET half of
    /// this gate still exercises a realistic (GDD §2.2 "3-5s") per-round MgPlay
    /// cost. This scopes the gate exactly to what T-114 actually adds: Board
    /// (movement + resolve), weapons, eventos, and FINAL_WAGER — the pieces this
    /// ticket is the milestone gate for.
    /// </para>
    ///
    /// <para>
    /// <b>Economic-invariant surface.</b> Two SessionStateMachine getters this
    /// ticket adds make the invariant externally verifiable without reaching into
    /// BoardModel internals: <see cref="SessionStateMachine.LastBoardResolution"/>
    /// (every CoinDelta — tile effects, weapon fire, and the evento-application
    /// LluviaDeMonedas Bank flow, all merged — for the round that just resolved)
    /// and <see cref="SessionStateMachine.Coins"/> (sampled every round). Checked:
    /// every CoinDelta amount is strictly positive (GDD §5.3: "nunca desaparecen
    /// 'al banco' sin representación visual" — a delta always names a real,
    /// visible movement); no seat's <see cref="SessionStateMachine.Coins"/> ever
    /// goes negative; FINAL_WAGER's stake/share redistribution conserves the
    /// pre-wager total exactly (it has no Bank leg — a closed system among the 4
    /// seats, unlike board tile/weapon/evento flows which legitimately create or
    /// sink coins via the visible Bank pot).
    /// </para>
    ///
    /// Pure C# — no UnityEngine, no Unity scene; runs in the dotnet test runner.
    /// </summary>
    [TestFixture]
    [Category("Slow")]
    public class Hito4GateTests
    {
        /// <summary>
        /// Never legitimately finishes (<see cref="IsFinished"/> always false), so
        /// MgPlay/FinalMg always exit via <c>RoundPhaseMachine</c>'s own elapsed-
        /// time ceiling — see class doc "[ASSUMED] no microgame content injected."
        /// </summary>
        private sealed class NeverFinishingMicrogame : V2Microgame
        {
            public MicrogameId Id => MicrogameId.Reacciona;
            public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult) { }
            public void Tick(in V2Snapshot input) { }
            public bool IsFinished => false;
            public V2Result GetResult() => throw new InvalidOperationException("never finishes -- GetResult must not be called");
            public RenderState GetRenderState() => new RenderState(0, 0);
        }

        // GDD §2.2 "MG_PLAY: 3-5s según definición" -- a representative duration so
        // the time-budget check exercises realistic per-round cost even with no
        // real microgame content (see class doc).
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
            var mg = new NeverFinishingMicrogame();
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
        }

        private static int Sum(int[] values)
        {
            int total = 0;
            foreach (int v in values) total += v;
            return total;
        }
    }
}
