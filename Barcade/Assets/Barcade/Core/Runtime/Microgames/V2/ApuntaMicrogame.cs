using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_04 — ¡APUNTA! (hold-charge aiming). v2 <see cref="IMicrogame"/>
    /// implementation (see that interface's doc for the v1/v2 coexistence
    /// rationale). The existing v1 <c>Barcade.Core.ApuntaMicrogame</c> (TASK-011,
    /// a much simpler single-shot aim-and-fire mechanic) is untouched.
    ///
    /// <para>
    /// <b>Simulation model.</b> Each seat has a fixed turret at one corner of the
    /// logical [0,1]² arena (<see cref="TurretCorner"/>) and an aim direction — one
    /// of the 8 <see cref="Direction8"/> values, read live from the stick every
    /// tick. Holding the button charges an oscillating meter,
    /// <c>p(t) = 0.5*(1 + sin(ω·t_hold))</c> with <c>ω = 2π/ChargeCycleSeconds</c>
    /// (GDD §4) — note this literally starts at 0.5, not 0: it is a "stop the
    /// needle" timing meter, not a "hold longer for more power" ramp. Releasing
    /// fires a projectile whose landing point is computed analytically at the
    /// instant of release (not simulated tick-by-tick): distance is linear in
    /// power between <see cref="ApuntaParams.ProjectileSpeedMin"/> and
    /// <see cref="ApuntaParams.ProjectileSpeedMax"/>, and (if <c>WindAccel</c> != 0)
    /// a deterministic +X drift proportional to that distance is added. The
    /// projectile then simply waits in a fixed-size pool until a fixed
    /// <c>ProjectileFlightSeconds</c> later, when it is resolved against any
    /// still-remaining (not yet consumed) target.
    /// </para>
    ///
    /// <para>
    /// <b>Multi-shot, not one-shot.</b> Unlike ¡REACCIONA! (T-103, one decision
    /// per tanda), APUNTA is continuous over the whole round: a seat may charge
    /// and fire repeatedly (GDD §4 "Victoria: más impactos en duration").
    /// </para>
    ///
    /// <para>
    /// <b>Turret corners and cold-start aim (design decision).</b> Corners are
    /// assigned Rojo=(0,0), Azul=(1,0), Amarillo=(1,1), Verde=(0,1) — clockwise in
    /// <c>PlayerSlot</c> order. Before the stick is ever touched (or whenever it
    /// returns to <see cref="Direction8.None"/>), the aim keeps its last non-None
    /// value; before any non-None value has ever been seen, it defaults to this
    /// seat's corner-to-arena-center diagonal (Rojo→NE, Azul→NW, Amarillo→SW,
    /// Verde→SE) so a turret never starts facing out of the arena. Neither rule is
    /// specified by the GDD text; both are documented per-orchestrator's design
    /// note for this ticket ("decide + document what happens while stick is None").
    /// </para>
    ///
    /// <para>
    /// <b>GDD gaps filled (documented assumptions, flagged for reviewer):</b>
    /// <see cref="ApuntaParams.HitRadius"/>, the central target zone bounds, and
    /// <see cref="ApuntaParams.ProjectileFlightSeconds"/> are not named anywhere in
    /// GDD §4 — only "objetivos en zona central" and "proyectil balístico" are
    /// specified, with no numbers. <see cref="ApuntaParams.GddDefaults"/> picks a
    /// central zone [0.4,0.6]² and a hitRadius/speed range verified (by the
    /// reachability solver test) to make every seeded target reachable from all
    /// 4 corners — see the worked geometry below. Flight time is a FIXED duration
    /// for every shot regardless of power/distance (decoupling "how far" from
    /// "how long to arrive"), the simplest model that satisfies every AC without
    /// requiring predictive interception for moving targets.
    /// </para>
    ///
    /// <para>
    /// <b>Reachability geometry (why the GddDefaults numbers work).</b> With
    /// corners at the exact unit-square corners and a central zone symmetric
    /// around (0.5,0.5), each corner's exact diagonal ray (one of the 8 discrete
    /// directions) passes through the zone's center, and — because the zone is
    /// tight enough relative to the 45° spacing between adjacent discrete
    /// directions — every point in a [0.4,0.6]² zone is closest to that single
    /// diagonal ray, with a worst-case perpendicular distance from the ray of
    /// about 0.14 logical units (occurring at the zone's off-diagonal corners,
    /// e.g. (0.4,0.6) seen from (0,0)). <c>HitRadius</c> = 0.18 clears that with
    /// margin, and <c>[ProjectileSpeedMin, ProjectileSpeedMax]</c> = [0.45, 0.95]
    /// covers the full range of distances from any corner to any point in the
    /// zone (≈0.566 to ≈0.849).
    /// </para>
    ///
    /// <para>
    /// <b>Same-tick contested target (GDD AC edge case).</b> When multiple
    /// projectiles arrive on the same tick and are candidates (within
    /// <c>HitRadius</c>) for overlapping targets, resolution is a greedy
    /// nearest-pair match: repeatedly assign the single globally-closest
    /// (arriving projectile, remaining target) pair, remove both from
    /// consideration, and repeat. This is exactly "the more precise one scores,
    /// the other passes to the next remaining target if any" (GDD §4) — a losing
    /// projectile's next-best remaining target becomes its new best candidate on
    /// the next iteration of the same resolution pass.
    /// </para>
    ///
    /// <para>
    /// <b>Ranking.</b> <c>PlayerRank.Metric</c> = hit count (the GDD's stated
    /// primary criterion, directly telemetry-meaningful). Place is decided by
    /// (hit count descending, summed precision — distance to target center,
    /// lower is better — ascending) with standard competition ranking (ties share
    /// a place; the next distinct group skips ahead), matching
    /// <c>ReaccionaMicrogame</c>'s ranking convention.
    /// </para>
    ///
    /// Pure C# — no UnityEngine dependency. C# 9 compatible. Zero heap allocation
    /// in steady-state <see cref="Tick"/> — targets, the projectile pool, and all
    /// per-seat state are fixed-size arrays allocated once in the constructor.
    /// </summary>
    public sealed class ApuntaMicrogame : IMicrogame
    {
        private const int SeatCount = 4;
        private const int PoolCapacity = 32;
        private const int FeedbackCapacity = 16;

        /// <summary>
        /// GDD §4: the stick's 8 discrete directions are "interpoladas a 0.25s
        /// para sensación analógica" — presentation-layer interpolation only (the
        /// sim itself snaps to the discrete angle instantly). This is the window
        /// <see cref="RenderEntity.Progress01"/> encodes progress over, on
        /// PlayerAvatar entities ([0,1], TASK-048 — previously packed 0..255 into
        /// <see cref="RenderEntity.VisualVariant"/>, moved off it since that field
        /// is GDD §10.4's discrete prefab/skin selector, not a scalar channel). A
        /// private constant rather than an <see cref="ApuntaParams"/> field: it's a
        /// presentation hint the sim exposes for the presenter's convenience, not a
        /// value that changes sim outcomes or determinism.
        /// </summary>
        private const float AimInterpSeconds = 0.25f;

        public const byte CueFired = 1;
        public const byte CueHit = 2;
        public const byte CueMiss = 3;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly ApuntaParams _params;
        private readonly InputInterpreter _interpreter;
        private readonly InputBridge _inputBridge = new InputBridge();
        private readonly RenderState _renderState;

        private SeededRandom _rng;
        private PlayerRoster _roster;
        private int _durationTicks;
        private int _flightTicks;
        private int _aimInterpTicks;

        private int _tick;
        private bool _isFinished;

        // Per-seat state.
        private readonly Direction8[] _aim = new Direction8[SeatCount];
        private readonly int[] _aimChangeTick = new int[SeatCount];
        private readonly int[] _chargeTicks = new int[SeatCount];
        private readonly int[] _hitCount = new int[SeatCount];
        private readonly int[] _shotsFired = new int[SeatCount];
        private readonly long[] _summedPrecisionMicros = new long[SeatCount];
        private readonly float[] _lastLandingX = new float[SeatCount];
        private readonly float[] _lastLandingY = new float[SeatCount];

        // Targets (fixed-size, sized to ApuntaParams.TargetCount at construction).
        private readonly float[] _targetX;
        private readonly float[] _targetY;
        private readonly float[] _targetVX;
        private readonly float[] _targetVY;
        private readonly bool[] _targetConsumed;

        // Projectile pool (fixed-size, zero-alloc).
        private readonly bool[] _poolActive = new bool[PoolCapacity];
        private readonly int[] _poolOwnerSeat = new int[PoolCapacity];
        private readonly float[] _poolLaunchX = new float[PoolCapacity];
        private readonly float[] _poolLaunchY = new float[PoolCapacity];
        private readonly float[] _poolLandingX = new float[PoolCapacity];
        private readonly float[] _poolLandingY = new float[PoolCapacity];
        private readonly int[] _poolLaunchTick = new int[PoolCapacity];
        private readonly int[] _poolArrivalTick = new int[PoolCapacity];

        // Scratch buffers for same-tick arrival resolution (zero-alloc reuse).
        private readonly int[] _scratchArriving = new int[PoolCapacity];
        private readonly bool[] _scratchResolved = new bool[PoolCapacity];

        public ApuntaMicrogame() : this(ApuntaParams.GddDefaults)
        {
        }

        public ApuntaMicrogame(ApuntaParams parameters)
        {
            _params = parameters;
            _interpreter = new InputInterpreter(parameters.InputConfig);
            // MEDIUM-2(a) fix: capacity must also cover in-flight Projectile
            // entities (up to PoolCapacity of them) so publishing them stays
            // zero-alloc — the array is sized once here, never resized.
            _renderState = new RenderState(SeatCount + parameters.TargetCount + PoolCapacity, FeedbackCapacity);

            _targetX = new float[parameters.TargetCount];
            _targetY = new float[parameters.TargetCount];
            _targetVX = new float[parameters.TargetCount];
            _targetVY = new float[parameters.TargetCount];
            _targetConsumed = new bool[parameters.TargetCount];
        }

        /// <inheritdoc/>
        public MicrogameId Id => MicrogameId.Apunta;

        /// <inheritdoc/>
        public bool IsFinished => _isFinished;

        /// <inheritdoc/>
        public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _rng = rng;
            _roster = roster;
            // Fix (TASK-026 review, LOW-4): GDD §11.1's example definition for
            // MECH_04 declares difficultyScaling: ["targetMoving.speed", "windAccel"]
            // — the GDD DOES specify which params scale with difficulty for this
            // mechanic. Wiring difficultyMult to actually scale TargetMovingSpeed/
            // WindAccel is deferred to T-106 (the v2 MicrogameDefinition data
            // migration), since difficultyScaling is a per-definition-field concept
            // that lives in that data layer, which hasn't landed yet — not because
            // GDD lacks the concept. difficultyMult is accepted here for interface
            // compliance only and currently has no effect.

            _interpreter.Reset();
            _tick = 0;
            _isFinished = false;
            _durationTicks = (int)MathF.Round(_params.DurationSeconds * _params.InputConfig.TicksPerSecond);
            _flightTicks = (int)MathF.Round(_params.ProjectileFlightSeconds * _params.InputConfig.TicksPerSecond);
            _aimInterpTicks = (int)MathF.Round(AimInterpSeconds * _params.InputConfig.TicksPerSecond);

            for (int i = 0; i < SeatCount; i++)
            {
                _aim[i] = DefaultAimFor(AllSlots[i]);
                // Cold-start aim is already "settled" — not mid-interpolation —
                // so back-date the change tick far enough that elapsed >= the
                // interp window immediately (avoiding int overflow in the
                // subtraction at any realistic _tick value).
                _aimChangeTick[i] = int.MinValue / 2;
                _chargeTicks[i] = 0;
                _hitCount[i] = 0;
                _shotsFired[i] = 0;
                _summedPrecisionMicros[i] = 0;
                _lastLandingX[i] = float.NaN;
                _lastLandingY[i] = float.NaN;
            }

            for (int i = 0; i < PoolCapacity; i++) _poolActive[i] = false;

            for (int i = 0; i < _params.TargetCount; i++)
            {
                float span = _params.CentralZoneMax - _params.CentralZoneMin;
                _targetX[i] = _params.CentralZoneMin + _rng.NextFloat() * span;
                _targetY[i] = _params.CentralZoneMin + _rng.NextFloat() * span;
                _targetConsumed[i] = false;

                if (_params.TargetMovingEnabled)
                {
                    Direction2D dir = _rng.NextDirection();
                    _targetVX[i] = dir.X * _params.TargetMovingSpeed;
                    _targetVY[i] = dir.Y * _params.TargetMovingSpeed;
                }
                else
                {
                    _targetVX[i] = 0f;
                    _targetVY[i] = 0f;
                }
            }
        }

        /// <inheritdoc/>
        public void Tick(in InputSnapshot input)
        {
            if (_isFinished) return;
            if (input.Players == null) throw new ArgumentException("InputSnapshot.Players must not be null.", nameof(input));

            _inputBridge.SetSource(input.Players);
            _interpreter.Tick(_inputBridge);
            _renderState.FeedbackCount = 0;

            MoveTargets();

            // Fix (TASK-026 review, HIGH-1): charge accumulation and all firing
            // (release-triggered and the timeout auto-fire alike) are gated on
            // _tick < _durationTicks. Overtime ticks (after duration expires but
            // before AnyProjectileActive() goes false, kept open so already-fired
            // shots can still land) must only run MoveTargets/ResolveArrivals/
            // PublishRenderState. Without this gate: (A) holding through the
            // auto-fire tick and releasing a beat later — the natural human
            // reaction — would fire a SECOND shot from the same physical hold,
            // since HoldDurationTicks keeps counting and the per-seat loop keeps
            // refreshing _chargeTicks every tick regardless of duration; (B)
            // continuous mashing through overtime would keep minting new
            // in-flight projectiles faster than they resolve, so
            // AnyProjectileActive() (which IsFinished waits on) never goes false —
            // a permanent hang for whatever sequencer eventually drives this
            // microgame. No new shots can start after duration expires, so any
            // in-flight projectile is bounded by ProjectileFlightSeconds from its
            // firing tick (which was < _durationTicks), guaranteeing termination.
            if (_tick < _durationTicks)
            {
                for (int i = 0; i < SeatCount; i++)
                {
                    if (!_roster.IsActive(AllSlots[i])) continue;

                    Direction8 raw = _interpreter.RawDirection(AllSlots[i]);
                    if (raw != Direction8.None && raw != _aim[i])
                    {
                        _aim[i] = raw;
                        _aimChangeTick[i] = _tick;
                    }

                    if (_interpreter.ButtonReleasedThisTick(AllSlots[i]))
                    {
                        Fire(i, _chargeTicks[i]);
                        _chargeTicks[i] = 0;
                    }
                    else
                    {
                        _chargeTicks[i] = _interpreter.HoldDurationTicks(AllSlots[i]);
                    }
                }

                // Timeout auto-fire (GDD §4 edge case): a seat still charging (never
                // released) when the round's last active tick is reached fires now,
                // with whatever power it currently holds.
                if (_tick == _durationTicks - 1)
                {
                    for (int i = 0; i < SeatCount; i++)
                    {
                        if (!_roster.IsActive(AllSlots[i])) continue;
                        if (_chargeTicks[i] > 0)
                        {
                            Fire(i, _chargeTicks[i]);
                            _chargeTicks[i] = 0;
                        }
                    }
                }
            }

            ResolveArrivals();
            PublishRenderState();

            _tick++;
            if (_tick >= _durationTicks && !AnyProjectileActive())
                _isFinished = true;
        }

        /// <inheritdoc/>
        public MicrogameResult GetResult()
        {
            if (!_isFinished)
                throw new InvalidOperationException("GetResult() called before IsFinished.");

            int n = 0;
            for (int i = 0; i < SeatCount; i++)
                if (_roster.IsActive(AllSlots[i])) n++;

            int[] seats = new int[n];
            int[] hits = new int[n];
            long[] precisions = new long[n];
            int w = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                seats[w] = i;
                hits[w] = _hitCount[i];
                precisions[w] = _summedPrecisionMicros[i];
                w++;
            }

            // Insertion sort, best-first: hits descending, precision (lower = better) ascending.
            for (int i = 1; i < n; i++)
            {
                int seatKey = seats[i], hitKey = hits[i];
                long precKey = precisions[i];
                int j = i - 1;
                while (j >= 0 && IsBetter(hitKey, precKey, hits[j], precisions[j]))
                {
                    seats[j + 1] = seats[j];
                    hits[j + 1] = hits[j];
                    precisions[j + 1] = precisions[j];
                    j--;
                }
                seats[j + 1] = seatKey;
                hits[j + 1] = hitKey;
                precisions[j + 1] = precKey;
            }

            var ranks = new PlayerRank[n];
            for (int i = 0; i < n; i++)
            {
                int place;
                if (i == 0) place = 1;
                else if (hits[i] == hits[i - 1] && precisions[i] == precisions[i - 1]) place = ranks[i - 1].Place;
                else place = i + 1;

                ranks[i] = new PlayerRank(seats[i], place, hits[i]);
            }

            return new MicrogameResult(ResultKind.Ranked, ranks, 0);
        }

        /// <inheritdoc/>
        public RenderState GetRenderState() => _renderState;

        // ------------------------------------------------------------------
        // Static, stateless helpers (usable standalone; also drive the solver test)
        // ------------------------------------------------------------------

        /// <summary>Each seat's fixed turret position — a corner of the logical [0,1]² arena.</summary>
        public static (float X, float Y) TurretCorner(PlayerSlot slot)
        {
            switch (slot)
            {
                case PlayerSlot.Rojo: return (0f, 0f);
                case PlayerSlot.Azul: return (1f, 0f);
                case PlayerSlot.Amarillo: return (1f, 1f);
                case PlayerSlot.Verde: return (0f, 1f);
                default: return (0.5f, 0.5f);
            }
        }

        /// <summary>Unit vector for one of the 8 discrete aim directions; (0,0) for <see cref="Direction8.None"/>.</summary>
        public static (float X, float Y) DirectionToUnit(Direction8 d)
        {
            const float diag = 0.70710678f; // 1/sqrt(2)
            switch (d)
            {
                case Direction8.N: return (0f, 1f);
                case Direction8.S: return (0f, -1f);
                case Direction8.E: return (1f, 0f);
                case Direction8.W: return (-1f, 0f);
                case Direction8.NE: return (diag, diag);
                case Direction8.SE: return (diag, -diag);
                case Direction8.NW: return (-diag, diag);
                case Direction8.SW: return (-diag, -diag);
                default: return (0f, 0f);
            }
        }

        /// <summary>
        /// GDD §4 oscillating charge meter: <c>p(t) = 0.5*(1 + sin(w*t_hold))</c>,
        /// <c>w = 2π/chargeCycleSeconds</c>. Pure function of hold duration — the
        /// same <paramref name="holdTicks"/> always yields the same power.
        /// </summary>
        public static float ChargePower(int holdTicks, float chargeCycleSeconds, int ticksPerSecond)
        {
            float tHold = (float)holdTicks / ticksPerSecond;
            float omega = 2f * MathF.PI / chargeCycleSeconds;
            return 0.5f * (1f + MathF.Sin(omega * tHold));
        }

        private static Direction8 DefaultAimFor(PlayerSlot slot)
        {
            switch (slot)
            {
                case PlayerSlot.Rojo: return Direction8.NE;
                case PlayerSlot.Azul: return Direction8.NW;
                case PlayerSlot.Amarillo: return Direction8.SW;
                case PlayerSlot.Verde: return Direction8.SE;
                default: return Direction8.N;
            }
        }

        private static bool IsBetter(int hitsA, long precA, int hitsB, long precB)
        {
            if (hitsA != hitsB) return hitsA > hitsB;
            return precA < precB;
        }

        // ------------------------------------------------------------------
        // Test/debug accessors (public — same convention as v1 mechanics, e.g.
        // AporreaMicrogame.GetPressCount / ApuntaMicrogame(v1).GetAimX).
        // ------------------------------------------------------------------

        public Direction8 CurrentAim(PlayerSlot slot) => _aim[(int)slot];
        public int CurrentChargeTicks(PlayerSlot slot) => _chargeTicks[(int)slot];
        public int HitCount(PlayerSlot slot) => _hitCount[(int)slot];
        public int ShotsFired(PlayerSlot slot) => _shotsFired[(int)slot];
        public float LastLandingX(PlayerSlot slot) => _lastLandingX[(int)slot];
        public float LastLandingY(PlayerSlot slot) => _lastLandingY[(int)slot];

        // ------------------------------------------------------------------

        private void MoveTargets()
        {
            if (!_params.TargetMovingEnabled) return;

            float dt = 1f / _params.InputConfig.TicksPerSecond;
            for (int i = 0; i < _params.TargetCount; i++)
            {
                if (_targetConsumed[i]) continue;

                _targetX[i] += _targetVX[i] * dt;
                _targetY[i] += _targetVY[i] * dt;

                // Fix (TASK-026 review, MEDIUM-3): clamp into the zone rather than
                // mirror-reflecting the exact overshoot. The old formula
                // (newPos = boundary + (boundary - pos)) only converges when the
                // per-tick step is small relative to the zone span; when the step
                // approaches or exceeds the span — including the degenerate
                // zero-span zone the constructor already allows — each bounce
                // re-applies a full step on top of an already-"corrected" position,
                // so the drift roughly doubles bounce over bounce instead of
                // shrinking, diverging without bound. Clamping trades a small
                // amount of "lost" bounce energy (invisible at 60 ticks/sec for any
                // reasonable speed/span ratio) for a hard guarantee the target never
                // leaves the zone. A zero-span zone naturally becomes a stationary
                // target this way: every tick clamps straight back to the one point.
                if (_targetX[i] < _params.CentralZoneMin) { _targetX[i] = _params.CentralZoneMin; _targetVX[i] = MathF.Abs(_targetVX[i]); }
                else if (_targetX[i] > _params.CentralZoneMax) { _targetX[i] = _params.CentralZoneMax; _targetVX[i] = -MathF.Abs(_targetVX[i]); }

                if (_targetY[i] < _params.CentralZoneMin) { _targetY[i] = _params.CentralZoneMin; _targetVY[i] = MathF.Abs(_targetVY[i]); }
                else if (_targetY[i] > _params.CentralZoneMax) { _targetY[i] = _params.CentralZoneMax; _targetVY[i] = -MathF.Abs(_targetVY[i]); }
            }
        }

        private void Fire(int seatIndex, int chargeTicksAtRelease)
        {
            float power = ChargePower(chargeTicksAtRelease, _params.ChargeCycleSeconds, _params.InputConfig.TicksPerSecond);
            (float dx, float dy) = DirectionToUnit(_aim[seatIndex]);
            (float cx, float cy) = TurretCorner(AllSlots[seatIndex]);

            float distance = _params.ProjectileSpeedMin + power * (_params.ProjectileSpeedMax - _params.ProjectileSpeedMin);
            float landX = cx + dx * distance + _params.WindAccel * distance;
            float landY = cy + dy * distance;

            _shotsFired[seatIndex]++;
            _lastLandingX[seatIndex] = landX;
            _lastLandingY[seatIndex] = landY;

            int poolIdx = FindFreePoolSlot();
            if (poolIdx >= 0)
            {
                _poolActive[poolIdx] = true;
                _poolOwnerSeat[poolIdx] = seatIndex;
                _poolLaunchX[poolIdx] = cx;
                _poolLaunchY[poolIdx] = cy;
                _poolLandingX[poolIdx] = landX;
                _poolLandingY[poolIdx] = landY;
                _poolLaunchTick[poolIdx] = _tick;
                _poolArrivalTick[poolIdx] = _tick + _flightTicks;
            }
            // Pool exhausted (extreme spam beyond PoolCapacity in-flight shots
            // simultaneously): the shot is simply not tracked for scoring, a
            // defensive guard mirroring InputInterpreter's mash-ring overflow rule.

            EmitFeedback(seatIndex, FeedbackLevel.Medium, CueFired);
        }

        private void ResolveArrivals()
        {
            int arrivingCount = 0;
            for (int i = 0; i < PoolCapacity; i++)
                if (_poolActive[i] && _poolArrivalTick[i] == _tick)
                    _scratchArriving[arrivingCount++] = i;

            if (arrivingCount == 0) return;

            for (int k = 0; k < arrivingCount; k++) _scratchResolved[k] = false;

            while (true)
            {
                int bestLocal = -1, bestTarget = -1;
                float bestDist = float.MaxValue;

                for (int k = 0; k < arrivingCount; k++)
                {
                    if (_scratchResolved[k]) continue;
                    int poolIdx = _scratchArriving[k];

                    for (int ti = 0; ti < _params.TargetCount; ti++)
                    {
                        if (_targetConsumed[ti]) continue;

                        float ddx = _poolLandingX[poolIdx] - _targetX[ti];
                        float ddy = _poolLandingY[poolIdx] - _targetY[ti];
                        float dist = MathF.Sqrt(ddx * ddx + ddy * ddy);

                        if (dist <= _params.HitRadius && dist < bestDist)
                        {
                            bestDist = dist;
                            bestLocal = k;
                            bestTarget = ti;
                        }
                    }
                }

                if (bestLocal < 0) break;

                int winningPoolIdx = _scratchArriving[bestLocal];
                int seatIdx = _poolOwnerSeat[winningPoolIdx];
                _hitCount[seatIdx]++;
                _summedPrecisionMicros[seatIdx] += (long)MathF.Round(bestDist * 1_000_000f);
                _targetConsumed[bestTarget] = true;
                _scratchResolved[bestLocal] = true;
                EmitFeedback(seatIdx, FeedbackLevel.High, CueHit);
            }

            for (int k = 0; k < arrivingCount; k++)
            {
                int poolIdx = _scratchArriving[k];
                if (!_scratchResolved[k])
                    EmitFeedback(_poolOwnerSeat[poolIdx], FeedbackLevel.Low, CueMiss);
                _poolActive[poolIdx] = false;
            }
        }

        private int FindFreePoolSlot()
        {
            for (int i = 0; i < PoolCapacity; i++)
                if (!_poolActive[i]) return i;
            return -1;
        }

        private bool AnyProjectileActive()
        {
            for (int i = 0; i < PoolCapacity; i++)
                if (_poolActive[i]) return true;
            return false;
        }

        private void EmitFeedback(int seatIndex, FeedbackLevel level, byte cue)
        {
            if (_renderState.FeedbackCount >= _renderState.Feedback.Length) return;
            _renderState.Feedback[_renderState.FeedbackCount] = new FeedbackEvent(seatIndex, level, cue);
            _renderState.FeedbackCount++;
        }

        private void PublishRenderState()
        {
            _renderState.Tick = _tick;
            _renderState.Hud.ActiveVerb = "¡APUNTA!";

            int entityCount = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                _renderState.Hud.Scores[i] = _hitCount[i];
                // MEDIUM-2(b) fix: the live oscillating charge power while a seat
                // is actively holding — 0 while not charging. This is THE visual
                // the player times against (GDD §4 "medidor oscilante").
                _renderState.Hud.Meters[i] = _chargeTicks[i] > 0
                    ? ChargePower(_chargeTicks[i], _params.ChargeCycleSeconds, _params.InputConfig.TicksPerSecond)
                    : 0f;

                if (!_roster.IsActive(AllSlots[i])) continue;

                (float cx, float cy) = TurretCorner(AllSlots[i]);
                (float dx, float dy) = DirectionToUnit(_aim[i]);

                // MEDIUM-2(c) / TASK-048 fix: GDD §4's 0.25s discrete-angle
                // interpolation is a presentation concern — the sim snaps _aim
                // instantly and only exposes how far along that window we are.
                // This is a continuous [0,1] value, so it belongs in Progress01
                // (see AimInterpSeconds doc), not VisualVariant — GDD §10.4 defines
                // VisualVariant as the discrete EntityKind+VisualVariant->prefab
                // selector, not a scalar channel (see RenderEntity's own doc for
                // the full contract). The presenter, not the sim, does the actual
                // smoothing between the previous and current Rotation.
                int elapsed = _tick - _aimChangeTick[i];
                float progress = elapsed >= _aimInterpTicks ? 1f : (float)elapsed / _aimInterpTicks;
                if (progress < 0f) progress = 0f;

                _renderState.Entities[entityCount].Kind = EntityKind.PlayerAvatar;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = cx;
                _renderState.Entities[entityCount].Y = cy;
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = MathF.Atan2(dy, dx) * (180f / MathF.PI);
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = 0; // no discrete avatar state today; 0 = default/only skin
                _renderState.Entities[entityCount].Progress01 = progress;
                entityCount++;
            }

            for (int i = 0; i < _params.TargetCount; i++)
            {
                _renderState.Entities[entityCount].Kind = EntityKind.Target;
                _renderState.Entities[entityCount].OwnerSeat = -1;
                _renderState.Entities[entityCount].X = _targetX[i];
                _renderState.Entities[entityCount].Y = _targetY[i];
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = (byte)(_targetConsumed[i] ? 1 : 0);
                _renderState.Entities[entityCount].Progress01 = 0f; // Entities is a reused struct pool -- always set every field, never leave a stale value from a prior tick's occupant
                entityCount++;
            }

            // MEDIUM-2(a) fix: publish every in-flight shot as a Projectile entity
            // so the presenter has something to draw between launch and landing.
            // Position is linearly interpolated from launch to landing over the
            // shot's fixed flight time — cheap, deterministic, and consistent with
            // "trajectories are deterministic" (AC3) — not simulated tick-by-tick.
            for (int i = 0; i < PoolCapacity; i++)
            {
                if (!_poolActive[i]) continue;

                float t = (float)(_tick - _poolLaunchTick[i]) / _flightTicks;
                if (t < 0f) t = 0f;
                else if (t > 1f) t = 1f;

                _renderState.Entities[entityCount].Kind = EntityKind.Projectile;
                _renderState.Entities[entityCount].OwnerSeat = _poolOwnerSeat[i];
                _renderState.Entities[entityCount].X = _poolLaunchX[i] + (_poolLandingX[i] - _poolLaunchX[i]) * t;
                _renderState.Entities[entityCount].Y = _poolLaunchY[i] + (_poolLandingY[i] - _poolLaunchY[i]) * t;
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = 0;
                _renderState.Entities[entityCount].Progress01 = 0f;
                entityCount++;
            }

            _renderState.EntityCount = entityCount;
        }
    }
}
