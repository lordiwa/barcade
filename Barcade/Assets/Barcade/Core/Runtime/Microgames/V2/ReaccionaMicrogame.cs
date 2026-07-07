using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// GDD §4 MECH_05 — ¡REACCIONA! (quick-draw). First implementation of the v2
    /// <see cref="IMicrogame"/> contract (see that interface's doc for the
    /// contract-decision rationale).
    ///
    /// <para>
    /// <b>Tanda state machine.</b> Each of <c>Rounds</c> tandas draws a fresh signal
    /// delay (and fakeout schedule) from the injected RNG, then every active seat is
    /// resolved to exactly one outcome for that tanda: <c>Reacted</c> (pressed at or
    /// after the signal, at least <c>AnticipationThresholdTicks</c> after it),
    /// <c>FalseStarted</c> (pressed before the signal, or pressed too soon after it —
    /// GDD §4 "anticipación estadística"), or <c>DidNotReact</c> (never pressed within
    /// <c>ReactionTimeoutSeconds</c> of the signal — see "DNF" below). A tanda
    /// concludes once every active seat has a non-pending outcome; the next tanda (if
    /// any) starts on the same tick the previous one concluded.
    /// </para>
    ///
    /// <para>
    /// <b>Debounce interaction (orchestrator note for T-103).</b> Press edges come
    /// from an internal <see cref="InputInterpreter"/> (T-101), whose 2-tick-confirm
    /// debounce reports every genuine press exactly 1 tick after it physically
    /// happened — uniformly for every seat (see <c>InputInterpreter.DebounceConfirmTicks</c>).
    /// The <c>AnticipationThresholdTicks</c> (GDD: 5 ticks / 90 ms) is applied directly
    /// to this debounce-confirmed latency, with no compensating subtraction. Two
    /// reasons: (1) the confirmed edge is the only signal Core ever observes — there is
    /// no access to a "true" pre-debounce tick to correct against; (2) the uniform
    /// +1-tick offset shifts every seat's measured latency by the same amount, so
    /// relative ranking (who was fastest) and tie detection are completely unaffected —
    /// it only means the effective true-physical anticipation threshold is
    /// ~1 tick tighter than the nominal 90 ms, which is imperceptible to players and
    /// is the same trade-off already documented on <c>InputInterpreter</c> itself. See
    /// <c>ReaccionaMicrogameTests.AnticipationThreshold_AppliesToDebounceConfirmedLatency_NotRawPressTick</c>.
    /// </para>
    ///
    /// <para>
    /// <b>InputInterpreter ownership.</b> This microgame owns a private, dedicated
    /// <see cref="InputInterpreter"/> instance (reset on every <see cref="Initialize"/>)
    /// rather than receiving a shared one — REACCIONA never needs hold/mash state to
    /// survive across a microgame boundary, so a self-contained instance is simplest to
    /// test and keeps this ticket's blast radius small. A future Framework-integration
    /// ticket may instead hoist one long-lived InputInterpreter across the whole play
    /// session (calling its <c>Reset()</c> at MG_INTRO, per T-101's original design)
    /// and pass it in — flagged for the reviewer as a possible follow-up, not a defect.
    /// </para>
    ///
    /// <para>
    /// <b>Ranking rule (GDD gap, documented assumption).</b> GDD §4 does not specify
    /// exactly how "best of 3 tandas" aggregates into a final ranking. This
    /// implementation sums, per seat, a per-tanda "badness" value across all tandas
    /// played — <c>latencyTicks</c> if the seat reacted legitimately, or a fixed large
    /// <see cref="FailurePenaltyTicks"/> if it false-started or did not react — then
    /// ranks ascending by that sum (standard competition ranking: ties share a place,
    /// the next distinct group skips ahead). This single rule handles best-of-1
    /// (the sum degenerates to that one tanda's value), best-of-3, and the
    /// all-active-seats-false-start case uniformly: everyone gets the same penalty sum
    /// and therefore ties at Place 1 — never fabricating a winner (GDD AC7).
    /// </para>
    ///
    /// <para>
    /// <b>DNF (did-not-react) — an addition beyond the literal GDD text.</b> GDD §4
    /// bounds the *pre*-signal wait (1.5-4.5s) but says nothing about a seat that
    /// never presses *after* the real signal. Without a bound, <see cref="IsFinished"/>
    /// could never become true for such a tanda. <c>ReactionTimeoutSeconds</c>
    /// (default 3s, <see cref="ReaccionaParams"/>) guarantees termination; a DNF seat
    /// pays the same <see cref="FailurePenaltyTicks"/> as a false start. Flagged for
    /// reviewer confirmation.
    /// </para>
    ///
    /// Pure C# — no UnityEngine dependency. C# 9 compatible. Zero heap allocation in
    /// steady-state <see cref="Tick"/> (all per-seat/per-tanda state is fixed-size
    /// arrays allocated once in the constructor).
    /// </summary>
    public sealed class ReaccionaMicrogame : IMicrogame
    {
        private const int SeatCount = 4;
        private const int EntityCapacity = 4;
        private const int FeedbackCapacity = 8;

        /// <summary>
        /// Fixed penalty (in tick units, comparable to a legitimate latency) applied to
        /// a seat's cumulative Metric for any tanda it does not legitimately react in
        /// (false start or DNF). Large enough that no realistic legitimate latency sum
        /// could ever match or exceed it.
        /// </summary>
        public const int FailurePenaltyTicks = 1_000_000;

        // Feedback cue codes (mechanic-defined; see FeedbackEvent.Cue doc).
        public const byte CueSignal = 1;
        public const byte CueFakeout = 2;
        public const byte CueFalseStart = 3;
        public const byte CueReacted = 4;
        public const byte CueDidNotReact = 5;

        // RenderEntity.VisualVariant codes for the PlayerAvatar entities this mechanic emits.
        public const byte VariantWaiting = 0;
        public const byte VariantFalseStarted = 1;
        public const byte VariantReacted = 2;
        public const byte VariantDidNotReact = 3;

        private enum SeatOutcome { Pending, Reacted, FalseStarted, DidNotReact }

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly ReaccionaParams _params;
        private readonly InputInterpreter _interpreter;
        private readonly RenderState _renderState;

        private ISeededRandom _rng;
        private PlayerRoster _roster;
        private int _reactionTimeoutTicks;

        private int _tick;          // absolute tick counter for the whole microgame lifetime
        private int _tandaIndex;    // 0-based, < _params.Rounds
        private int _tandaStartTick;
        private int _signalTick;    // absolute tick of the real signal for the current tanda
        private bool _signalFired;

        private readonly int[] _fakeoutTicks = new int[ReaccionaParams.MaxFakeouts];  // absolute ticks; -1 = unused slot
        private readonly bool[] _fakeoutFired = new bool[ReaccionaParams.MaxFakeouts];
        private int _fakeoutCountThisTanda;

        private readonly SeatOutcome[] _outcome = new SeatOutcome[SeatCount];
        private readonly int[] _latencyTicksThisTanda = new int[SeatCount];
        private readonly int[] _cumulativeMetric = new int[SeatCount];

        private bool _isFinished;

        public ReaccionaMicrogame() : this(ReaccionaParams.GddDefaults)
        {
        }

        public ReaccionaMicrogame(ReaccionaParams parameters)
        {
            _params = parameters;
            _interpreter = new InputInterpreter(parameters.InputConfig);
            _renderState = new RenderState(EntityCapacity, FeedbackCapacity);
        }

        /// <inheritdoc/>
        public MicrogameId Id => MicrogameId.Reacciona;

        /// <inheritdoc/>
        public bool IsFinished => _isFinished;

        /// <inheritdoc/>
        public void Initialize(ISeededRandom rng, PlayerRoster roster, float difficultyMult)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _rng = rng;
            _roster = roster;
            // GDD §4 defines no difficulty-scaled parameter for MECH_05; difficultyMult
            // is accepted for interface compliance only and currently has no effect.

            _interpreter.Reset();
            _tick = 0;
            _tandaIndex = 0;
            _isFinished = false;
            _reactionTimeoutTicks = (int)MathF.Round(_params.ReactionTimeoutSeconds * _params.InputConfig.TicksPerSecond);

            for (int i = 0; i < SeatCount; i++)
                _cumulativeMetric[i] = 0;

            StartTanda();
        }

        /// <inheritdoc/>
        public void Tick(IReadOnlyPlayerInputs inputs)
        {
            if (_isFinished) return;
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));

            _interpreter.Tick(inputs);
            _renderState.FeedbackCount = 0;

            for (int i = 0; i < _fakeoutCountThisTanda; i++)
            {
                if (!_fakeoutFired[i] && _tick == _fakeoutTicks[i])
                {
                    _fakeoutFired[i] = true;
                    EmitFeedback(-1, FeedbackLevel.Medium, CueFakeout);
                }
            }

            if (!_signalFired && _tick == _signalTick)
            {
                _signalFired = true;
                EmitFeedback(-1, FeedbackLevel.High, CueSignal);
            }

            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                if (_outcome[i] != SeatOutcome.Pending) continue;

                bool pressed = _interpreter.ButtonPressedThisTick(AllSlots[i]);

                if (pressed)
                {
                    if (_tick < _signalTick)
                    {
                        _outcome[i] = SeatOutcome.FalseStarted;
                        EmitFeedback(i, FeedbackLevel.Medium, CueFalseStart);
                    }
                    else
                    {
                        int latency = _tick - _signalTick;
                        if (latency < _params.AnticipationThresholdTicks)
                        {
                            _outcome[i] = SeatOutcome.FalseStarted;
                            EmitFeedback(i, FeedbackLevel.Medium, CueFalseStart);
                        }
                        else
                        {
                            _outcome[i] = SeatOutcome.Reacted;
                            _latencyTicksThisTanda[i] = latency;
                            EmitFeedback(i, FeedbackLevel.High, CueReacted);
                        }
                    }
                }
                else if (_tick >= _signalTick && (_tick - _signalTick) >= _reactionTimeoutTicks)
                {
                    _outcome[i] = SeatOutcome.DidNotReact;
                    EmitFeedback(i, FeedbackLevel.Low, CueDidNotReact);
                }
            }

            bool tandaDone = true;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                if (_outcome[i] == SeatOutcome.Pending) { tandaDone = false; break; }
            }

            if (tandaDone)
            {
                for (int i = 0; i < SeatCount; i++)
                {
                    if (!_roster.IsActive(AllSlots[i])) continue;
                    _cumulativeMetric[i] += _outcome[i] == SeatOutcome.Reacted
                        ? _latencyTicksThisTanda[i]
                        : FailurePenaltyTicks;
                }
            }

            PublishRenderState();

            if (tandaDone)
            {
                _tandaIndex++;
                if (_tandaIndex >= _params.Rounds)
                    _isFinished = true;
                else
                    StartTanda();
            }

            _tick++;
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
            int[] metrics = new int[n];
            int w = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                if (!_roster.IsActive(AllSlots[i])) continue;
                seats[w] = i;
                metrics[w] = _cumulativeMetric[i];
                w++;
            }

            // Insertion sort by metric ascending — n <= 4, no need for Array.Sort/LINQ.
            for (int i = 1; i < n; i++)
            {
                int seatKey = seats[i];
                int metricKey = metrics[i];
                int j = i - 1;
                while (j >= 0 && metrics[j] > metricKey)
                {
                    metrics[j + 1] = metrics[j];
                    seats[j + 1] = seats[j];
                    j--;
                }
                metrics[j + 1] = metricKey;
                seats[j + 1] = seatKey;
            }

            var ranks = new PlayerRank[n];
            for (int i = 0; i < n; i++)
            {
                int place;
                if (i == 0) place = 1;
                else if (metrics[i] == metrics[i - 1]) place = ranks[i - 1].Place;
                else place = i + 1;

                ranks[i] = new PlayerRank(seats[i], place, metrics[i]);
            }

            return new MicrogameResult(ResultKind.Ranked, ranks, 0);
        }

        /// <inheritdoc/>
        public RenderState GetRenderState() => _renderState;

        // ------------------------------------------------------------------

        private void StartTanda()
        {
            _tandaStartTick = _tick;

            float u = _rng.NextFloat();
            float delaySeconds = _params.SignalDelayMinSeconds + u * (_params.SignalDelayMaxSeconds - _params.SignalDelayMinSeconds);
            int delayTicks = (int)MathF.Round(delaySeconds * _params.InputConfig.TicksPerSecond);
            _signalTick = _tandaStartTick + delayTicks;
            _signalFired = false;

            _fakeoutCountThisTanda = _params.Fakeouts;
            for (int i = 0; i < ReaccionaParams.MaxFakeouts; i++)
            {
                if (i < _fakeoutCountThisTanda)
                {
                    float fu = _rng.NextFloat();
                    float fakeoutSeconds = fu * delaySeconds; // always < delaySeconds: strictly before the real signal
                    _fakeoutTicks[i] = _tandaStartTick + (int)MathF.Floor(fakeoutSeconds * _params.InputConfig.TicksPerSecond);
                }
                else
                {
                    _fakeoutTicks[i] = -1;
                }
                _fakeoutFired[i] = false;
            }

            for (int i = 0; i < SeatCount; i++)
            {
                _outcome[i] = SeatOutcome.Pending;
                _latencyTicksThisTanda[i] = 0;
            }
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
            _renderState.Hud.ActiveVerb = "¡REACCIONA!";

            int entityCount = 0;
            for (int i = 0; i < SeatCount; i++)
            {
                _renderState.Hud.Scores[i] = _cumulativeMetric[i];

                if (!_roster.IsActive(AllSlots[i])) continue;

                _renderState.Entities[entityCount].Kind = EntityKind.PlayerAvatar;
                _renderState.Entities[entityCount].OwnerSeat = i;
                _renderState.Entities[entityCount].X = entityCount / 3f; // simple left-to-right layout among active seats
                _renderState.Entities[entityCount].Y = 0.5f;
                _renderState.Entities[entityCount].Height = 0f;
                _renderState.Entities[entityCount].Rotation = 0f;
                _renderState.Entities[entityCount].Scale = 1f;
                _renderState.Entities[entityCount].VisualVariant = VariantFor(_outcome[i]);
                entityCount++;
            }
            _renderState.EntityCount = entityCount;
        }

        private static byte VariantFor(SeatOutcome outcome)
        {
            switch (outcome)
            {
                case SeatOutcome.FalseStarted: return VariantFalseStarted;
                case SeatOutcome.Reacted: return VariantReacted;
                case SeatOutcome.DidNotReact: return VariantDidNotReact;
                default: return VariantWaiting;
            }
        }
    }
}
