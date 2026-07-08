using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §7.2 — the special asymmetric "Jefe" (boss) phase, ronda 5-6,
    /// "mecanismo Crawl". v2 <see cref="IMicrogame"/> implementation.
    ///
    /// <para>
    /// <b>Roles (GDD literal).</b> Exactly one engaged seat is Jefe at all
    /// times — <see cref="JefeParams.JefeSeat"/> (the pre-decided score
    /// leader) starts with 3 hits and a bigger hitbox
    /// (<see cref="JefeParams.JefeHitboxMultiplier"/>). Every other engaged
    /// seat is an attacker: palanca moves, a button tap lands a melee hit
    /// whenever within melee range of the CURRENT Jefe's position.
    /// </para>
    ///
    /// <para>
    /// <b>Same-tick role swap, no invulnerability frame (GDD's own AC, the
    /// crux of this ticket).</b> <see cref="ResolveMeleeAttacks"/> iterates
    /// engaged seats in FIXED order (Rojo/Azul/Amarillo/Verde) every tick. For
    /// each seat that is NOT currently Jefe and lands a qualifying melee hit
    /// THIS tick, damage applies to WHOEVER <see cref="JefeSeat"/> names AT
    /// THAT INSTANT (re-read fresh every iteration, never cached) — if that
    /// brings hits to 0, the swap ("robo de cuerpo") happens IMMEDIATELY,
    /// inline, in the same statement: <see cref="JefeSeat"/> becomes the
    /// attacker who landed it, hits reset to 2, and the deposed Jefe is
    /// simply whatever <see cref="JefeSeat"/> no longer names — there is no
    /// separate "pending swap" flag, no second phase of Tick that applies it
    /// later, and therefore structurally no tick where zero or two seats hold
    /// the role. Because later iterations in the SAME tick re-read the fresh
    /// <see cref="JefeSeat"/>, a second attacker's qualifying hit on the very
    /// same tick lands on the BRAND NEW Jefe immediately — there is no grace
    /// tick either (see <see cref="JefeMicrogameV2Tests"/>'s own timeline
    /// proof, which drives exactly this scenario).
    /// </para>
    ///
    /// <para>
    /// <b>The Jefe's own area attack (GDD literal telegraph, P4; [ASSUMED]
    /// consequence).</b> The Jefe's own button tap triggers a telegraphed
    /// blast centered on the Jefe's CURRENT position (frozen at the trigger
    /// instant, same "aim-then-wait" pattern as
    /// <see cref="BombardeaMicrogame"/>'s shot), subject to
    /// <see cref="JefeParams.AoeCooldownSeconds"/> and a queue-of-1 (only one
    /// pending blast can exist — same structural guard as Bombardea's). It
    /// resolves exactly <see cref="JefeParams.TelegraphSeconds"/> later
    /// (GDD literal 0.6s): any engaged, non-Jefe (evaluated AT RESOLUTION,
    /// not at trigger — correctly handles a mid-flight role swap) seat within
    /// blast radius is STUNNED for <see cref="JefeParams.AttackerStunSeconds"/>
    /// (frozen: no movement, no melee) — [ASSUMED] "sin vidas ni eliminación"
    /// consequence, modeled on <c>CorreMicrogame</c>'s own established
    /// stun-on-hit pattern rather than inventing a lives/elimination system
    /// GDD names nowhere for this phase.
    /// </para>
    ///
    /// <para>
    /// <b>Coop-abandonment carry-forward (MECH_08/09).</b> Jefe/attacker
    /// eligibility uses <see cref="IsEngaged"/> (Human-or-Bot), not
    /// <see cref="PlayerRoster.IsActive"/> — an Idle/Empty seat can never be
    /// the Jefe, can never melee, and is never a valid melee/blast target.
    /// </para>
    ///
    /// <para>
    /// <b>Ranking/payout (asym1v3, TASK-050 carry-in).</b> A Jefe standing at
    /// timeout wins: <see cref="GetResult"/> ranks whoever
    /// <see cref="JefeSeat"/> names Place 1, every other engaged seat shares
    /// Place 2 — the exact same group-level binary-split shape
    /// <c>PersigueMicrogame</c> already established for MECH_06, feeding the
    /// identical [6,2,2,2] payoutTable shape via
    /// <see cref="Barcade.Core.Scoring.PayoutRules.ApplyCompetitive"/>.
    /// </para>
    ///
    /// Fixed 60 Hz tick. Pure C# — no UnityEngine dependency. C# 9 compatible.
    /// Zero heap allocation in steady-state <see cref="Tick"/> — all per-seat
    /// state is fixed-size arrays allocated once in the constructor; the
    /// single pending blast is a handful of scalar fields, not a pool.
    /// <see cref="GetResult"/> allocates once per round, the same accepted
    /// pattern as every other v2 mechanic.
    /// </summary>
    public sealed class JefeMicrogame : IMicrogame
    {
        private const int SeatCount = 4;
        private const int EntityCapacity = SeatCount + 1; // 4 avatars + 1 in-flight AoE marker
        private const int FeedbackCapacity = 4;

        private const float TicksPerSecond = 60f;
        private const float DtSeconds = 1f / TicksPerSecond;

        public const byte CueMeleeHit = 1;
        public const byte CueRoleSwap = 2;
        public const byte CueAoeFired = 3;
        public const byte CueAoeHit = 4;

        public const byte VariantAttacker = 0;
        public const byte VariantJefe = 1;
        public const byte VariantAttackerStunned = 2;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly JefeParams _params;
        private readonly InputInterpreter _interpreter;
        private readonly InputBridge _inputBridge = new InputBridge();
        private readonly RenderState _renderState;

        // Stored only for IMicrogame contract compliance (GDD §10.2) — no
        // in-round randomness to consume: every rule here is a deterministic
        // function of input and position alone, same rationale as
        // PersigueMicrogame/BombardeaMicrogame's own docs.
        private SeededRandom _rng;
        private PlayerRoster _roster;

        private readonly bool[] _engaged = new bool[SeatCount];
        private readonly float[] _avatarX = new float[SeatCount];
        private readonly float[] _avatarY = new float[SeatCount];
        private readonly int[] _stunRemainingTicks = new int[SeatCount];

        private int _jefeSeat;
        private int _jefeHits;

        private int _tick;
        private int _durationTicks;
        private int _telegraphTicks;
        private int _aoeCooldownTicks;
        private int _stunTicks;
        private bool _isFinished;

        private int _lastAoeFireTick = int.MinValue / 2;
        private bool _aoeActive;
        private float _aoeX;
        private float _aoeY;
        private int _aoeFireTick;

        public JefeMicrogame(JefeParams parameters)
        {
            _params = parameters;
            _interpreter = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            _renderState = new RenderState(EntityCapacity, FeedbackCapacity);
        }

        // Test injection (mirrors EsquivaMicrogame/PersigueMicrogame's own SetForcedAvatarPositions).
        private (float x, float y)[] _forcedAvatarPositions;

        /// <summary>Override starting avatar positions for deterministic tests. Call before <see cref="Initialize"/>. Indexed by <c>(int)PlayerSlot</c>.</summary>
        public void SetForcedAvatarPositions((float x, float y)[] positions) => _forcedAvatarPositions = positions;

        /// <summary>Test-only: sets the Jefe's remaining hits directly, so a timeline test can reach "1 hit left" without playing out prior melee exchanges.</summary>
        public void SetJefeHitsForTest(int hits) => _jefeHits = hits;

        /// <summary>Test-only: directly injects a pending AoE blast, bypassing the button-edge fire path — mirrors BombardeaMicrogame.ForceFireShotForTest.</summary>
        public void ForceFireAoeForTest(float x, float y)
        {
            _aoeActive = true;
            _aoeX = x;
            _aoeY = y;
            _aoeFireTick = _tick;
            _lastAoeFireTick = _tick;
        }

        /// <inheritdoc/>
        public MicrogameId Id => MicrogameId.JefePhase;

        /// <inheritdoc/>
        public bool IsFinished => _isFinished;

        /// <inheritdoc/>
        public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _rng = rng;
            _roster = roster;

            for (int i = 0; i < SeatCount; i++)
                _engaged[i] = roster.Seats[i] == SeatState.Human || roster.Seats[i] == SeatState.Bot;

            if (!_engaged[_params.JefeSeat])
                throw new ArgumentException("JefeParams.JefeSeat must be an engaged (Human/Bot) seat.", nameof(roster));

            _tick = 0;
            _isFinished = false;
            _durationTicks = (int)MathF.Round(_params.DurationSeconds * TicksPerSecond);
            _telegraphTicks = (int)MathF.Round(_params.TelegraphSeconds * TicksPerSecond);
            _aoeCooldownTicks = (int)MathF.Round(_params.AoeCooldownSeconds * TicksPerSecond);
            _stunTicks = (int)MathF.Round(_params.AttackerStunSeconds * TicksPerSecond);

            _jefeSeat = _params.JefeSeat;
            _jefeHits = 3;

            _lastAoeFireTick = int.MinValue / 2;
            _aoeActive = false;
            _aoeX = 0f;
            _aoeY = 0f;
            _aoeFireTick = 0;

            _interpreter.Reset();

            for (int i = 0; i < SeatCount; i++)
            {
                _stunRemainingTicks[i] = 0;

                if (_forcedAvatarPositions != null && i < _forcedAvatarPositions.Length)
                {
                    _avatarX[i] = _forcedAvatarPositions[i].x;
                    _avatarY[i] = _forcedAvatarPositions[i].y;
                }
                else
                {
                    (float sx, float sy) = StartCorner(i);
                    _avatarX[i] = sx;
                    _avatarY[i] = sy;
                }
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
                if (_stunRemainingTicks[i] > 0) _stunRemainingTicks[i]--;

            MoveAvatars();
            ResolveMeleeAttacks();
            ResolveJefeAoe();

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

            var ranks = new PlayerRank[n];
            int w = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;

                bool isJefe = i == _jefeSeat;
                int place = isJefe ? 1 : 2;
                int metric = isJefe ? _jefeHits : 0;

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

        public int JefeSeat => _jefeSeat;
        public int JefeHits => _jefeHits;
        public bool IsStunned(PlayerSlot slot) => _stunRemainingTicks[(int)slot] > 0;
        public (float x, float y) GetAvatarPosition(PlayerSlot slot) => (_avatarX[(int)slot], _avatarY[(int)slot]);
        public bool IsAoeActive => _aoeActive;
        public int AoeFireTick => _aoeFireTick;
        public (float x, float y) GetAoePosition() => (_aoeX, _aoeY);
        public float EffectiveHitboxRadius(PlayerSlot slot) =>
            (int)slot == _jefeSeat ? _params.AvatarRadius * _params.JefeHitboxMultiplier : _params.AvatarRadius;

        // ------------------------------------------------------------------

        private void MoveAvatars()
        {
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                if (_stunRemainingTicks[i] > 0) continue; // frozen -- no movement while stunned

                (float dx, float dy) = InputBridge.ToUnitVector(_interpreter.RawDirection(AllSlots[i]));
                _avatarX[i] = Clamp01(_avatarX[i] + dx * _params.AvatarSpeed * DtSeconds);
                _avatarY[i] = Clamp01(_avatarY[i] + dy * _params.AvatarSpeed * DtSeconds);
            }
        }

        /// <summary>
        /// The same-tick role-swap core (see class doc). Iterates a FIXED
        /// seat order; re-reads <see cref="_jefeSeat"/> fresh every iteration
        /// so a swap earlier in this same tick is immediately visible to
        /// later iterations — no cached "who was Jefe at the start of this
        /// tick" snapshot exists anywhere.
        /// </summary>
        private void ResolveMeleeAttacks()
        {
            for (int i = 0; i < SeatCount; i++)
            {
                if (i == _jefeSeat) continue; // re-read every iteration -- see doc
                if (!_engaged[i]) continue;
                if (_stunRemainingTicks[i] > 0) continue; // frozen -- cannot melee while stunned
                if (!_interpreter.ButtonPressedThisTick(AllSlots[i])) continue;

                float reach = _params.AvatarRadius + _params.AvatarRadius * _params.JefeHitboxMultiplier + _params.MeleeRange;
                float dx = _avatarX[i] - _avatarX[_jefeSeat];
                float dy = _avatarY[i] - _avatarY[_jefeSeat];
                if (MathF.Sqrt(dx * dx + dy * dy) > reach) continue;

                _jefeHits--;
                EmitFeedback(i, FeedbackLevel.Medium, CueMeleeHit);

                if (_jefeHits <= 0)
                {
                    // "Robo de cuerpo": the swap happens INLINE, in this same
                    // statement, on this same tick -- no separate pending flag,
                    // no later phase of Tick applies it. _jefeSeat is updated
                    // immediately, so the very next loop iteration (i+1,
                    // possibly the SAME tick) already sees the new Jefe.
                    _jefeSeat = i;
                    _jefeHits = 2;
                    EmitFeedback(i, FeedbackLevel.High, CueRoleSwap);
                }
            }
        }

        private void ResolveJefeAoe()
        {
            if (!_aoeActive
                && _engaged[_jefeSeat]
                && _stunRemainingTicks[_jefeSeat] == 0
                && _interpreter.ButtonPressedThisTick(AllSlots[_jefeSeat])
                && _tick - _lastAoeFireTick >= _aoeCooldownTicks)
            {
                _aoeActive = true;
                _aoeX = _avatarX[_jefeSeat];
                _aoeY = _avatarY[_jefeSeat];
                _aoeFireTick = _tick;
                _lastAoeFireTick = _tick;
                EmitFeedback(_jefeSeat, FeedbackLevel.Medium, CueAoeFired);
            }

            if (_aoeActive && _tick - _aoeFireTick >= _telegraphTicks)
            {
                for (int i = 0; i < SeatCount; i++)
                {
                    if (i == _jefeSeat) continue; // whoever is Jefe AT RESOLUTION is immune -- correctly handles a mid-flight role swap
                    if (!_engaged[i]) continue;

                    float dx = _avatarX[i] - _aoeX, dy = _avatarY[i] - _aoeY;
                    if (MathF.Sqrt(dx * dx + dy * dy) > _params.AoeBlastRadius) continue;

                    _stunRemainingTicks[i] = _stunTicks;
                    EmitFeedback(i, FeedbackLevel.High, CueAoeHit);
                }
                _aoeActive = false;
            }
        }

        private static (float, float) StartCorner(int seatIndex)
        {
            switch (seatIndex)
            {
                case 0: return (0.15f, 0.15f);
                case 1: return (0.85f, 0.15f);
                case 2: return (0.85f, 0.85f);
                default: return (0.15f, 0.85f);
            }
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
            _renderState.Hud.ActiveVerb = "¡JEFE!";

            int entityCount = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                bool isJefe = i == _jefeSeat;
                _renderState.Hud.Scores[i] = isJefe ? _jefeHits : 0;

                if (!_roster.IsActive(AllSlots[i])) continue;

                byte variant = isJefe ? VariantJefe : (_stunRemainingTicks[i] > 0 ? VariantAttackerStunned : VariantAttacker);

                _renderState.Entities[entityCount].Kind = EntityKind.PlayerAvatar;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = _avatarX[i];
                _renderState.Entities[entityCount].Y = _avatarY[i];
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = isJefe ? _params.JefeHitboxMultiplier : 1f;
                _renderState.Entities[entityCount].VisualVariant = variant;
                _renderState.Entities[entityCount].Progress01 = 0f;
                entityCount++;
            }

            if (_aoeActive)
            {
                float progress = _telegraphTicks > 0 ? (float)(_tick - _aoeFireTick) / _telegraphTicks : 1f;
                if (progress < 0f) progress = 0f; else if (progress > 1f) progress = 1f;

                _renderState.Entities[entityCount].Kind = EntityKind.Projectile;
                _renderState.Entities[entityCount].OwnerSeat = _jefeSeat;
                _renderState.Entities[entityCount].X = _aoeX;
                _renderState.Entities[entityCount].Y = _aoeY;
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = _params.AoeBlastRadius;
                _renderState.Entities[entityCount].VisualVariant = 0;
                _renderState.Entities[entityCount].Progress01 = progress;
                entityCount++;
            }

            _renderState.EntityCount = entityCount;
        }
    }
}
