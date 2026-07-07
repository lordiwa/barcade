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

        private int _tick;
        private bool _isFinished;

        // Per-seat state.
        private readonly Direction8[] _aim = new Direction8[SeatCount];
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
            _renderState = new RenderState(SeatCount + parameters.TargetCount, FeedbackCapacity);

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
            // GDD §4 defines no difficulty-scaled parameter for MECH_04; difficultyMult
            // is accepted for interface compliance only and currently has no effect.

            _interpreter.Reset();
            _tick = 0;
            _isFinished = false;
            _durationTicks = (int)MathF.Round(_params.DurationSeconds * _params.InputConfig.TicksPerSecond);
            _flightTicks = (int)MathF.Round(_params.ProjectileFlightSeconds * _params.InputConfig.TicksPerSecond);

            for (int i = 0; i < SeatCount; i++)
            {
                _aim[i] = DefaultAimFor(AllSlots[i]);
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

            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;

                Direction8 raw = _interpreter.RawDirection(AllSlots[i]);
                if (raw != Direction8.None) _aim[i] = raw;

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

                if (_targetX[i] < _params.CentralZoneMin) { _targetX[i] = _params.CentralZoneMin + (_params.CentralZoneMin - _targetX[i]); _targetVX[i] = -_targetVX[i]; }
                else if (_targetX[i] > _params.CentralZoneMax) { _targetX[i] = _params.CentralZoneMax - (_targetX[i] - _params.CentralZoneMax); _targetVX[i] = -_targetVX[i]; }

                if (_targetY[i] < _params.CentralZoneMin) { _targetY[i] = _params.CentralZoneMin + (_params.CentralZoneMin - _targetY[i]); _targetVY[i] = -_targetVY[i]; }
                else if (_targetY[i] > _params.CentralZoneMax) { _targetY[i] = _params.CentralZoneMax - (_targetY[i] - _params.CentralZoneMax); _targetVY[i] = -_targetVY[i]; }
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
                if (!_roster.IsActive(AllSlots[i])) continue;

                (float cx, float cy) = TurretCorner(AllSlots[i]);
                (float dx, float dy) = DirectionToUnit(_aim[i]);

                _renderState.Entities[entityCount].Kind = EntityKind.PlayerAvatar;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = cx;
                _renderState.Entities[entityCount].Y = cy;
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = MathF.Atan2(dy, dx) * (180f / MathF.PI);
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = 0;
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
                entityCount++;
            }

            _renderState.EntityCount = entityCount;
        }

        /// <summary>
        /// Adapts this tick's v2 <see cref="InputSnapshot.Players"/> array into
        /// <see cref="IReadOnlyPlayerInputs"/> (v1, per-seat) so the internal
        /// <see cref="InputInterpreter"/> can be reused unchanged — identical
        /// approach and rationale to <c>ReaccionaMicrogame</c>'s private bridge
        /// (duplicated here rather than shared, to avoid touching
        /// ReaccionaMicrogame.cs again while its review is in flight).
        /// </summary>
        private sealed class InputBridge : IReadOnlyPlayerInputs
        {
            private PlayerInput[] _players;

            public void SetSource(PlayerInput[] players) => _players = players;

            public Barcade.Core.InputSnapshot For(PlayerSlot slot)
            {
                PlayerInput p = _players[(int)slot];
                (float x, float y) = DirectionToUnit(p.Stick);
                ButtonState state = p.Button ? ButtonState.Held : ButtonState.Released;
                return new Barcade.Core.InputSnapshot(x, y, state);
            }
        }
    }
}
