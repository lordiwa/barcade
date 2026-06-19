using System.Collections.Generic;
using UnityEngine;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Renders the Apunta (Aim and Fire) microgame each frame.
    ///
    /// Rendering contract:
    ///   - One coloured square per player avatar at a fixed position.
    ///   - One target indicator (small white square) per player drawn at distance
    ///     from the avatar in the target direction.
    ///   - An aim-direction line (player colour, thin) extends from each avatar
    ///     in the direction the stick is pointing. Updated from input snapshot
    ///     passed via UpdateInput each frame.
    ///   - After latch: green fill = hit, red fill = miss on the avatar square.
    ///
    /// Read-only view: never mutates microgame state.
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public class ApuntaView : MonoBehaviour
    {
        private ApuntaMicrogame _game;
        private PlayerSlot[]    _players;

        // Per-player shapes.
        private List<GameObject> _avatarShapes  = new List<GameObject>();
        private List<GameObject> _targetShapes  = new List<GameObject>();
        private List<GameObject> _aimLines      = new List<GameObject>();

        // Latest aim directions per-player (stick input passed in from the host).
        private float[] _aimX;
        private float[] _aimY;

        private const float AvatarSize = 0.8f;
        private const float TargetSize = 0.4f;
        private const float AimLength  = 2.5f;
        private const float AimThick   = 0.08f;

        private static readonly float[] AvatarOffsetX = { -3f, 3f, -3f,  3f };
        private static readonly float[] AvatarOffsetY = {  2f, 2f, -2f, -2f };

        private static readonly Color TargetColor = Color.white;
        private static readonly Color HitColor    = Color.green;
        private static readonly Color MissColor   = Color.red;

        // ── Initialise ────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds this view to the given ApuntaMicrogame instance and player set.
        /// Call immediately after Prepare() has run on the microgame.
        /// </summary>
        public void Bind(ApuntaMicrogame game, PlayerSlot[] players)
        {
            _game    = game;
            _players = players;
            _aimX    = new float[players.Length];
            _aimY    = new float[players.Length];
            BuildShapes();
        }

        /// <summary>
        /// Called each frame by MicrogameHost to supply the current aim direction
        /// for each player (derived from stick input).
        /// </summary>
        public void UpdateInput(int playerIndex, float stickX, float stickY)
        {
            if (playerIndex >= 0 && playerIndex < _players.Length)
            {
                _aimX[playerIndex] = stickX;
                _aimY[playerIndex] = stickY;
            }
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        private void Update()
        {
            if (_game == null) return;
            RefreshShapes();
        }

        private void OnDisable()
        {
            DestroyShapes();
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private void BuildShapes()
        {
            DestroyShapes();

            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];
                Vector3 avatarPos = GetAvatarPos(i);

                // Avatar square.
                var avatarGo = ShapeFactory.MakeSquare(slot, avatarPos, AvatarSize, transform);
                avatarGo.name = $"Avatar_{slot}";
                _avatarShapes.Add(avatarGo);

                // Target indicator.
                float tx = _game.GetTargetDirX(slot);
                float ty = _game.GetTargetDirY(slot);
                Vector3 targetPos = avatarPos + new Vector3(tx, ty, 0f) * 3f;
                var targetGo = ShapeFactory.MakeSquare(TargetColor, targetPos, TargetSize, transform);
                targetGo.name = $"Target_{slot}";
                _targetShapes.Add(targetGo);

                // Aim-direction line (initial: pointing right).
                var aimGo = ShapeFactory.MakeLine(ShapeFactory.PlayerColor(slot),
                    avatarPos,
                    avatarPos + Vector3.right * AimLength,
                    AimThick,
                    transform);
                aimGo.name = $"AimLine_{slot}";
                _aimLines.Add(aimGo);
            }
        }

        private void RefreshShapes()
        {
            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];
                Vector3 avatarPos = GetAvatarPos(i);
                bool? latch = _game.GetLatch(slot);

                // Update avatar colour based on latch state.
                var mr = _avatarShapes[i].GetComponent<MeshRenderer>();
                if (mr != null && latch.HasValue)
                    mr.material.color = latch.Value ? HitColor : MissColor;

                // Update aim line direction.
                float ax = _aimX[i];
                float ay = _aimY[i];
                float mag = Mathf.Sqrt(ax * ax + ay * ay);
                if (mag > 0.1f)
                {
                    ax /= mag; ay /= mag;
                    var aimLine = _aimLines[i];
                    if (aimLine != null)
                        DestroyAndRebuildAimLine(i, avatarPos, ax, ay, slot);
                }
            }
        }

        private void DestroyAndRebuildAimLine(int i, Vector3 avatarPos, float dx, float dy, PlayerSlot slot)
        {
            if (_aimLines[i] != null) Destroy(_aimLines[i]);

            Vector3 end = avatarPos + new Vector3(dx, dy, 0f) * AimLength;
            var aimGo = ShapeFactory.MakeLine(ShapeFactory.PlayerColor(slot),
                avatarPos, end, AimThick, transform);
            aimGo.name = $"AimLine_{slot}";
            _aimLines[i] = aimGo;
        }

        private static Vector3 GetAvatarPos(int playerIndex)
        {
            int idx = playerIndex < 4 ? playerIndex : 0;
            return new Vector3(AvatarOffsetX[idx], AvatarOffsetY[idx], 0f);
        }

        private void DestroyShapes()
        {
            foreach (var go in _avatarShapes) if (go != null) Destroy(go);
            foreach (var go in _targetShapes) if (go != null) Destroy(go);
            foreach (var go in _aimLines)     if (go != null) Destroy(go);
            _avatarShapes.Clear();
            _targetShapes.Clear();
            _aimLines.Clear();
        }
    }
}
