using System;
using System.Collections.Generic;

namespace Barcade.Core.Runner
{
    /// <summary>
    /// Deterministic simulation for the Endless Run 2.5D side-view endless-runner demo.
    ///
    /// The runner auto-advances along the track; the player switches between depth lanes
    /// (0 = near, laneCount-1 = far) and jumps or slides to avoid obstacles.
    ///
    /// Coordinate model:
    ///   Distance: cumulative forward progress along the track.
    ///   Lane:     integer index in [0, laneCount); represents depth in the world.
    ///
    /// Obstacle collision rules (resolved when Distance crosses a spawn's DistancePos):
    ///   LOW obstacle  — safe only if airborne (jumping); otherwise → Lost.
    ///   HIGH obstacle — safe only if sliding; otherwise → Lost.
    ///   Coin          — collected (CoinsCollected += coinValue) when in the same lane.
    ///   Wrong lane    — spawns in other lanes are ignored entirely.
    ///
    /// Vertical-state snapshot for collision checks is captured AFTER consuming pending
    /// inputs but BEFORE advancing the jump/slide timers.  This means:
    ///   • RequestJump() + Tick(dt)  → airborne during the entire tick (including the
    ///     landing frame), so obstacles crossed in that tick are cleared.
    ///   • RequestSlide() + Tick(dt) → sliding during the entire tick, even if the slide
    ///     timer expires within the same tick; this is the generous/player-friendly rule.
    ///
    /// Spawning: seeded via ISeededRandom; spacing tightens as Speed escalates.
    /// Fairness guarantee: no obstacle wave blocks all lanes simultaneously.
    ///
    /// 4-player extension: instantiate one RunnerSim per player (no singletons).
    /// Pure C# — no UnityEngine dependency.
    /// </summary>
    public sealed class RunnerSim
    {
        // ── Config ────────────────────────────────────────────────────────────────

        private readonly int   _laneCount;
        private readonly float _baseSpeed;
        private readonly float _speedAccel;          // units/s² speed growth
        private readonly float _maxSpeed;
        private readonly float _jumpDuration;
        private readonly float _slideDuration;
        private readonly int   _coinValue;
        private readonly float _spawnLookAhead;      // distance units ahead to pre-generate spawns
        private readonly float _baseSpawnSpacing;    // initial distance between spawns
        private readonly float _minSpawnSpacing;     // floor spacing at max speed

        private readonly ISeededRandom _rng; // created once at ctor; NOT re-seeded on Restart()

        // ── Simulation state ──────────────────────────────────────────────────────

        private float _distance;
        private float _speed;
        private int   _lane;
        private int   _coinsCollected;

        // Jump sub-state (mirrors DodgeSim pattern).
        private float _jumpTimer;    // seconds remaining (0 = grounded)
        private bool  _jumpPending;  // consumed at next Tick if grounded and not sliding

        // Slide sub-state.
        private float _slideTimer;   // seconds remaining (0 = not sliding)
        private bool  _slidePending; // consumed at next Tick if grounded

        // ── Spawn state ───────────────────────────────────────────────────────────

        // Active spawns sorted ascending by DistancePos; read each frame by the view.
        private readonly List<SpawnInfo> _activeSpawns;

        // Next distance at which to generate a spawn.
        // Set to float.MaxValue when using _forcedSpawns (disables RNG generator).
        private float _nextSpawnDistance;

        // Per-lane distance at which the most recent obstacle was placed.
        // Used to enforce the fairness guarantee: never block all lanes in a tight window.
        private readonly float[] _laneLastObstacleDistance;

        // ── Test injection ────────────────────────────────────────────────────────

        // Optional forced starting lane applied at Restart().
        private int?  _forcedLane;

        // Optional forced spawn list; when non-null replaces RNG-based spawning.
        private SpawnInfo[] _forcedSpawns;

        // ── Terminal state ────────────────────────────────────────────────────────

        /// <summary>Current play state.</summary>
        public RunnerState State { get; private set; }

        /// <summary>
        /// Reason for loss; <see cref="RunnerLostReason.None"/> while playing.
        /// </summary>
        public RunnerLostReason LostReason { get; private set; }

        // ── Public accessors ──────────────────────────────────────────────────────

        /// <summary>Cumulative forward distance since last <see cref="Restart"/>.</summary>
        public float Distance => _distance;

