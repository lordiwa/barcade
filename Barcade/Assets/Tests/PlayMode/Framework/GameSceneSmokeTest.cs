using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Barcade.Framework.Tests
{
    /// <summary>
    /// PlayMode smoke test that loads the generated Game.unity scene, ticks for
    /// ~120 frames, and asserts that no Exception or Error was logged.
    ///
    /// This catches runtime wiring bugs (e.g. camera at z=0, null ref in Start/Awake,
    /// missing serialized refs) that the existing in-code-wiring tests cannot exercise
    /// because they never load the generated scene file.
    ///
    /// Requirement: Game.unity must already be in EditorBuildSettings (added by
    /// SceneBuilder.BuildGameScene) and must have been rebuilt after SceneBuilder fixes.
    ///
    /// If Game.unity is absent from build settings the test is skipped with a
    /// clear message rather than failing with an obscure LoadScene error.
    /// </summary>
    [TestFixture]
    public class GameSceneSmokeTest
    {
        private const string SceneName = "Game";
        private const int    TickCount = 120;   // ~2 s at 60 fps

        private readonly List<string> _errors     = new List<string>();
        private readonly List<string> _exceptions = new List<string>();

        // ── Setup / teardown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _errors.Clear();
            _exceptions.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
        }

        // ── Test ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads Game.unity, runs for ~120 frames (≈2s at 60fps), and asserts that
        /// no LogType.Error or LogType.Exception messages were emitted.
        ///
        /// A LogError from ArcadeInputBridge about a missing _actionsAsset is
        /// expected on the CI machine (no InputActionAsset baked into CI build)
        /// and is explicitly allowed — everything else fails the test.
        /// </summary>
        [UnityTest]
        public IEnumerator GameScene_Loads_AndRunsWithoutExceptions()
        {
            // Guard: skip if Game scene is not in build settings.
            bool found = false;
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByIndex(i);
                if (path.Contains(SceneName))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Assert.Ignore(
                    "Game.unity is not in EditorBuildSettings — " +
                    "run Barcade.EditorTools.SceneBuilder.BuildGameScene first, then re-run.");
                yield break;
            }

            // Load the scene additively so we don't lose the test runner.
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);

            // Tick for ~120 frames.
            for (int i = 0; i < TickCount; i++)
                yield return null;

            // Unload before asserting so any OnDestroy exceptions are also captured.
            yield return SceneManager.UnloadSceneAsync(SceneName);

            // Filter out the known-acceptable ArcadeInputBridge warning about
            // _actionsAsset being unassigned in CI / test environments.
            var unexpectedErrors = new List<string>();
            foreach (string msg in _errors)
            {
                if (msg.Contains("_actionsAsset is not assigned"))
                    continue;   // expected in CI — no InputActionAsset present
                unexpectedErrors.Add(msg);
            }

            Assert.That(_exceptions, Is.Empty,
                "No exceptions should be logged during a 120-frame run of Game.unity.\n" +
                string.Join("\n", _exceptions));

            Assert.That(unexpectedErrors, Is.Empty,
                "No unexpected errors should be logged during a 120-frame run of Game.unity.\n" +
                string.Join("\n", unexpectedErrors));
        }

        // ── Log capture ───────────────────────────────────────────────────────────

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Exception)
                _exceptions.Add(message + "\n" + stackTrace);
            else if (type == LogType.Error)
                _errors.Add(message);
        }
    }
}
