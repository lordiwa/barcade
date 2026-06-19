using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Barcade.Framework;

namespace Barcade.EditorTools
{
    /// <summary>
    /// Editor-only tool that creates or overwrites Assets/Barcade/Scenes/Game.unity,
    /// wiring up:
    ///   - Main Camera (orthographic)
    ///   - EventSystem (UGUI interaction)
    ///   - ArcadeInputBridge (with ArcadeControls.inputactions assigned to _actionsAsset)
    ///   - MicrogameLoopController (with DefaultMicrogamePool assigned)
    ///   - MicrogameHost
    ///   - CommandDisplayView + IntermissionView
    ///   - HUD Canvas + HudController + FeedbackController (flash overlay)
    ///   - ResultsScreen
    ///   - SessionController (wired to all of the above)
    ///
    /// Idempotent: re-running overwrites the scene cleanly.
    /// Adds the scene to EditorBuildSettings.scenes list.
    ///
    /// Invoke headless via:
    ///   -executeMethod Barcade.EditorTools.SceneBuilder.BuildGameScene
    ///
    /// Lives in Barcade.Editor (Editor-only assembly).
    /// </summary>
    public static class SceneBuilder
    {
        private const string SceneFolder = "Assets/Barcade/Scenes";
        private const string ScenePath   = "Assets/Barcade/Scenes/Game.unity";
        private const string PoolPath    = "Assets/Barcade/Content/DefaultMicrogamePool.asset";
        private const string InputPath   = "Assets/Barcade/Input/ArcadeControls.inputactions";

