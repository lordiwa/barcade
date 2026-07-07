using System;
using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;
using Barcade.Core.Microgames.V2;
// Barcade.SlowTests is NOT nested under Barcade.Core, so unqualified names do not
// resolve against Barcade.Core's own members the way the fast EditMode fixtures'
// Barcade.Core.Tests namespace does. Only the genuinely-duplicated type names
// (MicrogameResult / InputSnapshot / EsquivaMicrogame each exist in both
// Barcade.Core and Barcade.Core.Microgames.V2) need an alias — same as the fast
// v2 fixtures.
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Esquiva = Barcade.Core.Microgames.V2.EsquivaMicrogame;
using V2Manten = Barcade.Core.Microgames.V2.MantenMicrogame;
using V2Corre = Barcade.Core.Microgames.V2.CorreMicrogame;

namespace Barcade.SlowTests
{
    /// <summary>
    /// TASK-063 — slow-tier statistical escapability / fairness sweep. For each
    /// competitive mechanic, run its optimal-play oracle (the SAME solver bot the
    /// fast suite proved on ~20 seeds, extracted under <c>Oracles/</c>) across
    /// ≥1000 seeds — per hazard pattern where the mechanic has patterns — and
    /// assert the GDD fairness property holds on EVERY seed.
    ///
    /// This lives in its own project (<c>Barcade.Core.SlowTests</c>), kept OUT of
    /// the per-push fast leg (<c>fast-tests/Barcade.Core.FastTests</c>, 592 green):
    /// a 1000+-seed × multi-mechanic sweep takes minutes, so it runs on demand and
    /// on a scheduled CI workflow (<c>.github/workflows/slow-sweep.yml</c>), never
    /// on every push.
    ///
    /// SURFACING (AC3): a violated property does NOT fail-fast and hide the rest.
    /// Every failing seed is collected, its seed + property + a short diagnostic
    /// logged (so a human can reproduce it), and only then is the test failed with
    /// the full tally — a genuine fairness regression surfaces the WHOLE bad set
    /// for design review, not just the first bad seed.
    ///
    /// Pure C# — no UnityEngine, no Unity scene; runs in the dotnet test runner.
    /// </summary>
    [TestFixture]
    [Category("Slow")]
    public class FairnessSweepTests
    {
        /// <summary>
        /// Seeds per pattern/mechanic. AC1 requires ≥1000; PCG32 seeds 0..(N-1) are
        /// independent streams, and the first 20 overlap the fast suite's own
        /// sweeps, so those double as a parity check on the extracted oracles.
        /// </summary>
        private const int SweepSeeds = 1000;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        // ── ESQUIVA (MECH_02) — optimal EscapeBot survives every pattern/seed ────

        [Test]
        public void Esquiva_EscapabilityOracle_SurvivesFullDuration_AllPatterns_Sweep()
        {
            // GDD §4 MECH_02: "el bot óptimo sobrevive; no hay derrota forzada"
            // (Homing_soft: "limita giro a 30°/s para que siempre exista escape").
            //
            // DURATION = 5 s = the GDD-canonical Esquiva duration (EsquivaParams
            // .GddDefaults.DurationSeconds AND the mg_esquiva MicrogameDefinition
            // Duration both = 5.0). This sweep asserts the fairness property at the
            // parameters the mechanic ACTUALLY runs at — deliberately NOT the fast
            // EscapabilityBot test's 8 s, which is a longer stress duration.
            //
            // Why this matters (investigated for TASK-063, data in the hand-off):
            // this same faithful EscapeBot is CLEAN across 2000 seeds/pattern at
            // 5 s and 6 s on all four patterns, but at 7 s (9/2000) and 8 s
            // (11/2000) a small fraction of HomingSoft seeds trap it — late-round
            // states where 4-5 homing hazards have accumulated in the 1×1 arena and
            // the reactive one-ply bot freezes in a local optimum. That is a
            // stress-duration + reactive-oracle-myopia observation (escape still
            // provably exists on those seeds — a deeper solver dodges them), NOT a
            // spec-level fairness violation. Reported for design review; the gate
            // here stays at the real 5 s spec, where the property holds.
            var patterns = new[]
            {
                EsquivaHazardPattern.Rain, EsquivaHazardPattern.Sides,
                EsquivaHazardPattern.Cross, EsquivaHazardPattern.HomingSoft,
            };

            var failures = new List<string>();
            foreach (EsquivaHazardPattern pattern in patterns)
            {
                for (int seed = 0; seed < SweepSeeds; seed++)
                {
                    var mg = new V2Esquiva(new EsquivaParams(
                        spawnRateBasePerSec: 0.6f, spawnRampCoef: 0.15f, hazardSpeed: 0.15f,
                        hazardPattern: pattern, avatarSpeed: 0.35f,
                        avatarRadius: 0.03f, hazardRadius: 0.03f, durationSeconds: 5f, jumpEnabled: false));
                    mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                    var bot = new EsquivaEscapeBot();
                    var inputs = new FakeInputs();
                    int t = 0;
                    while (!mg.IsFinished && t < 1000)
                    {
                        RenderState rs = mg.GetRenderState();
                        foreach (PlayerSlot slot in AllSlots)
                            inputs.Set(slot, bot.Decide(rs, (int)slot));
                        mg.Tick(inputs.Build(t));
                        t++;
                    }

                    if (mg.IsEliminated(PlayerSlot.Rojo))
                        failures.Add($"pattern {pattern}, seed {seed}: optimal EscapeBot was ELIMINATED at tick " +
                                     $"{mg.GetEliminationTick(PlayerSlot.Rojo)} — a forced loss violates escapability");
                }
                TestContext.Progress.WriteLine($"[SLOW-SWEEP] ESQUIVA/{pattern}: {SweepSeeds} seeds swept.");
            }

            ReportAndAssert("ESQUIVA escapability (optimal EscapeBot survives full duration)",
                patterns.Length * SweepSeeds, failures);
        }

