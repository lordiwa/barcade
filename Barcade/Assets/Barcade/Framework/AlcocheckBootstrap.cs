using System.Collections;
using Barcade.Core.Alcocheck;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Barcade.Framework
{
    /// <summary>
    /// Bootstrap MonoBehaviour for the standalone "Alcocheck" drunk-balance demo.
    ///
    /// Responsibilities (thin-MonoBehaviour principle — NO game rules live here):
    ///   - Builds a 1-D balance-pad floor (5 flat-cube segments: red|yellow|GREEN|yellow|red)
    ///     and the avatar (Kenney model via Resources.Load, or a primitive-cube fallback).
    ///   - Each Update: reads the 1-D left/right stick axis, calls AlcocheckSim.Tick,
    ///     then syncs the avatar's roll + lateral slide to sim.Lean.  The avatar slides
    ///     over the pad — green at centre, yellow at middle, red near the topple edge —
    ///     making danger instantly readable from the top-down camera.
    ///   - On Lost: topple animation (avatar rolls to ±90°) then auto-restart.
    ///   - On Won: brief balanced pause then auto-restart.
    ///
    /// Balance-pad geometry:
    ///   pad half-width = leanOffsetScale (default 0.8 u)
    ///   zone boundaries as fractions of half-width:
    ///     |LeanFraction| &lt; zoneGreenFraction  (0.40) → green
    ///     |LeanFraction| &lt; zoneYellowFraction (0.75) → yellow
    ///     otherwise                                    → red
    ///   At |LeanFraction| = 1 the avatar is at the outer red edge (topple point).
    ///
    /// Input: 1-D left/right axis only.
    ///   Gamepad left-stick X | A/D keys | Left/Right arrows (1DAxis composites)
    ///
    /// 4-player extension: arrays of AlcocheckSim + avatars + per-player InputActions.
    /// All game rules live in Core (AlcocheckSim). This class is view-only.
    /// </summary>
    public sealed class AlcocheckBootstrap : MonoBehaviour
    {
        // ── Inspector-tunable parameters ─────────────────────────────────────────

        [Header("Simulation")]
        [SerializeField] private float maxLean             = 1.0471976f;  // π/3 = 60°  (wider safety margin)
        [SerializeField] private float gravityGain         = 2.0f;        // slower self-amplification → less twitchy
        [SerializeField] private float playerTorque        = 8f;
        [SerializeField] private float drunkTorque         = 2.5f;        // gentler random pushes
        [SerializeField] private float drunkChangeInterval = 0.5f;        // less frequent re-rolls
        [SerializeField] private float damping             = 2.2f;        // higher velocity damping → easier to correct
        [SerializeField] private float survivalDuration    = 12f;
        [SerializeField] private int   seed                = 42;

        [Header("Win / Loss")]
        [SerializeField] private float restartDelay   = 2f;   // pause after Won before rebuild
        [SerializeField] private float toppleDuration = 0.4f; // seconds to animate avatar falling flat

        [Header("Visual lean")]
        // Lateral X offset applied in addition to the roll so lean reads from above.
        // Also defines the half-width of the balance pad (at |LeanFraction|=1 the
        // avatar sits at the outer pad edge, i.e. the topple point).
        [SerializeField] private float leanOffsetScale = 0.8f;

        [Header("Balance pad")]
        // Zone fractions of leanOffsetScale (half-width):
        //   |LeanFraction| < zoneGreenFraction  → green zone
        //   |LeanFraction| < zoneYellowFraction → yellow zone
        //   otherwise                           → red zone
        [SerializeField] private float zoneGreenFraction  = 0.40f;
        [SerializeField] private float zoneYellowFraction = 0.75f;
        [SerializeField] private float padDepth     = 3.0f;   // Z extent of the pad
        [SerializeField] private float padThickness = 0.12f;  // Y thickness (pad top sits at Y = 0)
        [SerializeField] private Color greenColor  = new Color(0.15f, 0.75f, 0.15f);
        [SerializeField] private Color yellowColor = new Color(0.90f, 0.75f, 0.10f);
        [SerializeField] private Color redColor    = new Color(0.85f, 0.10f, 0.10f);

        [Header("Model")]
        // Resources path for the avatar.  Null / missing → primitive-cube fallback (headless-safe).
        [SerializeField] private string modelPath   = "Dodge/Enemy/character-a";
        [SerializeField] private float  modelScale  = 0.6f;
        [SerializeField] private Color  avatarColor = new Color(0.20f, 0.80f, 0.20f); // bright green

        // ── Core simulation ───────────────────────────────────────────────────────

        private AlcocheckSim _sim;

        // ── Avatar ────────────────────────────────────────────────────────────────

        private GameObject _avatarGO;
        private float      _avatarBaselineY; // resting Y (0f for models, 0.6f for cubes — half of localScale.y=1.2)

        // Visual-only lean angle: normally mirrors sim.Lean, overridden during topple animation.
        private float _visualLean;

        // ── Frame-state flags ─────────────────────────────────────────────────────

        // _simFrozen: sim + input stopped; visual sync (SyncAvatar) still runs each frame.
        // _restarting: scene rebuild in progress; Update skips entirely.
        private bool _simFrozen;
        private bool _restarting;

        // ── Input ─────────────────────────────────────────────────────────────────

        // Minimal local InputAction for 1-D left/right only.
        private InputAction _leanAction;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        private void Start()
        {
            // 1-D lean axis: gamepad left-stick X + WASD-style A/D + arrow keys.
            _leanAction = new InputAction("Lean", InputActionType.Value,
                binding: "<Gamepad>/leftStick/x");
            _leanAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            _leanAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");
            _leanAction.Enable();

            InitSim();
            BuildBalancePad(); // pad first — it's physically below the avatar
            BuildAvatar();
            FitCamera();
        }

        private void OnDestroy()
        {
            _leanAction?.Dispose();
        }

        private void Update()
        {
            if (_sim == null || _restarting) return;

            if (!_simFrozen)
            {
                float inputX = Mathf.Clamp(_leanAction.ReadValue<float>(), -1f, 1f);
                _sim.Tick(Time.deltaTime, inputX);

                // Mirror sim lean to the visual angle in normal play.
                _visualLean = _sim.Lean;

                if (_sim.State != AlcocheckState.Playing)
                {
                    _simFrozen = true;
                    StartCoroutine(HandleTerminal());
                }
            }

            SyncAvatar();
        }

        // ── Camera ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Positions Camera.main at a steep top-down angle (pitch=70°) roughly 6 units
        /// above the origin.  Close framing on the single avatar keeps the lean readable.
        /// </summary>
        private void FitCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // 70° pitch: enough side-view to see the roll tilt, close enough to read the
            // avatar sliding across the coloured balance-pad zones.
            cam.transform.rotation = Quaternion.Euler(70f, 0f, 0f);
            cam.transform.position = cam.transform.rotation * Vector3.back * 6f;
        }

        // ── Init ──────────────────────────────────────────────────────────────────

        private void InitSim()
        {
            _sim = new AlcocheckSim(
                maxLean:             maxLean,
                gravityGain:         gravityGain,
                playerTorque:        playerTorque,
                drunkTorque:         drunkTorque,
                drunkChangeInterval: drunkChangeInterval,
                damping:             damping,
                survivalDuration:    survivalDuration,
                seed:                seed);
        }

        // ── Balance-pad construction ──────────────────────────────────────────────

        /// <summary>
        /// Builds the 1-D balance pad: five flat-cube segments along the X axis.
        ///
        ///   [ red | yellow |   GREEN   | yellow | red ]
        ///
        /// Layout:
        ///   half-width = leanOffsetScale (the full lateral travel of the avatar)
        ///   greenBoundary  = zoneGreenFraction  * half-width
        ///   yellowBoundary = zoneYellowFraction * half-width
        ///
        ///   Segment widths (right half, mirrored for left):
        ///     Green  centre = 2 * greenBoundary
        ///     Yellow each   = yellowBoundary − greenBoundary
        ///     Red   each    = half-width − yellowBoundary
        ///
        ///   At |LeanFraction| = 0   → avatar over green centre.
        ///   At |LeanFraction| = zoneGreenFraction  → green / yellow boundary.
        ///   At |LeanFraction| = zoneYellowFraction → yellow / red boundary.
        ///   At |LeanFraction| = 1   → outer red edge (topple point).
        ///
        /// The pad top surface is at Y = 0; avatar cube bottom also sits at Y = 0.
        /// </summary>
        private void BuildBalancePad()
        {
            float halfWidth      = leanOffsetScale;
            float greenBoundary  = zoneGreenFraction  * halfWidth;
            float yellowBoundary = zoneYellowFraction * halfWidth;

            // Pad centre sits just below Y = 0 so its top surface aligns with the ground plane.
            float padCentreY = -padThickness * 0.5f;

            var padRoot = new GameObject("BalancePad");
            padRoot.transform.SetParent(transform, false);

            // Left red
            AddPadSegment(padRoot, "PadSeg_RedL",
                centerX: -(halfWidth + yellowBoundary) * 0.5f,
                width:    halfWidth - yellowBoundary,
                color:    redColor,
                centreY:  padCentreY);

            // Left yellow
            AddPadSegment(padRoot, "PadSeg_YellowL",
                centerX: -(yellowBoundary + greenBoundary) * 0.5f,
                width:    yellowBoundary - greenBoundary,
                color:    yellowColor,
                centreY:  padCentreY);

            // Centre green
            AddPadSegment(padRoot, "PadSeg_Green",
                centerX: 0f,
                width:    greenBoundary * 2f,
                color:    greenColor,
                centreY:  padCentreY);

            // Right yellow
            AddPadSegment(padRoot, "PadSeg_YellowR",
                centerX: (greenBoundary + yellowBoundary) * 0.5f,
                width:    yellowBoundary - greenBoundary,
                color:    yellowColor,
                centreY:  padCentreY);

            // Right red
            AddPadSegment(padRoot, "PadSeg_RedR",
                centerX: (yellowBoundary + halfWidth) * 0.5f,
                width:    halfWidth - yellowBoundary,
                color:    redColor,
                centreY:  padCentreY);
        }

        private void AddPadSegment(GameObject parent, string segName,
                                   float centerX, float width, Color color, float centreY)
        {
            var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = segName;
            seg.transform.SetParent(parent.transform, false);
            seg.transform.localPosition = new Vector3(centerX, centreY, 0f);
            seg.transform.localScale    = new Vector3(width, padThickness, padDepth);
            seg.GetComponent<Renderer>().material.color = color;
        }

        // ── Avatar construction ───────────────────────────────────────────────────

        private void BuildAvatar()
        {
            var prefab = !string.IsNullOrEmpty(modelPath)
                ? Resources.Load<GameObject>(modelPath)
                : null;

            if (prefab != null)
            {
                // Empty root drives the lean transform; model child carries its own yaw.
                _avatarGO = new GameObject("AlcocheckAvatar");
                _avatarGO.transform.SetParent(transform, false);

                var model = Instantiate(prefab, _avatarGO.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localScale    = Vector3.one * modelScale;
                model.transform.localRotation = Quaternion.identity;

                // Model pivot assumed at foot level (Y = 0 = pad top surface).
                _avatarBaselineY = 0f;
            }
            else
            {
                // Primitive-cube fallback — headless/batch-mode safe.
                // Scale (0.6, 1.2, 0.6): half-height = 0.6 → bottom sits at Y = 0 (pad top).
                _avatarGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _avatarGO.name = "AlcocheckAvatar";
                _avatarGO.transform.SetParent(transform, false);
                _avatarGO.transform.localScale = new Vector3(0.6f, 1.2f, 0.6f);
                _avatarGO.GetComponent<Renderer>().material.color = avatarColor;

                _avatarBaselineY = 0.6f; // half of localScale.y=1.2 → cube bottom at Y=0
            }

            _visualLean = 0f;
            _simFrozen  = false;
        }

        // ── Visual sync ───────────────────────────────────────────────────────────

        /// <summary>
        /// Applies <c>_visualLean</c> to the avatar each frame.
        ///
        /// Roll: Quaternion.Euler(0, 0, −leanDeg) rotates around world-Z so the character
        ///   tilts left/right — visible as a drunk sway under the steep top-down camera.
        ///
        /// Slide: X = LeanFraction × leanOffsetScale places the avatar over the coloured
        ///   pad zone that corresponds to the current danger level.
        /// </summary>
        private void SyncAvatar()
        {
            if (_avatarGO == null) return;

            float leanDeg  = _visualLean * Mathf.Rad2Deg;
            float fraction = _visualLean / maxLean; // normalised [-1, 1]

            _avatarGO.transform.rotation = Quaternion.Euler(0f, 0f, -leanDeg);
            _avatarGO.transform.position = new Vector3(
                fraction * leanOffsetScale,
                _avatarBaselineY,
                0f);
        }

        // ── Terminal handling ─────────────────────────────────────────────────────

        private IEnumerator HandleTerminal()
        {
            string outcome = _sim.State == AlcocheckState.Won
                ? "WIN"
                : $"LOSS ({_sim.LostReason})";
            Debug.Log($"[AlcocheckBootstrap] Game over: {outcome}.");

            if (_sim.LostReason == AlcocheckLostReason.ToppledOver)
            {
                // Topple animation: interpolate _visualLean toward ±90° (avatar falls flat).
                float sign       = _sim.Lean >= 0f ? 1f : -1f;
                float startLean  = _visualLean;
                float targetLean = sign * Mathf.PI * 0.5f;
                float elapsed    = 0f;

                while (elapsed < toppleDuration)
                {
                    elapsed    += Time.deltaTime;
                    _visualLean = Mathf.Lerp(startLean, targetLean, elapsed / toppleDuration);
                    yield return null;
                }
                _visualLean = targetLean;
                yield return new WaitForSeconds(0.5f);
            }
            else
            {
                // Won: hold the balanced pose for restartDelay.
                yield return new WaitForSeconds(restartDelay);
            }

            Debug.Log("[AlcocheckBootstrap] Restarting...");

            // Block Update during rebuild (SyncAvatar would reference stale objects).
            _restarting = true;

            // Destroy all child GameObjects (pad + avatar) via the transform hierarchy.
            foreach (Transform child in transform)
                Destroy(child.gameObject);
            _avatarGO = null;

            _simFrozen  = false;
            _visualLean = 0f;

            InitSim();
            BuildBalancePad();
            BuildAvatar();
            FitCamera();

            _restarting = false;
        }
    }
}
