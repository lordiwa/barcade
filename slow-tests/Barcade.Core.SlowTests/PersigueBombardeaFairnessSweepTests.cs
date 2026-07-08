using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Bots;
using Barcade.Core.Microgames.V2;
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Persigue = Barcade.Core.Microgames.V2.PersigueMicrogame;
using V2Bombardea = Barcade.Core.Microgames.V2.BombardeaMicrogame;

namespace Barcade.SlowTests
{
    /// <summary>
    /// TASK-040 (T-111) — slow-tier statistical balance sweeps for MECH_06/07,
    /// mirroring <see cref="FairnessSweepTests"/>'s own style (1000-seed loops,
    /// surfaced-not-hidden failure reporting).
    ///
    /// <para>
    /// <b>MECH_06 corral test (AC3, GDD §4):</b> "3 bots coordinados atrapan a
    /// un bot solista óptimo en &lt; duration en &gt;= 40% de semillas" — the
    /// trio runs <see cref="Bot.Medio"/>, the solo runs <see cref="Bot.Optimo"/>
    /// in <see cref="PersigueMode.SoloFlees"/> (the trio is the side trying to
    /// catch); the property is an aggregate catch-RATE across 1000 seeds, not a
    /// per-seed pass/fail, so this uses a single counter + one final band
    /// assertion rather than <c>FairnessSweepTests.ReportAndAssert</c>'s
    /// per-seed-violation list.
    /// </para>
    ///
    /// <para>
    /// <b>MECH_07 winrate band (AC5, Annex D.3):</b> "winrate del solista con
    /// 4xBot.Medio sobre 1000 semillas ∈ [40%,60%]" — all 4 seats run
    /// <see cref="Bot.Medio"/> (the canonical Annex D.3 "bots calibrados"
    /// shape), aggregated the same way.
    /// </para>
    /// </summary>
    [TestFixture]
    [Category("Slow")]
    public class PersigueBombardeaFairnessSweepTests
    {
        private const int SweepSeeds = 1000;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        // ── MECH_06: corral test (AC3) ───────────────────────────────────────────

        [Test]
        public void Persigue_SoloFlees_3MedioBotsCatchOptimalSolo_AtLeast40PercentOf1000Seeds()
        {
            int caught = 0;
            var failures = new List<string>();

            for (int seed = 0; seed < SweepSeeds; seed++)
            {
                var p = PersigueParams.GddDefaults(soloSeat: 0, PersigueMode.SoloFlees);
                var mg = new V2Persigue(p);
                mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                var policies = new IBotPolicy[4];
                for (int i = 0; i < 4; i++) policies[i] = new PersigueBotPolicy();
                var botRng = SeededRandom.Derive(seed, roundNumber: 0, RngStream.Bots);

                var players = new PlayerInput[4];
                int t = 0;
                int maxTicks = (int)MathF.Round(p.DurationSeconds * 60f) + 10;
                while (!mg.IsFinished && t < maxTicks)
                {
                    RenderState rs = mg.GetRenderState();
                    for (int i = 0; i < 4; i++)
                    {
                        BotSkill skill = i == 0 ? Bot.Optimo : Bot.Medio; // seat 0 = solo (Optimo), trio = Medio
                        players[i] = policies[i].Decide(skill, new BotView(rs, i), botRng);
                    }
                    mg.Tick(new V2Snapshot(t, players));
                    t++;
                }

                if (!mg.IsFinished)
                {
                    failures.Add($"seed {seed}: round did not finish within the tick bound");
                    continue;
                }

                if (mg.IsCaught(PlayerSlot.Rojo)) caught++;
            }

            float rate = (float)caught / SweepSeeds;
            TestContext.Progress.WriteLine($"[SLOW-SWEEP] PERSIGUE corral (soloFlees, 3xBot.Medio vs Bot.Optimo solo): {caught}/{SweepSeeds} caught ({rate:P1}).");
            if (failures.Count > 0)
            {
                TestContext.Progress.WriteLine($"[SLOW-SWEEP] {failures.Count} non-finishing run(s):");
                foreach (string f in failures) TestContext.Progress.WriteLine("    " + f);
            }

            Assert.That(failures, Is.Empty, "every round must finish within the tick bound");
            Assert.That(rate, Is.GreaterThanOrEqualTo(0.40f),
                $"GDD §4 MECH_06 AC3: 3 coordinated Bot.Medio must catch an optimal solo in >= 40% of {SweepSeeds} seeds (measured {rate:P1}).");
        }

        // ── MECH_07: winrate band (AC5) ──────────────────────────────────────────

        [Test]
        public void Bombardea_4MedioBots_SoloWinrateInBand_40To60Percent_Over1000Seeds()
        {
            int soloWins = 0;
            var failures = new List<string>();

            for (int seed = 0; seed < SweepSeeds; seed++)
            {
                var p = BombardeaParams.GddDefaults(soloSeat: 0);
                var mg = new V2Bombardea(p);
                mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                var policies = new IBotPolicy[4];
                for (int i = 0; i < 4; i++) policies[i] = new BombardeaBotPolicy();
                var botRng = SeededRandom.Derive(seed, roundNumber: 0, RngStream.Bots);

                var players = new PlayerInput[4];
                int t = 0;
                int maxTicks = (int)MathF.Round(p.DurationSeconds * 60f) + 200; // headroom for a last-second telegraph to resolve
                while (!mg.IsFinished && t < maxTicks)
                {
                    RenderState rs = mg.GetRenderState();
                    for (int i = 0; i < 4; i++)
                        players[i] = policies[i].Decide(Bot.Medio, new BotView(rs, i), botRng);
                    mg.Tick(new V2Snapshot(t, players));
                    t++;
                }

                if (!mg.IsFinished)
                {
                    failures.Add($"seed {seed}: round did not finish within the tick bound");
                    continue;
                }

                if (mg.SoloHitsLanded >= 1) soloWins++;
            }

            float winrate = (float)soloWins / SweepSeeds;
            TestContext.Progress.WriteLine($"[SLOW-SWEEP] BOMBARDEA winrate (4xBot.Medio): {soloWins}/{SweepSeeds} solo wins ({winrate:P1}).");
            if (failures.Count > 0)
            {
                TestContext.Progress.WriteLine($"[SLOW-SWEEP] {failures.Count} non-finishing run(s):");
                foreach (string f in failures) TestContext.Progress.WriteLine("    " + f);
            }

            Assert.That(failures, Is.Empty, "every round must finish within the tick bound");
            Assert.That(winrate, Is.InRange(0.40f, 0.60f),
                $"Annex D.3/AC5: solo winrate with 4xBot.Medio must be in [40%,60%] over {SweepSeeds} seeds (measured {winrate:P1}).");
        }
    }
}
