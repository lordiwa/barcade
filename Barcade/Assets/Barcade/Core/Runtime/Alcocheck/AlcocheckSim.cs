using System;

namespace Barcade.Core.Alcocheck
{
    /// <summary>
    /// Deterministic simulation for the Alcocheck drunk-balance microgame demo.
    ///
    /// Models the player as an inverted pendulum: without corrective input the avatar
    /// tips further from upright (unstable equilibrium at Lean=0).  A random "drunk"
    /// torque re-rolls periodically, adding unpredictable sway.  The player applies a
    /// 1-D left/right corrective torque to stay upright long enough to win.
    ///
    /// Pure C# — no UnityEngine dependency.
    ///
    /// Coordinate convention:
    ///   Lean > 0  = tilting right.
    ///   Lean &lt; 0  = tilting left.
    ///   inputX    = [-1, 1]; negative = push left (corrective when Lean > 0).
    ///
    /// 4-player extension: instantiate one AlcocheckSim per player (no singletons).
    /// </summary>
    public sealed class AlcocheckSim
    {
        // ── Config ────────────────────────────────────────────────────────────────

        private readonly float _maxLean;             // fall threshold in radians
        private readonly float _gravityGain;         // inverted-pendulum instability coefficient
        private readonly float _playerTorque;        // corrective torque per unit inputX
        private readonly float _drunkTorque;         // peak magnitude of random sway torque
        private readonly float _drunkChangeInterval; // seconds between sway re-rolls
        private readonly float _damping;             // angular-velocity damping coefficient
        private readonly float _survivalDuration;    // seconds to survive for a win

        private readonly int           _seed;        // stored for Restart() re-seeding
        private readonly ISeededRandom _injectedRng; // non-null → use this; null → build from _seed
        private          ISeededRandom _rng;         // active RNG (assigned at Restart)

        // ── Simulation state ──────────────────────────────────────────────────────

        private float _lean;
        private float _leanVelocity;
        private float _survivalElapsed;

        // Drunk sway sub-state.
        private float _drunkTorqueNow;    // current sampled torque value (bounded ±_drunkTorque)
        private float _drunkPhaseElapsed; // time accumulated into the current sway interval

        // ── Test injection ────────────────────────────────────────────────────────

        private (float lean, float velocity)? _forcedLean;

        // ── Terminal state ────────────────────────────────────────────────────────

        /// <summary>Current play state.</summary>
        public AlcocheckState State { get; private set; }

        /// <summary>
        /// Reason for loss; <see cref="AlcocheckLostReason.None"/> while playing or won.
        /// </summary>
        public AlcocheckLostReason LostReason { get; private set; }

        // ── Public accessors ──────────────────────────────────────────────────────

        /// <summary>Current lean angle in radians. Positive = tilting right.</summary>
        public float Lean => _lean;

        /// <summary>Current angular velocity in radians per second.</summary>
        public float LeanVelocity => _leanVelocity;

        /// <summary>Seconds elapsed since last <see cref="Restart"/>.</summary>
        public float SurvivalElapsed => _survivalElapsed;

        /// <summary>
        /// Lean normalised to the fall threshold: <c>Lean / maxLean</c>.
        /// Ranges roughly in [-1, 1]; |LeanFraction| reaching 1 means the avatar toppled.
        /// Use the absolute value as a danger indicator for UI and visual effects.
        /// </summary>
        public float LeanFraction => _lean / _maxLean;

        /// <summary>
        /// Current random drunk-sway torque, bounded by ±drunkTorque.
        /// Re-rolls every <c>drunkChangeInterval</c> seconds from the seeded RNG.
        /// Exposed for visual effects (e.g. screen tilt) and determinism tests.
        /// </summary>
        public float DrunkTorqueNow => _drunkTorqueNow;

        // ── Construction ──────────────────────────────────────────────────────────

        /// <param name="maxLean">
        /// Fall threshold in radians (default 0.7854 ≈ π/4 = 45°).
        /// </param>
        /// <param name="gravityGain">
        /// Inverted-pendulum coefficient — how strongly lean amplifies away from upright
        /// (analogous to g/L of a physical pendulum).  Higher = more unstable.
        /// </param>
        /// <param name="playerTorque">Corrective angular torque per unit of inputX.</param>
        /// <param name="drunkTorque">Peak magnitude (±) of the random drunk-sway torque.</param>
        /// <param name="drunkChangeInterval">Seconds between sway re-rolls (must be &gt; 0).</param>
        /// <param name="damping">Linear damping on angular velocity per second.</param>
        /// <param name="survivalDuration">Seconds to remain upright to win.</param>
        /// <param name="seed">
        /// Seed for the internal <see cref="SeededRandom"/> (used when <paramref name="rng"/> is null).
        /// <see cref="Restart"/> re-seeds from this value so every round replays the same drunk sequence.
        /// </param>
        /// <param name="rng">
        /// Optional externally supplied RNG for deterministic tests.  When non-null, <paramref name="seed"/>
        /// is stored but not used for RNG creation.  Note: <see cref="Restart"/> does NOT reset an
        /// injected RNG — create a fresh <see cref="AlcocheckSim"/> for a clean sequence in tests.
        /// </param>
        public AlcocheckSim(
            float maxLean             = 0.7853982f,  // π/4 ≈ 45°
            float gravityGain         = 3.5f,
            float playerTorque        = 8f,
            float drunkTorque         = 4f,
            float drunkChangeInterval = 0.4f,
            float damping             = 1.2f,
            float survivalDuration    = 12f,
            int   seed                = 42,
            ISeededRandom rng         = null)
        {
            if (maxLean             <= 0f) throw new ArgumentOutOfRangeException(nameof(maxLean));
            if (drunkChangeInterval <= 0f) throw new ArgumentOutOfRangeException(nameof(drunkChangeInterval));
            if (survivalDuration    <= 0f) throw new ArgumentOutOfRangeException(nameof(survivalDuration));

            _maxLean             = maxLean;
            _gravityGain         = gravityGain;
            _playerTorque        = playerTorque;
            _drunkTorque         = drunkTorque;
            _drunkChangeInterval = drunkChangeInterval;
            _damping             = damping;
            _survivalDuration    = survivalDuration;
            _seed                = seed;
            _injectedRng         = rng;

            // Seed the RNG once at construction time.  Restart() intentionally does NOT
            // re-seed so every round continues the sequence → distinct sway each play.
            _rng = _injectedRng ?? new SeededRandom(_seed);

            Restart();
        }

