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
    /// <b>Debounce interaction (orchestrator note for T-103; recalibrated in the
    /// TASK-025 fix round).</b> Press edges come from an internal
    /// <see cref="InputInterpreter"/> (T-101), whose 2-tick-confirm debounce reports
    /// every genuine press exactly 1 tick after it physically happened — uniformly
    /// for every seat (see <c>InputInterpreter.DebounceConfirmTicks</c>).
    /// <c>AnticipationThresholdTicks</c> is applied directly to this
    /// debounce-confirmed latency, with no compensating subtraction — the confirmed
    /// edge is the only signal Core ever observes, so there is no "true"
    /// pre-debounce tick to correct against. Because confirmed latency = true
    /// physical latency + 1 (the uniform debounce offset), a threshold of N ticks
    /// on confirmed latency actually rejects physical latencies below (N-1) ticks,
    /// not below N. The GDD default is <b>6</b> ticks: physical latencies below 5
    /// ticks (83.3 ms) are anticipation, 5 ticks and up is legitimate — the
    /// closest tick-quantized value at or under the GDD's 90 ms nominal cutoff
    /// without exceeding it. (5 ticks was tried first and is wrong: it only
    /// rejects physical latencies below 4 ticks/66.7 ms — looser toward
    /// anticipators than the GDD intends, not tighter as an earlier draft of this
    /// comment claimed.) The uniform +1-tick offset shifts every seat's measured
    /// latency by the same amount regardless of the threshold's value, so relative
    /// ranking (who was fastest) and tie detection are completely unaffected by
    /// this calibration. See
    /// <c>ReaccionaMicrogameTests.AnticipationThreshold_AppliesToDebounceConfirmedLatency_NotRawPressTick</c>.
    /// </para>
    ///
    /// <para>
    /// <b>InputInterpreter ownership and the v2-&gt;v1 input bridge.</b> The public
    /// <see cref="Tick"/> signature takes the GDD-literal, session-level v2
    /// <see cref="InputSnapshot"/> (<c>{ int Tick; PlayerInput[] Players; }</c>). T-101's
    /// <see cref="InputInterpreter"/> — reused here for its debounce and edge
    /// detection rather than reimplemented — was built against the pre-existing v1,
    /// per-seat <c>Barcade.Core.InputSnapshot</c>/<c>IReadOnlyPlayerInputs</c>. A
    /// small private <c>InputBridge</c> adapter translates each tick's v2
    /// <c>PlayerInput{Direction8,bool}</c> into the v1 shape (mapping the raw
    /// button bool to <c>Held</c>/<c>Released</c> — <c>InputInterpreter</c> only ever
    /// distinguishes "down" from "up", so either "down" member works identically) so
    /// <c>_interpreter.Tick(_inputBridge)</c> still works unchanged. This keeps the
    /// *public* v2 contract GDD-literal while still getting T-101's debounce for
    /// free, without duplicating its logic. The bridge is allocated once and its
    /// source array reference is reassigned (not copied) each tick — zero allocation.
    /// This microgame owns a private, dedicated <see cref="InputInterpreter"/>
    /// instance (reset on every <see cref="Initialize"/>) rather than receiving a
    /// shared one — REACCIONA never needs hold/mash state to survive across a
    /// microgame boundary, so a self-contained instance is simplest to test and
    /// keeps this ticket's blast radius small. A future Framework-integration ticket
    /// may instead hoist one long-lived InputInterpreter across the whole play
    /// session (calling its <c>Reset()</c> at MG_INTRO, per T-101's original design)
    /// and pass it in — flagged for the reviewer as a possible follow-up, not a defect.
    /// </para>
    ///
    /// <para>
    /// <b>Tick source.</b> <see cref="InputSnapshot.Tick"/> is accepted (GDD-literal)
    /// but not read — this microgame maintains its own internal tick counter,
    /// starting at 0 on every <see cref="Initialize"/>. All of its own timing
    /// (signal delay, fakeout schedule, reaction timeout) is inherently relative to
    /// "ticks since this microgame started," so a local counter is sufficient and
    /// self-contained. <c>RenderState.Tick</c> therefore reflects that local count,
    /// not necessarily a global session tick. GDD T-102 (SessionStateMachine) has
    /// not landed yet, so there is no real global tick source to synchronize
    /// against today; a future ticket can switch to reading <c>input.Tick</c> once
    /// one exists, without changing any of this mechanic's own logic.
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

        /// <summary>
        /// Adapts this tick's v2 <see cref="InputSnapshot.Players"/> array (GDD-literal,
        /// session-level) into <see cref="IReadOnlyPlayerInputs"/> (v1, per-seat) so the
        /// internal <see cref="InputInterpreter"/> can be reused unchanged. Allocated
        /// once; <see cref="SetSource"/> reassigns a field to the caller's array
        /// reference each tick — no copying, no allocation.
        /// </summary>
        private sealed class InputBridge : IReadOnlyPlayerInputs
        {
            private PlayerInput[] _players;

            public void SetSource(PlayerInput[] players) => _players = players;

            public Barcade.Core.InputSnapshot For(PlayerSlot slot)
            {
                PlayerInput p = _players[(int)slot];
                ToStickXY(p.Stick, out float x, out float y);
                // InputInterpreter only ever distinguishes "down" from "up" (it treats
                // Pressed and Held identically as "down") — Held is an arbitrary but
                // harmless choice for the raw "down" state.
                ButtonState state = p.Button ? ButtonState.Held : ButtonState.Released;
                return new Barcade.Core.InputSnapshot(x, y, state);
            }

            private static void ToStickXY(Direction8 d, out float x, out float y)
            {
                switch (d)
                {
                    case Direction8.N: x = 0f; y = 1f; break;
                    case Direction8.S: x = 0f; y = -1f; break;
                    case Direction8.E: x = 1f; y = 0f; break;
                    case Direction8.W: x = -1f; y = 0f; break;
                    case Direction8.NE: x = 1f; y = 1f; break;
                    case Direction8.SE: x = 1f; y = -1f; break;
                    case Direction8.NW: x = -1f; y = 1f; break;
                    case Direction8.SW: x = -1f; y = -1f; break;
                    default: x = 0f; y = 0f; break;
                }
            }
        }

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly ReaccionaParams _params;
        private readonly InputInterpreter _interpreter;
        private readonly InputBridge _inputBridge = new InputBridge();
        private readonly RenderState _renderState;

        private SeededRandom _rng;
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
        public void Initialize(SeededRandom rng, PlayerRoster roster, float difficultyMult)
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
        public void Tick(in InputSnapshot input)
        {
            if (_isFinished) return;
            if (input.Players == null) throw new ArgumentException("InputSnapshot.Players must not be null.", nameof(input));

            _inputBridge.SetSource(input.Players);
            _interpreter.Tick(_inputBridge);
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
                    int fakeoutTick = _tandaStartTick + (int)MathF.Floor(fakeoutSeconds * _params.InputConfig.TicksPerSecond);

                    // Floor(fu*delaySeconds*tps) can still land ON _signalTick's own
                    // tick when Round(delaySeconds*tps) rounds DOWN (fractional part
                    // < 0.5) and fu is close to 1 — both quantize to the same integer
                    // just below the continuous delaySeconds*tps value. Clamp so a
                    // fakeout never lands on or after the real signal, preserving the
                    // "strictly before" invariant this method's earlier comment already
                    // promised. See ReaccionaMicrogameTests's regression test pinned to
                    // seed 8664, which reproduces this exact collision.
                    if (fakeoutTick >= _signalTick) fakeoutTick = _signalTick - 1;

                    _fakeoutTicks[i] = fakeoutTick;
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
