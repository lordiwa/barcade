using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Barcade.Core;
using Barcade.Framework;

namespace Barcade.Framework.Tests
{
    /// <summary>
    /// PlayMode integration tests for the full round loop:
    ///   Intermission -> CommandShow -> Play -> Result -> (next round).
    ///
    /// Construction pattern (mirrors TASK-006 ArcadeInputBridge approach):
    ///   1. Create parent GO INACTIVE.
    ///   2. AddComponent on inactive GO — Awake deferred, Start deferred.
    ///   3. Inject all dependencies via InjectForTest().
    ///   4. SetActive(true) — Awake + Start fire with injected state in place.
    ///   5. Yield frames; assert against the INJECTED director's ScoreModel.
    ///
    /// Acceptance criteria verified:
    ///   AC1: Microgame actually runs — MicrogameHost.IsComplete becomes true.
    ///   AC2: Real MicrogameResult reaches the injected ScoreModel (GamesPlayed++).
    ///   AC3: Result window honoured — round does NOT advance before resultDuration.
    ///   AC4: CommandDisplayView active during CommandShow; hidden during Intermission.
    ///   AC5: IntermissionView active during Intermission; hidden during CommandShow.
    /// </summary>
    [TestFixture]
    public class MicrogameLoopIntegrationTests
    {
        // ── Roots destroyed in TearDown ───────────────────────────────────────────

        private readonly List<GameObject> _roots = new List<GameObject>();

        // ── Stub inputs ───────────────────────────────────────────────────────────

        private sealed class StubInputs : IReadOnlyPlayerInputs
        {
            private static readonly InputSnapshot Zero =
                new InputSnapshot(0f, 0f, ButtonState.Released);
            public InputSnapshot For(PlayerSlot slot) => Zero;
        }

        // ── Test registry ─────────────────────────────────────────────────────────

        /// <summary>
        /// Registry with one entry: EsquivaMicrogame whose single hazard is forced
        /// 100 units away so all 4 players always survive (win = true, deterministic).
        /// </summary>
        private static MicrogameRegistry BuildTestRegistry()
        {
            var reg = new MicrogameRegistry();
            reg.Register("esquiva", new MicrogameEntry(
                createLogic: () =>
                {
                    var g = new EsquivaMicrogame(
                        hazardCount:        1,
                        hazardSpeed:        0f,
                        avatarRadius:       0.5f,
                        hazardRadius:       0.5f,
                        avatarSpeed:        5f,
                        playAreaHalfExtent: 8f);
                    g.SetForcedHazardPositions(new[] { (100f, 100f) });
                    return g;
                },
                attachView: (game, players, root) =>
                {
                    var view = root.AddComponent<EsquivaView>();
                    view.Bind((EsquivaMicrogame)game, players);
                }
            ));
            return reg;
        }

        // ── RampSettings that won't clamp small play durations ────────────────────

        private static RampSettings NoClampRamp() =>
            new RampSettings(decayPerRound: 0.95f, minDuration: 0.001f);

        // ── Factory: build fully wired host (inactive until caller activates) ─────

        /// <summary>
        /// Creates a MicrogameHost on its own INACTIVE GO, injects the test registry,
        /// then activates it. Host.Awake sets the default registry, but SetRegistry
        /// overwrites it. Host is returned ready but only starts ticking once its
        /// parent GO has been activated (controlled by the caller).
        /// </summary>
        private MicrogameHost BuildHost(Transform parent)
        {
            // Keep host inactive while we configure it.
            var hostGO = new GameObject("TestHost");
            hostGO.SetActive(false);
            hostGO.transform.SetParent(parent);
            _roots.Add(hostGO);

            var host = hostGO.AddComponent<MicrogameHost>();
            // SetRegistry must be called before Awake (which fires on SetActive).
            // Because hostGO is INACTIVE, AddComponent defers Awake, so we can
            // safely call SetRegistry here before Awake runs.
            host.SetRegistry(BuildTestRegistry());

            // Activate host independently so its own Update() runs.
            hostGO.SetActive(true);
            return host;
        }

        // ── Factory: build wired controller (inactive until caller activates) ─────

