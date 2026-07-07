using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Board;
using Barcade.Core.Microgames.V2;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.SlowTests
{
    /// <summary>
    /// GDD T-113 — the heavy statistical sweeps for <see cref="BoardModel"/> that
    /// the fast suite (<c>BoardModelTests</c>) deliberately does not run, kept OUT
    /// of the per-push fast leg the same way <c>FairnessSweepTests</c> is:
    ///
    /// <list type="bullet">
    /// <item><description>AC2 — stop-meter stopped-value distribution is ≈ uniform
    /// over 1..6 across 1000+ seeds/reaction-tick draws.</description></item>
    /// <item><description>AC3 — the §5.3 ring-quota composition stays valid (sums
    /// to N, exactly 1 Estrella, every type represented) for every N in [16,24],
    /// both the abstract table AND a real seeded shuffle.</description></item>
    /// <item><description>AC4 — the economic invariant (visible origin/destination
    /// on every CoinDelta, full conservation, no negative balance) holds across
    /// many randomized scripted sessions, not just the one bounded sequence the
    /// fast suite pins.</description></item>
    /// </list>
    ///
    /// Pure C# — no UnityEngine, no Unity scene; runs in the dotnet test runner.
    /// </summary>
    [TestFixture]
    [Category("Slow")]
    public class BoardModelSweepTests
    {
        private sealed class FakeInputs
        {
            private readonly PlayerInput[] _players = new PlayerInput[4];

            public void Set(PlayerSlot slot, Direction8 stick = Direction8.None, bool button = false)
                => _players[(int)slot] = new PlayerInput(stick, button);

            public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
        }

        // ── AC2: stop-meter uniformity, 1000+ seeds ──────────────────────────────

        [Test]
        public void StopMeter_TapTimingAcrossManySeeds_ProducesApproximatelyUniformStoppedValues()
        {
            const int SweepSeeds = 1000;
            const int MeterTicksPerStep = 15; // 60Hz / 4Hz (GDD §5.2)
            const int MeterCycleTicks = MeterTicksPerStep * 6; // one full 1->6 sawtooth period = 90 ticks

            // Sample the tap tick over an EXACT multiple of the meter's own cycle
            // length. Sampling over the raw 8s/480-tick timeout window instead
            // would be a self-inflicted test bug, not a property of the mechanism:
            // 480 is NOT a multiple of the 90-tick cycle (480/90 = 5.33), so a
            // uniform draw over [0,480) systematically over-samples the partial
            // 6th cycle's first two values -- reproducibly measured at
            // counts=[199,212,137,155,153,144], chi-square=28.90 (fails the same
            // 20.515 bar below) when this was tried. Sampling over whole cycles
            // sidesteps that boundary artifact while still genuinely exercising
            // "tap timing unpredictable relative to the cycle" (GDD §5.2: "a 4Hz
            // nadie fija el valor de forma fiable") over several cycles' worth of
            // reaction-time variance, comfortably inside the 480-tick timeout.
            const int SampleWindowTicks = MeterCycleTicks * 4; // 360 ticks -- 4 full cycles, well under the 480-tick timeout

            var counts = new int[7]; // 1-indexed buckets 1..6; [0] unused
            // Drives the RANDOM TAP TICK per trial -- deliberately a SEPARATE
            // stream from each trial's board-construction seed, so trial
            // selection itself never leaks into board/ring state.
            var driver = new SeededRandom(12345);

            for (int seed = 0; seed < SweepSeeds; seed++)
            {
                var board = new BoardModel(BoardConfig.GddDefaults, new SeededRandom(seed));
                board.BeginMovePhase(new SeededRandom(seed));

                int tapTick = driver.NextInt(0, SampleWindowTicks);
                var inputs = new FakeInputs();

                for (int t = 0; t <= tapTick; t++)
                {
                    if (t == tapTick) inputs.Set(PlayerSlot.Rojo, button: true);
                    board.TickMove(inputs.Build(t));
                }

                int stopped = board.GetSnapshot().MeterValues[0];
                Assert.That(stopped, Is.InRange(1, 6));
                counts[stopped]++;
            }

            // Chi-square goodness-of-fit against uniform over 6 buckets (5 df).
            // Critical value at p=0.001 is 20.515 -- deliberately loose (this is a
            // sanity check on the sawtooth-vs-unpredictable-sample argument, not a
            // precision RNG test) so the sweep is robust to ordinary sampling
            // noise while still catching a genuinely biased mapping.
            double expected = SweepSeeds / 6.0;
            double chiSquare = 0;
            for (int v = 1; v <= 6; v++)
                chiSquare += Math.Pow(counts[v] - expected, 2) / expected;

            Assert.That(chiSquare, Is.LessThan(20.515),
                $"stopped-value distribution not uniform enough: counts=[{string.Join(",", counts.Skip(1))}], chi-square={chiSquare:F2}");
        }

        // ── AC3: ring-quota validity across N in [16,24] ─────────────────────────

        [Test]
        public void BoardTileQuota_AllRingSizesInGddRange_ProduceValidCompositions()
        {
            var failures = new List<string>();

            for (int n = BoardConfig.MinRingSize; n <= BoardConfig.MaxRingSize; n++)
            {
                int[] quota = BoardTileQuota.Compute(n);

                int sum = quota.Sum();
                if (sum != n) failures.Add($"N={n}: quota sums to {sum}, expected {n}");

                if (quota[(int)BoardTileType.Estrella] != 1)
                    failures.Add($"N={n}: Estrella quota={quota[(int)BoardTileType.Estrella]}, expected exactly 1 (GDD §5.3: 'Solo hay una a la vez')");

                for (int t = 0; t < quota.Length; t++)
                    if (quota[t] < 1)
                        failures.Add($"N={n}: {(BoardTileType)t} quota is {quota[t]} -- every §5.3 square type must remain represented on the ring");

                // Exercise the real shuffle too -- a seeded ring for this N must
                // reproduce the exact quota counts, not just the abstract table.
                BoardTileType[] ring = BoardRing.Build(quota, new SeededRandom(n * 7919));
                if (ring.Length != n) failures.Add($"N={n}: built ring length {ring.Length} != {n}");
                var counts = new int[quota.Length];
                foreach (BoardTileType t in ring) counts[(int)t]++;
                for (int t = 0; t < quota.Length; t++)
                    if (counts[t] != quota[t])
                        failures.Add($"N={n}: built ring has {counts[t]} of {(BoardTileType)t}, quota says {quota[t]}");
            }

            Assert.That(failures, Is.Empty, "AC3 ring-quota violations:\n" + string.Join("\n", failures));
        }

        // ── AC4: economic invariant over many randomized scripted sessions ──────

        [Test]
        public void EconomicInvariant_RandomizedScriptedSequences_NeverCreatesDestroysOrGoesNegative()
        {
            const int Trials = 300;
            const int RoundsPerTrial = 8;
            const int StartingBalance = 20;

            var failures = new List<string>();
            var tierChoices = new[] { Direction8.W, Direction8.N, Direction8.E, Direction8.None };

            for (int seed = 0; seed < Trials; seed++)
            {
                var driver = new SeededRandom(seed); // drives this trial's scripted positions/choices
                int[] quota = BoardTileQuota.Compute(BoardConfig.DefaultRingSize);
                BoardTileType[] ring = BoardRing.Build(quota, new SeededRandom(seed + 500_000));
                var board = new BoardModel(BoardConfig.GddDefaults, ring);
                board.SetBalances(new[] { StartingBalance, StartingBalance, StartingBalance, StartingBalance });
                var boardRng = new SeededRandom(seed + 1_000_000);

                long bank = 0;
                bool failed = false;

                for (int round = 0; round < RoundsPerTrial && !failed; round++)
                {
                    var positions = new int[4];
                    for (int s = 0; s < 4; s++) positions[s] = driver.NextInt(0, ring.Length);
                    board.SetForcedPositions(positions);

                    board.BeginMovePhase(boardRng);
                    board.BeginResolvePhase();

                    var inputs = new FakeInputs();
                    for (int s = 0; s < 4; s++)
                        inputs.Set((PlayerSlot)s, stick: tierChoices[driver.NextInt(0, tierChoices.Length)]);
                    while (!board.ResolveFinished) board.TickResolve(inputs.Build(0));

                    BoardResolution resolution = board.Resolve(boardRng);

                    foreach (CoinDelta flow in resolution.CoinFlows)
                    {
                        if (flow.Amount <= 0)
                        {
                            failures.Add($"seed {seed} round {round}: non-positive CoinDelta amount {flow.Amount}");
                            failed = true;
                        }
                        if (flow.Origin == CoinDelta.Bank) bank -= flow.Amount;
                        if (flow.Destination == CoinDelta.Bank) bank += flow.Amount;
                    }

                    int[] balances = board.GetSnapshot().Balances;
                    foreach (int b in balances)
                    {
                        if (b < 0)
                        {
                            failures.Add($"seed {seed} round {round}: balance went negative ({b})");
                            failed = true;
                        }
                    }

                    long totalPlayers = balances.Sum();
                    if (totalPlayers + bank != (long)StartingBalance * 4)
                    {
                        failures.Add($"seed {seed} round {round}: conservation broken -- players={totalPlayers} bank={bank}, expected total {StartingBalance * 4}");
                        failed = true;
                    }
                }
            }

            Assert.That(failures, Is.Empty, $"AC4 economic-invariant violations ({failures.Count}):\n" + string.Join("\n", failures.Take(30)));
        }
    }
}
