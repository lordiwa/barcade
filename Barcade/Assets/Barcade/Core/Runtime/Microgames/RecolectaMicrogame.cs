using System;
using System.Collections.Generic;

namespace Barcade.Core
{
    /// <summary>
    /// Plain data describing a single collectible in Recolecta.
    /// Good collectibles increment the player's net score; bad ones decrement it.
    /// </summary>
    public sealed class RecolectaCollectible
    {
        public float X { get; }
        public float Y { get; }
        public bool IsGood { get; }

        public RecolectaCollectible(float x, float y, bool isGood)
        {
            X      = x;
            Y      = y;
            IsGood = isGood;
        }
    }

    /// <summary>
    /// TASK-013 — Recolecta (Collect) microgame.
    ///
    /// Per player: an avatar moved by the stick within a 2D play area.
    /// Collectibles are placed at seeded positions, each good (+1) or bad (-1).
    /// Overlapping a collectible (once) changes the net score; each collectible
    /// is collected exactly once (subsequent overlap is ignored).
    /// Win = net collected >= quota before time limit.
    ///
    /// Pure C# — no UnityEngine dependency. C# 9 compatible.
    /// </summary>
    public sealed class RecolectaMicrogame : IMicrogame
    {
        // ── Configuration (base; difficulty scales at Prepare time) ─────────────

        private readonly int _baseQuota;
        private readonly float _collectRadius;
        private readonly float _avatarSpeed;
        private readonly float _playAreaHalfExtent;

        // Effective quota computed from difficulty at Prepare time.
        private int _quota;

        // ── Test overrides ───────────────────────────────────────────────────────

        private RecolectaCollectible[] _forcedCollectibles;
        private (float X, float Y)[] _forcedAvatarPositions;

        // ── Per-round state (reset in Prepare) ───────────────────────────────────

        private IMicrogameContext _ctx;

        // Per-player avatar positions.
        private Dictionary<PlayerSlot, float> _avatarX;
        private Dictionary<PlayerSlot, float> _avatarY;

        // Per-player net score.
        private Dictionary<PlayerSlot, int> _netScore;

        // Collectibles and per-player collected flags.
        private RecolectaCollectible[] _collectibles;
        private Dictionary<PlayerSlot, bool[]> _collected; // _collected[slot][i] = taken

        // ── Constructor ──────────────────────────────────────────────────────────

        /// <summary>Creates a RecolectaMicrogame.</summary>
        /// <param name="quota">Minimum net score to win.</param>
        /// <param name="collectRadius">Distance threshold for collection overlap.</param>
        /// <param name="avatarSpeed">Avatar movement speed (units/second).</param>
        /// <param name="playAreaHalfExtent">Half-extent of the square play area.</param>
        public RecolectaMicrogame(
            int quota,
            float collectRadius,
            float avatarSpeed,
            float playAreaHalfExtent = 8f)
        {
            _baseQuota          = quota;
            _collectRadius      = collectRadius;
            _avatarSpeed        = avatarSpeed;
            _playAreaHalfExtent = playAreaHalfExtent;
        }

        // ── Test injection ───────────────────────────────────────────────────────

        /// <summary>Override collectible layout for deterministic testing. Call before Prepare.</summary>
        public void SetForcedCollectibles(RecolectaCollectible[] collectibles)
        {
            _forcedCollectibles = collectibles;
        }

        /// <summary>Override avatar start positions for deterministic testing. Call before Prepare.</summary>
        public void SetForcedAvatarPositions((float, float)[] positions)
        {
            _forcedAvatarPositions = positions;
        }

        // ── IMicrogame ───────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public string CommandVerb => "¡RECOLECTA!";

        /// <inheritdoc/>
        public void Prepare(IMicrogameContext ctx)
        {
            _ctx = ctx;

            // Difficulty [0,1]: quota scales from base up to base+3 at max difficulty.
            float d = ctx.Difficulty < 0f ? 0f : (ctx.Difficulty > 1f ? 1f : ctx.Difficulty);
            _quota = _baseQuota + (int)MathF.Round(d * 3f);

            _avatarX  = new Dictionary<PlayerSlot, float>(ctx.Players.Length);
            _avatarY  = new Dictionary<PlayerSlot, float>(ctx.Players.Length);
            _netScore = new Dictionary<PlayerSlot, int>(ctx.Players.Length);
            _collected = new Dictionary<PlayerSlot, bool[]>(ctx.Players.Length);

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
                    // Default: spread out evenly.
                    float angle = (float)(i * 2.0 * System.Math.PI / ctx.Players.Length);
                    _avatarX[slot] = (float)System.Math.Cos(angle) * (_playAreaHalfExtent * 0.5f);
                    _avatarY[slot] = (float)System.Math.Sin(angle) * (_playAreaHalfExtent * 0.5f);
                }

