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
    ///   - Creates the avatar (Kenney character model via Resources.Load, or a
    ///     primitive-cube fallback for headless / batch runs).
    ///   - Each Update: reads the 1-D left/right stick axis, calls AlcocheckSim.Tick,
    ///     then syncs the avatar's roll rotation + lateral position to sim.Lean.
    ///   - On Lost: plays a brief topple animation (rolling avatar to ±90°) then
    ///     auto-restarts after a short beat.
    ///   - On Won: pauses on the balanced pose then auto-restarts.
    ///
    /// Visual lean: the avatar is rolled around the world-Z axis by Lean radians so the
    /// tilt is visible under the steep top-down camera.  A lateral X offset proportional
    /// to LeanFraction is added so the lean reads clearly even from directly overhead.
    ///
    /// Input: 1-D left/right axis only.  Bindings:
    ///   - Gamepad left-stick X
    ///   - Keyboard A/D (1DAxis composite)
    ///   - Keyboard Left/Right arrows (1DAxis composite)
    ///
    /// 4-player extension point: add a serialised playerCount field, arrays of
    /// AlcocheckSim + avatar GameObjects, and per-player InputActions.  The sim
    /// is already fully instance-isolated with no singletons.
    ///
    /// All game rules live in Core (AlcocheckSim). This class is view-only.
    /// </summary>
    public sealed class AlcocheckBootstrap : MonoBehaviour
    {
        // ── Inspector-tunable parameters ─────────────────────────────────────────

        [Header("Simulation")]
        [SerializeField] private float maxLean             = 0.7853982f;  // π/4 = 45°
        [SerializeField] private float gravityGain         = 3.5f;
        [SerializeField] private float playerTorque        = 8f;
        [SerializeField] private float drunkTorque         = 4f;
        [SerializeField] private float drunkChangeInterval = 0.4f;
        [SerializeField] private float damping             = 1.2f;
        [SerializeField] private float survivalDuration    = 12f;
        [SerializeField] private int   seed                = 42;

        [Header("Win / Loss")]
        [SerializeField] private float restartDelay   = 2f;   // pause after Won before rebuild
        [SerializeField] private float toppleDuration = 0.4f; // seconds to animate avatar falling flat

        [Header("Visual lean")]
        // Lateral X offset applied in addition to the roll so the lean is readable from above.
        [SerializeField] private float leanOffsetScale = 0.8f;

        [Header("Model")]
        // Resources path for the avatar.  If null or not found, falls back to a primitive cube.
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
        /// above the origin.  Close framing on the single avatar keeps the tilt readable.
        /// </summary>
        private void FitCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // Steep but not pure top-down: 70° pitch gives enough side-view to read the lean.
            cam.transform.rotation = Quaternion.Euler(70f, 0f, 0f);
            // Pull back so the avatar is comfortably framed.
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

                _avatarBaselineY = 0f;
            }
            else
            {
                // Primitive-cube fallback — headless/batch-mode safe.
                _avatarGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _avatarGO.name = "AlcocheckAvatar";
                _avatarGO.transform.SetParent(transform, false);
                _avatarGO.transform.localScale = new Vector3(0.6f, 1.2f, 0.6f);
                _avatarGO.GetComponent<Renderer>().material.color = avatarColor;

                _avatarBaselineY = 0.6f; // half the cube height
            }

            _visualLean = 0f;
            _simFrozen  = false;
        }

        // ── Visual sync ───────────────────────────────────────────────────────────

        /// <summary>
        /// Applies <c>_visualLean</c> to the avatar each frame.
        ///
        /// Roll: rotation around the world-Z axis (makes the character tilt left/right).
        ///   Positive Lean → negative Z rotation (character tilts right, visible under
        ///   the steep top-down camera).
        ///
        /// Lateral offset: shifts the avatar along +X proportional to LeanFraction so
        ///   the sway reads clearly even from a nearly-overhead viewpoint.
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
                // Topple animation: interpolate _visualLean toward ±90° (flat on the ground).
                float sign        = _sim.Lean >= 0f ? 1f : -1f;
                float startLean   = _visualLean;
                float targetLean  = sign * Mathf.PI * 0.5f;
                float elapsed     = 0f;

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
                // Won: hold the balanced pose for the restart delay.
                yield return new WaitForSeconds(restartDelay);
            }

            Debug.Log("[AlcocheckBootstrap] Restarting...");

            // Block Update during rebuild (SyncAvatar would reference stale objects).
            _restarting = true;

            if (_avatarGO != null)
                Destroy(_avatarGO);
            _avatarGO = null;

            _simFrozen  = false;
            _visualLean = 0f;

            InitSim();
            BuildAvatar();
            FitCamera();

            _restarting = false;
        }
    }
}
