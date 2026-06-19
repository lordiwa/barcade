using System.Collections.Generic;

namespace Barcade.Core
{
    /// <summary>
    /// Sample / demo microgame: "Press the button before time runs out."
    ///
    /// Each participating player wins by pressing their action button (ButtonState.Pressed)
    /// at least once before the time limit elapses. Players who do not press in time lose.
    ///
    /// Designed to exercise the full IMicrogame lifecycle deterministically in fast-tests
    /// without any UnityEngine dependency.
    ///
    /// Time tracking: elapsed time is accumulated via <see cref="Tick"/> deltaTime
    /// arguments. The game is considered expired when elapsed >= timeLimit.
    ///
    /// C# 9 compatible (Unity 6). No UnityEngine dependency.
    /// </summary>
    public sealed class SampleTapMicrogame : IMicrogame
    {
        // ── Configuration ────────────────────────────────────────────────────────

        private readonly float _timeLimit;

        // ── Per-round state (reset in Prepare) ───────────────────────────────────

        private IMicrogameContext _ctx;
        private float _elapsed;
        private Dictionary<PlayerSlot, bool> _pressed;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a SampleTapMicrogame with the given time limit in seconds.
        /// </summary>
        public SampleTapMicrogame(float timeLimit)
        {
            _timeLimit = timeLimit;
        }

        // ── IMicrogame ───────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string CommandVerb => "¡TOCA!";

        /// <inheritdoc/>
        public void Prepare(IMicrogameContext ctx)
        {
            _ctx     = ctx;
            _elapsed = 0f;
            _pressed = new Dictionary<PlayerSlot, bool>(_ctx.Players.Length);

            foreach (PlayerSlot slot in _ctx.Players)
                _pressed[slot] = false;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Accumulates deltaTime. On each Tick, any player whose button state is
        /// <see cref="ButtonState.Pressed"/> is marked as having tapped in time.
        /// Once elapsed time exceeds the time limit, subsequent Ticks still track
        /// presses (caller responsibility to stop calling Tick after expiry), but
        /// Evaluate uses the state at the time of the call.
        /// </remarks>
        public void Tick(float deltaTime, IReadOnlyPlayerInputs inputs)
        {
            _elapsed += deltaTime;

            // Record presses only within the time window.
            // We stop counting presses once time has expired so that late ticks
            // (e.g. a final overshooting tick) do not grant undeserved wins.
            bool withinWindow = (_elapsed - deltaTime) < _timeLimit;

            if (withinWindow && _pressed != null)
            {
                foreach (PlayerSlot slot in _ctx.Players)
                {
                    if (_pressed[slot]) continue; // already pressed — idempotent

                    InputSnapshot snap = inputs.For(slot);
                    if (snap.Button == ButtonState.Pressed)
                        _pressed[slot] = true;
                }
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Players who pressed their button at any point before the timer expired win.
        /// Players who did not press in time lose.
        /// </remarks>
        public MicrogameResult Evaluate()
        {
            var result = new MicrogameResult(_ctx.Players);

            foreach (PlayerSlot slot in _ctx.Players)
                result.SetOutcome(slot, _pressed[slot]);

            return result;
        }

        /// <inheritdoc/>
        public void Cleanup()
        {
            _pressed = null;
            _ctx     = null;
            _elapsed = 0f;
        }
    }
}