        /// <summary>
        /// Creates the parent root GO INACTIVE, adds controller + host children,
        /// injects all dependencies, then returns them. The caller must call
        /// rootGO.SetActive(true) to start the loop.
        /// </summary>
        private (GameObject rootGO, MicrogameLoopController controller, MicrogameHost host)
            BuildControllerInactive(
                SequencerDirector  director,
                IReadOnlyPlayerInputs inputs,
                float intermission,
                float commandShow,
                float result,
                CommandDisplayView commandDisplay   = null,
                IntermissionView   intermissionView = null)
        {
            // Root is INACTIVE — everything added here defers Awake/Start.
            var rootGO = new GameObject("TestRoot");
            rootGO.SetActive(false);
            _roots.Add(rootGO);

            // Host child — also inactive because parent is inactive.
            var hostGO = new GameObject("TestHost");
            hostGO.transform.SetParent(rootGO.transform);
            var host = hostGO.AddComponent<MicrogameHost>();
            // Awake has NOT fired yet (parent inactive), so we inject registry now.
            host.SetRegistry(BuildTestRegistry());

            // Controller child.
            var controller = rootGO.AddComponent<MicrogameLoopController>();

            // Inject ALL dependencies before activation.
            controller.InjectForTest(
                director:        director,
                inputs:          inputs,
                host:            host,
                intermission:    intermission,
                commandShow:     commandShow,
                result:          result,
                commandDisplay:  commandDisplay,
                intermissionView: intermissionView);

            return (rootGO, controller, host);
        }

