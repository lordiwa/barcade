using System;
using System.Collections.Generic;

namespace Barcade.Core
{
    /// <summary>
    /// TASK-011 — Apunta (Aim and Fire) microgame.
    ///
    /// Each player has a target at a seeded direction. The stick sets the aim direction.
    /// On ButtonState.Pressed ("fire"): hit if the angle between aim and target direction
    /// is &lt;= angleTolerance (degrees). Win = hit (latched); Loss = first miss (latched).
    /// No press by end = loss.
    ///
    /// Boundary: angle == angleTolerance → WIN (inclusive).
    ///
    /// Pure C# — no UnityEngine dependency. C# 9 compatible.
    /// </summary>
    public sealed class ApuntaMicrogame : IMicrogame
    {
        // ── Configuration (base; difficulty scales at Prepare time) ─────────────

        private readonly float _baseAngleTolerance; // degrees

        // Effective tolerance computed from difficulty at Prepare time.
        private float _angleTolerance;

        // ── Test overrides ───────────────────────────────────────────────────────

        private (float X, float Y)[] _forcedTargetDirections;

        // ── Per-round state (reset in Prepare) ───────────────────────────────────

        private IMicrogameContext _ctx;

        // Per-player target direction (unit vector).
        private Dictionary<PlayerSlot, float> _targetDirX;
        private Dictionary<PlayerSlot, float> _targetDirY;

        // Per-player outcome latch: null = not yet decided, true = win, false = miss.
        private Dictionary<PlayerSlot, bool?> _latch;

        // Per-player latest stick aim direction (updated every Tick, even after latch).
        private Dictionary<PlayerSlot, float> _aimDirX;
        private Dictionary<PlayerSlot, float> _aimDirY;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>Creates an ApuntaMicrogame.</summary>
        /// <param name="angleTolerance">
        /// Maximum angle (degrees) between aim and target direction for a hit (inclusive).
        /// </param>
        public ApuntaMicrogame(float angleTolerance = 15f)
        {
            _baseAngleTolerance = angleTolerance;
        }

        // ── Test injection ───────────────────────────────────────────────────────

        /// <summary>
        /// Override per-player target directions for deterministic testing.
        /// Array indexed by PlayerSlot int value (0..3).
        /// Call before Prepare.
        /// </summary>
        public void SetForcedTargetDirections((float, float)[] directions)
        {
            _forcedTargetDirections = directions;
        }

        // ── IMicrogame ───────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string CommandVerb => "¡APUNTA!";

        /// <inheritdoc/>
        public void Prepare(IMicrogameContext ctx)
        {
            _ctx = ctx;

            // Difficulty [0,1]: tolerance shrinks from base down to base/4 at max difficulty.
            // Higher difficulty → smaller tolerance → harder to hit.
            float d = ctx.Difficulty < 0f ? 0f : (ctx.Difficulty > 1f ? 1f : ctx.Difficulty);
            _angleTolerance = _baseAngleTolerance * (1f - d * 0.75f);
            if (_angleTolerance < 1f) _angleTolerance = 1f; // never below 1 degree

            _targetDirX = new Dictionary<PlayerSlot, float>(ctx.Players.Length);
            _targetDirY = new Dictionary<PlayerSlot, float>(ctx.Players.Length);
            _latch      = new Dictionary<PlayerSlot, bool?>(ctx.Players.Length);
            _aimDirX    = new Dictionary<PlayerSlot, float>(ctx.Players.Length);
            _aimDirY    = new Dictionary<PlayerSlot, float>(ctx.Players.Length);

            for (int i = 0; i < ctx.Players.Length; i++)
            {
                PlayerSlot slot = ctx.Players[i];

                if (_forcedTargetDirections != null && i < _forcedTargetDirections.Length)
                {
                    (float tx, float ty) = _forcedTargetDirections[i];
                    float len = MathF.Sqrt(tx * tx + ty * ty);
                    if (len > 1e-6f) { tx /= len; ty /= len; }
                    _targetDirX[slot] = tx;
                    _targetDirY[slot] = ty;
                }
                else
                {
                    Direction2D dir = ctx.Rng.NextDirection();
                    _targetDirX[slot] = dir.X;
                    _targetDirY[slot] = dir.Y;
                }

                _latch[slot]   = null; // undecided
                _aimDirX[slot] = 0f;
                _aimDirY[slot] = 0f;
            }
        }