        /// <summary>Current forward speed in world units per second.</summary>
        public float Speed => _speed;

        /// <summary>Current depth lane index in [0, laneCount).</summary>
        public int Lane => _lane;

        /// <summary>Total coins collected since last <see cref="Restart"/>.</summary>
        public int CoinsCollected => _coinsCollected;

        /// <summary>
        /// True while a jump is in progress.
        /// Collision checks use the snapshot taken BEFORE timer advancement; see class remarks.
        /// </summary>
        public bool IsAirborne => _jumpTimer > 0f;

        /// <summary>
        /// Jump arc progress: 0 at takeoff, approaching 1 at landing.
        /// Returns 0 when grounded.  Intended use: MathF.Sin(JumpProgress01 × π) for a parabola.
        /// </summary>
        public float JumpProgress01 => IsAirborne ? 1f - _jumpTimer / _jumpDuration : 0f;

        /// <summary>True while a slide is in progress.</summary>
        public bool IsSliding => _slideTimer > 0f;

        /// <summary>
        /// Slide animation progress: 0 at duck start, approaching 1 at slide end.
        /// Returns 0 when upright.  Use to squash the avatar (e.g., MathF.Sin(progress × π)).
        /// </summary>
        public float SlideProgress01 => IsSliding ? 1f - _slideTimer / _slideDuration : 0f;

        /// <summary>
        /// Upcoming spawns (obstacles and coins) sorted by ascending DistancePos.
        /// Read every frame by the view to create and cull visual representations.
        /// </summary>
        public IReadOnlyList<SpawnInfo> ActiveSpawns => _activeSpawns;

        // ── Construction ──────────────────────────────────────────────────────────

        /// <param name="laneCount">
        /// Number of parallel depth lanes (default 3: near=0, mid=1, far=2).
        /// </param>
        /// <param name="baseSpeed">
        /// Starting forward speed in world units per second (default 5).
        /// </param>
        /// <param name="speedAccel">
        /// Speed growth rate in units/s² (default 0.5).  Capped at <paramref name="maxSpeed"/>.
        /// </param>
        /// <param name="maxSpeed">Speed ceiling in units per second (default 20).</param>
        /// <param name="jumpDuration">
        /// Seconds the runner stays airborne per jump (default 0.5).
        /// Appended last so future ctor extensions don't break existing call sites.
        /// </param>
        /// <param name="slideDuration">
        /// Seconds a slide (duck) lasts before the runner stands back up (default 0.4).
        /// </param>
        /// <param name="coinValue">Score increment per coin collected (default 1).</param>
        /// <param name="spawnLookAhead">
        /// Distance units ahead of the runner to pre-generate spawns (default 30).
        /// </param>
        /// <param name="baseSpawnSpacing">
        /// Initial distance between spawns (default 8).  Tightens with speed:
        /// spacing = max(minSpawnSpacing, baseSpawnSpacing × baseSpeed / currentSpeed).
        /// </param>
        /// <param name="minSpawnSpacing">
        /// Spawn spacing floor at maximum speed (default 3).
        /// </param>
        /// <param name="seed">
        /// Seed for the internal <see cref="SeededRandom"/> (used when <paramref name="rng"/> is null).
        /// The RNG is created once and NOT re-seeded on <see cref="Restart"/> — each run continues
        /// the sequence so spawn patterns differ between rounds.
        /// </param>
        /// <param name="rng">
        /// Optional externally supplied RNG for fully deterministic tests.
        /// When non-null, <paramref name="seed"/> is stored but not used for RNG creation.
        /// <see cref="Restart"/> does NOT reset an injected RNG.
        /// </param>
        public RunnerSim(
            int           laneCount          = 3,
            float         baseSpeed          = 5f,
            float         speedAccel         = 0.5f,
            float         maxSpeed           = 20f,
            float         jumpDuration       = 0.5f,
            float         slideDuration      = 0.4f,
            int           coinValue          = 1,
            float         spawnLookAhead     = 30f,
            float         baseSpawnSpacing   = 8f,
            float         minSpawnSpacing    = 3f,
            int           seed               = 0,
            ISeededRandom rng                = null)
        {
            if (laneCount        <  1)           throw new ArgumentOutOfRangeException(nameof(laneCount));
            if (baseSpeed        <= 0f)          throw new ArgumentOutOfRangeException(nameof(baseSpeed));
            if (maxSpeed         <  baseSpeed)   throw new ArgumentOutOfRangeException(nameof(maxSpeed));
            if (jumpDuration     <= 0f)          throw new ArgumentOutOfRangeException(nameof(jumpDuration));
            if (slideDuration    <= 0f)          throw new ArgumentOutOfRangeException(nameof(slideDuration));
            if (baseSpawnSpacing <= 0f)          throw new ArgumentOutOfRangeException(nameof(baseSpawnSpacing));
            if (minSpawnSpacing  <= 0f)          throw new ArgumentOutOfRangeException(nameof(minSpawnSpacing));

            _laneCount        = laneCount;
            _baseSpeed        = baseSpeed;
            _speedAccel       = speedAccel;
            _maxSpeed         = maxSpeed;
            _jumpDuration     = jumpDuration;
            _slideDuration    = slideDuration;
            _coinValue        = coinValue;
            _spawnLookAhead   = spawnLookAhead;
            _baseSpawnSpacing = baseSpawnSpacing;
            _minSpawnSpacing  = minSpawnSpacing;

            // Created once; Restart() intentionally does NOT re-seed so each run differs.
            _rng = rng ?? new SeededRandom(seed);

            _activeSpawns             = new List<SpawnInfo>();
            _laneLastObstacleDistance = new float[laneCount];

            Restart();
        }