        // ── Teardown ──────────────────────────────────────────────────────────────

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _roots)
                if (go != null) Object.Destroy(go);
            _roots.Clear();
        }

        // ── AC1 + AC2: Microgame runs and real result reaches injected ScoreModel ──

        /// <summary>
        /// Drives one full round and asserts that the INJECTED director's ScoreModel
        /// has GamesPlayed >= 1 (real MicrogameResult fed via AdvanceRound).
        /// </summary>
        [UnityTest]
        public IEnumerator FullRound_MicrogameRuns_AndResultReachesInjectedScoreModel()
        {
            const float play = 0.1f;

            var director = new SequencerDirector(
                new List<MicrogameDescriptor>
                {
                    new MicrogameDescriptor("esquiva", play, 1)
                },
                new SeededRandom(42),
                NoClampRamp());

            var (rootGO, controller, host) = BuildControllerInactive(
                director:    director,
                inputs:      new StubInputs(),
                intermission: 0.05f,
                commandShow:  0.05f,
                result:       0.05f);

            // Activate — Start() runs with injected director in place.
            rootGO.SetActive(true);

            // Wait for one full round: 0.05 + 0.05 + 0.1 + 0.05 = 0.25s; allow 2s.
            yield return WaitForSeconds(2.0f);

            Assert.That(director.Score.GetGamesPlayed(PlayerSlot.Rojo),
                Is.GreaterThanOrEqualTo(1),
                "Injected director's ScoreModel must record at least one round.");

            // Verify it was a win (hazard forced far away — all players survive).
            Assert.That(director.Score.GetWins(PlayerSlot.Rojo),
                Is.GreaterThanOrEqualTo(1),
                "With hazard at (100,100), Rojo must have won at least one round.");
        }

        // ── AC3: Result window is honoured ────────────────────────────────────────

        /// <summary>
        /// TASK-007 regression: round must NOT advance before resultDuration elapses.
        ///
        /// With intermission=0, commandShow=0, play=0.05s, result=0.5s:
        ///   - At 0.15s: we are in the Result phase but the window has not elapsed
        ///     (0.1s of result elapsed < 0.5s window). GamesPlayed must be 0.
        ///   - At 0.80s: window has elapsed. GamesPlayed must be >= 1.
        /// </summary>
        [UnityTest]
        public IEnumerator ResultWindow_IsHonoured_RoundDoesNotAdvanceEarly()
        {
            const float play   = 0.05f;
            const float result = 0.5f;

            var director = new SequencerDirector(
                new List<MicrogameDescriptor>
                {
                    new MicrogameDescriptor("esquiva", play, 1)
                },
                new SeededRandom(42),
                NoClampRamp());

            var (rootGO, controller, host) = BuildControllerInactive(
                director:    director,
                inputs:      new StubInputs(),
                intermission: 0f,
                commandShow:  0f,
                result:       result);

            rootGO.SetActive(true);

            // After play ends (0.05s) we're in Result. Wait until 0.15s —
            // well inside the 0.5s result window.
            yield return WaitForSeconds(0.15f);

            int earlyGames = director.Score.GetGamesPlayed(PlayerSlot.Rojo);
            Assert.That(earlyGames, Is.EqualTo(0),
                $"GamesPlayed={earlyGames} at 0.15s — round must NOT advance before " +
                $"resultDuration={result}s elapses (TASK-007 regression).");

            // Now wait for the window to fully expire.
            yield return WaitForSeconds(result + 0.2f);

            int lateGames = director.Score.GetGamesPlayed(PlayerSlot.Rojo);
            Assert.That(lateGames, Is.GreaterThanOrEqualTo(1),
                "Round MUST have advanced after the result window elapsed.");
        }

        // ── AC1: MicrogameHost.IsComplete after play duration ─────────────────────

        /// <summary>
        /// Verifies MicrogameHost.IsComplete and LastResult are populated after
        /// the play duration elapses, confirming the microgame logic actually ran.
        /// </summary>
        [UnityTest]
        public IEnumerator MicrogameHost_BecomesComplete_AfterPlayDurationElapses()
        {
            const float play = 0.1f;

            var director = new SequencerDirector(
                new List<MicrogameDescriptor>
                {
                    new MicrogameDescriptor("esquiva", play, 1)
                },
                new SeededRandom(42),
                NoClampRamp());

            var (rootGO, controller, host) = BuildControllerInactive(
                director:    director,
                inputs:      new StubInputs(),
                intermission: 0f,
                commandShow:  0f,
                result:       2.0f);  // long result window so host result isn't reset

            rootGO.SetActive(true);

            // With intermission=0 and commandShow=0, Play begins on the first Tick.
            // Wait play + margin.
            yield return WaitForSeconds(play + 0.15f);

            Assert.That(host.IsComplete, Is.True,
                "MicrogameHost.IsComplete must be true after play duration elapses.");
            Assert.That(host.LastResult, Is.Not.Null,
                "MicrogameHost.LastResult must be non-null after microgame finishes.");

            // Bonus: all players survived (hazard at 100,100).
            Assert.That(host.LastResult.IsWin(PlayerSlot.Rojo),   Is.True, "Rojo must win (no hazard).");
            Assert.That(host.LastResult.IsWin(PlayerSlot.Azul),   Is.True, "Azul must win.");
            Assert.That(host.LastResult.IsWin(PlayerSlot.Amarillo), Is.True, "Amarillo must win.");
            Assert.That(host.LastResult.IsWin(PlayerSlot.Verde),  Is.True, "Verde must win.");
        }

        // ── AC4/AC5: View toggles across phase transitions ────────────────────────

        /// <summary>
        /// Verifies CommandDisplayView and IntermissionView toggle correctly:
        ///   - During Intermission: IntermissionView ON, CommandDisplay OFF.
        ///   - During CommandShow:  CommandDisplay ON,  IntermissionView OFF.
        ///
        /// Both views are injected before activation so Start() wires them.
        /// </summary>
        [UnityTest]
        public IEnumerator ViewToggles_CorrectAcrossPhaseTransitions()
        {
            const float intermissionDur = 0.15f;
            const float commandShowDur  = 0.15f;

            // Create views on separate active GOs so their Awake runs (sets inactive).
            var cmdDisplayGO = new GameObject("CmdDisplay");
            _roots.Add(cmdDisplayGO);
            var cmdDisplay = cmdDisplayGO.AddComponent<CommandDisplayView>();

            var intermissionGO = new GameObject("Intermission");
            _roots.Add(intermissionGO);
            var intermissionV = intermissionGO.AddComponent<IntermissionView>();

            var director = new SequencerDirector(
                new List<MicrogameDescriptor>
                {
                    new MicrogameDescriptor("esquiva", 0.05f, 1)
                },
                new SeededRandom(42),
                NoClampRamp());

            var (rootGO, controller, host) = BuildControllerInactive(
                director:        director,
                inputs:          new StubInputs(),
                intermission:    intermissionDur,
                commandShow:     commandShowDur,
                result:          0.05f,
                commandDisplay:  cmdDisplay,
                intermissionView: intermissionV);

            // Activate — Start() runs, calls BeginNextRound() -> ApplyPhaseViews(Intermission).
            rootGO.SetActive(true);

            // Yield one frame so Start() + first Update() have executed.
            yield return null;

            // ── Intermission phase ─────────────────────────────────────────────────
            Assert.That(intermissionV.gameObject.activeSelf, Is.True,
                "IntermissionView must be active during Intermission phase.");
            Assert.That(cmdDisplay.gameObject.activeSelf, Is.False,
                "CommandDisplayView must be hidden during Intermission phase.");

            // Skip past Intermission into CommandShow.
            yield return WaitForSeconds(intermissionDur + 0.05f);

            // ── CommandShow phase ──────────────────────────────────────────────────
            Assert.That(cmdDisplay.gameObject.activeSelf, Is.True,
                "CommandDisplayView must be active during CommandShow phase.");
            Assert.That(intermissionV.gameObject.activeSelf, Is.False,
                "IntermissionView must be hidden during CommandShow phase.");
        }

        // ── Helper ────────────────────────────────────────────────────────────────

        private static IEnumerator WaitForSeconds(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }
}
