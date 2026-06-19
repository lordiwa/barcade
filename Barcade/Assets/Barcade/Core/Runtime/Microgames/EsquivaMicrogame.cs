using System;
using System.Collections.Generic;

namespace Barcade.Core
{
    /// <summary>
    /// TASK-009 — Esquiva (Dodge) microgame.
    ///
    /// Each player controls an avatar position (2D) moved by the stick,
    /// clamped to the play area. Hazard shapes move on deterministic seeded paths.
    /// Per-player: collision when distance(avatar, hazard) &lt;= (avatarRadius + hazardRadius).
    /// Win = no collision by time limit. Loss is latched on first collision.
    ///
    /// Pure C# — no UnityEngine dependency. C# 9 compatible.
    /// </summary>
    public sealed class EsquivaMicrogame : IMicrogame
    {
        // ── Configuration ────────────────────────────────────────────────────────

        private readonly int _hazardCount;
        private readonly float _hazardSpeed;
        private readonly float _avatarRadius;
        private readonly float _hazardRadius;
        private readonly float _avatarSpeed;
        private readonly float _playAreaHalfExtent;

        // ── Test overrides ───────────────────────────────────────────────────────

        private (float X, float Y)[] _forcedHazardPositions;
        private (float X, float Y)[] _forcedAvatarPositions;

        // ── Per-round state (reset in Prepare) ───────────────────────────────────

        private IMicrogameContext _ctx;

        // Per-player avatar positions.
        private Dictionary<PlayerSlot, float> _avatarX;
        private Dictionary<PlayerSlot, float> _avatarY;

        // Per-player collision/loss latch.
        private Dictionary<PlayerSlot, bool> _hit;

        // Hazard positions and directions (shared across all players).
        private float[] _hazardX;
        private float[] _hazardY;
        private float[] _hazardDirX;
        private float[] _hazardDirY;

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates an EsquivaMicrogame with default parameters suitable for production use.
        /// Hazard positions are seeded from ctx.Rng at Prepare time.
        /// </summary>
        public EsquivaMicrogame(
            int hazardCount = 3,
            float hazardSpeed = 2f,
            float avatarRadius = 0.5f,
            float hazardRadius = 0.5f,
            float avatarSpeed = 5f,
            float playAreaHalfExtent = 8f)
        {
            _hazardCount         = hazardCount;
            _hazardSpeed         = hazardSpeed;
            _avatarRadius        = avatarRadius;
            _hazardRadius        = hazardRadius;
            _avatarSpeed         = avatarSpeed;
            _playAreaHalfExtent  = playAreaHalfExtent;
        }

        // ── Test injection ───────────────────────────────────────────────────────

        /// <summary>
        /// Override hazard positions for deterministic testing.
        /// Call before Prepare. Array is indexed by hazard slot.
        /// </summary>
        public void SetForcedHazardPositions((float, float)[] positions)
        {
            _forcedHazardPositions = positions;
        }

        /// <summary>
        /// Override avatar start positions for deterministic testing.
        /// Call before Prepare. Indexed by PlayerSlot int value (0..3).
        /// </summary>
        public void SetForcedAvatarPositions((float, float)[] positions)
        {
            _forcedAvatarPositions = positions;
        }

        // ── IMicrogame ───────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string CommandVerb => "¡ESQUIVA!";

        /// <inheritdoc/>
        public void Prepare(IMicrogameContext ctx)
        {
            _ctx = ctx;

            _avatarX = new Dictionary<PlayerSlot, float>(ctx.Players.Length);
            _avatarY = new Dictionary<PlayerSlot, float>(ctx.Players.Length);
            _hit     = new Dictionary<PlayerSlot, bool>(ctx.Players.Length);

            // Initialise avatar positions.
            for (int i = 0; i < ctx.Players.Length; i++)
            {
                PlayerSlot slot = ctx.Players[i];
                if (_forcedAvatarPositions != null && i < _forcedAvatarPositions.Length)
                {
                    _avatarX[slot] = _forcedAvatarPositions[i].Item1;
                    _avatarY[slot] = _forcedAvatarPositions[i].Item2;
                }
                else
                {
                    // Default spawn: evenly distributed around origin.
                    float angle = (float)(i * 2.0 * Math.PI / ctx.Players.Length);
                    _avatarX[slot] = (float)Math.Cos(angle) * (_playAreaHalfExtent * 0.5f);
                    _avatarY[slot] = (float)Math.Sin(angle) * (_playAreaHalfExtent * 0.5f);
                }
                _hit[slot] = false;
            }

            // Initialise hazards.
            _hazardX    = new float[_hazardCount];
            _hazardY    = new float[_hazardCount];
            _hazardDirX = new float[_hazardCount];
            _hazardDirY = new float[_hazardCount];

            for (int h = 0; h < _hazardCount; h++)
            {
                if (_forcedHazardPositions != null && h < _forcedHazardPositions.Length)
                {
                    _hazardX[h] = _forcedHazardPositions[h].Item1;
                    _hazardY[h] = _forcedHazardPositions[h].Item2;
                }
                else
                {
                    _hazardX[h] = (ctx.Rng.NextFloat() * 2f - 1f) * _playAreaHalfExtent;
                    _hazardY[h] = (ctx.Rng.NextFloat() * 2f - 1f) * _playAreaHalfExtent;
                }

                Direction2D dir = ctx.Rng.NextDirection();
                _hazardDirX[h] = dir.X;
                _hazardDirY[h] = dir.Y;
            }
        }

