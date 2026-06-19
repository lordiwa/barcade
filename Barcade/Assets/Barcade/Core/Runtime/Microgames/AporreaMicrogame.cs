using System;
using System.Collections.Generic;

namespace Barcade.Core
{
    /// <summary>
    /// TASK-010 — Aporrea (Mash) microgame.
    ///
    /// Per player: count rising-edge ButtonState.Pressed events.
    /// Win = count >= threshold before the time limit.
    /// Held state does NOT count as a new press (only ButtonState.Pressed = rising edge).
    ///
    /// Pure C# — no UnityEngine dependency. C# 9 compatible.
    /// </summary>
    public sealed class AporreaMicrogame : IMicrogame
    {
        // ── Configuration (base values; difficulty scales at Prepare time) ─────

        private readonly int _baseThreshold;
        private readonly float _timeLimit;

        // ── Per-round state (reset in Prepare) ───────────────────────────────────

        private IMicrogameContext _ctx;
        private float _elapsed;
        private Dictionary<PlayerSlot, int> _pressCount;

        // Effective threshold computed from difficulty at Prepare time.
        private int _threshold;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>Creates an AporreaMicrogame.</summary>
        /// <param name="threshold">Number of presses required to win.</param>
        /// <param name="timeLimit">Round duration in seconds.</param>
        public AporreaMicrogame(int threshold, float timeLimit)
        {
            _baseThreshold = threshold;
            _timeLimit     = timeLimit;
        }

        // ── IMicrogame ───────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string CommandVerb => "¡APORREA!";

        /// <inheritdoc/>
        public void Prepare(IMicrogameContext ctx)
        {
            _ctx     = ctx;
            _elapsed = 0f;

            // Difficulty [0,1]: scale threshold up to 2× at max difficulty.
            float d = ctx.Difficulty < 0f ? 0f : (ctx.Difficulty > 1f ? 1f : ctx.Difficulty);
            _threshold = _baseThreshold + (int)MathF.Round(d * _baseThreshold);

            _pressCount = new Dictionary<PlayerSlot, int>(ctx.Players.Length);
            foreach (PlayerSlot slot in ctx.Players)
                _pressCount[slot] = 0;
        }

        /// <inheritdoc/>
        public void Tick(float deltaTime, IReadOnlyPlayerInputs inputs)
        {
            bool withinWindow = _elapsed < _timeLimit;
            _elapsed += deltaTime;

            if (!withinWindow) return;

            foreach (PlayerSlot slot in _ctx.Players)
            {
                InputSnapshot snap = inputs.For(slot);
                if (snap.Button == ButtonState.Pressed)
                    _pressCount[slot]++;
            }
        }

        /// <inheritdoc/>
        public MicrogameResult Evaluate()
        {
            var result = new MicrogameResult(_ctx.Players);

            foreach (PlayerSlot slot in _ctx.Players)
                result.SetOutcome(slot, _pressCount[slot] >= _threshold);

            result.Freeze();
            return result;
        }

        /// <inheritdoc/>
        public void Cleanup()
        {
            _ctx        = null;
            _pressCount = null;
            _elapsed    = 0f;
            _threshold  = 0;
        }

        // ── State accessors (for tests and view) ─────────────────────────────────

        /// <summary>Returns how many rising-edge presses <paramref name="slot"/> has registered this round.</summary>
        public int GetPressCount(PlayerSlot slot) => _pressCount[slot];

        /// <summary>The effective press threshold needed to win (after difficulty scaling).</summary>
        public int Threshold => _threshold;
    }
}
