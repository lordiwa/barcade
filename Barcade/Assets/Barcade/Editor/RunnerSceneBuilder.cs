using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Barcade.Framework;

namespace Barcade.EditorTools
{
    /// <summary>
    /// Editor-only tool that creates or overwrites Assets/Barcade/Scenes/EndlessRun.unity,
    /// wiring up:
    ///   - Perspective camera side-angled so the runner travels left-to-right and depth
    ///     (the 3 Z-offset lanes) reads clearly in perspective.
    ///   - Directional light.
    ///   - One empty GameObject carrying <see cref="RunnerBootstrap"/>.
    ///
    /// Idempotent: re-running overwrites the scene cleanly.
    ///
    /// Invoke headless via:
    ///   -executeMethod Barcade.EditorTools.RunnerSceneBuilder.BuildEndlessRun
    ///
    /// Lives in Barcade.Editor (Editor-only assembly).
    /// </summary>
    public static class RunnerSceneBuilder
    {
        private const string SceneFolder = "Assets/Barcade/Scenes";
        private const string ScenePath   = "Assets/Barcade/Scenes/EndlessRun.unity";

        // ── Entry point ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates or overwrites EndlessRun.unity with the wired standalone endless-runner demo.
        /// Invoked headless via -executeMethod Barcade.EditorTools.RunnerSceneBuilder.BuildEndlessRun.
        /// </summary>
        [MenuItem("Barcade/Build Endless Run Scene")]
        public static void BuildEndlessRun()
        {
            Debug.Log("[RunnerSceneBuilder] Starting BuildEndlessRun...");

            EnsureSceneFolder();

            // Create a fresh empty scene (discards any existing runtime state cleanly).
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── Camera ───────────────────────────────────────────────────────────
            // Fixed side-view 2.5D camera — does NOT move during play.
            // Rendering model: runner is pinned at runnerAnchorX=0; obstacles/coins
            // scroll right-to-left toward it.  cam.X = runnerAnchorX + cameraXLead.
            //
            // Camera defaults (mirror RunnerBootstrap serialized fields exactly):
            //   runnerAnchorX=  0   (fixed world-X where runner is pinned)
            //   cameraXLead =  5   (camera right of anchor; runner appears left-of-centre)
            //   cameraY     =  6   (height above track)
            //   cameraZ     = -18  (pulled back for depth + reaction room)
            //   cameraPitch =  20  (slight downward tilt so track reads)
            //   cameraFov   =  60
            //   laneZSpacing=  2.0
            //
            // Runner at anchor X=0; cam.X=5; half-width≈11.6 → runner at ~28% from left.
            // Spawns visible from runnerAnchorX+0 to runnerAnchorX+spawnViewDistance (40).
            const float kFov        = 60f;
            const float kCameraX    = 0f  + 5f;   // runnerAnchorX + cameraXLead; fixed for whole run
            const float kCameraY    = 6f;
            const float kCameraPitch= 20f;
            const float kLaneCentreZ= 0f + 2.0f * 1f; // lane0Z + laneZSpacing * (laneCount-1)*0.5f = 0 + 2*1 = 2
            const float kCameraZ    = -18f + kLaneCentreZ;

            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            cameraGO.transform.rotation = Quaternion.Euler(kCameraPitch, 0f, 0f);
            cameraGO.transform.position = new Vector3(kCameraX, kCameraY, kCameraZ);

            var cam = cameraGO.AddComponent<Camera>();
            cam.orthographic    = false;
            cam.fieldOfView     = kFov;
            cam.nearClipPlane   = 0.1f;
            cam.farClipPlane    = 200f;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.08f, 0.15f);
            cameraGO.AddComponent<AudioListener>();

            // ── Directional light ─────────────────────────────────────────────────
            var lightGO = new GameObject("Directional Light");
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1.2f;
            light.color     = Color.white;

            // ── RunnerBootstrap ────────────────────────────────────────────────────
            var bootstrapGO = new GameObject("RunnerBootstrap");
            bootstrapGO.AddComponent<RunnerBootstrap>();

            Debug.Log("[RunnerSceneBuilder] RunnerBootstrap added.");

            // ── Save scene ────────────────────────────────────────────────────────
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            // ── Add to build settings ─────────────────────────────────────────────
            AddSceneToBuildSettings(ScenePath);

            Debug.Log("[RunnerSceneBuilder] EndlessRun.unity built and saved at " + ScenePath);

            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static void EnsureSceneFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Barcade"))
                AssetDatabase.CreateFolder("Assets", "Barcade");

            if (!AssetDatabase.IsValidFolder(SceneFolder))
                AssetDatabase.CreateFolder("Assets/Barcade", "Scenes");
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);

            // Remove any existing entry for this path (idempotent).
            scenes.RemoveAll(s => s.path == scenePath);

            // Append after existing scenes (standalone demo, not the boot scene).
            scenes.Add(new EditorBuildSettingsScene(scenePath, enabled: true));

            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[RunnerSceneBuilder] Added " + scenePath + " to EditorBuildSettings.");
        }
    }
}