        // ── Test injection ────────────────────────────────────────────────────────

        /// <summary>
        /// Override the runner's starting lane for deterministic tests.
        /// Call before <see cref="Restart"/>; the value persists across Restart calls.
        /// </summary>
        public void SetForcedLane(int lane) => _forcedLane = lane;

        /// <summary>
        /// Replace RNG-based spawn generation with a fixed set of spawns for collision tests.
        /// Spawns are sorted by <see cref="SpawnInfo.DistancePos"/> when <see cref="Restart"/>
        /// is called.  Pass <c>null</c> or an empty array to re-enable RNG spawning.
        /// Call before <see cref="Restart"/>.
        /// </summary>
        public void SetForcedSpawns(params SpawnInfo[] spawns) => _forcedSpawns = spawns;

        // ── Input API ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Queue a jump for the next <see cref="Tick"/>.
        /// Ignored if already airborne (no double-jump) or currently sliding.
        /// </summary>
        public void RequestJump()
        {
            if (State == RunnerState.Playing)
                _jumpPending = true;
        }

        /// <summary>
        /// Queue a slide for the next <see cref="Tick"/>.
        /// Ignored if the runner is airborne.  Restarts the slide timer when sliding.
        /// </summary>
        public void RequestSlide()
        {
            if (State == RunnerState.Playing)
                _slidePending = true;
        }

        /// <summary>
        /// Snap the runner one lane toward higher indices (clamp at laneCount-1).
        /// </summary>
        public void MoveLaneUp()
        {
            if (State == RunnerState.Playing)
                _lane = Math.Min(_laneCount - 1, _lane + 1);
        }

        /// <summary>
        /// Snap the runner one lane toward lower indices (clamp at 0).
        /// </summary>
        public void MoveLaneDown()
        {
            if (State == RunnerState.Playing)
                _lane = Math.Max(0, _lane - 1);
        }

