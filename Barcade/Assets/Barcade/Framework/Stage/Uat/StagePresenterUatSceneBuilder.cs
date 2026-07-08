// ============================================================================
// THROWAWAY / DEV-ONLY UAT SCAFFOLDING -- TASK-027 (StagePresenter 3D visual
// UAT). Lives in the Editor-only Barcade.Framework.Uat assembly; never ships
// in a Player build.
// ============================================================================
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Barcade.Framework.Stage;

namespace Barcade.Framework.Stage.Uat
{
    /// <summary>
    /// Editor menu item that builds the StagePresenter UAT rig into a fresh,
    /// UNSAVED scene -- deliberately never calls
    /// <c>EditorSceneManager.SaveScene</c> or touches Build Settings, so this
    /// harness never produces a committed .unity scene asset (only these .cs
    /// files are checked in, mirroring the spirit of
    /// <c>Barcade.EditorTools.DodgeSceneBuilder</c> without its
    /// save-to-disk/add-to-build-settings steps).
    ///
    /// Usage: <b>Barcade/UAT/Build StagePresenter UAT</b>, then press Play. Use
    /// the StagePresenterUatDriver component's Inspector (on the
    /// "StagePresenterUatDriver [THROWAWAY UAT]" GameObject) to switch camera
    /// rigs and retune the plane mapping live -- see that class's doc for what
    /// else is (and isn't) live-tunable.
    /// </summary>
    public static class StagePresenterUatSceneBuilder
    {
        [MenuItem("Barcade/UAT/Build StagePresenter UAT")]
        public static void Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Directional light (mirrors DodgeSceneBuilder).
            var lightGO = new GameObject("Directional Light");
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = Color.white;

            // Driver + the real, unmodified StagePresenter.
            var driverGO = new GameObject("StagePresenterUatDriver [THROWAWAY UAT -- DO NOT SHIP]");
            var driver = driverGO.AddComponent<StagePresenterUatDriver>();

            var presenterGO = new GameObject("StagePresenter");
            presenterGO.transform.SetParent(driverGO.transform, worldPositionStays: false);
            driver.Presenter = presenterGO.AddComponent<StagePresenter>();

            Selection.activeGameObject = driverGO;

            Debug.Log(
                "[StagePresenterUatSceneBuilder] Built. Press Play, then select " +
                "'StagePresenterUatDriver [THROWAWAY UAT -- DO NOT SHIP]' to switch " +
                "camera rigs and retune the plane mapping live in its Inspector. " +
                "This scene is intentionally NOT saved to disk and NOT added to " +
                "Build Settings.");
        }
    }
}
