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
    ///   - Animates tile collapses (lerp Y downward, then deactivate).
    ///   - Obstacles also get a fall animation when the grid beneath them collapses.
    ///   - On LostReason.Fell: player cube drops into the void (same animation as tiles)
    ///     and tile-collapse animation keeps running until the player is out of view,
    ///     then the scene auto-restarts.
    ///   - On LostReason.Caught or Won: brief pause then auto-restart.
    ///
    /// All rules (fall detection, catch detection, win/lose) live in Core.
    ///
    /// Model loading: hero, obstacles, and floor tiles are loaded via Resources.Load by path.
    /// If a model cannot be found (null return), the original CreatePrimitive(Cube) path is
    /// used as a fallback so headless runs and the fast test suite are never broken.
    /// </summary>
    public sealed class DodgeGameBootstrap : MonoBehaviour
    {
        // ── Inspector-tunable parameters ─────────────────────────────────────────

        [Header("Grid")]
        [SerializeField] private int   gridN            = 13;
        [SerializeField] private float graceDelay       = 3f;
        [SerializeField] private float collapseInterval = 3f;

        [Header("Player")]
        [SerializeField] private float playerSpeed    = 5f;
        [SerializeField] private Color playerColor    = new Color(0.20f, 0.80f, 0.20f); // bright green

        [Header("Obstacles")]
        [SerializeField] private int   obstacleCount   = 3;
        [SerializeField] private float obstacleSpeed   = 2f;
        [SerializeField] private float contactRadius   = 0.6f;
        [SerializeField] private Color obstacleColor   = new Color(0.80f, 0.10f, 0.10f); // dark red

        [Header("Win / Loss")]
        [SerializeField] private float survivalDuration = 20f;
        [SerializeField] private float restartDelay     = 3f;   // pause after Won/Caught before rebuild

        [Header("Tile visuals")]
        [SerializeField] private Color tileColorA = new Color(0.30f, 0.30f, 0.35f);
        [SerializeField] private Color tileColorB = new Color(0.25f, 0.25f, 0.28f);
        [SerializeField] private float fallDepth  = -6f;   // Y target for anything that falls
        [SerializeField] private float fallSpeed  = 4f;    // world units per second (tiles, player, obstacles)

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
        [SerializeField] private float modelScale   = 1f;   // uniform scale applied to every spawned model
        [SerializeField] private float modelGroundY = 0f;   // resting Y for player/obstacles when a model is used
        [SerializeField] private float modelYaw     = 0f;   // Y-axis rotation (degrees) so models face forward

        // ── Core simulation ───────────────────────────────────────────────────────

        private GridArena _arena;
        private DodgeSim  _sim;

        // ── Visual objects ────────────────────────────────────────────────────────

        private GameObject[,] _tiles;        // [x, z]
        private bool[,]       _tileFalling;  // tile is animating downward
        private float[,]      _tileY;        // current Y for tile fall animation

        private GameObject _playerGO;
        private float      _playerY;         // current Y for player fall animation
        private bool       _playerFalling;   // player death-fall in progress

        private GameObject[] _obstacleGOs;
        private float[]      _obsY;          // current Y for obstacle fall animation
        private bool[]       _obsFalling;    // obstacle is animating downward (off-grid death)

        // Resolved resting-Y per entity type (modelGroundY when a model loads; 0.35f for primitives).
        private float _playerBaselineY;
        private float _obstacleBaselineY;

        // ── Input ─────────────────────────────────────────────────────────────────

        // Minimal local InputAction (WASD + gamepad left-stick); no full asset required.
        // The thin-MonoBehaviour principle: reads input here, all rules stay in Core.
        private InputAction _moveAction;

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

            InitSim();
            BuildScene();
            FitCamera();
        }

        private void OnDestroy()
        {
            _moveAction?.Dispose();
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
        /// Formula: arena half-diagonal = N × √2 / 2.
        /// Required pull-back = halfDiag / tan(halfFov) × 1.25 margin.
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
                                  obstacleSpeed, contactRadius, survivalDuration);
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
                        // Empty root drives the fall animation; the model is a child.
                        tile = new GameObject($"Tile_{x}_{z}");
                        tile.transform.SetParent(arenaRoot.transform, false);
                        tile.transform.position = tilePos;

                        var modelInstance = Instantiate(floorPrefab, tile.transform);
                        modelInstance.transform.localPosition = Vector3.zero;
                        modelInstance.transform.localScale    = Vector3.one * modelScale;
                        modelInstance.transform.localRotation = Quaternion.Euler(0f, modelYaw, 0f);
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
                    }

                    _tiles[x, z] = tile;
                    _tileY[x, z] = tile.transform.position.y;
                }
            }
        }

        private void BuildPlayer()
        {
            var heroPrefab = Resources.Load<GameObject>(heroModelPath);

            if (heroPrefab != null)
            {
                // Empty root lets SyncVisuals drive position; model is a child.
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
        }

        private void BuildObstacles()
        {
            _obstacleGOs = new GameObject[obstacleCount];
            _obsY        = new float[obstacleCount];
            _obsFalling  = new bool[obstacleCount];

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
                    // Empty root; model is a child.
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

                _obstacleGOs[i] = obs;
                _obsY[i]        = _obstacleBaselineY;
                _obsFalling[i]  = false;
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
                _playerY = Mathf.MoveTowards(_playerY, fallDepth, fallSpeed * dt);
                var pp = _playerGO.transform.position;
                _playerGO.transform.position = new Vector3(pp.x, _playerY, pp.z);
            }
            else
            {
                // Normal play: snap to sim position.
                _playerGO.transform.position = new Vector3(
                    _sim.PlayerX - offset, _playerBaselineY, _sim.PlayerZ - offset);
                _playerY = _playerBaselineY; // keep in sync so fall animation starts from correct Y
            }

            // ── Obstacles ─────────────────────────────────────────────────────────
            for (int i = 0; i < obstacleCount; i++)
            {
                var obs = _obstacleGOs[i];
                if (obs == null || !obs.activeSelf) continue;

                // Transition to fall animation the frame the sim marks an obstacle dead.
                if (!_sim.IsObstacleAlive(i) && !_obsFalling[i])
                    _obsFalling[i] = true;

                if (_obsFalling[i])
                {
                    _obsY[i] = Mathf.MoveTowards(_obsY[i], fallDepth, fallSpeed * dt);
                    var op = obs.transform.position;
                    obs.transform.position = new Vector3(op.x, _obsY[i], op.z);

                    if (Mathf.Approximately(_obsY[i], fallDepth))
                        obs.SetActive(false);

                    continue;
                }

                // Normal play: snap to sim position.
                obs.transform.position = new Vector3(
                    _sim.GetObstacleX(i) - offset, _obstacleBaselineY, _sim.GetObstacleZ(i) - offset);
            }

            // ── Tiles ─────────────────────────────────────────────────────────────
            for (int x = 0; x < gridN; x++)
            {
                for (int z = 0; z < gridN; z++)
                {
                    var tile = _tiles[x, z];
                    if (tile == null || !tile.activeSelf) continue;

                    if (!_arena.IsSolid(x, z) && !_tileFalling[x, z])
                        _tileFalling[x, z] = true;

                    if (_tileFalling[x, z])
                    {
                        _tileY[x, z] = Mathf.MoveTowards(_tileY[x, z], fallDepth, fallSpeed * dt);
                        var tp = tile.transform.position;
                        tile.transform.position = new Vector3(tp.x, _tileY[x, z], tp.z);

                        if (Mathf.Approximately(_tileY[x, z], fallDepth))
                            tile.SetActive(false);
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
                // Won or Caught: no dramatic fall — just a readable pause.
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