        // ── MANTÉN (MECH_01) — perfect correction survives; null input falls ─────

        [Test]
        public void Manten_PerfectCorrectionOracle_SurvivesFullDuration_Sweep()
        {
            // GDD §4 MECH_01 AC2: "corrección perfecta -> 100% sobreviven."
            var failures = new List<string>();
            for (int seed = 0; seed < SweepSeeds; seed++)
            {
                var mg = new V2Manten(MantenParams.GddDefaults);
                mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                var oracle = new MantenCorrectionOracle();
                var inputs = new FakeInputs();
                int t = 0;
                while (!mg.IsFinished && t < 3000)
                {
                    RenderState rs = mg.GetRenderState();
                    foreach (PlayerSlot slot in AllSlots)
                        inputs.Set(slot, oracle.Decide(rs, (int)slot));
                    mg.Tick(inputs.Build(t));
                    t++;
                }

                foreach (PlayerSlot slot in AllSlots)
                    if (mg.HasFallen(slot))
                        failures.Add($"seed {seed}, seat {slot}: perfect-correction oracle FELL at tick " +
                                     $"{mg.GetFallenTick(slot)} — perfect play must survive the full duration");
            }

            ReportAndAssert("MANTÉN perfect-correction survival", SweepSeeds * AllSlots.Length, failures);
        }

        [Test]
        public void Manten_NullInput_EveryPendulumFallsBeforeDuration_Sweep()
        {
            // GDD §4 MECH_01 AC1 (the dual, non-vacuity pin): "sin input -> todos
            // caen antes de agotar el tiempo." Proves the survival sweep above is
            // non-trivial — the mechanic really is lose-by-default, so the oracle's
            // survival is earned, not free.
            int durationTicks = (int)MathF.Round(MantenParams.GddDefaults.DurationSeconds * 60f);

            var failures = new List<string>();
            for (int seed = 0; seed < SweepSeeds; seed++)
            {
                var mg = new V2Manten(MantenParams.GddDefaults);
                mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                var inputs = new FakeInputs(); // every seat defaults to Direction8.None
                int t = 0;
                while (!mg.IsFinished && t < durationTicks + 60) { mg.Tick(inputs.Build(t)); t++; }

                foreach (PlayerSlot slot in AllSlots)
                {
                    if (!mg.HasFallen(slot))
                        failures.Add($"seed {seed}, seat {slot}: null input did NOT fall — mechanic is not lose-by-default here");
                    else if (mg.GetFallenTick(slot) >= durationTicks)
                        failures.Add($"seed {seed}, seat {slot}: null input fell at tick {mg.GetFallenTick(slot)} " +
                                     $">= duration {durationTicks} — must fall strictly before time runs out");
                }
            }

            ReportAndAssert("MANTÉN null-input forced-fall (non-vacuity)", SweepSeeds * AllSlots.Length, failures);
        }

        // ── CORRE (MECH_03) — perfect jumps finish unstunned; rubber-band fair ───

