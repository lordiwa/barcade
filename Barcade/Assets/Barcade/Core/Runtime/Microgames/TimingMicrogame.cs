using System;
using System.Collections.Generic;

namespace Barcade.Core
{
    /// <summary>
    /// TASK-012 — Timing (Press on cue) microgame.
    ///
    /// A marker moves at constant speed along a 0..1 track:
    ///   position = (speed * elapsed) mod 1.
    /// Target zone: [zoneMin, zoneMax] (both inclusive).
    /// On ButtonState.Pressed: win if marker within zone (latch win), else latch miss.
    /// No press by end = loss.
    ///
    /// Boundary: marker at exactly zoneMin or zoneMax → WIN.
    ///
    /// Pure C# — no UnityEngine dependency. C# 9 compatible.
    /// </summary>
    public sealed class TimingMicrogame : IMicrogame
    {
        // ── Configuration ────────────────────────────────────────────────────────

        private readonly float _speed;
        private readonly float _zoneMin;
        private readonly float _zoneMax;

        // ── Per-round state (reset in Prepare) ───────────────────────────────────

        private IMicrogameContext _ctx;
        private float _elapsed;

        // Per-player outcome latch: null = not yet pressed, true = hit, false = missed.
        private Dictionary<PlayerSlot, bool?> _latch;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>Creates a TimingMicrogame.</summary>
        /// <param name="speed">Marker speed in track-units per second.</param>
        /// <param name="zoneMin">Start of the target zone (inclusive), [0,1).</param>
        /// <param name="zoneMax">End of the target zone (inclusive), (0,1].</param>
        public TimingMicrogame(float speed, float zoneMin, float zoneMax)
        {
            _speed   = speed;
            _zoneMin = zoneMin;
            _zoneMax = zoneMax;
        }

        // ── IMicrogame ───────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string CommandVerb => "¡AHORA!";

        /// <inheritdoc/>
        public void Prepare(IMicrogameContext ctx)
        {
            _ctx     = ctx;
            _elapsed = 0f;

            _latch = new Dictionary<PlayerSlot, bool?>(ctx.Players.Length);
            foreach (PlayerSlot slot in ctx.Players)
                _latch[slot] = null;
        }

        /// <inheritdoc/>
        public void Tick(float deltaTime, IReadOnlyPlayerInputs inputs)
        {
            _elapsed += deltaTime;

            float pos = MarkerPosition(_elapsed);

            foreach (PlayerSlot slot in _ctx.Players)
            {
                // Already decided — skip.
                if (_latch[slot].HasValue) continue;

                InputSnapshot snap = inputs.For(slot);
                if (snap.Button != ButtonState.Pressed) continue;

                // Evaluate at current marker position.
                bool inZone = pos >= _zoneMin && pos <= _zoneMax;
                _latch[slot] = inZone;
            }
        }

        /// <inheritdoc/>
        public MicrogameResult Evaluate()
        {
            var result = new MicrogameResult(_ctx.Players);

            foreach (PlayerSlot slot in _ctx.Players)
            {
                bool win = _latch[slot].HasValue && _latch[slot].Value;
                result.SetOutcome(slot, win);
            }

            result.Freeze();
            return result;
        }

        /// <inheritdoc/>
        public void Cleanup()
        {
            _ctx     = null;
            _latch   = null;
            _elapsed = 0f;
        }

        // ── State accessors (for tests and view) ─────────────────────────────────

        /// <summary>
        /// Returns the current marker position in [0, 1) based on elapsed time.
        /// </summary>
        public float GetMarkerPosition() => MarkerPosition(_elapsed);

        // ── Helpers ──────────────────────────────────────────────────────────────

        private float MarkerPosition(float elapsed)
        {
            float raw = _speed * elapsed;
            // Modulo 1: keep in [0, 1).
            float pos = raw - MathF.Floor(raw);
            return pos;
        }
    }
}
