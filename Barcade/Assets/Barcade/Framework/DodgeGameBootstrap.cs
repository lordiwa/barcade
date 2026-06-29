using System.Collections;
using Barcade.Core.Dodge;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Barcade.Framework
{
    /// <summary>
    /// Bootstrap MonoBehaviour for the standalone "Esquiva 3D" dodge/survival demo.
    ///
    /// Responsibilities (thin-MonoBehaviour principle — NO game rules live here):
    ///   - Procedurally builds the 3D scene: grid tiles, player cube, obstacle cubes.
    ///   - Each Update: reads Input System move vector, calls <see cref="DodgeSim.Tick"/>
    ///     and <see cref="GridArena.Tick"/>, then syncs Transforms to sim state.
    ///   - Animates tile collapses: gravity-accelerated fall + random tumble spin.
    ///   - Tiles flash a warning colour ~warningLead seconds before their ring collapses.
    ///   - Obstacles drop from <see cref="spawnDropHeight"/> when they activate (escalating cadence).
    ///   - Obstacles also get a fall animation when the grid beneath them collapses.
    ///   - Player and obstacles rotate to face their movement heading each frame.
    ///   - Space / gamepad-South triggers a hop (parabolic arc driven by DodgeSim.JumpProgress01).
    ///   - Contact with an active obstacle applies knockback (rule is in Core); only Fell causes loss.
    ///   - On LostReason.Fell: player cube drops into the void (same animation as tiles)
    ///     and tile-collapse animation keeps running until the player is out of view,
    ///     then the scene auto-restarts.
    ///   - On Won: brief pause then auto-restart.
    ///
    /// All rules (fall detection, knockback, win/lose) live in Core.
    ///
    /// Model loading: hero, obstacles, and floor tiles are loaded via Resources.Load by path.
    /// If a model cannot be found (null return), the original CreatePrimitive(Cube) path is
    /// used as a fallback so headless runs and the fast test suite are never broken.
    /// </summary>
    public sealed class DodgeGameBootstrap : MonoBehaviour
    {
        // ── Inspector-tunable parameters ─────────────────────────────────────────

        [Header("Grid")]
        [SerializeField] private int   gridN            = 21;
        [SerializeField] private float graceDelay       = 4f;
        [SerializeField] private float collapseInterval = 3.5f;

        [Header("Player")]
        [SerializeField] private float playerSpeed    = 5f;
        [SerializeField] private Color playerColor    = new Color(0.20f, 0.80f, 0.20f); // bright green

        [Header("Obstacles")]
        [SerializeField] private int   obstacleCount   = 8;
        [SerializeField] private float obstacleSpeed   = 2f;
        [SerializeField] private float contactRadius   = 0.6f;
        [SerializeField] private Color obstacleColor   = new Color(0.80f, 0.10f, 0.10f); // dark red

        [Header("Obstacle Spawning")]
        // Enemies trickle in with escalating cadence across the round.
        // gap[k] = max(spawnMinGap, spawnBaseGap - k * spawnGapDecay)
        [SerializeField] private float spawnStartDelay = 3f;
        [SerializeField] private float spawnBaseGap    = 5f;
        [SerializeField] private float spawnGapDecay   = 0.6f;
        [SerializeField] private float spawnMinGap     = 1.5f;
        // Y above the grid from which a newly spawned obstacle drops into place.
        [SerializeField] private float spawnDropHeight = 8f;

        [Header("Knockback")]
        [SerializeField] private float knockbackSpeed   = 8f;
        [SerializeField] private float knockbackDamping = 6f;

        [Header("Win / Loss")]
        [SerializeField] private float survivalDuration = 40f;
        [SerializeField] private float restartDelay     = 3f;   // pause after Won before rebuild

        [Header("Tile visuals")]
        [SerializeField] private Color tileColorA   = new Color(0.30f, 0.30f, 0.35f);
        [SerializeField] private Color tileColorB   = new Color(0.25f, 0.25f, 0.28f);
        // Warning: tiles flash this colour ~warningLead seconds before their ring collapses.
        [SerializeField] private Color warningColor = new Color(1f, 0.45f, 0f);   // orange
        [SerializeField] private float warningLead  = 1.5f;                        // seconds
        [SerializeField] private float fallDepth      = -6f;   // Y target for anything that falls
        [SerializeField] private float fallSpeed      = 4f;    // world units/sec for player/obstacle death-falls
        [SerializeField] private float spawnDropSpeed = 12f;   // world units/sec for sky drop-in (distinct from death-fall)

        [Header("Tile crumble")]
        // Tiles use gravity-accelerated fall + random tumble instead of constant-speed drop.
        [SerializeField] private float tileTumbleSpeed = 180f;  // baseline deg/sec spin; per-tile is ±40 % randomised
        [SerializeField] private float tileFallGravity = 18f;   // world units/sec² downward acceleration

        [Header("Character facing")]
        // Player and obstacles Slerp-rotate toward their movement heading each frame.
        [SerializeField] private float turnSpeed = 12f;     // Slerp rate (higher = snappier)

        [Header("Jump")]
        // Height (world units) at the peak of the hop arc; parabola driven by DodgeSim.JumpProgress01.
        [SerializeField] private float jumpApex = 1.5f;

        [Header("Models")]
        // Resources paths (no extension) used by Resources.Load<GameObject>.
        // If a load returns null the primitive-cube fallback is used automatically.
        [SerializeField] private string   heroModelPath  = "Dodge/Hero/animal-fox";
        [SerializeField] private string   floorModelPath = "Dodge/Floor/floor";
        [SerializeField] private string[] enemyModelPaths = {
            "Dodge/Enemy/character-a",
            "Dodge/Enemy/character-b",
            "Dodge/Enemy/character-c",
        };
        // UAT tuning knobs — adjust in the Inspector if a model sits too high/low or faces the wrong way.
        // modelYaw is an INTRINSIC forward-correction offset applied to the model child's localRotation;
        // the root transform's rotation is used for the heading-based facing so the two don't fight.
        [SerializeField] private float modelScale   = 0.6f;  // uniform scale applied to every spawned model
        [SerializeField] private float modelGroundY = 0f;    // resting Y for player/obstacles when a model is used
        [SerializeField] private float modelYaw     = 0f;    // Y-axis correction (degrees) for the model's intrinsic forward

        // ── Core simulation ───────────────────────────────────────────────────────

        private GridArena _arena;
        private DodgeSim  _sim;

        // ── Visual objects ────────────────────────────────────────────────────────

        private GameObject[,] _tiles;        // [x, z]
        private bool[,]       _tileFalling;  // tile is animating downward
        private float[,]      _tileY;        // current Y for tile fall animation

        // Per-tile crumble state — assigned the frame a tile starts falling.
        private Vector3[,] _tileSpinAxis;   // random world-space tumble axis
        private float[,]   _tileSpinSpeed;  // deg/sec spin for this tile
        private float[,]   _tileFallVel;    // current downward velocity (grows via gravity)

        // Warning-colour state per tile — set once when we enter/leave the warning window.
        private Renderer[,][] _tileRenderers;  // all renderers on the tile (children for models, self for cubes)
        private Color[,][]    _tileOrigColors; // original material colours cached at build time (parallel to _tileRenderers)
        private bool[,]       _tileWarning;    // true while the warning colour is currently applied

        private GameObject _playerGO;
        private float      _playerY;         // current Y for player fall animation
        private bool       _playerFalling;   // player death-fall in progress

        private GameObject[] _obstacleGOs;
        private float[]      _obsY;          // current Y for obstacle fall animation
        private bool[]       _obsFalling;    // obstacle is animating downward (off-grid death)

        // Sky drop-in animation state per obstacle.
        private bool[]  _obsWasSpawned; // tracks previous-frame spawn state for transition detection
        private bool[]  _obsDropping;   // true while the drop-in animation is running
        private float[] _obsDropY;      // current Y during drop-in

        // Resolved resting-Y per entity type (modelGroundY when a model loads; 0.35f for primitives).
        private float _playerBaselineY;
        private float _obstacleBaselineY;

        // ── Facing state ──────────────────────────────────────────────────────────

        // Player: previous sim-grid XZ used to compute heading, plus smoothed rotation.
        private float      _playerPrevX;
        private float      _playerPrevZ;
        private Quaternion _playerFacing;

        // Obstacles: same, per-obstacle.
        private float[]      _obsPrevX;
        private float[]      _obsPrevZ;
        private Quaternion[] _obsFacing;

        // ── Input ─────────────────────────────────────────────────────────────────

        // Minimal local InputAction (WASD + gamepad left-stick); no full asset required.
        // The thin-MonoBehaviour principle: reads input here, all rules stay in Core.
        private InputAction _moveAction;
        private InputAction _jumpAction; // Space / gamepad South → DodgeSim.RequestJump()

        // ── Frame-state flags ─────────────────────────────────────────────────────

        // _simFrozen: sim + input are stopped; visuals (SyncVisuals) still run each frame.
        // _restarting: scene rebuild in progress; Update skips entirely.
        private bool _simFrozen;
        private bool _restarting;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        private void Start()
        {
            // Create a minimal local move action (WASD + gamepad left-stick).
            _moveAction = new InputAction("Move", InputActionType.Value,
                binding: "<Gamepad>/leftStick");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up",    "<Keyboard>/w")
                .With("Down",  "<Keyboard>/s")
                .With("Left",  "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.Enable();

            _jumpAction = new InputAction("Jump", InputActionType.Button);
            _jumpAction.AddBinding("<Keyboard>/space");
            _jumpAction.AddBinding("<Gamepad>/buttonSouth");
            _jumpAction.performed += _ => _sim?.RequestJump();
            _jumpAction.Enable();

            InitSim();
            BuildScene();
            FitCamera();
        }

        private void OnDestroy()
        {
            _moveAction?.Dispose();
            _jumpAction?.Dispose();
        }

        private void Update()
        {
            if (_sim == null || _restarting) return;

            float dt = Time.deltaTime;

            // Tick sim + arena only while rules are still active.
            // Once _simFrozen, we stop processing input/rules but keep animating.
            if (!_simFrozen)
            {
                Vector2 move = _moveAction.ReadValue<Vector2>();

                _arena.Tick(dt);
                _sim.Tick(dt, move.x, move.y); // move.y → Z in 3D world

                if (_sim.State != DodgeState.Playing)
                {
                    _simFrozen = true;
                    StartCoroutine(HandleTerminal());
                }
            }

            // SyncVisuals always runs while not rebuilding — this drives all fall
            // animations (tiles, obstacles, and the player-fell death sequence).
            SyncVisuals();
        }

        // ── Camera auto-fit ───────────────────────────────────────────────────────

        /// <summary>
        /// Repositions Camera.main so the full N×N arena is always in frame,
        /// keeping the diagonal top-down angle (pitch 50°, yaw 45°).
        ///
        /// Formula: arena half-diagonal = gridN × √2 / 2.
        /// Required pull-back = halfDiag / tan(halfFov) × 1.25 margin.
        /// Already derives from gridN so it auto-scales to the 21×21 arena.
        /// </summary>
        private void FitCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            float halfFovRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfDiag   = gridN * Mathf.Sqrt(2f) * 0.5f;
            float distance   = halfDiag / Mathf.Tan(halfFovRad) * 1.25f;

            cam.transform.position = cam.transform.rotation * Vector3.back * distance;
        }

        // ── Init ──────────────────────────────────────────────────────────────────

        private void InitSim()
        {
            _arena = new GridArena(gridN, graceDelay, collapseInterval);
            _sim   = new DodgeSim(_arena, obstacleCount, playerSpeed,
                                  obstacleSpeed, contactRadius, survivalDuration,
                                  spawnStartDelay: spawnStartDelay,
                                  baseGap:         spawnBaseGap,
                                  gapDecay:        spawnGapDecay,
                                  minGap:          spawnMinGap,
                                  knockbackSpeed:  knockbackSpeed,
                                  knockbackDamping: knockbackDamping);
        }

        // ── Scene construction ────────────────────────────────────────────────────

        private void BuildScene()
        {
            BuildGrid();
            BuildPlayer();
            BuildObstacles();
        }

        private void BuildGrid()
        {
            _tiles       = new GameObject[gridN, gridN];
            _tileFalling = new bool[gridN, gridN];
            _tileY       = new float[gridN, gridN];

            // Crumble state — populated on the frame each tile starts falling.
            _tileSpinAxis  = new Vector3[gridN, gridN];
            _tileSpinSpeed = new float[gridN, gridN];
            _tileFallVel   = new float[gridN, gridN];

            // Warning-colour state.
            _tileRenderers  = new Renderer[gridN, gridN][];
            _tileOrigColors = new Color[gridN, gridN][];
            _tileWarning    = new bool[gridN, gridN];

            var arenaRoot = new GameObject("Arena");
            arenaRoot.transform.SetParent(transform, false);

            float offset = gridN * 0.5f - 0.5f; // centre the grid at world origin

            // Try to load the floor model once; reuse the prefab for every tile.
            var floorPrefab = Resources.Load<GameObject>(floorModelPath);

            for (int x = 0; x < gridN; x++)
            {
                for (int z = 0; z < gridN; z++)
                {
                    GameObject tile;
                    Vector3 tilePos = new Vector3(x - offset, -0.5f, z - offset);

                    if (floorPrefab != null)
                    {
                        // Empty root drives the fall/tumble animation; the model is a child.
                        tile = new GameObject($"Tile_{x}_{z}");
                        tile.transform.SetParent(arenaRoot.transform, false);
                        tile.transform.position = tilePos;

                        var modelInstance = Instantiate(floorPrefab, tile.transform);
                        modelInstance.transform.localPosition = Vector3.zero;
                        modelInstance.transform.localScale    = Vector3.one * modelScale;
                        modelInstance.transform.localRotation = Quaternion.Euler(0f, modelYaw, 0f);

                        // Cache all child renderers for warning colour swaps (models may have several).
                        _tileRenderers[x, z] = modelInstance.GetComponentsInChildren<Renderer>();
                    }
                    else
                    {
                        // Primitive-cube fallback — exact original code.
                        tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        tile.name = $"Tile_{x}_{z}";
                        tile.transform.SetParent(arenaRoot.transform, false);
                        tile.transform.position   = tilePos;
                        tile.transform.localScale = new Vector3(0.95f, 0.2f, 0.95f);

                        Color c = (x + z) % 2 == 0 ? tileColorA : tileColorB;
                        tile.GetComponent<Renderer>().material.color = c;

                        _tileRenderers[x, z] = new[] { tile.GetComponent<Renderer>() };
                    }

                    _tiles[x, z] = tile;
                    _tileY[x, z] = tile.transform.position.y;

                    // Cache each renderer's original colour (used to revert after warning window).
                    var rsList = _tileRenderers[x, z];
                    _tileOrigColors[x, z] = new Color[rsList != null ? rsList.Length : 0];
                    for (int r = 0; r < _tileOrigColors[x, z].Length; r++)
                        _tileOrigColors[x, z][r] = rsList[r] != null
                            ? rsList[r].material.color
                            : Color.white;
                }
            }
        }

        private void BuildPlayer()
        {
            var heroPrefab = Resources.Load<GameObject>(heroModelPath);

            if (heroPrefab != null)
            {
                // Empty root lets SyncVisuals drive position and heading rotation;
                // model child carries modelYaw as the intrinsic forward correction.
                _playerGO = new GameObject("Player");
                _playerGO.transform.SetParent(transform, false);

                var modelInstance = Instantiate(heroPrefab, _playerGO.transform);
                modelInstance.transform.localPosition = Vector3.zero;
                modelInstance.transform.localScale    = Vector3.one * modelScale;
                modelInstance.transform.localRotation = Quaternion.Euler(0f, modelYaw, 0f);

                _playerBaselineY = modelGroundY;
            }
            else
            {
                // Primitive-cube fallback — exact original code.
                _playerGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _playerGO.name = "Player";
                _playerGO.transform.SetParent(transform, false);
                _playerGO.transform.localScale = Vector3.one * 0.7f;
                _playerGO.GetComponent<Renderer>().material.color = playerColor;

                _playerBaselineY = 0.35f;
            }

            _playerY       = _playerBaselineY;
            _playerFalling = false;

            // Seed facing state at the sim's initial position so first-frame heading is zero.
            _playerPrevX  = _sim.PlayerX;
            _playerPrevZ  = _sim.PlayerZ;
            _playerFacing = Quaternion.identity;
        }

        private void BuildObstacles()
        {
            _obstacleGOs  = new GameObject[obstacleCount];
            _obsY         = new float[obstacleCount];
            _obsFalling   = new bool[obstacleCount];
            _obsWasSpawned = new bool[obstacleCount]; // all false — none spawned at build time
            _obsDropping  = new bool[obstacleCount];
            _obsDropY     = new float[obstacleCount];
            _obsPrevX     = new float[obstacleCount];
            _obsPrevZ     = new float[obstacleCount];
            _obsFacing    = new Quaternion[obstacleCount];

            // Resolve enemy prefabs once; cycle by index if fewer paths than obstacles.
            bool anyEnemyModel = enemyModelPaths != null && enemyModelPaths.Length > 0;
            _obstacleBaselineY = 0.35f; // default; overridden below if any model loads

            for (int i = 0; i < obstacleCount; i++)
            {
                GameObject obs;

                string path = anyEnemyModel
                    ? enemyModelPaths[i % enemyModelPaths.Length]
                    : null;

                var enemyPrefab = path != null ? Resources.Load<GameObject>(path) : null;

                if (enemyPrefab != null)
                {
                    // Empty root; model child carries the intrinsic modelYaw correction.
                    obs = new GameObject($"Obstacle_{i}");
                    obs.transform.SetParent(transform, false);

                    var modelInstance = Instantiate(enemyPrefab, obs.transform);
                    modelInstance.transform.localPosition = Vector3.zero;
                    modelInstance.transform.localScale    = Vector3.one * modelScale;
                    modelInstance.transform.localRotation = Quaternion.Euler(0f, modelYaw, 0f);

                    _obstacleBaselineY = modelGroundY;
                }
                else
                {
                    // Primitive-cube fallback — exact original code.
                    obs = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    obs.name = $"Obstacle_{i}";
                    obs.transform.SetParent(transform, false);
                    obs.transform.localScale = Vector3.one * 0.7f;
                    obs.GetComponent<Renderer>().material.color = obstacleColor;
                    // _obstacleBaselineY stays 0.35f (set above)
                }

                // Obstacles start hidden; they drop in when the sim activates them.
                obs.SetActive(false);

                _obstacleGOs[i] = obs;
                _obsY[i]        = _obstacleBaselineY;
                _obsFalling[i]  = false;

                // Seed facing state.
                _obsPrevX[i]  = _sim.GetObstacleX(i);
                _obsPrevZ[i]  = _sim.GetObstacleZ(i);
                _obsFacing[i] = Quaternion.identity;
            }
        }

        // ── Visual sync ───────────────────────────────────────────────────────────

        private void SyncVisuals()
        {
            float offset = gridN * 0.5f - 0.5f;
            float dt     = Time.deltaTime;

            // ── Player ────────────────────────────────────────────────────────────
            if (_playerFalling)
            {
                // Animate player dropping into the void; XZ stays fixed at last
                // sim position (sim is frozen so PlayerX/Z don't change).
                // No facing update while falling.
                _playerY = Mathf.MoveTowards(_playerY, fallDepth, fallSpeed * dt);
                var pp = _playerGO.transform.position;
                _playerGO.transform.position = new Vector3(pp.x, _playerY, pp.z);
            }
            else
            {
                // Normal play: snap to sim position + hop arc, then smooth-rotate to heading.
                float px = _sim.PlayerX - offset;
                float pz = _sim.PlayerZ - offset;

                // Parabolic hop arc: baseline + apex * sin(progress * PI) rises from 0
                // at takeoff (progress=0) to jumpApex at mid-jump and back to 0 at landing.
                // Independent of the death-fall (_playerFalling) branch.
                float jumpY = _sim.IsAirborne
                    ? jumpApex * Mathf.Sin(_sim.JumpProgress01 * Mathf.PI)
                    : 0f;
                _playerGO.transform.position = new Vector3(px, _playerBaselineY + jumpY, pz);
                _playerY = _playerBaselineY; // keep in sync so fall animation starts from correct Y

                // Heading based on sim-grid delta; ignore tiny values (stationary).
                float hdx = _sim.PlayerX - _playerPrevX;
                float hdz = _sim.PlayerZ - _playerPrevZ;
                if (hdx * hdx + hdz * hdz > 0.0001f)
                {
                    var target   = Quaternion.LookRotation(new Vector3(hdx, 0f, hdz), Vector3.up);
                    _playerFacing = Quaternion.Slerp(_playerFacing, target, turnSpeed * dt);
                }
                _playerGO.transform.rotation = _playerFacing;

                _playerPrevX = _sim.PlayerX;
                _playerPrevZ = _sim.PlayerZ;
            }

            // ── Obstacles ─────────────────────────────────────────────────────────
            for (int i = 0; i < obstacleCount; i++)
            {
                var obs = _obstacleGOs[i];
                if (obs == null) continue;

                // Detect the frame an obstacle transitions from NotSpawned → Spawned.
                // Show the GO and begin the sky drop-in animation.
                // Re-seed the facing "previous position" so the first heading delta is computed
                // from the actual frontier spawn point, not the Restart() baked position.
                if (_sim.IsObstacleSpawned(i) && !_obsWasSpawned[i])
                {
                    _obsWasSpawned[i] = true;
                    _obsDropping[i]   = true;
                    _obsDropY[i]      = spawnDropHeight;
                    _obsPrevX[i]      = _sim.GetObstacleX(i);
                    _obsPrevZ[i]      = _sim.GetObstacleZ(i);
                    obs.SetActive(true);
                }

                if (!obs.activeSelf) continue; // still hidden (not yet spawned)

                // Transition to fall animation the frame the sim marks an obstacle dead.
                if (!_sim.IsObstacleAlive(i) && !_obsFalling[i] && !_obsDropping[i])
                    _obsFalling[i] = true;

                if (_obsFalling[i])
                {
                    // Constant-speed drop into the void; no facing update while falling.
                    _obsY[i] = Mathf.MoveTowards(_obsY[i], fallDepth, fallSpeed * dt);
                    var op = obs.transform.position;
                    obs.transform.position = new Vector3(op.x, _obsY[i], op.z);

                    if (Mathf.Approximately(_obsY[i], fallDepth))
                        obs.SetActive(false);

                    continue;
                }

                if (_obsDropping[i])
                {
                    // Sky drop-in: move from spawnDropHeight down to baseline Y.
                    // Distinct from the death-fall animation (constant speed used for both,
                    // but the drop-in lands at baseline rather than fallDepth).
                    float ox = _sim.GetObstacleX(i) - offset;
                    float oz = _sim.GetObstacleZ(i) - offset;
                    _obsDropY[i] = Mathf.MoveTowards(_obsDropY[i], _obstacleBaselineY, spawnDropSpeed * dt);
                    obs.transform.position = new Vector3(ox, _obsDropY[i], oz);

                    if (Mathf.Approximately(_obsDropY[i], _obstacleBaselineY))
                        _obsDropping[i] = false;

                    continue; // skip normal position sync while the drop-in plays
                }

                // Normal play: snap to sim position, then smooth-rotate to heading.
                float nox = _sim.GetObstacleX(i) - offset;
                float noz = _sim.GetObstacleZ(i) - offset;
                obs.transform.position = new Vector3(nox, _obstacleBaselineY, noz);

                float ohdx = _sim.GetObstacleX(i) - _obsPrevX[i];
                float ohdz = _sim.GetObstacleZ(i) - _obsPrevZ[i];
                if (ohdx * ohdx + ohdz * ohdz > 0.0001f)
                {
                    var target   = Quaternion.LookRotation(new Vector3(ohdx, 0f, ohdz), Vector3.up);
                    _obsFacing[i] = Quaternion.Slerp(_obsFacing[i], target, turnSpeed * dt);
                }
                obs.transform.rotation = _obsFacing[i];

                _obsPrevX[i] = _sim.GetObstacleX(i);
                _obsPrevZ[i] = _sim.GetObstacleZ(i);
            }

            // ── Tiles ─────────────────────────────────────────────────────────────
            for (int x = 0; x < gridN; x++)
            {
                for (int z = 0; z < gridN; z++)
                {
                    var tile = _tiles[x, z];
                    if (tile == null || !tile.activeSelf) continue;

                    if (!_arena.IsSolid(x, z) && !_tileFalling[x, z])
                    {
                        // Tile breaks away: assign a random tumble and start gravity.
                        _tileSpinAxis[x, z]  = new Vector3(
                            Random.Range(-1f, 1f),
                            Random.Range(-1f, 1f),
                            Random.Range(-1f, 1f)).normalized;
                        _tileSpinSpeed[x, z] = Random.Range(tileTumbleSpeed * 0.6f,
                                                             tileTumbleSpeed * 1.4f);
                        _tileFallVel[x, z]   = 0f; // starts from rest; gravity accelerates it
                        _tileFalling[x, z]   = true;

                        // Revert any warning colour that was showing when the tile breaks.
                        if (_tileWarning[x, z] && _tileRenderers[x, z] != null)
                        {
                            var rrs = _tileRenderers[x, z];
                            for (int r = 0; r < rrs.Length; r++)
                                if (rrs[r] != null) rrs[r].material.color = _tileOrigColors[x, z][r];
                            _tileWarning[x, z] = false;
                        }
                    }

                    if (_tileFalling[x, z])
                    {
                        // Gravity-accelerated fall.
                        _tileFallVel[x, z] += tileFallGravity * dt;
                        _tileY[x, z]       -= _tileFallVel[x, z] * dt;

                        if (_tileY[x, z] <= fallDepth)
                        {
                            tile.SetActive(false);
                            continue;
                        }

                        // Tumble rotation in world space.
                        tile.transform.Rotate(
                            _tileSpinAxis[x, z], _tileSpinSpeed[x, z] * dt, Space.World);

                        var tp = tile.transform.position;
                        tile.transform.position = new Vector3(tp.x, _tileY[x, z], tp.z);
                    }
                    else
                    {
                        // Warning-colour telegraph: swap colour when the tile's ring is about
                        // to collapse.  Only set/revert once per transition to avoid per-frame
                        // material allocation.
                        float timeLeft   = _arena.TimeUntilFall(x, z);
                        bool  shouldWarn = timeLeft > 0f && timeLeft <= warningLead;

                        if (shouldWarn != _tileWarning[x, z] && _tileRenderers[x, z] != null)
                        {
                            var rrs = _tileRenderers[x, z];
                            for (int r = 0; r < rrs.Length; r++)
                            {
                                if (rrs[r] == null) continue;
                                rrs[r].material.color =
                                    shouldWarn ? warningColor : _tileOrigColors[x, z][r];
                            }
                            _tileWarning[x, z] = shouldWarn;
                        }
                    }
                }
            }
        }

        // ── Terminal handling ─────────────────────────────────────────────────────

        private IEnumerator HandleTerminal()
        {
            string outcome = _sim.State == DodgeState.Won
                ? "WIN"
                : $"LOSS ({_sim.LostReason})";
            Debug.Log($"[DodgeGameBootstrap] Game over: {outcome}.");

            if (_sim.LostReason == LostReason.Fell)
            {
                // Trigger player fall animation. SyncVisuals drives the Y interpolation
                // each frame; we wait here until the root has dropped out of view.
                _playerFalling = true;
                yield return new WaitUntil(() => _playerY <= fallDepth + 0.05f);
                // Brief beat at the bottom before the scene rebuilds.
                yield return new WaitForSeconds(0.5f);
            }
            else
            {
                // Won: no dramatic fall — just a readable pause.
                // (LostReason.Caught is never produced; contact now triggers knockback only.)
                yield return new WaitForSeconds(restartDelay);
            }

            Debug.Log("[DodgeGameBootstrap] Restarting...");

            // Block Update during the scene rebuild (SyncVisuals would reference stale objects).
            _restarting = true;

            foreach (Transform child in transform)
                Destroy(child.gameObject);

            // Reset visual-state flags before BuildScene recreates the objects.
            // _playerBaselineY is re-resolved by BuildPlayer() inside BuildScene().
            _simFrozen     = false;
            _playerFalling = false;
            _playerY       = _playerBaselineY;

            InitSim();
            BuildScene();
            FitCamera();

            _restarting = false;
        }
    }
}
