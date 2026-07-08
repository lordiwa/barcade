using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_07 — ¡BOMBARDEA! (asymmetric 1v3, inverted power, T-111). v2
    /// <see cref="IMicrogame"/> implementation.
    ///
    /// <para>
    /// <b>Solo: reticle + telegraphed shot (GDD literal).</b> The solo seat moves
    /// a reticle over the arena via its stick (<see cref="BombardeaParams.ReticleSpeed"/>).
    /// A button tap fires — but ONLY if no shot is currently pending
    /// (<see cref="_shotActive"/>, AC4's "cola máx = 1") AND at least
    /// <see cref="BombardeaParams.FireCooldownSeconds"/> has elapsed since the
    /// last fire (GDD literal 1 shot/0.9s) — both gates are checked independently
    /// so the queue-of-1 guarantee holds structurally even if a future
    /// recalibration sets <c>fireCooldown &lt; telegraphSec</c> (today's GDD
    /// defaults never overlap, but the guard does not rely on that).
    /// </para>
    ///
    /// <para>
    /// <b>Telegraph -&gt; damage (GDD literal + P4 guarantee).</b> A fired shot is
    /// marked at the reticle's position and published in <see cref="RenderState"/>
    /// (a <see cref="EntityKind.Projectile"/> entity) starting the SAME tick it
    /// fires; the blast applies exactly <see cref="BombardeaParams.TelegraphSeconds"/>
    /// later (GDD literal 0.7s — AC4 requires >= 0.5s, hard-floored by
    /// <see cref="BombardeaParams.MinTelegraphSeconds"/> in the params
    /// constructor, so this class never has to re-check it). Any active trio
    /// seat within <c>BlastRadius + AvatarRadius</c> of the marked position at
    /// that tick is hit.
    /// </para>
    ///
    /// <para>
    /// <b>Trio: dodge only (GDD literal).</b> Trio seats move freely via their
    /// stick; a hit does NOT eliminate — it only counts toward the solo's score
    /// (GDD: "El solista gana puntos por impacto; el trío gana sobreviviendo").
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] win condition (GDD gap-fill).</b> GDD names continuous
    /// scoring ("gana puntos por impacto") and a binary trio outcome
    /// ("sobrevive ~50% de las veces") but no explicit hit threshold. This
    /// mechanic treats ANY landed hit by duration's end as a solo win (Place 1,
    /// trio shares Place 2); zero hits is a trio win (Place 1 shared, solo Place
    /// 2) — the simplest reading that makes "sobrevive" literally mean "takes
    /// zero hits," and keeps the winrate a pure function of the three named,
    /// remotely-recalibratable knobs (fireCooldown/telegraphSec/blastRadius vs.
    /// trio dodge speed) rather than an unnamed extra threshold parameter. See
    /// <see cref="BombardeaMicrogameV2Tests"/> and the slow-suite winrate sweep
    /// for the calibration this assumption is measured against.
    /// </para>
    ///
    /// <para>
    /// <b>Round length: always the full duration (+ overtime for a still-pending
    /// shot).</b> Unlike <see cref="PersigueMicrogame"/> (which can finish early
    /// on a catch), GDD's own framing here is a scored round, not a sudden-death
    /// one — the round runs the full <see cref="BombardeaParams.DurationSeconds"/>
    /// (new fires gated off after that, mirroring <c>ApuntaMicrogame</c>'s
    /// overtime-shot-resolution pattern) so a last-second shot's telegraph still
    /// resolves before <see cref="IsFinished"/> goes true.
    /// </para>
    ///
    /// Fixed 60 Hz tick. Pure C# — no UnityEngine dependency. C# 9 compatible.
    /// Zero heap allocation in steady-state <see cref="Tick"/> — all per-seat
    /// state is fixed-size arrays allocated once in the constructor; the single
    /// pending shot is a handful of scalar fields, not a pool (AC4: at most one
    /// ever exists). <see cref="GetResult"/> allocates once per round, the same
    /// accepted pattern as every other v2 mechanic.
    /// </summary>
    public sealed class BombardeaMicrogame : IMicrogame
    {
        private const int SeatCount = 4;
        private const int EntityCapacity = SeatCount + 1; // 4 avatars + 1 in-flight shot
        private const int FeedbackCapacity = 4;

        private const float TicksPerSecond = 60f;
        private const float DtSeconds = 1f / TicksPerSecond;

        public const byte CueFired = 1;
        public const byte CueHit = 2;

        public const byte VariantTrio = 0;
        public const byte VariantSolo = 1;

        /// <summary>Projectile VisualVariant while telegraphing (GDD: "los de abajo siempre tienen ventana de reacción").</summary>
        public const byte ProjectileVariantTelegraph = 0;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly BombardeaParams _params;
        private readonly InputInterpreter _interpreter;
        private readonly InputBridge _inputBridge = new InputBridge();
        private readonly RenderState _renderState;

        // Stored only for IMicrogame contract compliance (GDD §10.2) — no
        // in-round randomness to consume, same rationale as PersigueMicrogame's
        // own doc: the shot position is the reticle's own (input-driven)
        // position, never randomly placed.
        private SeededRandom _rng;
        private PlayerRoster _roster;

        private int _tick;
        private int _durationTicks;
        private int _fireCooldownTicks;
        private int _telegraphTicks;
        private bool _isFinished;

        private readonly float[] _avatarX = new float[SeatCount];
        private readonly float[] _avatarY = new float[SeatCount];
        private readonly int[] _hitCount = new int[SeatCount]; // per-seat: times THIS trio seat was hit; solo's own slot unused for that meaning

        private int _soloHitsLanded;
        private int _lastFireTick = int.MinValue / 2;

        private bool _shotActive;
        private float _shotX;
        private float _shotY;
        private int _shotFireTick;

        public BombardeaMicrogame(BombardeaParams parameters)
        {
            _params = parameters;
            _interpreter = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            _renderState = new RenderState(EntityCapacity, FeedbackCapacity);
        }

        // Test injection (mirrors EsquivaMicrogame.SetForcedAvatarPositions).
        private (float x, float y)[] _forcedAvatarPositions;

        /// <summary>Override starting avatar/reticle positions for deterministic tests. Call before <see cref="Initialize"/>. Indexed by <c>(int)PlayerSlot</c>.</summary>
        public void SetForcedAvatarPositions((float x, float y)[] positions) => _forcedAvatarPositions = positions;

        /// <summary>Directly injects a pending shot for deterministic timeline tests, bypassing the button-edge fire path. Overwrites any already-pending shot.</summary>
        public void ForceFireShotForTest(float x, float y)
        {
            _shotActive = true;
            _shotX = x;
            _shotY = y;
            _shotFireTick = _tick;
            _lastFireTick = _tick;
        }

        /// <inheritdoc/>
        public MicrogameId Id => MicrogameId.Bombardea;

        /// <inheritdoc/>
        public bool IsFinished => _isFinished;

        /// <inheritdoc/>
        public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (!roster.IsActive(AllSlots[_params.SoloSeat]))
                throw new ArgumentException("BombardeaParams.SoloSeat must be an active seat.", nameof(roster));

            _rng = rng;
            _roster = roster;

            _tick = 0;
            _isFinished = false;
            _durationTicks = (int)MathF.Round(_params.DurationSeconds * TicksPerSecond);
            _fireCooldownTicks = (int)MathF.Round(_params.FireCooldownSeconds * TicksPerSecond);
            _telegraphTicks = (int)MathF.Round(_params.TelegraphSeconds * TicksPerSecond);

            _interpreter.Reset();

            _soloHitsLanded = 0;
            _lastFireTick = int.MinValue / 2;
            _shotActive = false;
            _shotX = 0f;
            _shotY = 0f;
            _shotFireTick = 0;

            for (int i = 0; i < SeatCount; i++)
            {
                if (_forcedAvatarPositions != null && i < _forcedAvatarPositions.Length)
                {
                    _avatarX[i] = _forcedAvatarPositions[i].x;
                    _avatarY[i] = _forcedAvatarPositions[i].y;
                }
                else
                {
                    _avatarX[i] = 0.5f;
                    _avatarY[i] = 0.5f;
                }

                _hitCount[i] = 0;
            }

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

                Direction8 raw = _interpreter.RawDirection(AllSlots[i]);
                (float dx, float dy) = InputBridge.ToUnitVector(raw);

                bool isSolo = i == _params.SoloSeat;
                float speed = isSolo ? _params.ReticleSpeed : _params.TrioAvatarSpeed;
                _avatarX[i] = Clamp01(_avatarX[i] + dx * speed * DtSeconds);
                _avatarY[i] = Clamp01(_avatarY[i] + dy * speed * DtSeconds);

                // New fires only before duration ends (mirrors ApuntaMicrogame's
                // overtime gate) — an already-pending shot still resolves after.
                if (isSolo && _tick < _durationTicks
                    && _interpreter.ButtonPressedThisTick(AllSlots[i])
                    && !_shotActive
                    && _tick - _lastFireTick >= _fireCooldownTicks)
                {
                    _shotActive = true;
                    _shotX = _avatarX[i];
                    _shotY = _avatarY[i];
                    _shotFireTick = _tick;
                    _lastFireTick = _tick;
                    EmitFeedback(i, FeedbackLevel.Medium, CueFired);
                }
            }

            if (_shotActive && _tick - _shotFireTick >= _telegraphTicks)
                ResolveShot();

            PublishRenderState();

            _tick++;
            if (_tick >= _durationTicks && !_shotActive)
                _isFinished = true;
        }

        /// <inheritdoc/>
        public MicrogameResult GetResult()
        {
            if (!_isFinished)
                throw new InvalidOperationException("GetResult() called before IsFinished.");

            bool soloWon = _soloHitsLanded >= 1;

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
                int metric = isSolo ? _soloHitsLanded : _hitCount[i];

                ranks[w] = new PlayerRank(i, place, metric);
                w++;
            }

            return new MicrogameResult(ResultKind.Ranked, ranks, 0);
        }

        /// <inheritdoc/>
        public RenderState GetRenderState() => _renderState;

        // ------------------------------------------------------------------
        // Test/telemetry accessors (public — same convention as every other v2 mechanic).
        // ------------------------------------------------------------------

        public (float x, float y) GetAvatarPosition(PlayerSlot slot) => (_avatarX[(int)slot], _avatarY[(int)slot]);
        public int GetHitCount(PlayerSlot slot) => _hitCount[(int)slot];
        public int SoloHitsLanded => _soloHitsLanded;
        public bool IsShotActive => _shotActive;
        public int ShotFireTick => _shotFireTick;
        public (float x, float y) GetShotPosition() => (_shotX, _shotY);

        // ------------------------------------------------------------------

        private void ResolveShot()
        {
            float combinedRadius = _params.BlastRadius + _params.AvatarRadius;
            bool anyHit = false;

            for (int i = 0; i < SeatCount; i++)
            {
                if (i == _params.SoloSeat) continue;
                if (!_roster.IsActive(AllSlots[i])) continue;

                float ddx = _avatarX[i] - _shotX;
                float ddy = _avatarY[i] - _shotY;
                float dist = MathF.Sqrt(ddx * ddx + ddy * ddy);
                if (dist > combinedRadius) continue;

                _hitCount[i]++;
                anyHit = true;
                EmitFeedback(i, FeedbackLevel.High, CueHit);
            }

            if (anyHit) _soloHitsLanded++;
            _shotActive = false;
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
            _renderState.Hud.ActiveVerb = "¡BOMBARDEA!";

            int entityCount = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                bool isSolo = i == _params.SoloSeat;
                _renderState.Hud.Scores[i] = isSolo ? _soloHitsLanded * _params.SoloScorePerHit : _hitCount[i];

                if (!_roster.IsActive(AllSlots[i])) continue;

                _renderState.Entities[entityCount].Kind = EntityKind.PlayerAvatar;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = _avatarX[i];
                _renderState.Entities[entityCount].Y = _avatarY[i];
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = isSolo ? VariantSolo : VariantTrio;
                _renderState.Entities[entityCount].Progress01 = 0f;
                entityCount++;
            }

            if (_shotActive)
            {
                float progress = _telegraphTicks > 0 ? (float)(_tick - _shotFireTick) / _telegraphTicks : 1f;
                if (progress < 0f) progress = 0f; else if (progress > 1f) progress = 1f;

                _renderState.Entities[entityCount].Kind = EntityKind.Projectile;
                _renderState.Entities[entityCount].OwnerSeat = _params.SoloSeat;
                _renderState.Entities[entityCount].X = _shotX;
                _renderState.Entities[entityCount].Y = _shotY;
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = _params.BlastRadius;
                _renderState.Entities[entityCount].VisualVariant = ProjectileVariantTelegraph;
                _renderState.Entities[entityCount].Progress01 = progress;
                entityCount++;
            }

            _renderState.EntityCount = entityCount;
        }
    }
}