        /// <inheritdoc/>
        public void Tick(float deltaTime, IReadOnlyPlayerInputs inputs)
        {
            // Move hazards — bounce off play area walls.
            for (int h = 0; h < _hazardCount; h++)
            {
                _hazardX[h] += _hazardDirX[h] * _hazardSpeed * deltaTime;
                _hazardY[h] += _hazardDirY[h] * _hazardSpeed * deltaTime;

                // Reflect off walls.
                if (_hazardX[h] > _playAreaHalfExtent || _hazardX[h] < -_playAreaHalfExtent)
                {
                    _hazardDirX[h] = -_hazardDirX[h];
                    _hazardX[h] = Clamp(_hazardX[h], -_playAreaHalfExtent, _playAreaHalfExtent);
                }
                if (_hazardY[h] > _playAreaHalfExtent || _hazardY[h] < -_playAreaHalfExtent)
                {
                    _hazardDirY[h] = -_hazardDirY[h];
                    _hazardY[h] = Clamp(_hazardY[h], -_playAreaHalfExtent, _playAreaHalfExtent);
                }
            }

            // Move each player's avatar and check collisions.
            foreach (PlayerSlot slot in _ctx.Players)
            {
                // Already hit — skip movement and collision check; loss is latched.
                if (_hit[slot]) continue;

                InputSnapshot snap = inputs.For(slot);
                float nx = snap.StickX;
                float ny = snap.StickY;

                // Normalise stick input to prevent diagonal speed bonus.
                float mag = MathF.Sqrt(nx * nx + ny * ny);
                if (mag > 1f) { nx /= mag; ny /= mag; }

                // Move avatar.
                _avatarX[slot] += nx * _avatarSpeed * deltaTime;
                _avatarY[slot] += ny * _avatarSpeed * deltaTime;

                // Clamp to play area.
                _avatarX[slot] = Clamp(_avatarX[slot], -_playAreaHalfExtent, _playAreaHalfExtent);
                _avatarY[slot] = Clamp(_avatarY[slot], -_playAreaHalfExtent, _playAreaHalfExtent);

                // Collision check against all hazards.
                float combinedRadius = _avatarRadius + _hazardRadius;
                for (int h = 0; h < _hazardCount; h++)
                {
                    float dx = _avatarX[slot] - _hazardX[h];
                    float dy = _avatarY[slot] - _hazardY[h];
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist <= combinedRadius)
                    {
                        _hit[slot] = true;
                        break;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public MicrogameResult Evaluate()
        {
            var result = new MicrogameResult(_ctx.Players);

            foreach (PlayerSlot slot in _ctx.Players)
                result.SetOutcome(slot, !_hit[slot]); // survived = win

            result.Freeze();
            return result;
        }

        /// <inheritdoc/>
        public void Cleanup()
        {
            _ctx        = null;
            _avatarX    = null;
            _avatarY    = null;
            _hit        = null;
            _hazardX    = null;
            _hazardY    = null;
            _hazardDirX = null;
            _hazardDirY = null;
        }

        // ── State accessors (for tests and view) ─────────────────────────────────

        /// <summary>Returns the current X position of the given player's avatar.</summary>
        public float GetAvatarX(PlayerSlot slot) => _avatarX[slot];

        /// <summary>Returns the current Y position of the given player's avatar.</summary>
        public float GetAvatarY(PlayerSlot slot) => _avatarY[slot];

        /// <summary>Returns true if the given player has been hit.</summary>
        public bool IsHit(PlayerSlot slot) => _hit[slot];

        /// <summary>Returns the number of hazards.</summary>
        public int HazardCount => _hazardCount;

        /// <summary>Returns the X position of hazard at index <paramref name="i"/>.</summary>
        public float GetHazardX(int i) => _hazardX[i];

        /// <summary>Returns the Y position of hazard at index <paramref name="i"/>.</summary>
        public float GetHazardY(int i) => _hazardY[i];

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static float Clamp(float v, float min, float max)
            => v < min ? min : (v > max ? max : v);
    }
}
