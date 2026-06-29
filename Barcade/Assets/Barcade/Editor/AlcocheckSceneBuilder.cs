using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Barcade.Framework;

namespace Barcade.EditorTools
{
    /// <summary>
    /// Editor-only tool that creates or overwrites Assets/Barcade/Scenes/Alcocheck.unity,
    /// wiring up:
    ///   - Perspective camera at a steep top-down angle (pitch=70°) that makes the
    ///     lean animation clearly readable while still showing the avatar's height.
    ///   - Directional light.
    ///   - One empty GameObject carrying <see cref="AlcocheckBootstrap"/>.
    ///
    /// Idempotent: re-running overwrites the scene cleanly.
    ///
    /// Invoke headless via:
    ///   -executeMethod Barcade.EditorTools.AlcocheckSceneBuilder.BuildAlcocheck
    ///
    /// Lives in Barcade.Editor (Editor-only assembly).
    /// </summary>
    public static class AlcocheckSceneBuilder
    {
        private const string SceneFolder = "Assets/Barcade/Scenes";
        private const string ScenePath   = "Assets/Barcade/Scenes/Alcocheck.unity";

        // ── Entry point ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates or overwrites Alcocheck.unity with the wired standalone drunk-balance demo.
        /// Invoked headless via -executeMethod Barcade.EditorTools.AlcocheckSceneBuilder.BuildAlcocheck.
        /// </summary>
        [MenuItem("Barcade/Build Alcocheck Scene")]
        public static void BuildAlcocheck()
        {
            Debug.Log("[AlcocheckSceneBuilder] Starting BuildAlcocheck...");

            EnsureSceneFolder();

            // Fresh empty scene (cleanly discards any runtime state).
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── Camera ───────────────────────────────────────────────────────────
            // Steep top-down view: pitch=70° so the lean tilt is readable from above
            // while still showing enough of the character's side profile.
            // No yaw — the avatar is centred at world origin (0, 0, 0).
            const float kFov      = 60f;
            const float kPitch    = 70f;
            const float kDistance = 6f; // world units from origin; close framing for single avatar

            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            cameraGO.transform.rotation = Quaternion.Euler(kPitch, 0f, 0f);

            // Position: step back along the camera's inverse-forward so the avatar
            // sits at the centre of the frustum.
            cameraGO.transform.position =
                cameraGO.transform.rotation * Vector3.back * kDistance;

            var cam = cameraGO.AddComponent<Camera>();
            cam.orthographic    = false;
            cam.fieldOfView     = kFov;
            cam.nearClipPlane   = 0.1f;
            cam.farClipPlane    = 50f;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.10f);
            cameraGO.AddComponent<AudioListener>();

            // ── Directional light ─────────────────────────────────────────────────
            var lightGO = new GameObject("Directional Light");
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1.2f;
            light.color     = Color.white;

            // ── AlcocheckBootstrap ────────────────────────────────────────────────
            var bootstrapGO = new GameObject("AlcocheckBootstrap");
            bootstrapGO.AddComponent<AlcocheckBootstrap>();

            Debug.Log("[AlcocheckSceneBuilder] AlcocheckBootstrap added.");

            // ── Save scene ────────────────────────────────────────────────────────
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            // ── Add to build settings ─────────────────────────────────────────────
            AddSceneToBuildSettings(ScenePath);

            Debug.Log("[AlcocheckSceneBuilder] Alcocheck.unity built and saved at " + ScenePath);

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

            Debug.Log("[AlcocheckSceneBuilder] Added " + scenePath + " to EditorBuildSettings.");
        }
    }
}
