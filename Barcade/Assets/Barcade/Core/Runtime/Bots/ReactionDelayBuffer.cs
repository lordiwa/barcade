using Barcade.Core.Microgames.V2;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// Models GDD Annex D.3's reactionDelayTicks as decision-to-actuation lag,
    /// shared by every <c>*BotPolicy</c>.
    ///
    /// <para>
    /// <b>[ASSUMED] why the delay is applied to the DECISION, not the
    /// OBSERVATION.</b> The obvious-looking alternative — buffer the last N
    /// ticks' <see cref="RenderState"/> and have the bot react to a STALE one —
    /// is unsound here: <see cref="RenderState"/> is documented as "a single
    /// mutable instance... fields are overwritten every tick" (see its own class
    /// doc), so holding onto an old reference does not preserve old data; by the
    /// time a delayed read happened, the fields would already reflect the
    /// CURRENT tick (a silent aliasing bug, not stale data). Delaying the
    /// ACTUATION instead sidesteps this entirely and is an equally honest model
    /// of human reaction lag: the bot's "brain" computes the correct response
    /// to the CURRENT tick's state immediately (matching every pre-existing
    /// solver's own zero-latency read pattern — <c>EsquivaEscapeBot</c>,
    /// <c>MantenCorrectionOracle</c> — so their proven logic can be reused
    /// unchanged as the "ideal decision" step), but that decision only reaches
    /// the "muscles" (the returned <see cref="PlayerInput"/>) reactionDelayTicks
    /// later — a fixed-capacity ring buffer of plain <see cref="PlayerInput"/>
    /// VALUES (never a <see cref="RenderState"/> reference), so no aliasing is
    /// possible.
    /// </para>
    ///
    /// <para>
    /// Before the buffer has accumulated <c>delayTicks</c> pushes, <see cref="PushAndPop"/>
    /// returns <c>default(PlayerInput)</c> (neutral stick, button up) — a bot
    /// that "hasn't reacted yet" sends no input, the same as a human mid-thought.
    /// Zero heap allocation after construction (fixed-size array).
    /// </para>
    /// </summary>
    public sealed class ReactionDelayBuffer
    {
        /// <summary>
        /// Generous headroom above any realistic reactionDelayTicks (Novato's
        /// mean+3sd is 20+15=35 ticks; Bot.Medio/Optimo are far lower) — a
        /// larger-than-needed capacity just means more of the buffer sits
        /// unused, not a correctness risk (see the clamp in <see cref="PushAndPop"/>).
        /// </summary>
        private const int Capacity = 128;

        private readonly PlayerInput[] _buffer = new PlayerInput[Capacity];
        private int _head; // next write index
        private int _count; // how many pushes so far, capped at Capacity

        /// <summary>
        /// Pushes <paramref name="idealInput"/> (this tick's instantaneous
        /// decision) and returns whichever input was pushed
        /// <paramref name="delayTicks"/> pushes ago (or <c>default</c> if fewer
        /// than that many pushes have happened yet). <paramref name="delayTicks"/>
        /// is clamped into <c>[0, Capacity-1]</c> — a caller passing a value at
        /// or above <see cref="Capacity"/> gets the OLDEST tracked input instead
        /// of silently reading garbage/wrapped-around data.
        /// </summary>
        public PlayerInput PushAndPop(PlayerInput idealInput, int delayTicks)
        {
            if (delayTicks < 0) delayTicks = 0;
            else if (delayTicks >= Capacity) delayTicks = Capacity - 1;

            _buffer[_head] = idealInput;
            int readIndex = (_head - delayTicks + Capacity) % Capacity;
            PlayerInput result = _count > delayTicks ? _buffer[readIndex] : default;

            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;

            return result;
        }
    }
}
