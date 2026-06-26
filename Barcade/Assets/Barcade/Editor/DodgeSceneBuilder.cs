using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Barcade.Framework;

namespace Barcade.EditorTools
{
    /// <summary>
    /// Editor-only tool that creates or overwrites Assets/Barcade/Scenes/Esquiva3D.unity,
    /// wiring up:
    ///   - Perspective camera angled for a diagonal top-down (isometric-ish) view.
    ///   - Directional light.
    ///   - One empty GameObject carrying <see cref="DodgeGameBootstrap"/>.
    ///
    /// Idempotent: re-running overwrites the scene cleanly.
    ///
    /// Invoke headless via:
    ///   -executeMethod Barcade.EditorTools.DodgeSceneBuilder.BuildEsquiva3D
    ///
    /// Lives in Barcade.Editor (Editor-only assembly).
    /// </summary>
    public static class DodgeSceneBuilder
    {
        private const string SceneFolder = "Assets/Barcade/Scenes";
        private const string ScenePath   = "Assets/Barcade/Scenes/Esquiva3D.unity";

        // ── Entry point ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates or overwrites Esquiva3D.unity with the wired standalone dodge demo.
        /// Invoked headless via -executeMethod Barcade.EditorTools.DodgeSceneBuilder.BuildEsquiva3D.
        /// </summary>
        [MenuItem("Barcade/Build Esquiva 3D Scene")]
        public static void BuildEsquiva3D()
        {
            Debug.Log("[DodgeSceneBuilder] Starting BuildEsquiva3D...");

            EnsureSceneFolder();

            // Create a fresh empty scene (discards any existing runtime state cleanly).
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── Camera ───────────────────────────────────────────────────────────
            // Diagonal top-down view: x≈50° pitch, y≈45° yaw, positioned to frame a
            // 9×9 grid centred at origin.  Distance ~14 units keeps the full grid in frame
            // with default FOV 60°.
            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            cameraGO.transform.rotation = Quaternion.Euler(50f, 45f, 0f);

            // Position: back-up along the inverse of the forward direction so the grid
            // centre (world origin) sits inside the frustum.
            cameraGO.transform.position =
                cameraGO.transform.rotation * Vector3.back * 14f;

            var cam = cameraGO.AddComponent<Camera>();
            cam.orthographic   = false;
            cam.fieldOfView    = 60f;
            cam.nearClipPlane  = 0.1f;
            cam.farClipPlane   = 100f;
            cam.clearFlags     = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.10f);
            cameraGO.AddComponent<AudioListener>();

            // ── Directional light ─────────────────────────────────────────────────
            var lightGO = new GameObject("Directional Light");
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1.2f;
            light.color     = Color.white;

            // ── DodgeGameBootstrap ────────────────────────────────────────────────
            var bootstrapGO = new GameObject("DodgeGameBootstrap");
            bootstrapGO.AddComponent<DodgeGameBootstrap>();

            Debug.Log("[DodgeSceneBuilder] DodgeGameBootstrap added.");

            // ── Save scene ────────────────────────────────────────────────────────
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            // ── Add to build settings ─────────────────────────────────────────────
            AddSceneToBuildSettings(ScenePath);

            Debug.Log("[DodgeSceneBuilder] Esquiva3D.unity built and saved at " + ScenePath);

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

            // Append after existing scenes (this is a standalone demo, not the boot scene).
            scenes.Add(new EditorBuildSettingsScene(scenePath, enabled: true));

            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[DodgeSceneBuilder] Added " + scenePath + " to EditorBuildSettings.");
        }
    }
}
