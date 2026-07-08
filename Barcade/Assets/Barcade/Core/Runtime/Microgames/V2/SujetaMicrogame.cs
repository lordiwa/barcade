using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_08 — ¡SUJETA!/¡SINCRONIZA! (cooperative hold-all-switches,
    /// T-112). v2 <see cref="IMicrogame"/> implementation. This is the BASE
    /// microgame only (3-8s round) — the 60-90s "fase cooperativa" (GDD §7.1,
    /// "MECH_08/MECH_09 ampliadas") is a separate, sibling-ticket scenario built
    /// on top of this mechanic; nothing here implements that expanded scenario.
    ///
    /// <para>
    /// <b>Rule (GDD literal, mode-independent — P2 bans a coop internal
    /// ranking, and by the same "no special-cased seat" spirit this mechanic
    /// gives every mode the exact same win/hold rule; only the countdown's
    /// VISIBILITY differs per <see cref="SujetaMode"/>).</b> Every ENGAGED seat
    /// (see <see cref="IsEngaged"/>) must hold its switch simultaneously for
    /// <see cref="SujetaParams.HoldWindowSeconds"/> to complete one window. An
    /// early release resets the CURRENT window's progress to 0 — never fails
    /// the round outright (GDD: "no falla la ronda entera"). The team succeeds
    /// once <see cref="SujetaParams.WindowsToWin"/> windows are completed within
    /// <see cref="SujetaParams.DurationSeconds"/>; otherwise the round times out
    /// as a coop failure. Both outcomes still pay (GDD §6.1 minimum-reward
    /// invariant; see <see cref="Barcade.Core.Scoring.PayoutRules.ApplyCoop"/>).
    /// </para>
    ///
    /// <para>
    /// <b>Coop-abandonment (GDD AC2, "4/4 -&gt; 3/3").</b> <see cref="IsEngaged"/>
    /// is deliberately NOT the same test every other v2 mechanic uses
    /// (<see cref="PlayerRoster.IsActive"/>, which already treats
    /// <see cref="SeatState.HumanIdle"/> as active for movement/render
    /// purposes). A <see cref="SeatState.HumanIdle"/> seat provides no input by
    /// definition — requiring its switch would make the coop objective
    /// impossible whenever a player steps away, exactly what GDD AC2 forbids.
    /// So the required-holds set here is Human OR Bot seats ONLY, snapshotted
    /// once at <see cref="Initialize"/> (seat occupancy does not change
    /// mid-round in this contract): an Idle (or Empty) seat is silently dropped
    /// from the requirement, converting 4/4 to 3/3 automatically — no special
    /// case in the hold-check loop itself, just a smaller set to iterate.
    /// </para>
    ///
    /// <para>
    /// <b>No internal ranking (GDD P2 / §6.1).</b> <see cref="GetResult"/>
    /// always returns <see cref="ResultKind.CoopSuccess"/>/<see cref="ResultKind.CoopFail"/>
    /// with an EMPTY <see cref="MicrogameResult.Ranks"/> array — there is no
    /// per-seat place to compute, structurally, not by omission. Every engaged
    /// seat receives the identical <see cref="Barcade.Core.Scoring.PayoutRules.ApplyCoop"/>
    /// payout.
    /// </para>
    ///
    /// <para>
    /// <b>infoAsym (GDD AC3).</b> In <see cref="SujetaMode.InfoAsym"/>, the true
    /// current-window progress is published ONLY at
    /// <c>HudState.Meters[<see cref="SujetaParams.ViewerSeat"/>]</c>; every other
    /// active seat's <c>Meters</c> entry is held at a constant 0 regardless of
    /// the real progress — not merely a convention a bot/presenter is expected
    /// to respect, but the actual published value carries zero timing
    /// information for a non-viewer (a constant is indistinguishable from "no
    /// data"). In <see cref="SujetaMode.HoldTogether"/> (and the unimplemented
    /// <see cref="SujetaMode.PlatformSync"/> fallback), every active seat's
    /// <c>Meters</c> entry shows the SAME true progress (GDD: "marcador de
    /// progreso circular común, gigante").
    /// </para>
    ///
    /// <para>
    /// <b>Difficulty scaling (GDD §9.1: "aplicado a velocidad/densidad", mapped
    /// here as the closest analogue for a hold mechanic).</b> [ASSUMED]
    /// <c>difficultyMult</c> multiplies the effective hold-window tick count
    /// directly — a harder round asks the team to hold together LONGER per
    /// window, the same direct-multiply convention
    /// <c>EsquivaMicrogame</c>/<c>CorreMicrogame</c> already use for their own
    /// difficulty-scaled fields.
    /// </para>
    ///
    /// Fixed 60 Hz tick. Pure C# — no UnityEngine dependency. C# 9 compatible.
    /// Zero heap allocation in steady-state <see cref="Tick"/> — all per-seat
    /// state is fixed-size arrays allocated once in the constructor.
    /// <see cref="GetResult"/> allocates once per round (an empty array), the
    /// same accepted pattern as every other v2 mechanic.
    /// </summary>
    public sealed class SujetaMicrogame : IMicrogame
    {
        private const int SeatCount = 4;
        private const int EntityCapacity = SeatCount;
        private const int FeedbackCapacity = 4;

        private const float TicksPerSecond = 60f;

        public const byte CueWindowCompleted = 1;
        public const byte CueWindowReset = 2;

        public const byte VariantNotHeld = 0;
        public const byte VariantHeld = 1;
        public const byte VariantExcluded = 2;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private static readonly (float x, float y)[] SwitchLayout =
        {
            (0.3f, 0.3f), (0.7f, 0.3f), (0.3f, 0.7f), (0.7f, 0.7f)
        };

        private readonly SujetaParams _params;
        private readonly InputInterpreter _interpreter;
        private readonly InputBridge _inputBridge = new InputBridge();
        private readonly RenderState _renderState;

        // Stored only for IMicrogame contract compliance (GDD §10.2) — no
        // in-round randomness to consume: the hold/window rule is a pure
        // function of input alone, same rationale as PersigueMicrogame/
        // BombardeaMicrogame's own docs.
        private SeededRandom _rng;
        private PlayerRoster _roster;

        private readonly bool[] _engaged = new bool[SeatCount];

        private int _tick;
        private int _durationTicks;
        private int _effectiveHoldWindowTicks;
        private bool _isFinished;

        private int _currentWindowHoldTicks;
        private int _windowsCompleted;

        public SujetaMicrogame(SujetaParams parameters)
        {
            _params = parameters;
            _interpreter = new InputInterpreter(InputInterpreterConfig.GddDefaults);
            _renderState = new RenderState(EntityCapacity, FeedbackCapacity);
        }

        /// <inheritdoc/>
        public MicrogameId Id => MicrogameId.Sujeta;

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

            int holdWindowTicks = (int)MathF.Round(_params.HoldWindowSeconds * TicksPerSecond);
            _effectiveHoldWindowTicks = Math.Max(1, (int)MathF.Round(holdWindowTicks * difficultyMult));

            _currentWindowHoldTicks = 0;
            _windowsCompleted = 0;

            _interpreter.Reset();

            for (int i = 0; i < SeatCount; i++)
                _engaged[i] = roster.Seats[i] == SeatState.Human || roster.Seats[i] == SeatState.Bot;

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

            bool allEngagedHeld = true;
            bool anyEngaged = false;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_engaged[i]) continue;
                anyEngaged = true;
                if (_interpreter.HoldDurationTicks(AllSlots[i]) <= 0)
                {
                    allEngagedHeld = false;
                    break;
                }
            }

            if (anyEngaged && allEngagedHeld)
            {
                _currentWindowHoldTicks++;
                if (_currentWindowHoldTicks >= _effectiveHoldWindowTicks)
                {
                    _windowsCompleted++;
                    _currentWindowHoldTicks = 0;
                    EmitFeedback(-1, FeedbackLevel.High, CueWindowCompleted);
                }
            }
            else if (_currentWindowHoldTicks > 0)
            {
                // GDD literal: an early release resets the CURRENT window's
                // progress -- it never fails the round outright.
                _currentWindowHoldTicks = 0;
                EmitFeedback(-1, FeedbackLevel.Medium, CueWindowReset);
            }

            PublishRenderState();

            _tick++;
            if (_tick >= _durationTicks || _windowsCompleted >= _params.WindowsToWin)
                _isFinished = true;
        }

        /// <inheritdoc/>
        public MicrogameResult GetResult()
        {
            if (!_isFinished)
                throw new InvalidOperationException("GetResult() called before IsFinished.");

            bool success = _windowsCompleted >= _params.WindowsToWin;
            return new MicrogameResult(
                success ? ResultKind.CoopSuccess : ResultKind.CoopFail,
                Array.Empty<PlayerRank>(),
                _windowsCompleted);
        }

        /// <inheritdoc/>
        public RenderState GetRenderState() => _renderState;

        // ------------------------------------------------------------------
        // Test/telemetry accessors (public — same convention as every other v2 mechanic).
        // ------------------------------------------------------------------

        public bool IsEngaged(PlayerSlot slot) => _engaged[(int)slot];
        public int CurrentWindowHoldTicks => _currentWindowHoldTicks;
        public int WindowsCompleted => _windowsCompleted;
        public int EffectiveHoldWindowTicks => _effectiveHoldWindowTicks;

        // ------------------------------------------------------------------

        private void EmitFeedback(int seat, FeedbackLevel level, byte cue)
        {
            if (_renderState.FeedbackCount >= _renderState.Feedback.Length) return;
            _renderState.Feedback[_renderState.FeedbackCount] = new FeedbackEvent(seat, level, cue);
            _renderState.FeedbackCount++;
        }

        private void PublishRenderState()
        {
            _renderState.Tick = _tick;
            _renderState.Hud.ActiveVerb = _params.Mode == SujetaMode.InfoAsym ? "¡SINCRONIZA!" : "¡SUJETA!";

            float trueProgress = _effectiveHoldWindowTicks > 0
                ? (float)_currentWindowHoldTicks / _effectiveHoldWindowTicks
                : 0f;

            int entityCount = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                // GDD P2/§6.1: identical coop scoring -- every seat's HUD score
                // is the SAME shared windows-completed count, reinforcing
                // visually that there is no internal ranking to chase.
                _renderState.Hud.Scores[i] = _windowsCompleted;

                bool isViewer = i == _params.ViewerSeat;
                bool showsRealProgress = _params.Mode != SujetaMode.InfoAsym || isViewer;
                _renderState.Hud.Meters[i] = showsRealProgress ? trueProgress : 0f;

                if (!_roster.IsActive(AllSlots[i])) continue;

                bool held = _interpreter.HoldDurationTicks(AllSlots[i]) > 0;
                byte variant = !_engaged[i] ? VariantExcluded : (held ? VariantHeld : VariantNotHeld);

                _renderState.Entities[entityCount].Kind = EntityKind.Switch;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = SwitchLayout[i].x;
                _renderState.Entities[entityCount].Y = SwitchLayout[i].y;
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = variant;
                _renderState.Entities[entityCount].Progress01 = 0f;
                entityCount++;
            }

            _renderState.EntityCount = entityCount;
        }
    }
}