        // ── Entry point ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates or overwrites Game.unity with the full wired scene.
        /// Invoked headless via -executeMethod Barcade.EditorTools.SceneBuilder.BuildGameScene.
        /// </summary>
        [MenuItem("Barcade/Build Game Scene")]
        public static void BuildGameScene()
        {
            Debug.Log("[SceneBuilder] Starting BuildGameScene...");

            EnsureSceneFolder();

            // Create a new empty scene (discards any existing runtime state cleanly).
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Load assets
            var poolAsset  = AssetDatabase.LoadAssetAtPath<MicrogamePool>(PoolPath);
            var inputAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(InputPath);

            if (poolAsset == null)
                Debug.LogWarning("[SceneBuilder] DefaultMicrogamePool.asset not found at " + PoolPath +
                                 ". Run 'Barcade/Generate Microgame Content' first.");

            if (inputAsset == null)
                Debug.LogError("[SceneBuilder] ArcadeControls.inputactions not found at " + InputPath +
                               ". _actionsAsset WILL be unassigned -- cabinet input will not work.");

            // ── Camera ───────────────────────────────────────────────────────────
            var cameraGO = new GameObject("Main Camera");
            cameraGO.tag = "MainCamera";
            // Standard 2D position: z=-10 so the camera looks +Z into the play area.
            // Without this, the camera sits at z=0 and clips everything drawn at z=0
            // behind the near plane — the entire play area is invisible.
            cameraGO.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraGO.AddComponent<Camera>();
            camera.orthographic     = true;
            camera.orthographicSize = 5f;
            camera.clearFlags       = CameraClearFlags.SolidColor;
            camera.backgroundColor  = new Color(0.05f, 0.05f, 0.10f);
            cameraGO.AddComponent<AudioListener>();

            // ── EventSystem ──────────────────────────────────────────────────────
            var eventGO = new GameObject("EventSystem");
            eventGO.AddComponent<EventSystem>();
            eventGO.AddComponent<StandaloneInputModule>();

            // ── ArcadeInputBridge ─────────────────────────────────────────────────
            // Exactly ONE per scene (Awake's singleton guard handles any extras).
            var inputBridgeGO = new GameObject("ArcadeInputBridge");
            var bridge = inputBridgeGO.AddComponent<ArcadeInputBridge>();

            if (inputAsset != null)
            {
                // Use SerializedObject to assign the private [SerializeField] _actionsAsset.
                var so = new SerializedObject(bridge);
                var prop = so.FindProperty("_actionsAsset");
                if (prop != null)
                {
                    prop.objectReferenceValue = inputAsset;
                    so.ApplyModifiedProperties();
                    Debug.Log("[SceneBuilder] ArcadeInputBridge._actionsAsset assigned.");
                }
                else
                {
                    Debug.LogError("[SceneBuilder] Could not find '_actionsAsset' property on ArcadeInputBridge. " +
                                   "Input will not work -- check the field name.");
                }
            }

            // ── HUD Canvas ────────────────────────────────────────────────────────
            var hudCanvasGO = new GameObject("HUDCanvas");
            var hudCanvas = hudCanvasGO.AddComponent<Canvas>();
            hudCanvas.renderMode     = RenderMode.ScreenSpaceOverlay;
            hudCanvas.sortingOrder   = 10;
            var hudScaler = hudCanvasGO.AddComponent<CanvasScaler>();
            hudScaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            hudScaler.referenceResolution  = new Vector2(1920f, 1080f);
            hudCanvasGO.AddComponent<GraphicRaycaster>();

            // Flash overlay (full-screen white Image, starts hidden)
            var flashGO = new GameObject("FlashOverlay");
            flashGO.transform.SetParent(hudCanvasGO.transform, false);
            var flashRT = flashGO.AddComponent<RectTransform>();
            flashRT.anchorMin        = Vector2.zero;
            flashRT.anchorMax        = Vector2.one;
            flashRT.offsetMin        = Vector2.zero;
            flashRT.offsetMax        = Vector2.zero;
            var flashImage = flashGO.AddComponent<Image>();
            flashImage.color         = new Color(1f, 1f, 1f, 0f);
            flashImage.raycastTarget = false;
            flashGO.SetActive(false);

            // HudController lives on the canvas GO
            var hudController = hudCanvasGO.AddComponent<HudController>();

            // ── FeedbackController ────────────────────────────────────────────────
            var feedbackGO = new GameObject("FeedbackController");
            var feedback = feedbackGO.AddComponent<FeedbackController>();
            feedback.SetCamera(camera);
            feedback.SetFlashOverlay(flashImage);

            // ── CommandDisplayView Canvas ─────────────────────────────────────────
            var cmdCanvasGO = new GameObject("CommandCanvas");
            var cmdCanvas = cmdCanvasGO.AddComponent<Canvas>();
            cmdCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            cmdCanvas.sortingOrder = 5;
            cmdCanvasGO.AddComponent<CanvasScaler>();
            cmdCanvasGO.AddComponent<GraphicRaycaster>();
            var cmdDisplayView = cmdCanvasGO.AddComponent<CommandDisplayView>();

            // Semi-transparent dark backdrop ensures the verb is always legible
            // regardless of what the microgame is drawing behind it.
            var cmdBgGO = new GameObject("CommandBackground");
            cmdBgGO.transform.SetParent(cmdCanvasGO.transform, false);
            var cmdBgRT = cmdBgGO.AddComponent<RectTransform>();
            cmdBgRT.anchorMin = Vector2.zero;
            cmdBgRT.anchorMax = Vector2.one;
            cmdBgRT.offsetMin = Vector2.zero;
            cmdBgRT.offsetMax = Vector2.zero;
            var cmdBgImage = cmdBgGO.AddComponent<Image>();
            cmdBgImage.color         = new Color(0f, 0f, 0f, 0.55f);
            cmdBgImage.raycastTarget = false;

            // Verb label — occupies the upper 60% of the canvas, centred.
            var verbGO = new GameObject("VerbLabel");
            verbGO.transform.SetParent(cmdCanvasGO.transform, false);
            var verbRT = verbGO.AddComponent<RectTransform>();
            verbRT.anchorMin = new Vector2(0f, 0.4f);
            verbRT.anchorMax = new Vector2(1f, 1.0f);
            verbRT.offsetMin = new Vector2(20f,  0f);
            verbRT.offsetMax = new Vector2(-20f, -20f);
            var verbText = verbGO.AddComponent<Text>();
            verbText.text      = "";
            verbText.fontSize  = 120;   // larger for bar-cabinet viewing distance
            verbText.fontStyle = FontStyle.Bold;
            verbText.alignment = TextAnchor.MiddleCenter;
            verbText.color     = Color.white;

            // Hint label — occupies the lower 35% of the canvas, below the verb.
            var hintGO = new GameObject("HintLabel");
            hintGO.transform.SetParent(cmdCanvasGO.transform, false);
            var hintRT = hintGO.AddComponent<RectTransform>();
            hintRT.anchorMin = new Vector2(0.1f, 0.05f);
            hintRT.anchorMax = new Vector2(0.9f, 0.42f);
            hintRT.offsetMin = Vector2.zero;
            hintRT.offsetMax = Vector2.zero;
            var hintText = hintGO.AddComponent<Text>();
            hintText.text      = "";
            hintText.fontSize  = 52;
            hintText.fontStyle = FontStyle.Normal;
            hintText.alignment = TextAnchor.MiddleCenter;
            hintText.color     = new Color(0.9f, 0.9f, 0.9f);

            // Wire both labels via SerializedObject
            var cmdSO    = new SerializedObject(cmdDisplayView);
            var verbProp = cmdSO.FindProperty("_verbLabel");
            var hintProp = cmdSO.FindProperty("_hintLabel");
            if (verbProp != null) verbProp.objectReferenceValue = verbText;
            if (hintProp != null) hintProp.objectReferenceValue = hintText;
            cmdSO.ApplyModifiedProperties();

            // ── IntermissionView ─────────────────────────────────────────────────
            var intermissionCanvasGO = new GameObject("IntermissionCanvas");
            var intermissionCanvas = intermissionCanvasGO.AddComponent<Canvas>();
            intermissionCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            intermissionCanvas.sortingOrder = 4;
            intermissionCanvasGO.AddComponent<CanvasScaler>();
            intermissionCanvasGO.AddComponent<GraphicRaycaster>();
            var intermissionView = intermissionCanvasGO.AddComponent<IntermissionView>();

            // ── ResultsScreen Canvas ─────────────────────────────────────────────
            var resultsCanvasGO = new GameObject("ResultsCanvas");
            var resultsCanvas = resultsCanvasGO.AddComponent<Canvas>();
            resultsCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            resultsCanvas.sortingOrder = 20;
            resultsCanvasGO.AddComponent<CanvasScaler>();
            resultsCanvasGO.AddComponent<GraphicRaycaster>();

            // Dark background
            var resultsBgGO = new GameObject("ResultsBackground");
            resultsBgGO.transform.SetParent(resultsCanvasGO.transform, false);
            var resultsBgRT = resultsBgGO.AddComponent<RectTransform>();
            resultsBgRT.anchorMin = Vector2.zero;
            resultsBgRT.anchorMax = Vector2.one;
            resultsBgRT.offsetMin = Vector2.zero;
            resultsBgRT.offsetMax = Vector2.zero;
            var resultsBg = resultsBgGO.AddComponent<Image>();
            resultsBg.color = new Color(0f, 0f, 0f, 0.88f);

            // Title label
            var titleGO = new GameObject("TitleLabel");
            titleGO.transform.SetParent(resultsCanvasGO.transform, false);
            var titleRT = titleGO.AddComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0f, 0.75f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.offsetMin = new Vector2(20f, 0f);
            titleRT.offsetMax = new Vector2(-20f, -20f);
            var titleText = titleGO.AddComponent<Text>();
            titleText.text      = "RESULTADOS";
            titleText.fontSize  = 72;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color     = Color.white;

            // Row container
            var rowContainerGO = new GameObject("RowContainer");
            rowContainerGO.transform.SetParent(resultsCanvasGO.transform, false);
            var rowContainerRT = rowContainerGO.AddComponent<RectTransform>();
            rowContainerRT.anchorMin = new Vector2(0.1f, 0.1f);
            rowContainerRT.anchorMax = new Vector2(0.9f, 0.75f);
            rowContainerRT.offsetMin = Vector2.zero;
            rowContainerRT.offsetMax = Vector2.zero;

            // Restart button
            var restartGO = new GameObject("RestartButton");
            restartGO.transform.SetParent(resultsCanvasGO.transform, false);
            var restartRT = restartGO.AddComponent<RectTransform>();
            restartRT.anchorMin         = new Vector2(0.3f, 0.02f);
            restartRT.anchorMax         = new Vector2(0.7f, 0.12f);
            restartRT.offsetMin         = Vector2.zero;
            restartRT.offsetMax         = Vector2.zero;
            var restartBg = restartGO.AddComponent<Image>();
            restartBg.color             = new Color(0.2f, 0.7f, 0.2f);
            var restartBtn = restartGO.AddComponent<Button>();

            var restartLabelGO = new GameObject("RestartLabel");
            restartLabelGO.transform.SetParent(restartGO.transform, false);
            var restartLabelRT = restartLabelGO.AddComponent<RectTransform>();
            restartLabelRT.anchorMin = Vector2.zero;
            restartLabelRT.anchorMax = Vector2.one;
            restartLabelRT.offsetMin = Vector2.zero;
            restartLabelRT.offsetMax = Vector2.zero;
            var restartText = restartLabelGO.AddComponent<Text>();
            restartText.text      = "JUGAR DE NUEVO";
            restartText.fontSize  = 42;
            restartText.fontStyle = FontStyle.Bold;
            restartText.alignment = TextAnchor.MiddleCenter;
            restartText.color     = Color.white;

            // ResultsScreen component on the canvas GO
            var resultsScreen = resultsCanvasGO.AddComponent<ResultsScreen>();

            // Wire ResultsScreen refs via SerializedObject
            var rsSO   = new SerializedObject(resultsScreen);
            var rsRoot = rsSO.FindProperty("_root");
            var rsTitle= rsSO.FindProperty("_titleLabel");
            var rsRows = rsSO.FindProperty("_rowContainer");
            var rsBtn  = rsSO.FindProperty("_restartButton");
            if (rsRoot  != null) rsRoot.objectReferenceValue  = resultsCanvasGO;
            if (rsTitle != null) rsTitle.objectReferenceValue = titleText;
            if (rsRows  != null) rsRows.objectReferenceValue  = rowContainerRT;
            if (rsBtn   != null) rsBtn.objectReferenceValue   = restartBtn;
            rsSO.ApplyModifiedProperties();

            // ── MicrogameHost ─────────────────────────────────────────────────────
            // MicrogameRoot is a stable empty transform at world origin used as the
            // parent for per-round view GameObjects. Assigning it to _viewParent keeps
            // the scene hierarchy clean and makes it easy to locate active views.
            var microgameRootGO = new GameObject("MicrogameRoot");
            microgameRootGO.transform.position = Vector3.zero;

            var hostGO = new GameObject("MicrogameHost");
            var microgameHost = hostGO.AddComponent<MicrogameHost>();

            // Assign _viewParent via SerializedObject so per-round view roots are
            // parented deterministically under MicrogameRoot.
            var mhSO         = new SerializedObject(microgameHost);
            var mhViewParent = mhSO.FindProperty("_viewParent");
            if (mhViewParent != null)
            {
                mhViewParent.objectReferenceValue = microgameRootGO.transform;
                mhSO.ApplyModifiedProperties();
                Debug.Log("[SceneBuilder] MicrogameHost._viewParent assigned to MicrogameRoot.");
            }
            else
            {
                Debug.LogWarning("[SceneBuilder] Could not find '_viewParent' on MicrogameHost — " +
                                 "view roots will spawn unparented.");
            }

            // ── MicrogameLoopController ───────────────────────────────────────────
            var loopGO = new GameObject("MicrogameLoopController");
            var loopController = loopGO.AddComponent<MicrogameLoopController>();

            // Wire MicrogameLoopController refs via SerializedObject
            var lcSO          = new SerializedObject(loopController);
            var lcPool        = lcSO.FindProperty("_microgamePoolAsset");
            var lcHost        = lcSO.FindProperty("_microgameHost");
            var lcInputBridge = lcSO.FindProperty("_inputBridge");
            var lcCmdDisplay  = lcSO.FindProperty("_commandDisplayView");
            var lcIntermission= lcSO.FindProperty("_intermissionView");
            // Pacing: verb shown for 2.5 s so players can read the command.
            var lcCmdDuration = lcSO.FindProperty("_commandShowDuration");
            // Ramp: 3% decay per round, minimum 5 s floor — no round ever feels like a blink.
            var lcRampDecay   = lcSO.FindProperty("_rampDecayPerRound");
            var lcRampMin     = lcSO.FindProperty("_rampMinDuration");
            if (lcPool         != null) lcPool.objectReferenceValue         = poolAsset;
            if (lcHost         != null) lcHost.objectReferenceValue         = microgameHost;
            if (lcInputBridge  != null) lcInputBridge.objectReferenceValue  = bridge;
            if (lcCmdDisplay   != null) lcCmdDisplay.objectReferenceValue   = cmdDisplayView;
            if (lcIntermission != null) lcIntermission.objectReferenceValue = intermissionView;
            if (lcCmdDuration  != null) lcCmdDuration.floatValue            = 2.5f;
            if (lcRampDecay    != null) lcRampDecay.floatValue              = 0.97f;
            if (lcRampMin      != null) lcRampMin.floatValue                = 5.0f;
            lcSO.ApplyModifiedProperties();

            // Wire HudController to the loop controller
            var hcSO   = new SerializedObject(hudController);
            var hcLoop = hcSO.FindProperty("_loopController");
            if (hcLoop != null) { hcLoop.objectReferenceValue = loopController; hcSO.ApplyModifiedProperties(); }

            // ── SessionController ─────────────────────────────────────────────────
            var sessionGO = new GameObject("SessionController");
            var session = sessionGO.AddComponent<SessionController>();
            var scSO     = new SerializedObject(session);
            var scLoop   = scSO.FindProperty("_loopController");
            var scResults= scSO.FindProperty("_resultsScreen");
            var scFeed   = scSO.FindProperty("_feedbackController");
            var scHud    = scSO.FindProperty("_hudController");
            if (scLoop    != null) scLoop.objectReferenceValue    = loopController;
            if (scResults != null) scResults.objectReferenceValue = resultsScreen;
            if (scFeed    != null) scFeed.objectReferenceValue    = feedback;
            if (scHud     != null) scHud.objectReferenceValue     = hudController;
            scSO.ApplyModifiedProperties();

            // ── Save scene ────────────────────────────────────────────────────────
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            // ── Add to build settings ─────────────────────────────────────────────
            AddSceneToBuildSettings(ScenePath);

            Debug.Log("[SceneBuilder] Game.unity built and saved at " + ScenePath);

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

            // Add at index 0 as the main game scene.
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, enabled: true));

            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("[SceneBuilder] Added " + scenePath + " to EditorBuildSettings.");
        }
    }
}
