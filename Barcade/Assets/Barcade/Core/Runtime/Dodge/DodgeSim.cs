using System;

namespace Barcade.Core.Dodge
{
    /// <summary>
    /// Deterministic simulation for the Esquiva 3D dodge/survival demo.
    ///
    /// Coordinate space: continuous (posX, posZ) over the arena grid.
    /// Cell (cx, cz) occupies the unit square [cx, cx+1) × [cz, cz+1).
    /// Default spawn: player at grid centre; obstacles spread around the inner area.
    ///
    /// Pure C# — no UnityEngine dependency.
    /// </summary>
    public sealed class DodgeSim
    {
        // ── Config ────────────────────────────────────────────────────────────────

        private readonly GridArena _arena;
        private readonly float     _playerSpeed;
        private readonly float     _obstacleSpeed;
        private readonly float     _contactRadius;
        private readonly float     _survivalDuration;
        private readonly float     _jumpDuration;
        private readonly int       _obstacleCount;

        // ── Obstacle state ────────────────────────────────────────────────────────

        private float[] _obsX;
        private float[] _obsZ;
        private bool[]  _obsAlive;

        // ── Player state ──────────────────────────────────────────────────────────

        private float _playerX;
        private float _playerZ;

        // Jump sub-state.
        private float _jumpTimer;   // seconds remaining in current jump (0 = grounded)
        private bool  _jumpPending; // RequestJump() was called; consumed at next Tick if grounded

        // ── Simulation clock ──────────────────────────────────────────────────────

        private float _elapsed;

        // ── Test injection ────────────────────────────────────────────────────────

        // Optional forced start positions; if null, defaults are used at Restart().
        private (float x, float z)?   _forcedPlayerStart;
        private (float x, float z)[]  _forcedObstacleStarts;

        // ── State machine ─────────────────────────────────────────────────────────

        /// <summary>Current play state.</summary>
        public DodgeState State { get; private set; }

        /// <summary>Reason for loss; <see cref="LostReason.None"/> while playing or won.</summary>
        public LostReason LostReason { get; private set; }

        // ── Public accessors ──────────────────────────────────────────────────────

        /// <summary>Player X position (continuous, grid space).</summary>
        public float PlayerX => _playerX;

        /// <summary>Player Z position (continuous, grid space).</summary>
        public float PlayerZ => _playerZ;

        /// <summary>Number of obstacles (alive or not).</summary>
        public int ObstacleCount => _obstacleCount;

        /// <summary>Seconds elapsed since last Restart.</summary>
        public float SurvivalElapsed => _elapsed;

        /// <summary>
        /// True while a jump is in progress; fall and catch checks are suppressed.
        /// Becomes false on the landing frame so ground checks run immediately on landing.
        /// </summary>
        public bool IsAirborne => _jumpTimer > 0f;

        /// <summary>
        /// Jump arc progress: 0 at takeoff, approaching 1 at landing.
        /// Returns 0 when grounded. Intended use: Mathf.Sin(JumpProgress01 * PI) for a parabola.
        /// </summary>
        public float JumpProgress01 => IsAirborne ? 1f - _jumpTimer / _jumpDuration : 0f;

        // ── Construction ──────────────────────────────────────────────────────────

        /// <param name="arena">Grid arena (shared; its Tick is NOT called here — caller drives it).</param>
        /// <param name="obstacleCount">Number of chaser cubes (default 3).</param>
        /// <param name="playerSpeed">Player movement speed in world units/second.</param>
        /// <param name="obstacleSpeed">Obstacle chase speed in world units/second.</param>
        /// <param name="contactRadius">Distance at which an obstacle is considered touching the player.</param>
        /// <param name="survivalDuration">Seconds the player must survive to win.</param>
        /// <param name="jumpDuration">
        /// Seconds a jump keeps the player airborne (immune to fall/catch checks).
        /// Appended last so existing call sites compile unchanged.
        /// </param>
        public DodgeSim(
            GridArena arena,
            int   obstacleCount    = 3,
            float playerSpeed      = 5f,
            float obstacleSpeed    = 2f,
            float contactRadius    = 0.6f,
            float survivalDuration = 20f,
            float jumpDuration     = 0.6f)
        {
            _arena            = arena ?? throw new ArgumentNullException(nameof(arena));
            _obstacleCount    = obstacleCount;
            _playerSpeed      = playerSpeed;
            _obstacleSpeed    = obstacleSpeed;
            _contactRadius    = contactRadius;
            _survivalDuration = survivalDuration;
            _jumpDuration     = jumpDuration;

            _obsX     = new float[obstacleCount];
            _obsZ     = new float[obstacleCount];
            _obsAlive = new bool[obstacleCount];

            Restart();
        }

