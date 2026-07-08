using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_06 — ¡PERSIGUE!/¡ESCAPA! (asymmetric 1v3, T-111). v2
    /// <see cref="IMicrogame"/> implementation (see that interface's doc for the
    /// v1/v2 coexistence rationale — this mechanic has no v1 predecessor).
    ///
    /// <para>
    /// <b>Arena and movement.</b> Normalized [0,1]² logical arena, same convention
    /// as every other v2 mechanic. Every active seat's avatar moves via its stick
    /// at <see cref="EffectiveSpeed"/> units/second, clamped to the arena, then
    /// pushed out of any cover-obstacle overlap (<see cref="ResolveObstacleOverlap"/>).
    /// A tap of the button dashes: an instant displacement of
    /// <see cref="EffectiveDashDistance"/> along the seat's current facing (last
    /// non-<see cref="Direction8.None"/> stick direction), through the SAME
    /// clamp+obstacle-resolve path as ordinary movement, gated by an exact
    /// <see cref="PersigueParams.DashCooldownSeconds"/> cooldown at tick
    /// resolution (GDD literal 1.5s — AC3).
    /// </para>
    ///
    /// <para>
    /// <b>Mobility-only balance (GDD §4 hard design rule, AC6 — structural, not
    /// a convention).</b> Every seat runs through the exact same per-tick code
    /// path in <see cref="Tick"/>: read stick, move, clamp, resolve obstacles,
    /// maybe dash, then run the SAME catch-collision check
    /// (<see cref="ResolveCatches"/>) regardless of which seat is solo. The ONLY
    /// place <see cref="PersigueParams.SoloSeat"/> is consulted for a numeric
    /// difference is <see cref="EffectiveSpeed"/>/<see cref="EffectiveDashDistance"/>
    /// (a lookup, not a branch in the movement/collision RULES themselves) — no
    /// other line of this class reads <c>SoloSeat</c> to change what a seat is
    /// allowed to do, only how fast. <see cref="PersigueMicrogameV2Tests"/>'s own
    /// structural test asserts this by construction: swapping which seat is solo
    /// changes nothing about collision geometry, obstacle layout, or the catch
    /// radius.
    /// </para>
    ///
    /// <para>
    /// <b>Two modes, one shared catch rule (GDD §4).</b> <see cref="PersigueMode.SoloHunts"/>:
    /// the solo avatar overlapping an uncaught trio avatar catches that trio
    /// seat; the solo wins once every active trio seat is caught (else the trio
    /// wins at <see cref="PersigueParams.DurationSeconds"/>). <see cref="PersigueMode.SoloFlees"/>:
    /// any trio avatar overlapping the (not-yet-caught) solo avatar catches the
    /// solo immediately (trio wins); the solo wins by never being caught before
    /// duration ends. Both modes reuse the identical circle-overlap test
    /// (combined radius = 2x <see cref="PersigueParams.AvatarRadius"/>, same for
    /// every seat) — only WHICH side's catch is being watched for flips by mode
    /// (see <see cref="ResolveCatches"/>).
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] Ranking: a group-level binary split, not a 4-way podium.</b>
    /// GDD gives asym1v3 no independent payout shape of its own (TASK-050 carry-in
    /// — see <see cref="Barcade.Core.Content.MicrogameDefinitionValidator"/>'s own
    /// doc): the winning side (solo alone, or the trio as a whole) is Place 1,
    /// the losing side shares Place 2 — the same shared-place-on-tie mechanism
    /// every v2 mechanic already has, applied at group granularity because GDD's
    /// own text for both modes is itself binary ("el solista atrapa... / el
    /// solista gana si sobrevive duration"), not a finer per-seat ranking.
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] Cover obstacles (GDD names "2-3 obstáculos," no geometry).</b>
    /// <see cref="GetObstacles"/> defines 4 fixed layouts (one per
    /// <see cref="PersigueArenaLayout"/>) as static data — never RNG-placed,
    /// matching the GDD's own framing of arenaLayout as a per-definition variant,
    /// not a per-round random draw.
    /// </para>
    ///
    /// Fixed 60 Hz tick. Pure C# — no UnityEngine dependency. C# 9 compatible.
    /// Zero heap allocation in steady-state <see cref="Tick"/> — all per-seat and
    /// obstacle state is fixed-size arrays allocated once in the constructor.
    /// <see cref="GetResult"/> allocates once per round, the same accepted
    /// pattern as every other v2 mechanic.
    /// </summary>
    public sealed class PersigueMicrogame : IMicrogame
    {
        private const int SeatCount = 4;
        private const int MaxObstacles = 3;
        private const int EntityCapacity = SeatCount + MaxObstacles;
        private const int FeedbackCapacity = 4;

        private const float TicksPerSecond = 60f;
        private const float DtSeconds = 1f / TicksPerSecond;

        public const byte CueDash = 1;
        public const byte CueCaught = 2;

        /// <summary>VisualVariant bit0 = role (0=trio,1=solo), bit1 = caught. A bot policy reads bit0 off its own avatar to learn its role — never hidden state (Annex D.3: "el bot no hace trampa").</summary>
        public const byte VariantTrioActive = 0;
        public const byte VariantSoloActive = 1;
        public const byte VariantTrioCaught = 2;
        public const byte VariantSoloCaught = 3;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly PersigueParams _params;
        private readonly InputInterpreter _interpreter;
        private readonly InputBridge _inputBridge = new InputBridge();
        private readonly RenderState _renderState;

        // Stored only for IMicrogame contract compliance (GDD §10.2: "toda
        // aleatoriedad pasa por el rng inyectado") — this mechanic has NO
        // in-round randomness to consume: the cover layout is fixed
        // per-definition data (GetObstacles), not a random draw, and every
        // movement/dash/catch rule is a deterministic function of input alone.
        private SeededRandom _rng;
        private PlayerRoster _roster;

        private int _tick;
        private int _durationTicks;
        private int _dashCooldownTicks;
        private bool _isFinished;

        private readonly float[] _avatarX = new float[SeatCount];
        private readonly float[] _avatarY = new float[SeatCount];
        private readonly float[] _facingX = new float[SeatCount];
        private readonly float[] _facingY = new float[SeatCount];
        private readonly int[] _dashCooldownRemainingTicks = new int[SeatCount];
        private readonly bool[] _caught = new bool[SeatCount];
        private readonly int[] _catchTick = new int[SeatCount];

        private readonly float[] _obstacleX = new float[MaxObstacles];
        private readonly float[] _obstacleY = new float[MaxObstacles];
        private int _obstacleCount;

        // Test injection (mirrors EsquivaMicrogame.SetForcedAvatarPositions).
        private (float x, float y)[] _forcedAvatarPositions;

        public PersigueMicrogame(PersigueParams parameters)
        {
            _params = parameters;
            _interpreter = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            _renderState = new RenderState(EntityCapacity, FeedbackCapacity);
        }

        /// <summary>Override starting avatar positions for deterministic tests. Call before <see cref="Initialize"/>. Indexed by <c>(int)PlayerSlot</c>.</summary>
        public void SetForcedAvatarPositions((float x, float y)[] positions) => _forcedAvatarPositions = positions;

        /// <inheritdoc/>
        public MicrogameId Id => MicrogameId.Persigue;

        /// <inheritdoc/>
        public bool IsFinished => _isFinished;

        /// <inheritdoc/>
        public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (!roster.IsActive(AllSlots[_params.SoloSeat]))
                throw new ArgumentException("PersigueParams.SoloSeat must be an active seat.", nameof(roster));

            _rng = rng;
            _roster = roster;

            _tick = 0;
            _isFinished = false;
            _durationTicks = (int)MathF.Round(_params.DurationSeconds * TicksPerSecond);
            _dashCooldownTicks = (int)MathF.Round(_params.DashCooldownSeconds * TicksPerSecond);

            _interpreter.Reset();

            for (int i = 0; i < SeatCount; i++)
            {
                if (_forcedAvatarPositions != null && i < _forcedAvatarPositions.Length)
                {
                    _avatarX[i] = _forcedAvatarPositions[i].x;
                    _avatarY[i] = _forcedAvatarPositions[i].y;
                }
                else
                {
                    (float ax, float ay) = StartCorner(i);
                    _avatarX[i] = ax;
                    _avatarY[i] = ay;
                }

                (float fx, float fy) = DefaultFacing(i);
                _facingX[i] = fx;
                _facingY[i] = fy;

                _dashCooldownRemainingTicks[i] = 0;
                _caught[i] = false;
                _catchTick[i] = -1;
            }

            GetObstacles(_params.ArenaLayout, _obstacleX, _obstacleY, out _obstacleCount);

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

            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;

                if (_dashCooldownRemainingTicks[i] > 0) _dashCooldownRemainingTicks[i]--;

                Direction8 raw = _interpreter.RawDirection(AllSlots[i]);
                (float dx, float dy) = InputBridge.ToUnitVector(raw);
                if (raw != Direction8.None)
                {
                    _facingX[i] = dx;
                    _facingY[i] = dy;
                }

                float speed = EffectiveSpeed(i);
                MoveAndResolve(i, dx * speed * DtSeconds, dy * speed * DtSeconds);

                if (_interpreter.ButtonPressedThisTick(AllSlots[i]) && _dashCooldownRemainingTicks[i] == 0)
                {
                    float dashDistance = EffectiveDashDistance(i);
                    MoveAndResolve(i, _facingX[i] * dashDistance, _facingY[i] * dashDistance);
                    _dashCooldownRemainingTicks[i] = _dashCooldownTicks;
                    EmitFeedback(i, FeedbackLevel.Medium, CueDash);
                }
            }

            ResolveCatches();

            bool earlyFinish = _params.Mode == PersigueMode.SoloHunts
                ? AllTrioCaught()
                : _caught[_params.SoloSeat];

            PublishRenderState();

            _tick++;
            if (_tick >= _durationTicks || earlyFinish)
                _isFinished = true;
        }

        /// <inheritdoc/>
        public MicrogameResult GetResult()
        {
            if (!_isFinished)
                throw new InvalidOperationException("GetResult() called before IsFinished.");

            bool soloWon = _params.Mode == PersigueMode.SoloHunts
                ? AllTrioCaught()
                : !_caught[_params.SoloSeat];

            int n = 0;
            for (int i = 0; i < SeatCount; i++)
                if (_roster.IsActive(AllSlots[i])) n++;

            var ranks = new PlayerRank[n];
            int w = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;

                bool isSolo = i == _params.SoloSeat;
                int place = isSolo == soloWon ? 1 : 2;
                int metric = ComputeMetric(i, isSolo);

                ranks[w] = new PlayerRank(i, place, metric);
                w++;
            }

            return new MicrogameResult(ResultKind.Ranked, ranks, 0);
        }

        /// <inheritdoc/>
        public RenderState GetRenderState() => _renderState;

        // ------------------------------------------------------------------
        // Static, stateless helpers (usable standalone; also drive bot/test code)
        // ------------------------------------------------------------------

        /// <summary>
        /// The 4 GDD-named fixed cover layouts (GDD gap-fill — see class doc).
        /// <paramref name="outX"/>/<paramref name="outY"/> must have length &gt;=
        /// <see cref="MaxObstacles"/> (3); only the first <paramref name="count"/>
        /// entries are meaningful.
        /// </summary>
        public static void GetObstacles(PersigueArenaLayout layout, float[] outX, float[] outY, out int count)
        {
            switch (layout)
            {
                case PersigueArenaLayout.Cross:
                    outX[0] = 0.5f; outY[0] = 0.25f;
                    outX[1] = 0.25f; outY[1] = 0.70f;
                    outX[2] = 0.75f; outY[2] = 0.70f;
                    count = 3;
                    break;
                case PersigueArenaLayout.Corners:
                    outX[0] = 0.25f; outY[0] = 0.25f;
                    outX[1] = 0.75f; outY[1] = 0.75f;
                    count = 2;
                    break;
                case PersigueArenaLayout.Central:
                    outX[0] = 0.40f; outY[0] = 0.5f;
                    outX[1] = 0.60f; outY[1] = 0.5f;
                    count = 2;
                    break;
                default: // Diagonal
                    outX[0] = 0.2f; outY[0] = 0.2f;
                    outX[1] = 0.5f; outY[1] = 0.5f;
                    outX[2] = 0.8f; outY[2] = 0.8f;
                    count = 3;
                    break;
            }
        }

        /// <summary>Per-seat starting corner, inset from the true arena corner — same convention as <c>EsquivaMicrogame.StartCorner</c>.</summary>
        private static (float, float) StartCorner(int seatIndex)
        {
            switch (seatIndex)
            {
                case 0: return (0.15f, 0.15f); // Rojo
                case 1: return (0.85f, 0.15f); // Azul
                case 2: return (0.85f, 0.85f); // Amarillo
                default: return (0.15f, 0.85f); // Verde
            }
        }

        /// <summary>Cold-start facing before the stick is ever touched — corner-to-center diagonal, same convention as <c>ApuntaMicrogame.DefaultAimFor</c>.</summary>
        private static (float, float) DefaultFacing(int seatIndex)
        {
            const float diag = 0.70710678f;
            switch (seatIndex)
            {
                case 0: return (diag, diag);   // Rojo -> NE
                case 1: return (-diag, diag);  // Azul -> NW
                case 2: return (-diag, -diag); // Amarillo -> SW
                default: return (diag, -diag); // Verde -> SE
            }
        }

        // ------------------------------------------------------------------
        // Test/telemetry accessors (public — same convention as every other v2 mechanic).
        // ------------------------------------------------------------------

        public (float x, float y) GetAvatarPosition(PlayerSlot slot) => (_avatarX[(int)slot], _avatarY[(int)slot]);
        public bool IsCaught(PlayerSlot slot) => _caught[(int)slot];
        public int GetCatchTick(PlayerSlot slot) => _catchTick[(int)slot];
        public int GetDashCooldownRemainingTicks(PlayerSlot slot) => _dashCooldownRemainingTicks[(int)slot];
        public float EffectiveSpeed(PlayerSlot slot) => EffectiveSpeed((int)slot);
        public float EffectiveDashDistance(PlayerSlot slot) => EffectiveDashDistance((int)slot);
        public int ObstacleCount => _obstacleCount;
        public (float x, float y) GetObstaclePosition(int index) => (_obstacleX[index], _obstacleY[index]);

        // ------------------------------------------------------------------

        private float EffectiveSpeed(int seatIndex) =>
            seatIndex == _params.SoloSeat ? _params.BaseAvatarSpeed * (1f + _params.SoloSpeedBonus) : _params.BaseAvatarSpeed;

        private float EffectiveDashDistance(int seatIndex) =>
            seatIndex == _params.SoloSeat ? _params.SoloDashDistance : _params.TrioDashDistance;

        private void MoveAndResolve(int seatIndex, float dx, float dy)
        {
            _avatarX[seatIndex] = Clamp01(_avatarX[seatIndex] + dx);
            _avatarY[seatIndex] = Clamp01(_avatarY[seatIndex] + dy);
            ResolveObstacleOverlap(seatIndex);
        }

        private void ResolveObstacleOverlap(int seatIndex)
        {
            float minDist = _params.AvatarRadius + _params.ObstacleRadius;
            for (int o = 0; o < _obstacleCount; o++)
            {
                float ddx = _avatarX[seatIndex] - _obstacleX[o];
                float ddy = _avatarY[seatIndex] - _obstacleY[o];
                float dist = MathF.Sqrt(ddx * ddx + ddy * ddy);
                if (dist >= minDist) continue;

                if (dist < 1e-6f) { ddx = 1f; ddy = 0f; dist = 1f; } // degenerate: avatar exactly on obstacle center
                float push = minDist - dist;
                _avatarX[seatIndex] = Clamp01(_avatarX[seatIndex] + ddx / dist * push);
                _avatarY[seatIndex] = Clamp01(_avatarY[seatIndex] + ddy / dist * push);
            }
        }

        private void ResolveCatches()
        {
            int solo = _params.SoloSeat;
            if (!_roster.IsActive(AllSlots[solo])) return;

            float combinedCatchRadius = _params.AvatarRadius * 2f;

            for (int i = 0; i < SeatCount; i++)
            {
                if (i == solo) continue;
                if (!_roster.IsActive(AllSlots[i])) continue;
                if (_params.Mode == PersigueMode.SoloHunts && _caught[i]) continue;
                if (_params.Mode == PersigueMode.SoloFlees && _caught[solo]) continue;

                float ddx = _avatarX[solo] - _avatarX[i];
                float ddy = _avatarY[solo] - _avatarY[i];
                float dist = MathF.Sqrt(ddx * ddx + ddy * ddy);
                if (dist > combinedCatchRadius) continue;

                if (_params.Mode == PersigueMode.SoloHunts)
                {
                    _caught[i] = true;
                    _catchTick[i] = _tick;
                    EmitFeedback(i, FeedbackLevel.High, CueCaught);
                }
                else
                {
                    _caught[solo] = true;
                    _catchTick[solo] = _tick;
                    EmitFeedback(solo, FeedbackLevel.High, CueCaught);
                }
            }
        }

        private bool AllTrioCaught()
        {
            for (int i = 0; i < SeatCount; i++)
            {
                if (i == _params.SoloSeat) continue;
                if (!_roster.IsActive(AllSlots[i])) continue;
                if (!_caught[i]) return false;
            }
            return true;
        }

        private int CountTrioCaught()
        {
            int c = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                if (i == _params.SoloSeat) continue;
                if (_roster.IsActive(AllSlots[i]) && _caught[i]) c++;
            }
            return c;
        }

        private int ComputeMetric(int seatIndex, bool isSolo)
        {
            if (_params.Mode == PersigueMode.SoloHunts)
                return isSolo ? CountTrioCaught() : (_caught[seatIndex] ? _catchTick[seatIndex] : -1);

            int solo = _params.SoloSeat;
            return _caught[solo] ? _catchTick[solo] : _durationTicks;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private void EmitFeedback(int seat, FeedbackLevel level, byte cue)
        {
            if (_renderState.FeedbackCount >= _renderState.Feedback.Length) return;
            _renderState.Feedback[_renderState.FeedbackCount] = new FeedbackEvent(seat, level, cue);
            _renderState.FeedbackCount++;
        }

        private void PublishRenderState()
        {
            _renderState.Tick = _tick;
            _renderState.Hud.ActiveVerb = _params.Mode == PersigueMode.SoloHunts ? "¡PERSIGUE!" : "¡ESCAPA!";

            int entityCount = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                bool isSolo = i == _params.SoloSeat;
                _renderState.Hud.Scores[i] = isSolo ? CountTrioCaught() : (_caught[i] ? 1 : 0);

                if (!_roster.IsActive(AllSlots[i])) continue;

                byte variant;
                if (isSolo) variant = _caught[i] ? VariantSoloCaught : VariantSoloActive;
                else variant = _caught[i] ? VariantTrioCaught : VariantTrioActive;

                _renderState.Entities[entityCount].Kind = EntityKind.PlayerAvatar;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = _avatarX[i];
                _renderState.Entities[entityCount].Y = _avatarY[i];
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = MathF.Atan2(_facingY[i], _facingX[i]) * (180f / MathF.PI);
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = variant;
                _renderState.Entities[entityCount].Progress01 = 0f;
                entityCount++;
            }

            for (int o = 0; o < _obstacleCount; o++)
            {
                _renderState.Entities[entityCount].Kind = EntityKind.Obstacle;
                _renderState.Entities[entityCount].OwnerSeat = -1;
                _renderState.Entities[entityCount].X = _obstacleX[o];
                _renderState.Entities[entityCount].Y = _obstacleY[o];
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
