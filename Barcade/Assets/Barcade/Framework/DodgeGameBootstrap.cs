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
    ///   - Each Update: reads Input System move vector, calls <see cref="DodgeSim.Tick"/>,
    ///     and <see cref="GridArena.Tick"/>, then syncs Transforms to sim state.
    ///   - Animates tile collapses (lerp Y downward, then deactivate).
    ///   - On Won/Lost: logs result and auto-restarts after <see cref="restartDelay"/> seconds.
    ///
    /// All rules (fall detection, catch detection, win/lose) live in Core.
    /// </summary>
    public sealed class DodgeGameBootstrap : MonoBehaviour
    {
        // ── Inspector-tunable parameters ─────────────────────────────────────────

        [Header("Grid")]
        [SerializeField] private int   gridN            = 9;
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
        [SerializeField] private float restartDelay     = 3f;

        [Header("Tile visuals")]
        [SerializeField] private Color tileColorA = new Color(0.30f, 0.30f, 0.35f);
        [SerializeField] private Color tileColorB = new Color(0.25f, 0.25f, 0.28f);
        [SerializeField] private float fallDepth  = -6f;   // Y target for a fallen tile
        [SerializeField] private float fallSpeed  = 4f;    // world units per second

        // ── Core simulation ───────────────────────────────────────────────────────

        private GridArena _arena;
        private DodgeSim  _sim;

        // ── Visual objects ────────────────────────────────────────────────────────

        private GameObject[,] _tiles;       // [x, z]
        private bool[,]       _tileFalling; // animating downward
        private float[,]      _tileY;       // current Y for fallen animation

        private GameObject   _playerGO;
        private GameObject[] _obstacleGOs;

        // ── Input ─────────────────────────────────────────────────────────────────

        // Using legacy Input fallback so the demo works without a full InputActionAsset
        // wired to a PlayerInput. Override _inputAction in an InputActions asset for
        // production use.  The thin-MonoBehaviour principle: reads input, passes to sim.
        private InputAction _moveAction;

        // ── Restart flag ──────────────────────────────────────────────────────────

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
        }

        private void OnDestroy()
        {
            _moveAction?.Dispose();
        }

        private void Update()
        {
            if (_sim == null || _restarting) return;

            Vector2 move = _moveAction.ReadValue<Vector2>();
            float dt = Time.deltaTime;

            // Tick Core (all rules inside).
            _arena.Tick(dt);
            _sim.Tick(dt, move.x, move.y); // y maps to Z in the 3D world

            SyncVisuals();

            if (_sim.State != DodgeState.Playing && !_restarting)
                StartCoroutine(HandleTerminal());
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

            for (int x = 0; x < gridN; x++)
            {
                for (int z = 0; z < gridN; z++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = $"Tile_{x}_{z}";
                    tile.transform.SetParent(arenaRoot.transform, false);
                    tile.transform.position = new Vector3(x - offset, -0.5f, z - offset);
                    tile.transform.localScale = new Vector3(0.95f, 0.2f, 0.95f); // flat tile, small gap

                    // Checkerboard tint.
                    Color c = (x + z) % 2 == 0 ? tileColorA : tileColorB;
                    tile.GetComponent<Renderer>().material.color = c;

                    _tiles[x, z]  = tile;
                    _tileY[x, z]  = tile.transform.position.y;
                }
            }
        }

        private void BuildPlayer()
        {
            _playerGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _playerGO.name = "Player";
            _playerGO.transform.SetParent(transform, false);
            _playerGO.transform.localScale = Vector3.one * 0.7f;
            _playerGO.GetComponent<Renderer>().material.color = playerColor;
        }

        private void BuildObstacles()
        {
            _obstacleGOs = new GameObject[obstacleCount];
            for (int i = 0; i < obstacleCount; i++)
            {
                var obs = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obs.name = $"Obstacle_{i}";
                obs.transform.SetParent(transform, false);
                obs.transform.localScale = Vector3.one * 0.7f;
                obs.GetComponent<Renderer>().material.color = obstacleColor;
                _obstacleGOs[i] = obs;
            }
        }

        // ── Visual sync ───────────────────────────────────────────────────────────

        private void SyncVisuals()
        {
            float offset = gridN * 0.5f - 0.5f;

            // Player position: Core uses (posX, posZ) in tile-space; world Y = 0.
            _playerGO.transform.position = new Vector3(
                _sim.PlayerX - offset, 0.35f, _sim.PlayerZ - offset);

            // Obstacles.
            for (int i = 0; i < obstacleCount; i++)
            {
                if (!_sim.IsObstacleAlive(i))
                {
                    _obstacleGOs[i].SetActive(false);
                    continue;
                }
                _obstacleGOs[i].SetActive(true);
                _obstacleGOs[i].transform.position = new Vector3(
                    _sim.GetObstacleX(i) - offset, 0.35f, _sim.GetObstacleZ(i) - offset);
            }

            // Tiles: start falling animation when a tile becomes non-solid.
            float dt = Time.deltaTime;
            for (int x = 0; x < gridN; x++)
            {
                for (int z = 0; z < gridN; z++)
                {
                    var tile = _tiles[x, z];
                    if (tile == null || !tile.activeSelf) continue;

                    if (!_arena.IsSolid(x, z) && !_tileFalling[x, z])
                        _tileFalling[x, z] = true; // start fall

                    if (_tileFalling[x, z])
                    {
                        _tileY[x, z] = Mathf.MoveTowards(_tileY[x, z], fallDepth, fallSpeed * dt);
                        var p = tile.transform.position;
                        tile.transform.position = new Vector3(p.x, _tileY[x, z], p.z);

                        if (Mathf.Approximately(_tileY[x, z], fallDepth))
                            tile.SetActive(false);
                    }
                }
            }
        }

        // ── Terminal handling ─────────────────────────────────────────────────────

        private IEnumerator HandleTerminal()
        {
            _restarting = true;

            string outcome = _sim.State == DodgeState.Won
                ? "WIN"
                : $"LOSS ({_sim.LostReason})";
            Debug.Log($"[DodgeGameBootstrap] Game over: {outcome}. Restarting in {restartDelay}s.");

            yield return new WaitForSeconds(restartDelay);

            // Destroy visual children and rebuild.
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            InitSim();
            BuildScene();

            _restarting = false;
        }
    }
}