        // ── Test injection ────────────────────────────────────────────────────────

        /// <summary>
        /// Override the player's starting position for deterministic tests.
        /// Call before <see cref="Restart"/>.
        /// </summary>
        public void SetForcedPlayerStart(float x, float z)
            => _forcedPlayerStart = (x, z);

        /// <summary>
        /// Override obstacle starting positions for deterministic tests.
        /// Array length must equal <see cref="ObstacleCount"/>.
        /// Call before <see cref="Restart"/>.
        /// </summary>
        public void SetForcedObstacleStarts((float x, float z)[] starts)
            => _forcedObstacleStarts = starts;

        // ── Jump ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Queue a jump for the next <see cref="Tick"/>.
        /// Ignored if the player is already airborne (no double-jump or timer extension).
        /// </summary>
        public void RequestJump()
        {
            if (State == DodgeState.Playing)
                _jumpPending = true;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Reset simulation to initial positions and <see cref="DodgeState.Playing"/>.
        /// Does NOT reset the arena — that is the caller's responsibility.
        /// </summary>
        public void Restart()
        {
            State      = DodgeState.Playing;
            LostReason = LostReason.None;
            _elapsed   = 0f;
            _jumpTimer   = 0f;
            _jumpPending = false;

            float center = _arena.N * 0.5f;

            // Player: forced start or grid centre.
            if (_forcedPlayerStart.HasValue)
            {
                _playerX = _forcedPlayerStart.Value.x;
                _playerZ = _forcedPlayerStart.Value.z;
            }
            else
            {
                _playerX = center;
                _playerZ = center;
            }

            // Obstacles: forced starts or evenly distributed around the inner area.
            for (int i = 0; i < _obstacleCount; i++)
            {
                if (_forcedObstacleStarts != null && i < _forcedObstacleStarts.Length)
                {
                    _obsX[i] = _forcedObstacleStarts[i].x;
                    _obsZ[i] = _forcedObstacleStarts[i].z;
                }
                else
                {
                    // Evenly space obstacles in a circle at radius N*0.45 from centre.
                    double angle = i * (2.0 * Math.PI / _obstacleCount);
                    float  r     = _arena.N * 0.45f;
                    _obsX[i] = center + (float)Math.Cos(angle) * r;
                    _obsZ[i] = center + (float)Math.Sin(angle) * r;
                }
                _obsAlive[i] = true;
            }
        }

        // ── Per-frame update ──────────────────────────────────────────────────────

        /// <summary>
        /// Advance the simulation by <paramref name="dt"/> seconds.
        /// <paramref name="inputX"/> and <paramref name="inputZ"/> are the normalised
        /// stick axes (each in [-1, 1]).  The arena must be ticked separately.
        /// No-op once the state is <see cref="DodgeState.Won"/> or <see cref="DodgeState.Lost"/>.
        /// </summary>
        public void Tick(float dt, float inputX, float inputZ)
        {
            if (State != DodgeState.Playing) return;
            if (dt <= 0f) return;

            _elapsed += dt;

            // Consume a pending jump only if the player is grounded; airborne → ignore
            // (no double-jump, no timer refresh).
            if (_jumpPending)
            {
                if (_jumpTimer <= 0f)
                    _jumpTimer = _jumpDuration;
                _jumpPending = false;
            }

            bool wasAirborne = _jumpTimer > 0f;
            if (wasAirborne)
                _jumpTimer = MathF.Max(0f, _jumpTimer - dt);
            bool airborneNow = _jumpTimer > 0f;

            MovePlayer(dt, inputX, inputZ);
            MoveObstacles(dt);

            // While airborne all mid-air frames skip fall/catch checks.
            // The landing frame (wasAirborne && !airborneNow) and all grounded frames
            // run checks as usual — landing on a void cell or into an enemy still counts.
            if (!airborneNow)
            {
                CheckPlayerFell();
                if (State != DodgeState.Playing) return;
                CheckPlayerCaught();
                if (State != DodgeState.Playing) return;
            }

            CheckWin();
        }

        // ── Obstacle accessors ────────────────────────────────────────────────────

        /// <summary>X position of obstacle <paramref name="i"/>.</summary>
        public float GetObstacleX(int i) => _obsX[i];

        /// <summary>Z position of obstacle <paramref name="i"/>.</summary>
        public float GetObstacleZ(int i) => _obsZ[i];

        /// <summary>Whether obstacle <paramref name="i"/> is still in play.</summary>
        public bool IsObstacleAlive(int i) => _obsAlive[i];

        // ── Private helpers ───────────────────────────────────────────────────────

        private void MovePlayer(float dt, float inputX, float inputZ)
        {
            // Normalise diagonal input to prevent speed bonus.
            float mag = MathF.Sqrt(inputX * inputX + inputZ * inputZ);
            if (mag > 1f) { inputX /= mag; inputZ /= mag; }

            _playerX += inputX * _playerSpeed * dt;
            _playerZ += inputZ * _playerSpeed * dt;
        }

        private void MoveObstacles(float dt)
        {
            for (int i = 0; i < _obstacleCount; i++)
            {
                if (!_obsAlive[i]) continue;

                // Steer toward player.
                float dx = _playerX - _obsX[i];
                float dz = _playerZ - _obsZ[i];
                float dist = MathF.Sqrt(dx * dx + dz * dz);

                if (dist > 1e-4f)
                {
                    _obsX[i] += (dx / dist) * _obstacleSpeed * dt;
                    _obsZ[i] += (dz / dist) * _obstacleSpeed * dt;
                }

                // Kill obstacle if it is over a non-solid cell.
                int cx = (int)MathF.Floor(_obsX[i]);
                int cz = (int)MathF.Floor(_obsZ[i]);
                if (!_arena.IsSolid(cx, cz))
                    _obsAlive[i] = false;
            }
        }

        private void CheckPlayerFell()
        {
            int cx = (int)MathF.Floor(_playerX);
            int cz = (int)MathF.Floor(_playerZ);
            if (!_arena.IsSolid(cx, cz))
            {
                State      = DodgeState.Lost;
                LostReason = LostReason.Fell;
            }
        }

        private void CheckPlayerCaught()
        {
            for (int i = 0; i < _obstacleCount; i++)
            {
                if (!_obsAlive[i]) continue;

                float dx   = _playerX - _obsX[i];
                float dz   = _playerZ - _obsZ[i];
                float dist = MathF.Sqrt(dx * dx + dz * dz);

                if (dist <= _contactRadius)
                {
                    State      = DodgeState.Lost;
                    LostReason = LostReason.Caught;
                    return;
                }
            }
        }

        private void CheckWin()
        {
            // Win if the survival timer has been reached.
            if (_elapsed >= _survivalDuration)
            {
                State = DodgeState.Won;
                return;
            }

            // Win if there were obstacles and all have now fallen off the grid.
            // Zero-obstacle games rely solely on the timer (vacuous "all fallen" must not win early).
            if (_obstacleCount > 0)
            {
                bool allFallen = true;
                for (int i = 0; i < _obstacleCount; i++)
                    if (_obsAlive[i]) { allFallen = false; break; }

                if (allFallen)
                    State = DodgeState.Won;
            }
        }
    }
}
