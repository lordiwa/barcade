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

        // Spawn schedule config.
        private readonly float _spawnStartDelay;
        private readonly float _baseGap;
        private readonly float _gapDecay;
        private readonly float _minGap;

        // Knockback config.
        private readonly float _knockbackSpeed;
        private readonly float _knockbackDamping;

        // ── Obstacle state ────────────────────────────────────────────────────────

        private float[] _obsX;
        private float[] _obsZ;
        private bool[]  _obsAlive;
        private bool[]  _obsSpawned;    // true once the obstacle has been activated
        private float[] _obsSpawnTimes; // absolute elapsed-time at which each obstacle activates

        // ── Player state ──────────────────────────────────────────────────────────

        private float _playerX;
        private float _playerZ;

        // Jump sub-state.
        private float _jumpTimer;   // seconds remaining in current jump (0 = grounded)
        private bool  _jumpPending; // RequestJump() was called; consumed at next Tick if grounded

        // Knockback velocity — set on contact, decays toward zero each Tick.
        private float _knockVelX;
        private float _knockVelZ;

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

        /// <summary>Number of obstacles (spawned or not).</summary>
        public int ObstacleCount => _obstacleCount;

        /// <summary>Seconds elapsed since last Restart.</summary>
        public float SurvivalElapsed => _elapsed;

        /// <summary>
        /// True while a jump is in progress; fall and knockback checks are suppressed.
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
        /// Seconds a jump keeps the player airborne (immune to fall/knockback checks).
        /// Appended last so existing call sites compile unchanged.
        /// </param>
        /// <param name="spawnStartDelay">
        /// Seconds before the first obstacle activates (default 0 = immediate on first Tick).
        /// </param>
        /// <param name="baseGap">
        /// Base inter-spawn gap in seconds (default 5). The gap for obstacle k is
        /// max(minGap, baseGap − k × gapDecay), so later obstacles arrive faster.
        /// </param>
        /// <param name="gapDecay">Seconds subtracted from the gap per obstacle index (default 0.6).</param>
        /// <param name="minGap">Minimum gap between spawns in seconds (default 1.5).</param>
        /// <param name="knockbackSpeed">
        /// Speed (world units/s) of the impulse applied to the player on obstacle contact (default 8).
        /// </param>
        /// <param name="knockbackDamping">
        /// Linear damping factor per second; knockback velocity is multiplied by
        /// max(0, 1 − knockbackDamping × dt) each Tick (default 6).
        /// </param>
        public DodgeSim(
            GridArena arena,
            int   obstacleCount    = 3,
            float playerSpeed      = 5f,
            float obstacleSpeed    = 2f,
            float contactRadius    = 0.6f,
            float survivalDuration = 20f,
            float jumpDuration     = 0.6f,
            float spawnStartDelay  = 0f,
            float baseGap          = 5f,
            float gapDecay         = 0.6f,
            float minGap           = 1.5f,
            float knockbackSpeed   = 8f,
            float knockbackDamping = 6f)
        {
            _arena            = arena ?? throw new ArgumentNullException(nameof(arena));
            _obstacleCount    = obstacleCount;
            _playerSpeed      = playerSpeed;
            _obstacleSpeed    = obstacleSpeed;
            _contactRadius    = contactRadius;
            _survivalDuration = survivalDuration;
            _jumpDuration     = jumpDuration;
            _spawnStartDelay  = spawnStartDelay;
            _baseGap          = baseGap;
            _gapDecay         = gapDecay;
            _minGap           = minGap;
            _knockbackSpeed   = knockbackSpeed;
            _knockbackDamping = knockbackDamping;

            _obsX        = new float[obstacleCount];
            _obsZ        = new float[obstacleCount];
            _obsAlive    = new bool[obstacleCount];
            _obsSpawned  = new bool[obstacleCount];
            _obsSpawnTimes = new float[obstacleCount];

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
            _knockVelX   = 0f;
            _knockVelZ   = 0f;

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

            // Precompute escalating spawn times for each obstacle.
            // spawn_time[k] = spawnStartDelay + sum_{j<k}(gap_j)
            // gap_j = max(minGap, baseGap - j * gapDecay)
            float spawnT = _spawnStartDelay;
            for (int i = 0; i < _obstacleCount; i++)
            {
                _obsSpawnTimes[i] = spawnT;
                _obsSpawned[i]    = false;
                _obsAlive[i]      = false; // inactive until spawned

                // XZ resting position: forced start or circle distribution.
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

                if (i < _obstacleCount - 1)
                    spawnT += MathF.Max(_minGap, _baseGap - i * _gapDecay);
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

            // Activate any obstacles that have reached their scheduled spawn time.
            UpdateObstacleSpawns();

            // Move player (applies input + accumulated knockback velocity, then decays velocity).
            MovePlayer(dt, inputX, inputZ);
            MoveObstacles(dt);

            // While airborne all mid-air frames skip ground checks.
            // The landing frame (wasAirborne && !airborneNow) and all grounded frames
            // run checks as usual — landing on a void cell or into an enemy still counts.
            if (!airborneNow)
            {
                // Detect contact with active obstacles and refresh knockback velocity.
                // NOTE: this is applied next frame via MovePlayer; the fall check below
                // catches any push-off-edge that accumulated from prior-frame knockback.
                // NOTE: LostReason.Caught is intentionally never produced — contact
                // now triggers knockback only. The enum value is kept to avoid churn.
                ApplyKnockbackIfContact();

                CheckPlayerFell();
                if (State != DodgeState.Playing) return;
            }

            CheckWin();
        }

        // ── Obstacle accessors ────────────────────────────────────────────────────

        /// <summary>X position of obstacle <paramref name="i"/>.</summary>
        public float GetObstacleX(int i) => _obsX[i];

        /// <summary>Z position of obstacle <paramref name="i"/>.</summary>
        public float GetObstacleZ(int i) => _obsZ[i];

        /// <summary>Whether obstacle <paramref name="i"/> is still in play (alive and spawned).</summary>
        public bool IsObstacleAlive(int i) => _obsAlive[i];

        /// <summary>
        /// Whether obstacle <paramref name="i"/> has been activated.
        /// A not-yet-spawned obstacle does not move, does not apply knockback,
        /// and does not satisfy the all-fallen win condition.
        /// </summary>
        public bool IsObstacleSpawned(int i) => _obsSpawned[i];

        // ── Private helpers ───────────────────────────────────────────────────────

        private void UpdateObstacleSpawns()
        {
            for (int i = 0; i < _obstacleCount; i++)
            {
                if (!_obsSpawned[i] && _elapsed >= _obsSpawnTimes[i])
                {
                    PlaceObstacleOnFrontier(i); // relocate to solid frontier at activation time
                    _obsSpawned[i] = true;
                    _obsAlive[i]   = true;
                }
            }
        }

        /// <summary>
        /// Relocates obstacle <paramref name="i"/> to a position on the current solid frontier
        /// so it cannot spawn on a ring that has already collapsed.
        ///
        /// Skipped when a forced start is set for this index (test-injection path — the caller
        /// has explicitly chosen a position; honour it as-is).
        ///
        /// Formula: obstacles are evenly distributed on a circle at the solid-frontier ring
        /// (<c>FallenRingCount</c> clamped to at most the last ring), then the radius is
        /// shrunk by 1 tile at a time until the target cell is solid; if no solid cell is
        /// found the obstacle falls back to the arena centre.
        /// </summary>
        private void PlaceObstacleOnFrontier(int i)
        {
            // Forced start → honour it; don't move the obstacle.
            if (_forcedObstacleStarts != null && i < _forcedObstacleStarts.Length)
                return;

            float center   = _arena.N * 0.5f;
            int   frontier = Math.Min(_arena.FallenRingCount, _arena.TotalRings - 1);
            float radius   = MathF.Max(0f, center - frontier - 1.0f);
            double angle   = i * (2.0 * Math.PI / MathF.Max(1, _obstacleCount));

            float x  = center + (float)Math.Cos(angle) * radius;
            float z  = center + (float)Math.Sin(angle) * radius;
            int   cx = (int)MathF.Floor(x);
            int   cz = (int)MathF.Floor(z);

            // Walk inward one tile at a time until we land on a solid cell.
            while (!_arena.IsSolid(cx, cz) && radius > 0f)
            {
                radius -= 1f;
                x  = center + (float)Math.Cos(angle) * radius;
                z  = center + (float)Math.Sin(angle) * radius;
                cx = (int)MathF.Floor(x);
                cz = (int)MathF.Floor(z);
            }

            // Last-resort fallback: centre is always the deepest solid cell.
            if (!_arena.IsSolid(cx, cz)) { x = center; z = center; }

            _obsX[i] = x;
            _obsZ[i] = z;
        }

        private void MovePlayer(float dt, float inputX, float inputZ)
        {
            // Normalise diagonal input to prevent speed bonus.
            float mag = MathF.Sqrt(inputX * inputX + inputZ * inputZ);
            if (mag > 1f) { inputX /= mag; inputZ /= mag; }

            _playerX += inputX * _playerSpeed * dt;
            _playerZ += inputZ * _playerSpeed * dt;

            // Apply accumulated knockback velocity from the previous contact frame.
            _playerX += _knockVelX * dt;
            _playerZ += _knockVelZ * dt;

            // Decay knockback toward zero (linear per-frame factor).
            float retain = MathF.Max(0f, 1f - _knockbackDamping * dt);
            _knockVelX *= retain;
            _knockVelZ *= retain;
        }

        private void MoveObstacles(float dt)
        {
            for (int i = 0; i < _obstacleCount; i++)
            {
                if (!_obsSpawned[i] || !_obsAlive[i]) continue;

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

        private void ApplyKnockbackIfContact()
        {
            for (int i = 0; i < _obstacleCount; i++)
            {
                if (!_obsSpawned[i] || !_obsAlive[i]) continue;

                float dx   = _playerX - _obsX[i];
                float dz   = _playerZ - _obsZ[i];
                float dist = MathF.Sqrt(dx * dx + dz * dz);

                if (dist <= _contactRadius)
                {
                    // Refresh knockback velocity pointing directly away from this obstacle.
                    // Guard against exact overlap (dist ≈ 0) by defaulting to +X.
                    if (dist > 1e-4f)
                    {
                        _knockVelX = (dx / dist) * _knockbackSpeed;
                        _knockVelZ = (dz / dist) * _knockbackSpeed;
                    }
                    else
                    {
                        _knockVelX = _knockbackSpeed;
                        _knockVelZ = 0f;
                    }
                }
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

        private void CheckWin()
        {
            // Win if the survival timer has been reached.
            if (_elapsed >= _survivalDuration)
            {
                State = DodgeState.Won;
                return;
            }

            // Win if all obstacles have been activated and subsequently fallen off the grid.
            // An obstacle that has not yet spawned does not count as fallen —
            // the round cannot be won before enemies have had a chance to appear.
            if (_obstacleCount > 0)
            {
                bool allFallen = true;
                for (int i = 0; i < _obstacleCount; i++)
                {
                    if (!_obsSpawned[i] || _obsAlive[i]) { allFallen = false; break; }
                }
                if (allFallen) State = DodgeState.Won;
            }
        }
    }
}
