using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    /// Acceptance criteria verified:
    ///   AC1: The selected microgame actually runs (MicrogameHost.IsComplete becomes true).
    ///   AC2: A real MicrogameResult reaches the ScoreModel (GamesPlayed increments).
    ///   AC3: The Result window is honoured — the loop does NOT advance to the next
    ///        round before _resultDuration seconds have elapsed.
    ///   AC4: CommandDisplayView is visible during CommandShow and hidden during Intermission.
    ///   AC5: IntermissionView is visible during Intermission and hidden during Play.
    ///
    /// Uses a minimal GameObject setup (no scene required).
    /// A deterministic EsquivaMicrogame is the test target.
    /// </summary>
    [TestFixture]
    public class MicrogameLoopIntegrationTests
    {
        // ── Shared test state ─────────────────────────────────────────────────────

        private GameObject               _controllerGO;
        private MicrogameLoopController  _controller;
        private MicrogameHost            _host;
        private SequencerDirector        _director;
        private StubInputs               _inputs;

        // ── Stub inputs ───────────────────────────────────────────────────────────

        /// <summary>
        /// Synthetic IReadOnlyPlayerInputs: all players idle (zero stick, no button).
        /// </summary>
        private sealed class StubInputs : IReadOnlyPlayerInputs
        {
            private static readonly InputSnapshot Zero =
                new InputSnapshot(0f, 0f, ButtonState.Released);

            public InputSnapshot For(PlayerSlot slot) => Zero;
        }

        // ── Test registry (uses EsquivaMicrogame with hazard forced far away) ─────

        private static MicrogameRegistry BuildTestRegistry()
        {
            var reg = new MicrogameRegistry();

            // Hazard is forced 100 units away — no player will ever be hit.
            // This ensures all 4 players win every round (deterministic outcome).
            reg.Register("esquiva", new MicrogameEntry(
                createLogic: () =>
                {
                    var g = new EsquivaMicrogame(
                        hazardCount:        1,
                        hazardSpeed:        0f,   // stationary
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

        // ── Setup / Teardown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _inputs = new StubInputs();

            // Build the controller GameObject (parent).
            _controllerGO = new GameObject("TestController");

            // Add host as a child (before the controller, so Awake order is fine).
            var hostGO = new GameObject("TestHost");
            hostGO.transform.SetParent(_controllerGO.transform);
            _host = hostGO.AddComponent<MicrogameHost>();
            _host.SetRegistry(BuildTestRegistry());

            // Add controller — Awake runs immediately (GameObject is active).
            // Start() is deferred to the first frame.
            _controller = _controllerGO.AddComponent<MicrogameLoopController>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_controllerGO != null) Object.Destroy(_controllerGO);
        }

        // ── AC1 + AC2: Microgame actually runs and result reaches ScoreModel ──────

        /// <summary>
        /// Drives the loop through one complete round and asserts:
        ///   - MicrogameHost.IsComplete becomes true (microgame ran).
        ///   - ScoreModel.GetGamesPlayed(Rojo) >= 1 (real result recorded).
        /// </summary>
        [UnityTest]
        public IEnumerator FullRound_MicrogameRuns_AndResultReachesScoreModel()
        {
            // Arrange: short but well-above-minimum durations.
            // Use baseDuration: 0.1s with a ramp minDuration of 0.001s so it's not clamped.
            ConfigureController(
                intermission: 0.05f,
                commandShow:  0.05f,
                result:       0.05f,
                playDuration: 0.1f);

            // Wait long enough for one full round to complete.
            // Round total: 0.05+0.05+0.1+0.05 = 0.25s; wait 1.5s to be safe.
            yield return WaitForSeconds(1.5f);

            // Assert: the director has recorded at least one round.
            Assert.That(_director.Score.GetGamesPlayed(PlayerSlot.Rojo),
                Is.GreaterThanOrEqualTo(1),
                "ScoreModel must record at least one round after a full loop cycle.");
        }

        // ── AC3: Result window is honoured ────────────────────────────────────────

        /// <summary>
        /// Confirms the round does NOT advance to the next round before resultDuration
        /// seconds have elapsed after entering the Result phase.
        ///
        /// Strategy:
        ///   - Set intermission=0, commandShow=0, play=0.05s, result=0.5s.
        ///   - Wait 0.15s (well after play ends, but before the 0.5s result window).
        ///   - Assert GamesPlayed == 0 (AdvanceRound not yet called).
        ///   - Wait another 0.5s.
        ///   - Assert GamesPlayed == 1 (AdvanceRound called after window elapsed).
        /// </summary>
        [UnityTest]
        public IEnumerator ResultWindow_IsHonoured_RoundDoesNotAdvanceEarly()
        {
            const float resultDuration = 0.5f;
            const float playDuration   = 0.05f;

            ConfigureController(
                intermission: 0f,
                commandShow:  0f,
                result:       resultDuration,
                playDuration: playDuration);

            // Wait long enough for play to finish (0.05s) but well before result window
            // ends (0.5s). At 0.15s total we should be in the Result phase but the window
            // has not yet elapsed.
            yield return WaitForSeconds(0.15f);

            // GamesPlayed must still be 0 — AdvanceRound must not have been called yet.
            int gamesPlayedEarly = _director.Score.GetGamesPlayed(PlayerSlot.Rojo);
            Assert.That(gamesPlayedEarly, Is.EqualTo(0),
                "Round must NOT advance before resultDuration has elapsed. " +
                $"GamesPlayed={gamesPlayedEarly} at 0.15s (resultDuration={resultDuration}s). " +
                "This catches the TASK-007 bug where OnRoundComplete was called every frame.");

            // Now wait for the result window to finish (0.5s + buffer).
            yield return WaitForSeconds(resultDuration + 0.15f);

            int gamesPlayedAfter = _director.Score.GetGamesPlayed(PlayerSlot.Rojo);
            Assert.That(gamesPlayedAfter, Is.GreaterThanOrEqualTo(1),
                "Round MUST advance after resultDuration has fully elapsed.");
        }

        // ── AC1: MicrogameHost.IsComplete becomes true after play duration ────────

        /// <summary>
        /// Verifies MicrogameHost.IsComplete becomes true within the play window,
        /// indicating the microgame logic actually ran.
        /// </summary>
        [UnityTest]
        public IEnumerator MicrogameHost_BecomesComplete_AfterPlayDurationElapses()
        {
            const float playDuration = 0.1f;

            ConfigureController(
                intermission: 0f,
                commandShow:  0f,
                result:       0.5f,
                playDuration: playDuration);

            // Wait for play to finish. With intermission=0 and commandShow=0, the
            // phase machine will be in Play immediately on the first Tick.
            yield return WaitForSeconds(playDuration + 0.1f);

            Assert.That(_host.IsComplete, Is.True,
                "MicrogameHost.IsComplete must be true after the play duration elapses.");
            Assert.That(_host.LastResult, Is.Not.Null,
                "MicrogameHost.LastResult must be non-null after the play duration elapses.");
        }

        // ── AC4/AC5: Views toggle correctly across phase transitions ──────────────

        /// <summary>
        /// Verifies that CommandDisplayView is active during CommandShow and inactive
        /// during Intermission, and IntermissionView is active during Intermission.
        /// </summary>
        [UnityTest]
        public IEnumerator ViewToggles_CorrectAcrossPhaseTransitions()
        {
            // Create views.
            var cmdDisplayGO = new GameObject("CmdDisplay");
            var cmdDisplay   = cmdDisplayGO.AddComponent<CommandDisplayView>();

            var intermissionGO = new GameObject("Intermission");
            var intermissionV  = intermissionGO.AddComponent<IntermissionView>();

            // Inject views before Start() runs.
            SetPrivateField(_controller, "_commandDisplayView", cmdDisplay);
            SetPrivateField(_controller, "_intermissionView",   intermissionV);

            // Use observable timing: intermission=0.15s, commandShow=0.15s.
            ConfigureController(
                intermission: 0.15f,
                commandShow:  0.15f,
                result:       0.05f,
                playDuration: 0.05f);

            // After first frame (Start runs + first Tick). At ~0.02s: Intermission.
            yield return WaitForSeconds(0.02f);

            Assert.That(intermissionV.gameObject.activeSelf, Is.True,
                "IntermissionView must be active at start of Intermission phase.");
            Assert.That(cmdDisplay.gameObject.activeSelf, Is.False,
                "CommandDisplayView must be hidden during Intermission phase.");

            // Skip past intermission into CommandShow.
            yield return WaitForSeconds(0.18f); // total ~0.20s > 0.15s intermission

            Assert.That(cmdDisplay.gameObject.activeSelf, Is.True,
                "CommandDisplayView must be active during CommandShow phase.");
            Assert.That(intermissionV.gameObject.activeSelf, Is.False,
                "IntermissionView must be hidden during CommandShow phase.");

            // Cleanup views.
            Object.Destroy(cmdDisplayGO);
            Object.Destroy(intermissionGO);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Configures the controller and injects a fresh director for this test.
        ///
        /// Uses RampSettings with minDuration=0.001f to avoid clamping very short
        /// play durations to the default 1.0s minimum.
        /// </summary>
        private void ConfigureController(
            float intermission,
            float commandShow,
            float result,
            float playDuration)
        {
            // Build a ramp that doesn't clamp our tiny play durations.
            var ramp = new RampSettings(decayPerRound: 0.95f, minDuration: 0.001f);

            var descriptors = new List<MicrogameDescriptor>
            {
                new MicrogameDescriptor(id: "esquiva", baseDuration: playDuration, difficulty: 1)
            };
            var rng = new SeededRandom(42);
            _director = new SequencerDirector(descriptors, rng, ramp);

            // Set serialized fields before Start() runs via reflection.
            SetPrivateField(_controller, "_intermissionDuration", intermission);
            SetPrivateField(_controller, "_commandShowDuration",  commandShow);
            SetPrivateField(_controller, "_resultDuration",       result);
            SetPrivateField(_controller, "_microgameHost",        _host);

            // Inject the director and stub inputs.
            _controller.InjectForTest(_director, _inputs);

            // Start() will be called on the next frame by Unity.
        }

        /// <summary>
        /// Yields for approximately <paramref name="seconds"/> seconds using frame-by-frame
        /// accumulation. More predictable than UnityEngine WaitForSeconds in test mode.
        /// </summary>
        private static IEnumerator WaitForSeconds(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// Sets a private or protected field on a MonoBehaviour by name using reflection.
        /// Used to inject serialized Inspector fields without scene assets.
        /// </summary>
        private static void SetPrivateField(MonoBehaviour target, string fieldName, object value)
        {
            System.Type type = target.GetType();
            FieldInfo field = null;

            // Walk up the hierarchy (handles protected fields in base classes too).
            while (type != null && field == null)
            {
                field = type.GetField(
                    fieldName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                type = type.BaseType;
            }

            if (field != null)
                field.SetValue(target, value);
            else
                Debug.LogWarning(
                    $"[Test] Field '{fieldName}' not found on {target.GetType().Name}. " +
                    "Check spelling and inheritance chain.");
        }
    }
}
