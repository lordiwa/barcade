using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_03 — ¡CORRE! (endless run). T-107 slice 2: v2
    /// <see cref="IMicrogame"/> implementation, adapting the existing
    /// <see cref="Barcade.Core.Runner.RunnerSim"/> demo onto the canonical contract.
    /// That RunnerSim (and its own tests) is untouched — conserved, not modified;
    /// this class mines its jump/obstacle-crossing structure but is a fresh,
    /// re-architected engine-free implementation on the v2 surface.
    ///
    /// <para>
    /// <b>Ruled scope (LOCKED for this slice).</b> RunnerSim's lane-switching,
    /// slide/duck, and HIGH obstacles are STRIPPED. ¡CORRE! is jump-only with one
    /// FIXED lane per player, and exactly one control scheme (GDD literal: "mash
    /// para correr, palanca-arriba para saltar — nunca al revés"): the button
    /// MASH accelerates (via the §3.3 mash curve), and a stick-up TAP jumps.
    /// Nothing here reads the button as a jump or the stick as acceleration; a
    /// lateral or downward stick has no effect at all (no lane change, no slide).
    /// </para>
    ///
    /// <para>
    /// <b>Velocity (GDD literal).</b> <c>v = vBase + vGain·mashNorm</c>, where
    /// <c>mashNorm</c> is the §3.3 response curve (<c>clamp01((f-2)/7)</c>) applied
    /// to the seat's mash frequency by the shared <see cref="InputInterpreter"/>
    /// (<see cref="Barcade.Core.InputInterpreter.MashForce"/>), reached via the
    /// canonical <see cref="InputBridge"/> — the same input pipeline every other v2
    /// mechanic uses. A seat is never fully stopped except while stunned: with no
    /// mash it still creeps at <c>vBase</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Jump (GDD literal + [ASSUMED] realization of "tap").</b> A rising edge of
    /// stick-up (any upward direction — N/NE/NW — [ASSUMED], "palanca arriba") while
    /// grounded and not stunned starts a jump lasting
    /// <see cref="CorreParams.JumpAirtimeSeconds"/>. "Tap" is realized as an edge:
    /// the stick must return to neutral (or a non-up direction) before another jump
    /// registers, so a held-up stick jumps exactly once, not continuously. The
    /// airborne snapshot is captured after consuming the jump but before the airtime
    /// counter decrements — so a jump requested on the very tick an obstacle is
    /// crossed still counts as airborne for that crossing (the generous,
    /// player-friendly rule, mirroring RunnerSim's own documented convention).
    /// </para>
    ///
    /// <para>
    /// <b>Obstacles: one shared track across all 4 lanes (GDD AC1, headline).</b>
    /// Unlike RunnerSim (which gives each player instance its OWN RNG stream), a
    /// single obstacle track is generated once per round from the injected
    /// <see cref="SeededRandom"/> and shared verbatim by every lane — "equidad
    /// total: todos ven la misma pista." Each seat crosses that one track at its own
    /// distance via a monotonic per-seat obstacle pointer. Obstacle spacing is a
    /// density-derived gap jittered by the RNG, floored at
    /// <see cref="CorreParams.MinObstacleGap"/> ([ASSUMED] jumpability floor: set
    /// above the worst-case airborne span so a single well-timed jump always clears
    /// one obstacle with grounded room to re-time before the next — see
    /// <c>CorreMicrogameV2Tests</c>'s 6 Hz-mash perfect-jump sweep).
    /// </para>
    ///
    /// <para>
    /// <b>Collision (GDD literal).</b> Crossing an obstacle while grounded halts the
    /// seat at that obstacle and applies a stun of
    /// <see cref="CorreParams.StunSeconds"/> (GDD literal 0.6 s) — NOT an
    /// elimination: the seat is frozen (velocity 0) for the stun, then resumes.
    /// Crossing while airborne clears it harmlessly.
    /// </para>
    ///
    /// <para>
    /// <b>Rubber-band (GDD literal + [ASSUMED] tie handling).</b> "El último recibe
    /// vBase +8%" — the last-place seat's base speed is multiplied by
    /// <c>(1 + rubberBandPct)</c>. Last place is the set of active seats at the
    /// minimum distance, evaluated at the start of each tick; a tie for last boosts
    /// ALL tied seats equally ([ASSUMED], the only symmetric reading — it is exactly
    /// what makes two players with identical mash stay identical, GDD AC3). The
    /// assist requires ≥2 active seats ([ASSUMED]: a solo runner has no rival to
    /// catch up to, so "el último" is vacuous and no boost applies).
    /// </para>
    ///
    /// <para>
    /// <b>Victory / ranking (GDD literal).</b> "Mayor distancia al agotar el tiempo":
    /// <see cref="PlayerRank.Metric"/> = distance in milli-units (directly
    /// telemetry-meaningful), ranked DESCENDING, with the standard
    /// shared-place-on-exact-tie rule already established by every other v2 mechanic.
    /// The <c>raceToFinish</c> variant ("primero en cruzar meta") is
    /// [ASSUMED SCOPE] out of scope for this slice — declared on
    /// <see cref="CorreParams.RaceToFinish"/> for schema completeness only, not read.
    /// </para>
    ///
    /// <para>
    /// <b>Difficulty scaling (GDD §9.1: "aplicado a velocidad/densidad").</b>
    /// <c>difficultyMult</c> multiplies <see cref="CorreParams.ObstacleDensity"/>
    /// ("densidad") directly at <see cref="Initialize"/> time — [ASSUMED] mapping
    /// (a runner's difficulty knob is track density; boosting player velocity would
    /// help, not challenge). The gap floor keeps even a high-density track jumpable.
    /// </para>
    ///
    /// Fixed 60 Hz tick (GDD §3.2/§10.2). Pure C# — no UnityEngine dependency. C# 9
    /// compatible. Zero heap allocation in steady-state <see cref="Tick"/> (all
    /// per-seat state is fixed-size arrays allocated once in the constructor; the
    /// obstacle track is allocated once in <see cref="Initialize"/>, never inside
    /// <see cref="Tick"/>). <see cref="GetResult"/> allocates once per round, the
    /// same accepted class as every other v2 mechanic.
    /// </summary>
    public sealed class CorreMicrogame : IMicrogame
    {
        private const int SeatCount = 4;
        private const int FeedbackCapacity = 4;
        // 4 lane avatars + 1 upcoming-obstacle marker per lane.
        private const int EntityCapacity = SeatCount + SeatCount;

        private const float TicksPerSecond = 60f;
        private const float DtSeconds = 1f / TicksPerSecond;

        /// <summary>Feedback cue: this tick, the named seat crossed an obstacle grounded and was stunned.</summary>
        public const byte CueStunned = 1;

        public const byte VariantRunning = 0;
        public const byte VariantAirborne = 1;
        public const byte VariantStunned = 2;

        /// <summary>
        /// [ASSUMED] presentation constant: the fixed screen-X the running avatars
        /// sit at while the track scrolls past them (a side-view runner convention).
        /// Presentation only — never affects the sim.
        /// </summary>
        private const float RunnerScreenX = 0.2f;

        /// <summary>[ASSUMED] presentation constant: look-ahead window (logical units) the next-obstacle marker is mapped across.</summary>
        private const float ObstacleLookAhead = 20f;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly CorreParams _params;
        private readonly InputInterpreter _interpreter;
        private readonly InputBridge _inputBridge = new InputBridge();
        private readonly RenderState _renderState;

        private SeededRandom _rng;
        private PlayerRoster _roster;

        private float _effectiveObstacleDensity;
        private float _rubberBandedVBase;
        private int _durationTicks;
        private int _jumpAirtimeTicks;
        private int _stunDurationTicks;

        private int _tick;
        private bool _isFinished;

        private readonly float[] _distance = new float[SeatCount];
        private readonly float[] _velocity = new float[SeatCount];
        private readonly int[] _airborneTicks = new int[SeatCount];
        private readonly int[] _stunRemaining = new int[SeatCount];
        private readonly int[] _nextObstacle = new int[SeatCount];
        private readonly bool[] _wasUp = new bool[SeatCount];

        // Shared obstacle track — one array, allocated per round in Initialize
        // (like MantenMicrogame's noise lattice), read-only during Tick.
        private float[] _obstaclePos;
        private int _obstacleCount;

        // Test injection (mirrors RunnerSim.SetForcedSpawns / Esquiva's
        // ForceSpawnHazardForTest) — a fixed track replacing RNG generation.
        private float[] _forcedObstacles;

        public CorreMicrogame() : this(CorreParams.GddDefaults)
        {
        }

        public CorreMicrogame(CorreParams parameters)
        {
            _params = parameters;
            _interpreter = new InputInterpreter(parameters.InputConfig);
            _renderState = new RenderState(EntityCapacity, FeedbackCapacity);
        }

        /// <summary>
        /// Replaces RNG obstacle generation with a fixed, ascending track for
        /// deterministic collision/jump tests. Call before <see cref="Initialize"/>.
        /// Pass an empty array for a clear track (no obstacles).
        /// </summary>
        public void SetForcedObstacles(params float[] positions) => _forcedObstacles = positions;

        /// <inheritdoc/>
        public MicrogameId Id => MicrogameId.Corre;

        /// <inheritdoc/>
        public bool IsFinished => _isFinished;

        /// <inheritdoc/>
        public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _rng = rng;
            _roster = roster;

            _tick = 0;
            _isFinished = false;
            _durationTicks = (int)MathF.Round(_params.DurationSeconds * TicksPerSecond);
            _jumpAirtimeTicks = (int)MathF.Round(_params.JumpAirtimeSeconds * TicksPerSecond);
            _stunDurationTicks = (int)MathF.Round(_params.StunSeconds * TicksPerSecond);

            _effectiveObstacleDensity = _params.ObstacleDensity * difficultyMult;
            _rubberBandedVBase = _params.VBase * (1f + _params.RubberBandPct);

            _interpreter.Reset();

            for (int i = 0; i < SeatCount; i++)
            {
                _distance[i] = 0f;
                _velocity[i] = 0f;
                _airborneTicks[i] = 0;
                _stunRemaining[i] = 0;
                _nextObstacle[i] = 0;
                _wasUp[i] = false;
            }

            GenerateObstacleTrack();
            PublishRenderState();
        }

        /// <inheritdoc/>
        public void Tick(in InputSnapshot input)
        {
            if (_isFinished) return;
            if (input.Players == null) throw new ArgumentException("InputSnapshot.Players must not be null.", nameof(input));

            _inputBridge.SetSource(input.Players);
            _interpreter.Tick(_inputBridge);
            _renderState.FeedbackCount = 0;

            // Rubber-band: last place = the active seats at the minimum distance,
            // evaluated from this tick's starting distances. Only meaningful with
            // ≥2 active seats.
            int activeCount = 0;
            float minDistance = float.MaxValue;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                activeCount++;
                if (_distance[i] < minDistance) minDistance = _distance[i];
            }
            bool rubberBandEnabled = activeCount >= 2;

            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;

                // Up-edge tracking runs every tick (even while stunned) so a
                // stick held through a stun does not auto-jump on recovery.
                Direction8 raw = _interpreter.RawDirection(AllSlots[i]);
                bool upNow = raw == Direction8.N || raw == Direction8.NE || raw == Direction8.NW;
                bool upEdge = upNow && !_wasUp[i];
                _wasUp[i] = upNow;

                if (_stunRemaining[i] > 0)
                {
                    _stunRemaining[i]--;
                    _velocity[i] = 0f; // frozen, does not advance
                    continue;
                }

                // Consume jump (grounded only — no double-jump).
                if (upEdge && _airborneTicks[i] == 0)
                    _airborneTicks[i] = _jumpAirtimeTicks;

                bool wasAirborne = _airborneTicks[i] > 0;
                if (_airborneTicks[i] > 0) _airborneTicks[i]--;

                float mashNorm = _interpreter.MashForce(AllSlots[i]);
                float effVBase = (rubberBandEnabled && _distance[i] <= minDistance)
                    ? _rubberBandedVBase
                    : _params.VBase;
                float v = effVBase + _params.VGain * mashNorm;
                _velocity[i] = v;

                float newDistance = _distance[i] + v * DtSeconds;

                while (_nextObstacle[i] < _obstacleCount && _obstaclePos[_nextObstacle[i]] <= newDistance)
                {
                    if (wasAirborne)
                    {
                        _nextObstacle[i]++; // cleared in the air
                    }
                    else
                    {
                        // Grounded hit: halt at the obstacle and stun. Not an
                        // elimination — the seat resumes when the stun expires.
                        newDistance = _obstaclePos[_nextObstacle[i]];
                        _nextObstacle[i]++;
                        _stunRemaining[i] = _stunDurationTicks;
                        _velocity[i] = 0f;
                        EmitFeedback(i, FeedbackLevel.High, CueStunned);
                        break;
                    }
                }

                _distance[i] = newDistance;
            }

            PublishRenderState();

            _tick++;
            if (_tick >= _durationTicks)
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
            float[] dist = new float[n];
            int w = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                seats[w] = i;
                dist[w] = _distance[i];
                w++;
            }

            // Insertion sort DESCENDING by distance (further = better place). n <= 4.
            for (int i = 1; i < n; i++)
            {
                int seatKey = seats[i];
                float distKey = dist[i];
                int j = i - 1;
                while (j >= 0 && dist[j] < distKey)
                {
                    dist[j + 1] = dist[j];
                    seats[j + 1] = seats[j];
                    j--;
                }
                dist[j + 1] = distKey;
                seats[j + 1] = seatKey;
            }

            var ranks = new PlayerRank[n];
            for (int i = 0; i < n; i++)
            {
                int place;
                if (i == 0) place = 1;
                else if (dist[i] == dist[i - 1]) place = ranks[i - 1].Place; // exact tie shares a place
                else place = i + 1;

                int metric = (int)MathF.Round(dist[i] * 1000f);
                ranks[i] = new PlayerRank(seats[i], place, metric);
            }

            return new MicrogameResult(ResultKind.Ranked, ranks, 0);
        }

        /// <inheritdoc/>
        public RenderState GetRenderState() => _renderState;

        // ------------------------------------------------------------------
        // Test/telemetry accessors (public — same convention as every other v2
        // mechanic, e.g. EsquivaMicrogame.GetHazardSlotPosition / Manten.GetTheta).
        // ------------------------------------------------------------------

        public float GetDistance(PlayerSlot slot) => _distance[(int)slot];
        public float GetVelocity(PlayerSlot slot) => _velocity[(int)slot];
        public bool IsAirborne(PlayerSlot slot) => _airborneTicks[(int)slot] > 0;
        public bool IsStunned(PlayerSlot slot) => _stunRemaining[(int)slot] > 0;
        public int GetStunTicksRemaining(PlayerSlot slot) => _stunRemaining[(int)slot];

        /// <summary>The shared obstacle track's length (identical for all 4 lanes).</summary>
        public int ObstacleCount => _obstacleCount;

        /// <summary>Position (logical distance) of obstacle <paramref name="index"/> on the shared track.</summary>
        public float GetObstaclePosition(int index) => _obstaclePos[index];

        /// <summary>Airborne duration of a jump, seconds — exposed so a jump bot can size its lead window.</summary>
        public float JumpAirtimeSeconds => _params.JumpAirtimeSeconds;

        /// <summary>Test/telemetry accessor: ObstacleDensity after difficultyMult, as applied by the most recent <see cref="Initialize"/>.</summary>
        public float EffectiveObstacleDensity => _effectiveObstacleDensity;

        // ------------------------------------------------------------------

        private void GenerateObstacleTrack()
        {
            if (_forcedObstacles != null)
            {
                EnsureObstacleCapacity(_forcedObstacles.Length);
                Array.Copy(_forcedObstacles, _obstaclePos, _forcedObstacles.Length);
                _obstacleCount = _forcedObstacles.Length;
                return;
            }

            // The fastest any seat can travel: rubber-banded base + full mash gain.
            float maxVelocity = _rubberBandedVBase + _params.VGain;
            float horizon = _params.FirstObstacleDistance
                            + maxVelocity * _params.DurationSeconds
                            + _params.MinObstacleGap;

            // Gaps are floored at MinObstacleGap, so this is a hard upper bound on
            // the obstacle count regardless of density/difficulty.
            int capacity = (int)(horizon / _params.MinObstacleGap) + 2;
            EnsureObstacleCapacity(capacity);

            float meanGap = 1f / _effectiveObstacleDensity;
            float pos = _params.FirstObstacleDistance;
            int k = 0;
            while (pos <= horizon && k < _obstaclePos.Length)
            {
                _obstaclePos[k++] = pos;
                // Jitter in [0.6, 1.4]·meanGap, floored at the jumpability gap.
                float gap = MathF.Max(_params.MinObstacleGap, meanGap * (0.6f + 0.8f * _rng.NextFloat()));
                pos += gap;
            }
            _obstacleCount = k;
        }

        private void EnsureObstacleCapacity(int needed)
        {
            if (_obstaclePos == null || _obstaclePos.Length < needed)
                _obstaclePos = new float[needed];
        }

        private void EmitFeedback(int seatIndex, FeedbackLevel level, byte cue)
        {
            if (_renderState.FeedbackCount >= _renderState.Feedback.Length) return;
            _renderState.Feedback[_renderState.FeedbackCount] = new FeedbackEvent(seatIndex, level, cue);
            _renderState.FeedbackCount++;
        }

        /// <summary>[ASSUMED] presentation layout: each lane is a fixed horizontal band; the actual race signal is Distance, not screen Y.</summary>
        private static float LaneY(int seatIndex) => 0.2f + 0.2f * seatIndex;

        private void PublishRenderState()
        {
            _renderState.Tick = _tick;
            _renderState.Hud.ActiveVerb = "¡CORRE!";

            float speedSpan = _params.VBase + _params.VGain;
            int entityCount = 0;

            for (int i = 0; i < SeatCount; i++)
            {
                _renderState.Hud.Scores[i] = (int)MathF.Round(_distance[i]);
                _renderState.Hud.Meters[i] = speedSpan > 0f ? MathF.Min(1f, _velocity[i] / speedSpan) : 0f;

                if (!_roster.IsActive(AllSlots[i])) continue;

                float jumpProgress = _airborneTicks[i] > 0
                    ? 1f - (float)_airborneTicks[i] / _jumpAirtimeTicks
                    : 0f;

                _renderState.Entities[entityCount].Kind = EntityKind.PlayerAvatar;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = RunnerScreenX;
                _renderState.Entities[entityCount].Y = LaneY(i);
                _renderState.Entities[entityCount].Height = _airborneTicks[i] > 0 ? MathF.Sin(MathF.PI * jumpProgress) * 0.15f : 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = _stunRemaining[i] > 0
                    ? VariantStunned
                    : (_airborneTicks[i] > 0 ? VariantAirborne : VariantRunning);
                _renderState.Entities[entityCount].Progress01 = jumpProgress;
                entityCount++;
            }

            // One upcoming-obstacle marker per active lane, positioned by its
            // relative distance ahead of that lane's runner (presentation only).
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                if (_nextObstacle[i] >= _obstacleCount) continue;
                if (entityCount >= _renderState.Entities.Length) break;

                float relative = _obstaclePos[_nextObstacle[i]] - _distance[i];
                float t = relative / ObstacleLookAhead;
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;

                _renderState.Entities[entityCount].Kind = EntityKind.Hazard;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = RunnerScreenX + t * (1f - RunnerScreenX);
                _renderState.Entities[entityCount].Y = LaneY(i);
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = 0;
                _renderState.Entities[entityCount].Progress01 = t;
                entityCount++;
            }

            _renderState.EntityCount = entityCount;
        }
    }
}
