using System.Collections;
using System.Collections.Generic;
using Barcade.Core.Runner;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Barcade.Framework
{
    /// <summary>
    /// Bootstrap MonoBehaviour for the standalone "Endless Run" 2.5D side-view runner demo.
    ///
    /// Rendering model — FIXED runner, SCROLLING world (classic side-scroller):
    ///   - The runner avatar is pinned at <see cref="runnerAnchorX"/> in world X (default 0).
    ///     It only moves in Y (jump arc) and Z (lane lerp).
    ///   - The camera is also fixed; it does NOT follow <see cref="RunnerSim.Distance"/>.
    ///   - Obstacles and coins stream right-to-left:
    ///       worldX = runnerAnchorX + (spawn.DistancePos − sim.Distance)
    ///     A spawn 20 units ahead appears 20 units to the runner's right and slides left
    ///     to X = runnerAnchorX as Distance grows — the classic "todo viene de derecha a
    ///     izquierda" feel.
    ///
    /// Responsibilities (thin-MonoBehaviour principle — NO game rules live here):
    ///   - Builds a 2.5D scene: static ground slab, 3 depth lanes in Z, a fixed
    ///     side-angled perspective camera, a directional light, and the runner avatar.
    ///   - Each Update: reads Input System actions, calls <see cref="RunnerSim.Tick"/>,
    ///     then syncs all Transforms and visuals to sim state.
    ///   - Runner visual: Kenney model (Resources.Load heroModelPath) with primitive-cube
    ///     fallback — headless-safe.  Lane changes lerp runner Z; jump lifts Y on a
    ///     parabolic arc; slide squashes avatar Y.
    ///   - Spawn visuals: created on entering view, position updated every frame to scroll
    ///     left, destroyed on exit or sim resolution.
    ///   - On Lost: brief pause then auto-restart (mirrors HandleTerminal + _restarting guard).
    ///   - 4-player-ready architecture: sim is a per-instance object; no singletons.
    ///     Wire N players by creating N RunnerBootstrap instances, each with its own sim.
    ///     4P split-screen is intentionally not connected yet — see "4-player extension" comment.
    ///
    /// All game rules (collision, collection, escalation, fairness) live in Core.
    /// </summary>
    public sealed class RunnerBootstrap : MonoBehaviour
    {
        // ── Inspector-tunable simulation parameters ───────────────────────────────

        [Header("Simulation")]
        [SerializeField] private int   laneCount          = 3;
        [SerializeField] private float baseSpeed          = 5f;
        [SerializeField] private float speedAccel         = 0.5f;
        [SerializeField] private float maxSpeed           = 20f;
        [SerializeField] private float jumpDuration       = 0.5f;
        [SerializeField] private float slideDuration      = 0.4f;
        [SerializeField] private int   coinValue          = 1;
        [SerializeField] private float spawnLookAhead     = 30f;
        [SerializeField] private float baseSpawnSpacing   = 8f;
        [SerializeField] private float minSpawnSpacing    = 3f;
        [SerializeField] private int   seed               = 0;

        [Header("Lane layout")]
        // World-Z distance between adjacent lane centres (wider = more readable depth cue).
        [SerializeField] private float laneZSpacing       = 2.0f;
        // World-Z of lane 0 (near lane); higher lanes are at z + laneZSpacing * i.
        [SerializeField] private float lane0Z             = 0f;
        // Fixed world-X where the runner avatar is pinned.  Obstacles/coins scroll toward it.
        [SerializeField] private float runnerAnchorX      = 0f;

        [Header("Camera")]
        // X offset of camera ahead of the anchor (positive = right, runner appears left-of-centre).
        // cam.X = runnerAnchorX + cameraXLead.  Camera does NOT move during play.
        [SerializeField] private float cameraXLead        = 5f;
        [SerializeField] private float cameraY            = 6f;
        // Z position of camera (negative = viewer side, in front of the near lane).
        [SerializeField] private float cameraZ            = -18f;
        [SerializeField] private float cameraFov          = 60f;
        // Camera pitch (look slightly down toward the track).
        [SerializeField] private float cameraPitch        = 20f;

        [Header("Jump / Slide")]
        // World-unit height at the peak of the parabolic hop arc.
        [SerializeField] private float jumpApex           = 2.0f;
        // Minimum Y-scale fraction at peak squash (0 = flat, 1 = no squash, default 0.5).
        [SerializeField] private float slideSquashMin     = 0.5f;

        [Header("Lane lerp")]
        // Speed at which the runner's Z lerps to the target lane (world units / second).
        [SerializeField] private float laneLerpSpeed      = 8f;

        [Header("Win / Loss")]
        [SerializeField] private float restartDelay       = 2f;  // pause before scene rebuild

        [Header("Obstacle sizes")]
        [SerializeField] private float lowObstacleHeight  = 0.5f;   // Y scale of LOW cube
        [SerializeField] private float highObstacleY      = 0.8f;   // Y centre of HIGH cube (overhang)
        [SerializeField] private float highObstacleHeight = 0.6f;   // Y scale of HIGH cube
        [SerializeField] private float coinSize           = 0.3f;   // uniform scale of coin cube
        // Show spawns within this many units AHEAD of the runner (rightward).
        [SerializeField] private float spawnViewDistance  = 40f;
        // Cull spawn visuals once they pass this many units BEHIND the runner (leftward).
        [SerializeField] private float spawnViewBehind    = 5f;

        [Header("Models")]
        // Resources path (no extension) for the runner avatar.  Null/missing → primitive-cube.
        [SerializeField] private string heroModelPath     = "Dodge/Hero/animal-fox";
        [SerializeField] private float  modelScale        = 0.6f;
        [SerializeField] private float  modelGroundY      = 0f;     // resting Y when a model loads
        [SerializeField] private float  modelYaw          = 0f;     // intrinsic forward-correction (deg)

        [Header("Colors")]
        [SerializeField] private Color avatarColor        = new Color(0.20f, 0.80f, 0.20f);
        [SerializeField] private Color lowObstacleColor   = new Color(0.80f, 0.20f, 0.10f);
        [SerializeField] private Color highObstacleColor  = new Color(0.40f, 0.10f, 0.80f);
        [SerializeField] private Color coinColor          = new Color(1.00f, 0.85f, 0.10f);
        [SerializeField] private Color groundColor        = new Color(0.25f, 0.25f, 0.28f);

        // ── Core simulation ───────────────────────────────────────────────────────

        private RunnerSim _sim;

        // Incremented each restart so the RNG continues its sequence → distinct patterns.
        // (The sim itself already handles RNG continuity; this counter is kept for future
        // per-round analytics / HUD.)
        private int _roundCounter;

        // ── Avatar visuals ────────────────────────────────────────────────────────

        private GameObject _avatarGO;
        private float      _avatarBaselineY;    // resting Y (0 for model, 0.5f for cube fallback)
        private Vector3    _avatarBaselineScale; // localScale set at build time; squash is computed from this, never compounded
        private float      _avatarCurrentZ;     // smoothly lerped toward target lane Z each frame

        // ── Spawn visuals ─────────────────────────────────────────────────────────

        // Maps (DistancePos, Lane) → visual GameObject.  Created on first appearance,
        // destroyed when the spawn leaves view or is resolved by the sim.
        private readonly Dictionary<(float, int), GameObject> _spawnVisuals
            = new Dictionary<(float, int), GameObject>();

        // Scratch set used each frame to diff active spawns vs existing visuals.
        private readonly HashSet<(float, int)> _activeKeys = new HashSet<(float, int)>();

        // ── Ground ────────────────────────────────────────────────────────────────

        private GameObject _groundGO; // follows runner X to give the illusion of an infinite track

        // ── Frame-state flags ─────────────────────────────────────────────────────

        private bool _simFrozen;   // sim + input stopped; visuals still run
        private bool _restarting;  // scene rebuild in progress; Update skips

        // ── Input ─────────────────────────────────────────────────────────────────

        // Minimal local InputActions — no full InputActionAsset required.
        private InputAction _jumpAction;
        private InputAction _slideAction;
        private InputAction _laneUpAction;
        private InputAction _laneDownAction;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        private void Start()
        {
            // Jump: Space / gamepad South.
            _jumpAction = new InputAction("Jump", InputActionType.Button);
            _jumpAction.AddBinding("<Keyboard>/space");
            _jumpAction.AddBinding("<Gamepad>/buttonSouth");
            _jumpAction.performed += _ => _sim?.RequestJump();
            _jumpAction.Enable();

            // Slide: S / Left-Ctrl / gamepad buttonEast (or West).
            _slideAction = new InputAction("Slide", InputActionType.Button);
            _slideAction.AddBinding("<Keyboard>/s");
            _slideAction.AddBinding("<Keyboard>/leftCtrl");
            _slideAction.AddBinding("<Gamepad>/buttonEast");
            _slideAction.performed += _ => _sim?.RequestSlide();
            _slideAction.Enable();

            // Lane Up (toward far lane): Up-arrow / W / gamepad d-pad Up.
            _laneUpAction = new InputAction("LaneUp", InputActionType.Button);
            _laneUpAction.AddBinding("<Keyboard>/upArrow");
            _laneUpAction.AddBinding("<Keyboard>/w");
            _laneUpAction.AddBinding("<Gamepad>/dpad/up");
            _laneUpAction.performed += _ => _sim?.MoveLaneUp();
            _laneUpAction.Enable();

            // Lane Down (toward near lane): Down-arrow / gamepad d-pad Down.
            // NOTE: Down-arrow is separate from the slide action so lane-change and
            // slide are distinct bindings — rebind in the Inspector as desired.
            _laneDownAction = new InputAction("LaneDown", InputActionType.Button);
            _laneDownAction.AddBinding("<Keyboard>/downArrow");
            _laneDownAction.AddBinding("<Gamepad>/dpad/down");
            _laneDownAction.performed += _ => _sim?.MoveLaneDown();
            _laneDownAction.Enable();

            InitSim();
            BuildScene();
            FitCamera();
        }

        private void OnDestroy()
        {
            _jumpAction?.Dispose();
            _slideAction?.Dispose();
            _laneUpAction?.Dispose();
            _laneDownAction?.Dispose();
        }

        private void Update()
        {
            if (_sim == null || _restarting) return;

            float dt = Time.deltaTime;

            if (!_simFrozen)
            {
                _sim.Tick(dt);

                if (_sim.State == RunnerState.Lost)
                {
                    _simFrozen = true;
                    Debug.Log($"[RunnerBootstrap] LOSS — distance: {_sim.Distance:F1}  " +
                              $"coins: {_sim.CoinsCollected}  round: {_roundCounter}");
                    StartCoroutine(HandleTerminal());
                }
            }

            SyncVisuals(dt);
        }

        // ── Camera ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Positions Camera.main at a fixed side-on angle ahead of the runner anchor.
        /// The camera does NOT move during play — only spawn visuals scroll.
        /// </summary>
        private void FitCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            cam.fieldOfView    = cameraFov;
            cam.nearClipPlane  = 0.1f;
            cam.farClipPlane   = 200f;
            cam.transform.rotation = Quaternion.Euler(cameraPitch, 0f, 0f);

            float laneCentreZ = lane0Z + laneZSpacing * (laneCount - 1) * 0.5f;
            cam.transform.position = new Vector3(
                runnerAnchorX + cameraXLead,
                cameraY,
                cameraZ + laneCentreZ);
        }

        // ── Init ──────────────────────────────────────────────────────────────────

        private void InitSim()
        {
            _sim = new RunnerSim(
                laneCount:        laneCount,
                baseSpeed:        baseSpeed,
                speedAccel:       speedAccel,
                maxSpeed:         maxSpeed,
                jumpDuration:     jumpDuration,
                slideDuration:    slideDuration,
                coinValue:        coinValue,
                spawnLookAhead:   spawnLookAhead,
                baseSpawnSpacing: baseSpawnSpacing,
                minSpawnSpacing:  minSpawnSpacing,
                seed:             seed + _roundCounter);
        }

        // ── Scene construction ────────────────────────────────────────────────────

        private void BuildScene()
        {
            BuildGround();
            BuildAvatar();
        }

        private void BuildGround()
        {
            // Static flat slab centred on the anchor/camera.  In the fixed-runner model
            // the ground does not need to scroll — obstacles convey motion.
            // Width (Z) covers all lanes with margin; length (X) is large enough to fill
            // the camera view on both sides (camera sees ~24 units wide at these defaults).
            _groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundGO.name = "Ground";
            _groundGO.transform.SetParent(transform, false);

            float trackWidth = laneZSpacing * (laneCount + 1);
            float laneCentreZ = lane0Z + laneZSpacing * (laneCount - 1) * 0.5f;
            _groundGO.transform.localScale    = new Vector3(200f, 0.1f, trackWidth);
            _groundGO.transform.localPosition = new Vector3(runnerAnchorX + cameraXLead, -0.1f, laneCentreZ);

            if (_groundGO.GetComponent<Renderer>() != null)
                _groundGO.GetComponent<Renderer>().material.color = groundColor;
        }

        private void BuildAvatar()
        {
            var heroPrefab = !string.IsNullOrEmpty(heroModelPath)
                ? Resources.Load<GameObject>(heroModelPath)
                : null;

            if (heroPrefab != null)
            {
                // Empty root drives position + jump/slide transforms; model child carries
                // the intrinsic forward-yaw correction.
                _avatarGO = new GameObject("Runner");
                _avatarGO.transform.SetParent(transform, false);

                var model = Instantiate(heroPrefab, _avatarGO.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localScale    = Vector3.one * modelScale;
                model.transform.localRotation = Quaternion.Euler(0f, modelYaw, 0f);

                _avatarBaselineY    = modelGroundY;
                // Root starts at (1,1,1); model child carries its own modelScale.
                _avatarBaselineScale = Vector3.one;
            }
            else
            {
                // Primitive-cube fallback — headless / batch-mode safe.
                _avatarGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _avatarGO.name = "Runner";
                _avatarGO.transform.SetParent(transform, false);
                _avatarGO.transform.localScale = new Vector3(0.5f, 1.0f, 0.5f);

                var rend = _avatarGO.GetComponent<Renderer>();
                if (rend != null) rend.material.color = avatarColor;

                _avatarBaselineY    = 0.5f;  // half of Y scale: cube bottom at Y=0
                _avatarBaselineScale = new Vector3(0.5f, 1.0f, 0.5f);
            }

            // Seed the current Z at the starting lane position.
            _avatarCurrentZ  = LaneToWorldZ(_sim.Lane);
        }

        // ── Visual sync ───────────────────────────────────────────────────────────

        private void SyncVisuals(float dt)
        {
            if (_sim == null) return;

            SyncAvatar(dt);
            SyncCamera();
            SyncSpawnVisuals();
            SyncGround();
        }

        private void SyncAvatar(float dt)
        {
            if (_avatarGO == null) return;

            // X = fixed anchor — the runner does NOT move forward in world space.
            // Speed/distance are sim concerns only; only Y (jump) and Z (lane) change.
            float worldX = runnerAnchorX;

            // Z = lane position, smoothly lerped for a snappy-but-readable lane change.
            float targetZ   = LaneToWorldZ(_sim.Lane);
            _avatarCurrentZ = Mathf.Lerp(_avatarCurrentZ, targetZ, laneLerpSpeed * dt);

            // Y = baseline + parabolic hop arc (only while airborne).
            float jumpY = _sim.IsAirborne
                ? jumpApex * Mathf.Sin(_sim.JumpProgress01 * Mathf.PI)
                : 0f;

            // Slide: squash Y scale while sliding; restore fully when not sliding.
            // squashFactor is 1 at slide start/end and drops to slideSquashMin at peak.
            // IMPORTANT: always computed from _avatarBaselineScale (never read live scale)
            // so the squash cannot compound across frames or during the Lost freeze.
            float squashFactor = _sim.IsSliding
                ? 1f - (1f - slideSquashMin) * Mathf.Sin(_sim.SlideProgress01 * Mathf.PI)
                : 1f;

            _avatarGO.transform.position  = new Vector3(worldX, _avatarBaselineY + jumpY, _avatarCurrentZ);
            _avatarGO.transform.localScale = new Vector3(
                _avatarBaselineScale.x,
                _avatarBaselineScale.y * squashFactor,
                _avatarBaselineScale.z);

            // NOTE: if using a model, squash is applied to the root GameObject, which
            // also scales the model child.  A production-quality fix: apply squash to
            // the model child only.  Acceptable for this prototype / cube-fallback path.
        }

        private void SyncCamera()
        {
            // Camera is FIXED — no per-frame update needed in the scrolling-world model.
            // FitCamera() already placed it at (runnerAnchorX + cameraXLead, cameraY, …).
            // This method is intentionally left as a no-op so future per-player camera
            // overrides (e.g. shaking on loss) have a clear place to live.
        }

        private void SyncGround()
        {
            // Ground is a static slab — no per-frame update required in the
            // scrolling-world model.  Moving obstacles convey speed.
        }

        private void SyncSpawnVisuals()
        {
            var   spawns   = _sim.ActiveSpawns;
            float distance = _sim.Distance;

            // ── Build the set of keys that should have a visual ───────────────────
            // Visibility window: [-spawnViewBehind, +spawnViewDistance] relative to runner.
            _activeKeys.Clear();
            for (int i = 0; i < spawns.Count; i++)
            {
                var   s    = spawns[i];
                float relX = s.DistancePos - distance;
                if (relX > spawnViewDistance || relX < -spawnViewBehind) continue;
                _activeKeys.Add((s.DistancePos, s.Lane));
            }

            // ── Destroy visuals whose spawn is no longer active or in-window ──────
            var toRemove = new List<(float, int)>();
            foreach (var kv in _spawnVisuals)
            {
                if (!_activeKeys.Contains(kv.Key))
                    toRemove.Add(kv.Key);
            }
            foreach (var key in toRemove)
            {
                if (_spawnVisuals.TryGetValue(key, out var go) && go != null)
                    Destroy(go);
                _spawnVisuals.Remove(key);
            }

            // ── Create visuals for newly entered spawns ───────────────────────────
            for (int i = 0; i < spawns.Count; i++)
            {
                var s   = spawns[i];
                var key = (s.DistancePos, s.Lane);
                if (!_activeKeys.Contains(key)) continue;
                if (_spawnVisuals.ContainsKey(key)) continue;
                var go = CreateSpawnVisual(s);
                if (go != null)
                    _spawnVisuals[key] = go;
            }

            // ── Scroll ALL visible spawn visuals left ─────────────────────────────
            // worldX = runnerAnchorX + (DistancePos - Distance).
            // A spawn 10 units ahead of the runner appears 10 units to its right;
            // as Distance grows the visual slides left until it coincides with the
            // runner at X = runnerAnchorX, then passes and gets culled.
            foreach (var kv in _spawnVisuals)
            {
                if (kv.Value == null) continue;
                float relX   = kv.Key.Item1 - distance;          // DistancePos - Distance
                var   pos    = kv.Value.transform.localPosition;
                kv.Value.transform.localPosition = new Vector3(runnerAnchorX + relX, pos.y, pos.z);
            }
        }

        // ── Spawn visual factory ──────────────────────────────────────────────────

        private GameObject CreateSpawnVisual(SpawnInfo s)
        {
            // Initial world X: runner anchor + relative distance ahead.
            // SyncSpawnVisuals updates this every frame so it scrolls toward the runner.
            float worldX = runnerAnchorX + (s.DistancePos - _sim.Distance);
            float worldZ = LaneToWorldZ(s.Lane);

            switch (s.Kind)
            {
                case SpawnKind.LowObstacle:
                {
                    // Short wide cube sitting on the ground — must jump over it.
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Low_{s.DistancePos:F1}_L{s.Lane}";
                    go.transform.SetParent(transform, false);
                    go.transform.localScale    = new Vector3(0.6f, lowObstacleHeight, laneZSpacing * 0.8f);
                    go.transform.localPosition = new Vector3(worldX, lowObstacleHeight * 0.5f, worldZ);
                    SetColor(go, lowObstacleColor);
                    return go;
                }

                case SpawnKind.HighObstacle:
                {
                    // Raised overhang cube — must slide under it.
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"High_{s.DistancePos:F1}_L{s.Lane}";
                    go.transform.SetParent(transform, false);
                    go.transform.localScale    = new Vector3(0.6f, highObstacleHeight, laneZSpacing * 0.8f);
                    go.transform.localPosition = new Vector3(worldX, highObstacleY, worldZ);
                    SetColor(go, highObstacleColor);
                    return go;
                }

                case SpawnKind.Coin:
                {
                    // Small rotating-ish yellow cube.
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Coin_{s.DistancePos:F1}_L{s.Lane}";
                    go.transform.SetParent(transform, false);
                    go.transform.localScale    = Vector3.one * coinSize;
                    go.transform.localPosition = new Vector3(worldX, 0.8f, worldZ);
                    go.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
                    SetColor(go, coinColor);
                    return go;
                }

                default:
                    return null;
            }
        }

        // ── Terminal handling ─────────────────────────────────────────────────────

        private IEnumerator HandleTerminal()
        {
            // Brief stumble pause then rebuild.
            yield return new WaitForSeconds(restartDelay);

            Debug.Log("[RunnerBootstrap] Restarting...");

            // Block Update during rebuild (SyncVisuals would reference stale objects).
            _restarting = true;

            // Destroy all children (avatar, ground, spawn visuals).
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            _avatarGO = null;
            _groundGO = null;
            _spawnVisuals.Clear();
            _activeKeys.Clear();
            _avatarCurrentZ = 0f;

            _simFrozen = false;
            _roundCounter++;

            InitSim();
            BuildScene();
            FitCamera();

            _restarting = false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Converts a lane index to the world-Z position of that lane.</summary>
        private float LaneToWorldZ(int lane)
            => lane0Z + lane * laneZSpacing;

        private static void SetColor(GameObject go, Color color)
        {
            var rend = go != null ? go.GetComponent<Renderer>() : null;
            if (rend != null)
                rend.material.color = color;
        }

        // ── 4-player extension note ───────────────────────────────────────────────
        //
        // To add 4-player split-screen:
        //   1. Convert this MonoBehaviour to accept a player-index ctor/init param.
        //   2. Allocate N RunnerSim instances (one per player slot).
        //   3. Allocate N avatar GameObjects, one per sim.
        //   4. Bind N separate InputAction sets (per-player device assignment).
        //   5. Add N cameras with viewports: new Rect(i * 0.25f, 0, 0.25f, 1).
        //   6. Remove all singleton Camera.main references; use per-player Camera refs.
        //   No Core changes required — RunnerSim is already per-instance with no statics.
    }
}
