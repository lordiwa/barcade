using System;

namespace Barcade.Core
{
    /// <summary>
    /// Engine-free per-seat input interpretation layer (GDD T-101, §3.2/§3.3).
    ///
    /// Consumes the existing <see cref="InputSnapshot"/> stream — one snapshot per
    /// seat, sampled once per fixed 60 Hz simulation tick via
    /// <see cref="IReadOnlyPlayerInputs"/> — and derives, per seat, the signals the
    /// GDD says are computed by an InputInterpreter and never stored on the raw
    /// input struct: press/release edges, tap, hold duration, and mash frequency
    /// with the §3.3 response curve. It also collapses 8-way diagonal stick states
    /// to the most-recently-dominant cardinal for 4-direction consumers
    /// (anti-ghosting, §3.2).
    ///
    /// <para>
    /// <b>Delta from the GDD listing:</b> the GDD's <c>PlayerInput</c> carries a raw
    /// <c>bool Button</c> and an 8-way <c>Direction8 Stick</c>, held inside a
    /// session-level <c>InputSnapshot { int Tick; PlayerInput[] Players; }</c>. The
    /// type that actually exists in Core today, <see cref="InputSnapshot"/>, is
    /// per-seat (no <c>Tick</c>/<c>Players[]</c> wrapper), carries a continuous
    /// (StickX, StickY) pair instead of an 8-way enum, and a
    /// <see cref="ButtonState"/> enum whose <see cref="ButtonState.Pressed"/>
    /// member already bakes in a rising-edge concept upstream (see
    /// <c>AporreaMicrogame</c>, which treats <c>Pressed</c> as "just went down").
    /// This interpreter does not trust that pre-baked edge — it treats
    /// <c>Pressed || Held</c> as the raw "button down this tick" boolean and
    /// re-derives press/release edges itself with its own debounce, because the
    /// upstream edge carries no debounce guarantee. Stick direction is likewise
    /// re-derived to 8-way via <see cref="ToDirection8"/> since no 8-way type
    /// existed in Core before this ticket. The existing <see cref="InputSnapshot"/>
    /// and its consumers are left untouched — this is purely an additive layer on
    /// top of them.
    /// </para>
    ///
    /// <para>
    /// Fixed to the cabinet's 4 physical seats (<see cref="PlayerSlot"/>). All
    /// per-seat state is allocated once at construction; <see cref="Tick"/> and
    /// every query method perform zero heap allocations in steady state.
    /// </para>
    ///
    /// C# 9 compatible. No UnityEngine dependency — safe for the dotnet fast-test runner.
    /// </summary>
    public sealed class InputInterpreter
    {
        private const int SeatCount = 4;

        /// <summary>
        /// Fixed capacity of each seat's mash-edge ring buffer. Sized generously
        /// above any realistic press rate for the 500 ms default window (a human
        /// cannot sustain anywhere near 128 presses/second) so the buffer never
        /// needs to grow — allocated once at construction, never resized.
        /// </summary>
        private const int MashRingCapacity = 64;

        /// <summary>
        /// Number of consecutive identical raw samples required before a button
        /// transition is confirmed. Models the GDD's ~8 ms hardware bounce filter
        /// at tick granularity: a single-tick flip that reverts on the very next
        /// tick never reaches this threshold, so it is silently absorbed — no
        /// press/release edge is emitted and hold duration/mash counts are
        /// unaffected. A genuine transition is delayed by exactly one tick under
        /// the same rule; this uniform delay does not distort tap/hold
        /// measurement since both edges of a real press incur it equally.
        /// </summary>
        private const int DebounceConfirmTicks = 2;

        private static readonly PlayerSlot[] AllSlots =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private readonly InputInterpreterConfig _config;
        private readonly SeatState[] _seats;
        private int _currentTick;

        public InputInterpreter(InputInterpreterConfig config)
        {
            _config = config;
            _seats = new SeatState[SeatCount];
            for (int i = 0; i < SeatCount; i++)
                _seats[i] = new SeatState(MashRingCapacity);
        }

        /// <summary>
        /// Advances the interpreter by one fixed simulation tick, sampling every
        /// seat's current <see cref="InputSnapshot"/> from <paramref name="inputs"/>.
        /// The internal tick counter keeps advancing across <see cref="Reset"/>
        /// calls — only per-seat accumulated state is cleared there — so
        /// timestamps recorded before a reset never collide with ones recorded
        /// after it.
        /// </summary>
        public void Tick(IReadOnlyPlayerInputs inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));