        [Test]
        public void Corre_PerfectRunner_FinishesUnstunned_Sweep()
        {
            // GDD §4 MECH_03 AC2: "6 Hz constante + saltos perfectos termina sin
            // aturdimiento" — the track is fair (every obstacle jumpable) by
            // construction across ≥1000 seeds, not tuned for one lucky layout.
            var failures = new List<string>();
            for (int seed = 0; seed < SweepSeeds; seed++)
            {
                var mg = new V2Corre(CorreParams.GddDefaults);
                mg.Initialize(new SeededRandom(seed), PlayerRoster.AllHuman, 1f);

                var bot = new CorrePerfectRunner();
                var inputs = new FakeInputs();
                var everStunned = new bool[4];
                int t = 0;
                while (!mg.IsFinished && t < 3000)
                {
                    foreach (PlayerSlot slot in AllSlots) bot.Drive(mg, inputs, slot, t);
                    mg.Tick(inputs.Build(t));
                    for (int k = 0; k < 4; k++)
                        if (mg.IsStunned(AllSlots[k])) everStunned[k] = true;
                    t++;
                }

                if (!mg.IsFinished)
                    failures.Add($"seed {seed}: round did not finish within the tick bound (possible non-termination)");

                foreach (PlayerSlot slot in AllSlots)
                    if (everStunned[(int)slot])
                        failures.Add($"seed {seed}, seat {slot}: a 6 Hz masher with perfect jumps was STUNNED — " +
                                     $"an unjumpable obstacle violates track fairness");
            }

            ReportAndAssert("CORRE perfect-jump finishes unstunned", SweepSeeds * AllSlots.Length, failures);
        }

        [Test]
        public void Corre_RubberBand_NeverInvertsTwoIdenticalMashPlayers_Sweep()
        {
            // GDD §4 MECH_03 AC3: "rubber-band nunca invierte por sí solo un
            // resultado entre dos jugadores con mash idéntico (test estadístico
            // sobre 1 000 semillas)." Rojo and Azul receive byte-identical input
            // every tick and never jump — they must finish EXACTLY tied and share a
            // place. Any asymmetric last-place boost would break the tie.
            var roster = new PlayerRoster(new[] { SeatState.Human, SeatState.Human, SeatState.Empty, SeatState.Empty });

            var failures = new List<string>();
            for (int seed = 0; seed < SweepSeeds; seed++)
            {
                var mg = new V2Corre(CorreParams.GddDefaults);
                mg.Initialize(new SeededRandom(seed), roster, 1f);

                var inputs = new FakeInputs();
                int t = 0;
                while (!mg.IsFinished && t < 3000)
                {
                    bool button = Mash.Button(t, 6);
                    inputs.Set(PlayerSlot.Rojo, Direction8.None, button);
                    inputs.Set(PlayerSlot.Azul, Direction8.None, button);
                    mg.Tick(inputs.Build(t));
                    t++;
                }

                float dRojo = mg.GetDistance(PlayerSlot.Rojo);
                float dAzul = mg.GetDistance(PlayerSlot.Azul);
                if (dRojo != dAzul)
                {
                    failures.Add($"seed {seed}: identical-mash distances diverged (Rojo={dRojo}, Azul={dAzul}) — " +
                                 $"rubber-band inverted an identical pair");
                    continue;
                }

                V2Result result = mg.GetResult();
                var bySeat = new Dictionary<int, PlayerRank>();
                foreach (PlayerRank r in result.Ranks) bySeat[r.Seat] = r;
                if (bySeat[(int)PlayerSlot.Rojo].Place != bySeat[(int)PlayerSlot.Azul].Place)
                    failures.Add($"seed {seed}: identical-mash players got different places " +
                                 $"(Rojo={bySeat[(int)PlayerSlot.Rojo].Place}, Azul={bySeat[(int)PlayerSlot.Azul].Place})");
            }

            ReportAndAssert("CORRE rubber-band non-inversion (identical mash stays tied)", SweepSeeds, failures);
        }

        // ── Surfacing helper (AC3) ────────────────────────────────────────────────

        /// <summary>
        /// Logs the sweep tally and every failing seed (so a human can reproduce
        /// each one), then fails the test if any property was violated. Surfacing,
        /// not hiding: the failure carries the full count and the first failing
        /// seeds inline; the complete list is written to the test output above.
        /// </summary>
        private static void ReportAndAssert(string label, int totalRuns, List<string> failures)
        {
            TestContext.Progress.WriteLine(
                $"[SLOW-SWEEP] {label}: {totalRuns} runs, {failures.Count} property violation(s).");

            if (failures.Count > 0)
            {
                TestContext.Progress.WriteLine($"[SLOW-SWEEP] FAILING SEEDS for {label} (surfaced for design review):");
                foreach (string f in failures)
                    TestContext.Progress.WriteLine("    " + f);
            }

            int preview = Math.Min(failures.Count, 25);
            string head = preview > 0
                ? "\nFirst failing seeds:\n" + string.Join("\n", failures.GetRange(0, preview))
                : string.Empty;

            Assert.That(failures, Is.Empty,
                $"{label}: {failures.Count}/{totalRuns} runs violated the GDD fairness property. " +
                $"Every failing seed was logged above for reproduction.{head}");
        }
    }
}
