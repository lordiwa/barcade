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
            // Side-view 2.5D: camera sits to the side-and-below the track, pitching
            // slightly downward so the ground plane and all 3 lanes are visible.
            // Matches RunnerBootstrap inspector defaults so edit-mode preview and play
            // mode start from the same view; the Bootstrap moves the X each frame.
            //
            // Camera defaults (mirroring RunnerBootstrap serialized fields):
            //   cameraXLead = -5   (units behind runner's X)
            //   cameraY     =  4   (height above track)
            //   cameraZ     = -8   (in front of near lane — side of the Z axis)
            //   cameraPitch =  20  (look slightly down toward track)
            //   cameraFov   =  60
            //
            // At start runner is at X=0, lane 1 (middle), lane0Z=0, laneZSpacing=1.5
            // → middle lane Z = 1.5; camera Z = -8 + 1.5 = -6.5.
            const float kFov        = 60f;
            const float kCameraX    = 0f  - 5f;   // runner starts at 0; lead offset
            const float kCameraY    = 4f;
            const float kCameraPitch= 20f;
            const float kLaneCentreZ= 0f + 1.5f * 1f; // lane0Z + laneZSpacing * (laneCount-1)*0.5f = 0 + 1.5*1 = 1.5
            const float kCameraZ    = -8f + kLaneCentreZ;

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