        // ── Test injection ────────────────────────────────────────────────────────

        /// <summary>
        /// Override the initial lean angle and velocity for deterministic tests.
        /// Call before <see cref="Restart"/>.  The values persist across Restart calls.
        /// </summary>
        public void SetForcedLean(float lean, float leanVelocity = 0f)
            => _forcedLean = (lean, leanVelocity);

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Reset the simulation to the initial upright position (or forced lean) and
        /// <see cref="AlcocheckState.Playing"/>.
        ///
        /// The internal RNG is NOT re-seeded — it continues its sequence from where it
        /// left off so every subsequent round produces a distinct drunk-sway pattern
        /// (no rote-learning at the cabinet).  Construction-time determinism is
        /// preserved: two freshly-constructed sims with the same seed produce identical
        /// trajectories on their first round.
        /// </summary>
        public void Restart()
        {
            State      = AlcocheckState.Playing;
            LostReason = AlcocheckLostReason.None;

            _survivalElapsed   = 0f;
            _drunkTorqueNow    = 0f;
            // Prime phase so the very first Tick of each round triggers a re-roll.
            _drunkPhaseElapsed = _drunkChangeInterval;

            if (_forcedLean.HasValue)
            {
                _lean         = _forcedLean.Value.lean;
                _leanVelocity = _forcedLean.Value.velocity;
            }
            else
            {
                _lean         = 0f;
                _leanVelocity = 0f;
            }

            // _rng is intentionally NOT reset here; see ctor comment.
        }

        // ── Per-frame update ──────────────────────────────────────────────────────

        /// <summary>
        /// Advance the simulation by <paramref name="dt"/> seconds.
        ///
        /// <paramref name="inputX"/> is the corrective stick axis in [-1, 1]:
        ///   negative (left)  = corrective when Lean &gt; 0 (tilting right).
        ///   positive (right) = corrective when Lean &lt; 0 (tilting left).
        ///
        /// Dynamics (Euler integration each tick):
        /// <code>
        ///   angularAccel = gravityGain * sin(Lean)    // inverted-pendulum: pushes AWAY from upright
        ///                + drunkTorqueNow              // random sway (re-rolls every drunkChangeInterval)
        ///                + inputX * playerTorque       // player's corrective torque
        ///                - damping * LeanVelocity      // velocity damping
        ///   LeanVelocity += angularAccel * dt
        ///   Lean         += LeanVelocity  * dt
        /// </code>
        ///
        /// No-op once the state is <see cref="AlcocheckState.Won"/> or <see cref="AlcocheckState.Lost"/>.
        /// </summary>
        public void Tick(float dt, float inputX)
        {
            if (State != AlcocheckState.Playing) return;
            if (dt <= 0f) return;

            // Pre-integration guard: if lean was forced (or drifted) to or past the fall
            // threshold without a Tick, resolve immediately rather than integrating further.
            if (MathF.Abs(_lean) >= _maxLean)
            {
                State      = AlcocheckState.Lost;
                LostReason = AlcocheckLostReason.ToppledOver;
                return;
            }

            _survivalElapsed += dt;

            // ── Drunk sway re-roll ────────────────────────────────────────────────
            // Accumulate phase elapsed; re-roll whenever a full interval has passed.
            // The while loop handles the edge case where dt > drunkChangeInterval.
            _drunkPhaseElapsed += dt;
            while (_drunkPhaseElapsed >= _drunkChangeInterval)
            {
                _drunkPhaseElapsed -= _drunkChangeInterval;
                // Sample uniformly in [-drunkTorque, +drunkTorque].
                _drunkTorqueNow = (_rng.NextFloat() * 2f - 1f) * _drunkTorque;
            }

            // ── Inverted-pendulum dynamics (Euler) ────────────────────────────────
            //
            //   angularAccel = gravityGain * sin(Lean)   // unstable: pushes AWAY from upright
            //                + drunkTorqueNow             // random drunk sway
            //                + inputX * playerTorque      // player's corrective torque
            //                - damping * LeanVelocity     // angular-velocity damping
            //
            float angularAccel =
                _gravityGain * MathF.Sin(_lean)
                + _drunkTorqueNow
                + inputX * _playerTorque
                - _damping * _leanVelocity;

            _leanVelocity += angularAccel * dt;
            _lean         += _leanVelocity * dt;

            // ── Terminal checks ───────────────────────────────────────────────────

            if (MathF.Abs(_lean) >= _maxLean)
            {
                State      = AlcocheckState.Lost;
                LostReason = AlcocheckLostReason.ToppledOver;
                return;
            }

            if (_survivalElapsed >= _survivalDuration)
                State = AlcocheckState.Won;
        }
    }
}