                _netScore[slot] = 0;
            }

            // Build collectibles.
            if (_forcedCollectibles != null)
            {
                _collectibles = _forcedCollectibles;
            }
            else
            {
                // Default: mix of good and bad, seeded positions.
                int count = 8;
                _collectibles = new RecolectaCollectible[count];
                for (int c = 0; c < count; c++)
                {
                    float cx = (ctx.Rng.NextFloat() * 2f - 1f) * _playAreaHalfExtent;
                    float cy = (ctx.Rng.NextFloat() * 2f - 1f) * _playAreaHalfExtent;
                    bool good = (c % 2 == 0); // alternate good/bad
                    _collectibles[c] = new RecolectaCollectible(cx, cy, good);
                }
            }

            // Initialise per-player collected flags.
            foreach (PlayerSlot slot in ctx.Players)
                _collected[slot] = new bool[_collectibles.Length];
        }

        /// <inheritdoc/>
        public void Tick(float deltaTime, IReadOnlyPlayerInputs inputs)
        {
            foreach (PlayerSlot slot in _ctx.Players)
            {
                InputSnapshot snap = inputs.For(slot);
                float nx = snap.StickX;
                float ny = snap.StickY;

                // Normalise to prevent diagonal speed bonus.
                float mag = MathF.Sqrt(nx * nx + ny * ny);
                if (mag > 1f) { nx /= mag; ny /= mag; }

                // Move avatar.
                _avatarX[slot] += nx * _avatarSpeed * deltaTime;
                _avatarY[slot] += ny * _avatarSpeed * deltaTime;

                // Clamp to play area.
                _avatarX[slot] = Clamp(_avatarX[slot], -_playAreaHalfExtent, _playAreaHalfExtent);
                _avatarY[slot] = Clamp(_avatarY[slot], -_playAreaHalfExtent, _playAreaHalfExtent);

                // Check overlap with each uncollected collectible.
                bool[] taken = _collected[slot];
                for (int c = 0; c < _collectibles.Length; c++)
                {
                    if (taken[c]) continue; // already collected

                    float dx = _avatarX[slot] - _collectibles[c].X;
                    float dy = _avatarY[slot] - _collectibles[c].Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    if (dist <= _collectRadius)
                    {
                        taken[c] = true;
                        _netScore[slot] += _collectibles[c].IsGood ? 1 : -1;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public MicrogameResult Evaluate()
        {
            var result = new MicrogameResult(_ctx.Players);

            foreach (PlayerSlot slot in _ctx.Players)
                result.SetOutcome(slot, _netScore[slot] >= _quota);

            result.Freeze();
            return result;
        }

        /// <inheritdoc/>
        public void Cleanup()
        {
            _ctx          = null;
            _avatarX      = null;
            _avatarY      = null;
            _netScore     = null;
            _collected    = null;
            _collectibles = null;
            _quota        = 0;
        }

        // ── State accessors (for tests and view) ─────────────────────────────────

        /// <summary>Returns the current net score for <paramref name="slot"/>.</summary>
        public int GetNetScore(PlayerSlot slot) => _netScore[slot];

        /// <summary>Returns the current X position of <paramref name="slot"/>'s avatar.</summary>
        public float GetAvatarX(PlayerSlot slot) => _avatarX[slot];

        /// <summary>Returns the current Y position of <paramref name="slot"/>'s avatar.</summary>
        public float GetAvatarY(PlayerSlot slot) => _avatarY[slot];

        /// <summary>The collect radius (overlap threshold).</summary>
        public float CollectRadius => _collectRadius;

        /// <summary>The win quota.</summary>
        public int Quota => _quota;

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static float Clamp(float v, float min, float max)
            => v < min ? min : (v > max ? max : v);
    }
}