        /// <summary>
        /// Move the runner by <paramref name="delta"/> lanes, clamped to [0, laneCount-1].
        /// Positive delta = toward higher indices; negative = toward lower.
        /// </summary>
        public void RequestLane(int delta)
        {
            if (State != RunnerState.Playing) return;
            _lane = Math.Max(0, Math.Min(_laneCount - 1, _lane + delta));
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Reset the simulation to the initial state and <see cref="RunnerState.Playing"/>.
        ///
        /// The internal RNG is NOT re-seeded — it continues its sequence so every run
        /// produces a distinct spawn pattern (no rote-learning at the cabinet).
        /// Forced lane and forced spawns (set via <see cref="SetForcedLane"/> /
        /// <see cref="SetForcedSpawns"/>) persist and are applied on each Restart.
        /// </summary>
        public void Restart()
        {
            State      = RunnerState.Playing;
            LostReason = RunnerLostReason.None;

            _distance       = 0f;
            _speed          = _baseSpeed;
            _coinsCollected = 0;

            _jumpTimer    = 0f;
            _jumpPending  = false;
            _slideTimer   = 0f;
            _slidePending = false;

            // Starting lane: forced value or the middle of the range.
            _lane = _forcedLane.HasValue
                ? Math.Max(0, Math.Min(_laneCount - 1, _forcedLane.Value))
                : _laneCount / 2;

            // Spawns.
            _activeSpawns.Clear();

            if (_forcedSpawns != null && _forcedSpawns.Length > 0)
            {
                // Forced-spawn path: copy and sort; disable RNG generator.
                _activeSpawns.AddRange(_forcedSpawns);
                _activeSpawns.Sort((a, b) => a.DistancePos.CompareTo(b.DistancePos));
                _nextSpawnDistance = float.MaxValue;
            }
            else
            {
                // RNG path: first spawn at one base-spacing ahead.
                _nextSpawnDistance = _baseSpawnSpacing;
                for (int l = 0; l < _laneCount; l++)
                    _laneLastObstacleDistance[l] = float.NegativeInfinity;
            }
        }

        // ── Per-frame update ──────────────────────────────────────────────────────

        /// <summary>
        /// Advance the simulation by <paramref name="dt"/> seconds.
        /// No-op once <see cref="State"/> is <see cref="RunnerState.Lost"/>.
        /// </summary>
        public void Tick(float dt)
        {
            if (State != RunnerState.Playing) return;
            if (dt <= 0f) return;

            // ── 1. Consume pending inputs ─────────────────────────────────────────
            //
            // Jump: queued jump starts the timer if grounded and not mid-slide.
            // A pending jump while airborne or sliding is discarded (no double-jump,
            // no jump-out-of-slide).
            if (_jumpPending)
            {
                if (_jumpTimer <= 0f && _slideTimer <= 0f)
                    _jumpTimer = _jumpDuration;
                _jumpPending = false;
            }

            // Slide: queued slide starts/restarts the timer if grounded.
            // Ignored while airborne (can't duck mid-jump).
            if (_slidePending)
            {
                if (_jumpTimer <= 0f)
                    _slideTimer = _slideDuration;
                _slidePending = false;
            }

            // ── 2. Snapshot vertical state for collision resolution ────────────────
            //
            // Capture AFTER consuming inputs but BEFORE advancing timers.
            // This means a RequestJump() + Tick() pair treats the runner as airborne
            // for the entire tick (generous / player-friendly rule).
            bool wasAirborne = IsAirborne;
            bool wasSliding  = IsSliding;

            // ── 3. Advance jump / slide timers ────────────────────────────────────
            if (_jumpTimer  > 0f) _jumpTimer  = MathF.Max(0f, _jumpTimer  - dt);
            if (_slideTimer > 0f) _slideTimer = MathF.Max(0f, _slideTimer - dt);

            // ── 4. Escalate speed (capped at maxSpeed) ────────────────────────────
            _speed = MathF.Min(_maxSpeed, _speed + _speedAccel * dt);

            // ── 5. Advance distance ───────────────────────────────────────────────
            float prevDistance = _distance;
            _distance += _speed * dt;

            // ── 6. Generate new spawns ahead (skipped when using forced spawns) ───
            if (_forcedSpawns == null || _forcedSpawns.Length == 0)
                GenerateSpawnsUpTo(_distance + _spawnLookAhead);

            // ── 7. Resolve spawns the runner crossed this tick ────────────────────
            ResolvePassedSpawns(prevDistance, wasAirborne, wasSliding);
        }

        // ── Spawn generation ──────────────────────────────────────────────────────

        /// <summary>
        /// Generates RNG-based spawns up to <paramref name="horizon"/> distance.
        ///
        /// Spacing formula: max(minSpawnSpacing, baseSpawnSpacing × baseSpeed / currentSpeed).
        /// As speed doubles, spacing halves → obstacles arrive more frequently.
        ///
        /// Fairness guarantee: when placing an obstacle, any lane whose most recent
        /// obstacle is within <c>safetyWindow = minSpawnSpacing × laneCount</c> distance
        /// units is excluded from selection.  This ensures there is always at least one
        /// survivable lane (or the coin lane) at any distance point.
        /// </summary>
        private void GenerateSpawnsUpTo(float horizon)
        {
            // safetyWindow: minimum look-back preventing all laneCount lanes
            // from being blocked simultaneously in a tight distance cluster.
            float safetyWindow = _minSpawnSpacing * _laneCount;

            while (_nextSpawnDistance <= horizon)
            {
                float dist     = _nextSpawnDistance;
                float kindRoll = _rng.NextFloat();

                SpawnInfo spawn;

                if (kindRoll < 0.4f)
                {
                    // 40 % coins — any lane, no fairness restriction needed.
                    int lane = _rng.NextInt(0, _laneCount);
                    spawn    = new SpawnInfo(dist, lane, SpawnKind.Coin);
                }
                else
                {
                    // 60 % obstacles (30 % low, 30 % high).
                    SpawnKind kind = kindRoll < 0.7f ? SpawnKind.LowObstacle : SpawnKind.HighObstacle;

                    // Build an eligible-lane pool: exclude lanes whose last obstacle is
                    // within the safety window — prevents all-lanes-blocked clusters.
                    // Use a fixed-size local array to avoid heap allocation per tick.
                    int   eligibleCount = 0;
                    int[] eligible      = new int[_laneCount]; // small, stack-friendly

                    for (int l = 0; l < _laneCount; l++)
                    {
                        if (dist - _laneLastObstacleDistance[l] >= safetyWindow)
                            eligible[eligibleCount++] = l;
                    }

                    // Defensive fallback: if somehow every lane is within the window
                    // (can only occur if safetyWindow < actual gap — config error),
                    // allow any lane so the generator never stalls.
                    if (eligibleCount == 0)
                    {
                        for (int l = 0; l < _laneCount; l++)
                            eligible[eligibleCount++] = l;
                    }

                    int chosenLane = eligible[_rng.NextInt(0, eligibleCount)];
                    _laneLastObstacleDistance[chosenLane] = dist;

                    spawn = new SpawnInfo(dist, chosenLane, kind);
                }

                _activeSpawns.Add(spawn);

                // Tighten spacing proportionally as speed rises, floored at minSpawnSpacing.
                float spacing = MathF.Max(
                    _minSpawnSpacing,
                    _baseSpawnSpacing * _baseSpeed / _speed);

                _nextSpawnDistance += spacing;
            }
        }

        /// <summary>
        /// Resolves all spawns the runner crossed in the range
        /// (<paramref name="prevDistance"/>, <see cref="_distance"/>].
        ///
        /// Spawns in the runner's current lane trigger a collision or collection.
        /// Spawns in other lanes are silently removed (runner passed through; no interaction).
        /// All resolved spawns are removed from <see cref="_activeSpawns"/>.
        ///
        /// <paramref name="wasAirborne"/> and <paramref name="wasSliding"/> are the vertical
        /// state snapshot captured before timer advancement this tick (see Tick step 2).
        /// </summary>
        private void ResolvePassedSpawns(float prevDistance, bool wasAirborne, bool wasSliding)
        {
            int removeCount = 0;

            for (int i = 0; i < _activeSpawns.Count; i++)
            {
                var s = _activeSpawns[i];

                // _activeSpawns is sorted ascending; once we reach a spawn past
                // the current distance we know all remaining are also ahead.
                if (s.DistancePos > _distance) break;

                // Count this spawn for removal regardless of lane or state.
                removeCount++;

                // Already past before this tick? (shouldn't happen in normal operation
                // since we remove on first pass — guard defensively)
                if (s.DistancePos <= prevDistance) continue;

                // Spawn in a different lane: runner passes through without interaction.
                if (s.Lane != _lane) continue;

                // Already lost from an earlier spawn in this same tick: skip resolution
                // but still count for removal so we clean up the entire crossed range.
                if (State != RunnerState.Playing) continue;

                // Same lane — apply the collision rule for this spawn kind.
                switch (s.Kind)
                {
                    case SpawnKind.LowObstacle:
                        // Safe only if the runner was airborne when the obstacle was reached.
                        if (!wasAirborne)
                        {
                            State      = RunnerState.Lost;
                            LostReason = RunnerLostReason.HitObstacle;
                        }
                        break;

                    case SpawnKind.HighObstacle:
                        // Safe only if the runner was sliding when the obstacle was reached.
                        if (!wasSliding)
                        {
                            State      = RunnerState.Lost;
                            LostReason = RunnerLostReason.HitObstacle;
                        }
                        break;

                    case SpawnKind.Coin:
                        _coinsCollected += _coinValue;
                        break;
                }
            }

            // Bulk-remove all spawns that were passed this tick.
            if (removeCount > 0)
                _activeSpawns.RemoveRange(0, removeCount);
        }
    }
}
