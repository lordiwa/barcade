using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_09 — ¡IGUALA! (`colorRelay` cooperative mode, T-112). v2
    /// <see cref="IMicrogame"/> implementation. [ASSUMED SCOPE] the competitive
    /// `simonSolo` variant ("todos memorizan una secuencia... gana el de mayor
    /// longitud correcta con menor tiempo") is a SEPARATE mode this ticket's
    /// ACs never name — not implemented; <see cref="Initialize"/> only ever
    /// runs the colorRelay rules below.
    ///
    /// <para>
    /// <b>Color/zone/seat mapping (GDD literal shape, [ASSUMED] exact
    /// assignment).</b> GDD: "4 zonas de color... en cruz (N/E/S/O)
    /// coincidiendo con el color del puesto físico más cercano" — Core has no
    /// notion of physical cabinet geometry, so the N/E/S/O -&gt; seat-color
    /// assignment is a fixed, arbitrary (but documented) convention: Up=Rojo,
    /// Right=Azul, Down=Amarillo, Left=Verde (<see cref="ColorForZone"/>). A
    /// color's "owner" seat IS that color's own seat index — Rojo(0) owns
    /// color 0, etc. — there is no separate ownership table to keep in sync.
    /// </para>
    ///
    /// <para>
    /// <b>Rule (GDD literal).</b> A sequence of colors is generated once at
    /// <see cref="Initialize"/> (<see cref="GenerateSequence"/>). At any time
    /// the relay expects exactly one color: <c>sequence[Progress]</c>. Only
    /// that color's OWNER may confirm it — a seat confirms by aiming its stick
    /// at that zone (<see cref="InputInterpreter.CollapsedCardinal"/>, the
    /// existing TASK-023 diagonal-collapse helper, reused verbatim — not
    /// reimplemented) and tapping the button on the SAME tick. A correct
    /// owner-confirm advances <see cref="Progress"/> by 1; the relay succeeds
    /// once <see cref="Progress"/> reaches <see cref="IgualaParams.SequenceLength"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Wrong-owner foul (GDD literal, AC3).</b> If some OTHER seat aims at
    /// and confirms the CURRENTLY EXPECTED zone (not a random other zone —
    /// GDD's foul is specifically about confirming "el color" shown, i.e. the
    /// active one; aiming at an inactive zone and pressing is simply ignored,
    /// not a foul), that seat's action is a foul: <see cref="Progress"/> drops
    /// by exactly 1 (floored at 0 — "−1 al progreso común, nunca elimina"),
    /// and the erring seat is recorded both as a <see cref="FeedbackEvent"/>
    /// (<see cref="CueWrongOwner"/>) and via the public
    /// <see cref="LastErringSeat"/>/<see cref="GetErrorCount"/> telemetry — the
    /// GDD's own "para el '¡fuiste tú!' del grupo" social-attribution intent
    /// (P2).
    /// </para>
    ///
    /// <para>
    /// <b>[ASSUMED] a pure timeout (nobody confirms in time) is neutral, not a
    /// foul.</b> GDD names exactly one foul — a WRONG-owner confirm. It says
    /// nothing about what happens if the window simply expires with no input
    /// at all. Treating "nobody acted" as free (the window just restarts for
    /// the SAME expected color) is the reading most consistent with GDD's own
    /// coop framing across MECH_08/09 ("nunca falla la ronda entera" /
    /// retryable-until-time-runs-out) — a team that reacts slowly keeps
    /// getting more tries, right up until <see cref="IgualaParams.DurationSeconds"/>
    /// runs out; only an actively WRONG action costs progress.
    /// </para>
    ///
    /// <para>
    /// <b>Coop-abandonment (carried forward from MECH_08's review).</b> A
    /// color owned by an Idle or Empty seat can never be confirmed by anyone
    /// — so <see cref="GenerateSequence"/> draws ONLY from ENGAGED seats
    /// (<see cref="IsEngaged"/>, the exact Human-or-Bot predicate
    /// <c>SujetaMicrogame</c> introduced), never from an abandoned seat's
    /// color, the same "never impossible by abandonment" guarantee. With
    /// fewer than 2 engaged seats the no-3-consecutive rule below is
    /// vacuously waived (mathematically impossible to satisfy with a single
    /// available color) — a documented degenerate-case fallback, not a
    /// silent gap.
    /// </para>
    ///
    /// <para>
    /// <b>No-3-consecutive generation (GDD AC1, single deterministic draw per
    /// symbol).</b> For sequence index k &gt;= 2 where the previous two symbols
    /// are already equal, the candidate draw excludes that repeated color
    /// entirely (drawing from the remaining engaged colors only) — this
    /// GUARANTEES the property by construction, with exactly one
    /// <see cref="SeededRandom"/> draw per symbol (never an unbounded
    /// reject-and-redraw loop, which would make RNG consumption
    /// non-deterministic in count).
    /// </para>
    ///
    /// <para>
    /// <b>No internal ranking (GDD P2 / §6.1).</b> <see cref="GetResult"/>
    /// always returns an EMPTY <see cref="MicrogameResult.Ranks"/> array for
    /// <see cref="ResultKind.CoopSuccess"/>/<see cref="ResultKind.CoopFail"/> —
    /// structural, not by omission — even though per-seat error counts are
    /// separately tracked and published (telemetry/social-attribution only,
    /// never converted into a differential payout).
    /// </para>
    ///
    /// <para>
    /// <b>Difficulty scaling (GDD §9.1).</b> [ASSUMED] <c>difficultyMult</c>
    /// divides the EFFECTIVE base reaction window
    /// (<see cref="EffectiveReactWindow0"/>) — a harder round starts the relay
    /// with a shorter window, the same "harder = less margin" mapping
    /// Esquiva/Corre already use for their own difficulty-scaled fields
    /// (WindowDecay itself is left unscaled for simplicity).
    /// </para>
    ///
    /// Fixed 60 Hz tick. Pure C# — no UnityEngine dependency. C# 9 compatible.
    /// Zero heap allocation in steady-state <see cref="Tick"/> — the sequence
    /// and all per-seat state are fixed-size arrays allocated once in the
    /// constructor. <see cref="GetResult"/> allocates once per round (an empty
    /// array), the same accepted pattern as every other v2 mechanic.
    /// </summary>
    public sealed class IgualaMicrogame : IMicrogame
    {
        private const int SeatCount = 4;
        private const int EntityCapacity = SeatCount + SeatCount; // 4 zone targets + 4 player avatars
        private const int FeedbackCapacity = 4;

        private const float TicksPerSecond = 60f;

        public const byte CueCorrectConfirm = 1;
        public const byte CueWrongOwner = 2;

        public const byte VariantInactiveZone = 0;
        public const byte VariantActiveZone = 1;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private static readonly (float x, float y)[] ZonePosition =
        {
            (0.5f, 0.2f), // Up    -> Rojo (0)
            (0.8f, 0.5f), // Right -> Azul (1)
            (0.5f, 0.8f), // Down  -> Amarillo (2)
            (0.2f, 0.5f), // Left  -> Verde (3)
        };

        private readonly IgualaParams _params;
        private readonly InputInterpreter _interpreter;
        private readonly InputBridge _inputBridge = new InputBridge();
        private readonly RenderState _renderState;

        private SeededRandom _rng;
        private PlayerRoster _roster;

        private readonly bool[] _engaged = new bool[SeatCount];
        private readonly int[] _engagedColors = new int[SeatCount];
        private int _engagedCount;

        private readonly int[] _colorSequence;
        private readonly int[] _errorCountBySeat = new int[SeatCount];

        private int _tick;
        private int _durationTicks;
        private float _effectiveReactWindow0;
        private bool _isFinished;

        private int _progress;
        private int _windowRemainingTicks;
        private int _lastErringSeat = -1;

        public IgualaMicrogame(IgualaParams parameters)
        {
            _params = parameters;
            _interpreter = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            _renderState = new RenderState(EntityCapacity, FeedbackCapacity);
            _colorSequence = new int[parameters.SequenceLength];
        }

        /// <inheritdoc/>
        public MicrogameId Id => MicrogameId.Iguala;

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
            _effectiveReactWindow0 = difficultyMult > 0f ? _params.ReactWindow0 / difficultyMult : _params.ReactWindow0;

            _progress = 0;
            _lastErringSeat = -1;

            _interpreter.Reset();

            _engagedCount = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                _engaged[i] = roster.Seats[i] == SeatState.Human || roster.Seats[i] == SeatState.Bot;
                _errorCountBySeat[i] = 0;
                if (_engaged[i]) _engagedColors[_engagedCount++] = i;
            }
            if (_engagedCount == 0)
                throw new ArgumentException("PlayerRoster has no engaged (Human/Bot) seats.", nameof(roster));

            GenerateSequence();

            _windowRemainingTicks = WindowTicksFor(_progress);

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

            if (_windowRemainingTicks > 0) _windowRemainingTicks--;

            bool eventHappened = false;
            int expectedColor = _progress < _params.SequenceLength ? _colorSequence[_progress] : -1;

            if (expectedColor >= 0)
            {
                for (int i = 0; i < SeatCount; i++)
                {
                    if (!_roster.IsActive(AllSlots[i])) continue;
                    if (!_interpreter.ButtonPressedThisTick(AllSlots[i])) continue;

                    CardinalDir zone = _interpreter.CollapsedCardinal(AllSlots[i]);
                    if (zone == CardinalDir.None) continue;
                    if (ColorForZone(zone) != expectedColor) continue; // irrelevant confirm -- not the active zone

                    if (i == expectedColor)
                    {
                        _progress++;
                        _lastErringSeat = -1;
                        EmitFeedback(i, FeedbackLevel.Medium, CueCorrectConfirm);
                    }
                    else
                    {
                        _progress = _progress > 0 ? _progress - 1 : 0;
                        _errorCountBySeat[i]++;
                        _lastErringSeat = i;
                        EmitFeedback(i, FeedbackLevel.Medium, CueWrongOwner);
                    }

                    eventHappened = true;
                    break; // one relevant confirm per tick
                }
            }

            if (_progress < _params.SequenceLength && (eventHappened || _windowRemainingTicks <= 0))
                _windowRemainingTicks = WindowTicksFor(_progress);

            PublishRenderState();

            _tick++;
            if (_tick >= _durationTicks || _progress >= _params.SequenceLength)
                _isFinished = true;
        }

        /// <inheritdoc/>
        public MicrogameResult GetResult()
        {
            if (!_isFinished)
                throw new InvalidOperationException("GetResult() called before IsFinished.");

            bool success = _progress >= _params.SequenceLength;
            return new MicrogameResult(
                success ? ResultKind.CoopSuccess : ResultKind.CoopFail,
                Array.Empty<PlayerRank>(),
                _progress);
        }

        /// <inheritdoc/>
        public RenderState GetRenderState() => _renderState;

        // ------------------------------------------------------------------
        // Static, stateless helpers (usable standalone; also drive tests/bots)
        // ------------------------------------------------------------------

        /// <summary>GDD literal formula: reactWindow(n) = max(0.45, reactWindow0 - windowDecay*n).</summary>
        public static float ReactWindowSeconds(float reactWindow0, float windowDecay, int n)
        {
            float raw = reactWindow0 - windowDecay * n;
            return MathF.Max(IgualaParams.ReactWindowFloorSeconds, raw);
        }

        /// <summary>[ASSUMED] fixed N/E/S/O -> seat-color assignment (see class doc). Never called with <see cref="CardinalDir.None"/>.</summary>
        public static int ColorForZone(CardinalDir zone)
        {
            switch (zone)
            {
                case CardinalDir.Up: return 0;
                case CardinalDir.Right: return 1;
                case CardinalDir.Down: return 2;
                default: return 3; // Left
            }
        }

        /// <summary>Inverse of <see cref="ColorForZone"/>.</summary>
        public static CardinalDir ZoneForColor(int color)
        {
            switch (color)
            {
                case 0: return CardinalDir.Up;
                case 1: return CardinalDir.Right;
                case 2: return CardinalDir.Down;
                default: return CardinalDir.Left;
            }
        }

        // ------------------------------------------------------------------
        // Test/telemetry accessors (public — same convention as every other v2 mechanic).
        // ------------------------------------------------------------------

        public bool IsEngaged(PlayerSlot slot) => _engaged[(int)slot];
        public int Progress => _progress;
        public int GetSequenceColor(int index) => _colorSequence[index];
        public int SequenceLength => _params.SequenceLength;
        public int WindowRemainingTicks => _windowRemainingTicks;
        public int LastErringSeat => _lastErringSeat;
        public int GetErrorCount(PlayerSlot slot) => _errorCountBySeat[(int)slot];
        public float EffectiveReactWindow0 => _effectiveReactWindow0;

        /// <summary>Current expected window length in ticks for <see cref="Progress"/> — exposed for exact tick-resolution tests.</summary>
        public int WindowTicksFor(int n) => (int)MathF.Round(ReactWindowSeconds(_effectiveReactWindow0, _params.WindowDecay, n) * TicksPerSecond);

        // ------------------------------------------------------------------

        private void GenerateSequence()
        {
            for (int k = 0; k < _colorSequence.Length; k++)
            {
                if (_engagedCount >= 2 && k >= 2 && _colorSequence[k - 1] == _colorSequence[k - 2])
                    _colorSequence[k] = PickExcluding(_colorSequence[k - 1]);
                else
                    _colorSequence[k] = _engagedColors[_rng.NextInt(0, _engagedCount)];
            }
        }

        /// <summary>Draws uniformly from the engaged-color pool, excluding <paramref name="excluded"/> — exactly one RNG draw, requires <see cref="_engagedCount"/> &gt;= 2.</summary>
        private int PickExcluding(int excluded)
        {
            int r = _rng.NextInt(0, _engagedCount - 1);
            int seen = 0;
            for (int p = 0; p < _engagedCount; p++)
            {
                if (_engagedColors[p] == excluded) continue;
                if (seen == r) return _engagedColors[p];
                seen++;
            }
            return _engagedColors[0]; // unreachable defensive fallback
        }

        private void EmitFeedback(int seat, FeedbackLevel level, byte cue)
        {
            if (_renderState.FeedbackCount >= _renderState.Feedback.Length) return;
            _renderState.Feedback[_renderState.FeedbackCount] = new FeedbackEvent(seat, level, cue);
            _renderState.FeedbackCount++;
        }

        private void PublishRenderState()
        {
            _renderState.Tick = _tick;
            _renderState.Hud.ActiveVerb = "¡IGUALA!";

            int expectedColor = _progress < _params.SequenceLength ? _colorSequence[_progress] : -1;
            float windowProgress = _windowRemainingTicks > 0 && expectedColor >= 0
                ? 1f - (float)_windowRemainingTicks / MathF.Max(1, WindowTicksFor(_progress))
                : 0f;

            int entityCount = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                _renderState.Hud.Scores[i] = _errorCountBySeat[i];
                _renderState.Hud.Meters[i] = windowProgress; // shared visibility -- no infoAsym variant for MECH_09
            }

            for (int color = 0; color < SeatCount; color++)
            {
                (float x, float y) = ZonePosition[color];
                _renderState.Entities[entityCount].Kind = EntityKind.Target;
                _renderState.Entities[entityCount].OwnerSeat = color; // the zone's owner seat
                _renderState.Entities[entityCount].X = x;
                _renderState.Entities[entityCount].Y = y;
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = color == expectedColor ? VariantActiveZone : VariantInactiveZone;
                _renderState.Entities[entityCount].Progress01 = color == expectedColor ? windowProgress : 0f;
                entityCount++;
            }

            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;

                CardinalDir aim = _interpreter.CollapsedCardinal(AllSlots[i]);
                (float x, float y) = aim == CardinalDir.None ? (0.5f, 0.5f) : ZonePosition[ColorForZone(aim)];

                _renderState.Entities[entityCount].Kind = EntityKind.PlayerAvatar;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = x;
                _renderState.Entities[entityCount].Y = y;
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