        /// <inheritdoc/>
        public void Tick(float deltaTime, IReadOnlyPlayerInputs inputs)
        {
            foreach (PlayerSlot slot in _ctx.Players)
            {
                InputSnapshot snap = inputs.For(slot);

                // Record live stick aim every frame so the view can track it.
                _aimDirX[slot] = snap.StickX;
                _aimDirY[slot] = snap.StickY;

                // Already decided — ignore further input.
                if (_latch[slot].HasValue) continue;
                if (snap.Button != ButtonState.Pressed) continue;

                // Compute aim direction from stick.
                float ax = snap.StickX;
                float ay = snap.StickY;
                float aMag = MathF.Sqrt(ax * ax + ay * ay);

                // If stick at rest, treat as a miss (no valid aim direction).
                if (aMag < 1e-6f)
                {
                    _latch[slot] = false;
                    continue;
                }

                ax /= aMag;
                ay /= aMag;

                // Angle between aim and target using dot product.
                float dot = ax * _targetDirX[slot] + ay * _targetDirY[slot];
                // Clamp to [-1,1] to guard against floating-point drift.
                dot = dot < -1f ? -1f : (dot > 1f ? 1f : dot);
                float angleRad = MathF.Acos(dot);
                float angleDeg = angleRad * (180f / MathF.PI);

                _latch[slot] = angleDeg <= _angleTolerance; // inclusive boundary
            }
        }

        /// <inheritdoc/>
        public MicrogameResult Evaluate()
        {
            var result = new MicrogameResult(_ctx.Players);

            foreach (PlayerSlot slot in _ctx.Players)
            {
                // No press occurred → loss.
                bool win = _latch[slot].HasValue && _latch[slot].Value;
                result.SetOutcome(slot, win);
            }

            result.Freeze();
            return result;
        }

        /// <inheritdoc/>
        public void Cleanup()
        {
            _ctx            = null;
            _targetDirX     = null;
            _targetDirY     = null;
            _latch          = null;
            _aimDirX        = null;
            _aimDirY        = null;
            _angleTolerance = 0f;
        }

        // ── State accessors (for tests and view) ─────────────────────────────────

        /// <summary>Returns the target direction X for <paramref name="slot"/>.</summary>
        public float GetTargetDirX(PlayerSlot slot) => _targetDirX[slot];

        /// <summary>Returns the target direction Y for <paramref name="slot"/>.</summary>
        public float GetTargetDirY(PlayerSlot slot) => _targetDirY[slot];

        /// <summary>
        /// Returns the current latch state for <paramref name="slot"/>:
        /// null = not yet fired, true = hit, false = missed.
        /// </summary>
        public bool? GetLatch(PlayerSlot slot) => _latch[slot];

        /// <summary>Effective angle tolerance in degrees after difficulty scaling.</summary>
        public float EffectiveAngleTolerance => _angleTolerance;

        /// <summary>
        /// Returns the latest stick X for <paramref name="slot"/> recorded this Tick.
        /// Updated every frame before the latch early-out so the view always tracks movement.
        /// Returns 0 if not yet ticked or after Cleanup.
        /// </summary>
        public float GetAimX(PlayerSlot slot)
        {
            if (_aimDirX == null) return 0f;
            float v;
            return _aimDirX.TryGetValue(slot, out v) ? v : 0f;
        }

        /// <summary>
        /// Returns the latest stick Y for <paramref name="slot"/> recorded this Tick.
        /// Updated every frame before the latch early-out so the view always tracks movement.
        /// Returns 0 if not yet ticked or after Cleanup.
        /// </summary>
        public float GetAimY(PlayerSlot slot)
        {
            if (_aimDirY == null) return 0f;
            float v;
            return _aimDirY.TryGetValue(slot, out v) ? v : 0f;
        }
    }
}
