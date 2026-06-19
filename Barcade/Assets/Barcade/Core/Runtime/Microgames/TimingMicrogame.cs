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
        // ── Configuration (base; difficulty scales at Prepare time) ─────────────

        private readonly float _baseSpeed;
        private readonly float _baseZoneMin;
        private readonly float _baseZoneMax;

        // Effective values computed from difficulty at Prepare time.
        private float _speed;
        private float _zoneMin;
        private float _zoneMax;

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
            _baseSpeed   = speed;
            _baseZoneMin = zoneMin;
            _baseZoneMax = zoneMax;
        }

        // ── IMicrogame ───────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string CommandVerb => "¡AHORA!";

        /// <inheritdoc/>
        public void Prepare(IMicrogameContext ctx)
        {
            _ctx     = ctx;
            _elapsed = 0f;

            // Difficulty [0,1]: speed increases up to 2× and zone narrows by up to 50%.
            float d = ctx.Difficulty < 0f ? 0f : (ctx.Difficulty > 1f ? 1f : ctx.Difficulty);
            _speed = _baseSpeed * (1f + d);

            float baseWidth = _baseZoneMax - _baseZoneMin;
            float baseCentre = (_baseZoneMin + _baseZoneMax) * 0.5f;
            float newHalfWidth = baseWidth * 0.5f * (1f - d * 0.5f);
            if (newHalfWidth < 0.01f) newHalfWidth = 0.01f; // never degenerate
            _zoneMin = baseCentre - newHalfWidth;
            _zoneMax = baseCentre + newHalfWidth;

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
            _speed   = 0f;
            _zoneMin = 0f;
            _zoneMax = 0f;
        }

        // ── State accessors (for tests and view) ─────────────────────────────────

        /// <summary>
        /// Returns the current marker position in [0, 1) based on elapsed time.
        /// </summary>
        public float GetMarkerPosition() => MarkerPosition(_elapsed);

        /// <summary>Returns the effective marker speed after difficulty scaling.</summary>
        public float EffectiveSpeed => _speed;

        /// <summary>Returns the effective zone minimum after difficulty scaling.</summary>
        public float EffectiveZoneMin => _zoneMin;

        /// <summary>Returns the effective zone maximum after difficulty scaling.</summary>
        public float EffectiveZoneMax => _zoneMax;

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