            int tick = _currentTick;
            for (int i = 0; i < SeatCount; i++)
            {
                InputSnapshot snap = inputs.For(AllSlots[i]);
                ProcessSeat(_seats[i], snap, tick);
            }
            _currentTick = tick + 1;
        }

        /// <summary>
        /// Clears all accumulated per-seat state (edges, hold duration, mash
        /// history, diagonal-collapse memory) without resetting the internal tick
        /// clock. Call this on entering MG_INTRO so no input state bleeds from one
        /// microgame into the next (§3.2). A button already held down when Reset
        /// is called is treated as a brand-new press by the interpreter's next
        /// confirmed sample — the new microgame never inherits an in-progress hold.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < SeatCount; i++)
            {
                SeatState seat = _seats[i];
                seat.ConfirmedDown = false;
                seat.PendingDown = false;
                seat.PendingRunLength = 0;
                seat.PressedThisTick = false;
                seat.ReleasedThisTick = false;
                seat.TapThisTick = false;
                seat.HoldDurationTicks = 0;
                seat.PressTick = -1;
                seat.MashRingStart = 0;
                seat.MashRingCount = 0;
                seat.RawDirection = Direction8.None;
                seat.CollapsedCardinal = CardinalDir.None;
                seat.LastDominantCardinal = CardinalDir.None;
            }
        }

        // ------------------------------------------------------------------
        // Per-seat query API
        // ------------------------------------------------------------------

        /// <summary>True on the tick a debounced press edge (up -> down) is confirmed.</summary>
        public bool ButtonPressedThisTick(PlayerSlot slot) => _seats[(int)slot].PressedThisTick;

        /// <summary>True on the tick a debounced release edge (down -> up) is confirmed.</summary>
        public bool ButtonReleasedThisTick(PlayerSlot slot) => _seats[(int)slot].ReleasedThisTick;

        /// <summary>True only on the tick a release edge is confirmed whose matching press was within the tap window.</summary>
        public bool IsTapThisTick(PlayerSlot slot) => _seats[(int)slot].TapThisTick;

        /// <summary>Consecutive ticks the debounced button has been held down; 0 while up.</summary>
        public int HoldDurationTicks(PlayerSlot slot) => _seats[(int)slot].HoldDurationTicks;

        /// <summary>Raw mash frequency over the trailing mash window, in Hz (edges in window / window seconds).</summary>
        public float MashFrequencyHz(PlayerSlot slot)
        {
            SeatState seat = _seats[(int)slot];
            return seat.MashRingCount / _config.MashWindowSeconds;
        }

        /// <summary>
        /// GDD §3.3 response curve applied to <see cref="MashFrequencyHz"/>: 0
        /// below <see cref="InputInterpreterConfig.MashMinHz"/>, linear to 1 at
        /// <see cref="InputInterpreterConfig.MashSatHz"/>, clamped to [0,1].
        /// </summary>
        public float MashForce(PlayerSlot slot)
        {
            float hz = MashFrequencyHz(slot);
            float span = _config.MashSatHz - _config.MashMinHz;
            float t = (hz - _config.MashMinHz) / span;
            if (t < 0f) return 0f;
            if (t > 1f) return 1f;
            return t;
        }

        /// <summary>The raw (uncollapsed) 8-way direction derived from this tick's stick vector.</summary>
        public Direction8 RawDirection(PlayerSlot slot) => _seats[(int)slot].RawDirection;

        /// <summary>The diagonal-collapse result for 4-direction consumers: a pure cardinal, a collapsed diagonal, or None at rest.</summary>
        public CardinalDir CollapsedCardinal(PlayerSlot slot) => _seats[(int)slot].CollapsedCardinal;

        // ------------------------------------------------------------------
        // Static, stateless helpers (usable standalone by other consumers)
        // ------------------------------------------------------------------

        /// <summary>
        /// Converts a continuous stick vector to an 8-way discrete direction using
        /// the sign of each axis — matches <see cref="InputSnapshot.CardinalDirection"/>'s
        /// axis convention (+X = East/Right, +Y = North/Up).
        /// </summary>
        public static Direction8 ToDirection8(float stickX, float stickY)
        {
            bool right = stickX > 0f;
            bool left = stickX < 0f;
            bool up = stickY > 0f;
            bool down = stickY < 0f;

            if (!right && !left && !up && !down) return Direction8.None;
            if (right && up) return Direction8.NE;
            if (right && down) return Direction8.SE;
            if (left && up) return Direction8.NW;
            if (left && down) return Direction8.SW;
            if (right) return Direction8.E;
            if (left) return Direction8.W;
            return up ? Direction8.N : Direction8.S;
        }

        /// <summary>
        /// Diagonal-collapse helper (GDD §3.2 anti-ghosting). A pure cardinal
        /// passes through unchanged (and becomes the caller's new "most-recent
        /// dominant" for subsequent calls). A diagonal collapses to
        /// <paramref name="previousDominant"/> — the last pure cardinal seen.
        /// <see cref="Direction8.None"/> collapses to <see cref="CardinalDir.None"/>
        /// without implying any change to dominant-cardinal memory: a centred
        /// stick is not a reset, it simply reports no direction this tick.
        ///
        /// Cold start (a diagonal with no prior dominant cardinal — e.g. the very
        /// first tick, or right after <see cref="Reset"/>) deterministically
        /// prefers the horizontal component: NE/SE collapse to East, NW/SW
        /// collapse to West — mirroring the existing
        /// <see cref="InputSnapshot.CardinalDirection"/> tie-break, which also
        /// favors the X axis.
        ///
        /// Pure, stateless, and allocation-free — usable directly by any consumer
        /// that maintains its own dominant-cardinal memory, not just through a
        /// full <see cref="InputInterpreter"/> instance.
        /// </summary>
        public static CardinalDir CollapseDiagonal(Direction8 raw, CardinalDir previousDominant)
        {
            switch (raw)
            {
                case Direction8.None: return CardinalDir.None;
                case Direction8.N: return CardinalDir.Up;
                case Direction8.E: return CardinalDir.Right;
                case Direction8.S: return CardinalDir.Down;
                case Direction8.W: return CardinalDir.Left;
                case Direction8.NE:
                case Direction8.SE:
                    return previousDominant != CardinalDir.None ? previousDominant : CardinalDir.Right;
                case Direction8.NW:
                case Direction8.SW:
                    return previousDominant != CardinalDir.None ? previousDominant : CardinalDir.Left;
                default: return CardinalDir.None;
            }
        }

        // ------------------------------------------------------------------
        // Per-tick per-seat processing
        // ------------------------------------------------------------------

        private void ProcessSeat(SeatState seat, InputSnapshot snap, int tick)
        {
            bool rawDown = snap.Button == ButtonState.Pressed || snap.Button == ButtonState.Held;

            if (rawDown == seat.PendingDown)
                seat.PendingRunLength++;
            else
            {
                seat.PendingDown = rawDown;
                seat.PendingRunLength = 1;
            }

            bool wasDown = seat.ConfirmedDown;
            seat.PressedThisTick = false;
            seat.ReleasedThisTick = false;
            seat.TapThisTick = false;

            if (seat.PendingRunLength >= DebounceConfirmTicks && seat.PendingDown != seat.ConfirmedDown)
            {
                seat.ConfirmedDown = seat.PendingDown;

                if (seat.ConfirmedDown && !wasDown)
                {
                    seat.PressedThisTick = true;
                    seat.PressTick = tick;
                    seat.HoldDurationTicks = 1;
                    RecordMashPress(seat, tick);
                }
                else if (!seat.ConfirmedDown && wasDown)
                {
                    seat.ReleasedThisTick = true;
                    if (seat.PressTick >= 0 && (tick - seat.PressTick) <= _config.TapWindowTicks)
                        seat.TapThisTick = true;
                    seat.HoldDurationTicks = 0;
                    seat.PressTick = -1;
                }
            }
            else if (seat.ConfirmedDown)
            {
                seat.HoldDurationTicks++;
            }

            PurgeMashWindow(seat, tick);

            Direction8 raw = ToDirection8(snap.StickX, snap.StickY);
            seat.RawDirection = raw;
            CardinalDir collapsed = CollapseDiagonal(raw, seat.LastDominantCardinal);
            seat.CollapsedCardinal = collapsed;
            if (collapsed != CardinalDir.None)
                seat.LastDominantCardinal = collapsed;
        }

        private static void RecordMashPress(SeatState seat, int tick)
        {
            int capacity = seat.MashRing.Length;
            if (seat.MashRingCount == capacity)
            {
                // Ring full: drop the oldest entry. Guarantees no unbounded growth
                // and no allocation even under a press rate beyond MashRingCapacity.
                seat.MashRingStart = (seat.MashRingStart + 1) % capacity;
                seat.MashRingCount--;
            }

            int writeIndex = (seat.MashRingStart + seat.MashRingCount) % capacity;
            seat.MashRing[writeIndex] = tick;
            seat.MashRingCount++;
        }

        private void PurgeMashWindow(SeatState seat, int tick)
        {
            int windowTicks = _config.MashWindowTicks;
            int capacity = seat.MashRing.Length;
            while (seat.MashRingCount > 0 && tick - seat.MashRing[seat.MashRingStart] >= windowTicks)
            {
                seat.MashRingStart = (seat.MashRingStart + 1) % capacity;
                seat.MashRingCount--;
            }
        }

        /// <summary>Mutable per-seat accumulator. Allocated once (4 instances) at construction — never per tick.</summary>
        private sealed class SeatState
        {
            public bool ConfirmedDown;
            public bool PendingDown;
            public int PendingRunLength;

            public bool PressedThisTick;
            public bool ReleasedThisTick;
            public bool TapThisTick;

            public int HoldDurationTicks;
            public int PressTick = -1;

            public readonly int[] MashRing;
            public int MashRingStart;
            public int MashRingCount;

            public Direction8 RawDirection;
            public CardinalDir CollapsedCardinal;
            public CardinalDir LastDominantCardinal;

            public SeatState(int mashRingCapacity)
            {
                MashRing = new int[mashRingCapacity];
            }
        }
    }
}
