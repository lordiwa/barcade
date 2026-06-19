using System.Collections.Generic;
using UnityEngine;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Renders the Esquiva (Dodge) microgame each frame.
    ///
    /// Rendering contract:
    ///   - One coloured square per player avatar (player colour, sized to avatarRadius*2).
    ///   - One white/grey square per hazard (sized to hazardRadius*2).
    ///   - Squares move each frame via transform.position set from state accessors.
    ///   - When a player is hit the square turns dark (semi-transparent).
    ///   - World positions map from game coordinates directly (1 game-unit = 1 world-unit).
    ///
    /// Read-only view: never mutates microgame state.
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public class EsquivaView : MonoBehaviour
    {
        private EsquivaMicrogame _game;
        private PlayerSlot[]     _players;

        private readonly List<GameObject> _avatarShapes  = new List<GameObject>();
        private readonly List<GameObject> _hazardShapes  = new List<GameObject>();

        private const float AvatarSize  = 1.0f; // world units (avatarRadius * 2)
        private const float HazardSize  = 1.0f;

        private static readonly Color HazardColor = new Color(1f, 0.5f, 0f); // orange
        private static readonly Color HitOverlay  = new Color(0.2f, 0.2f, 0.2f, 0.5f);

        // ── Initialise ────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds this view to the given EsquivaMicrogame instance and player set.
        /// Call immediately after Prepare() has run on the microgame.
        /// </summary>
        public void Bind(EsquivaMicrogame game, PlayerSlot[] players)
        {
            _game    = game;
            _players = players;
            BuildShapes();
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        private void Update()
        {
            if (_game == null) return;
            RefreshPositions();
        }

        private void OnDisable()
        {
            DestroyShapes();
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private void BuildShapes()
        {
            DestroyShapes();

            // Player avatars.
            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];
                var go = ShapeFactory.MakeSquare(slot,
                    GetAvatarPos(slot),
                    AvatarSize,
                    transform);
                go.name = $"Avatar_{slot}";
                _avatarShapes.Add(go);
            }

            // Hazards.
            for (int h = 0; h < _game.HazardCount; h++)
            {
                var go = ShapeFactory.MakeSquare(HazardColor,
                    GetHazardPos(h),
                    HazardSize,
                    transform);
                go.name = $"Hazard_{h}";
                _hazardShapes.Add(go);
            }
        }

        private void RefreshPositions()
        {
            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];
                _avatarShapes[i].transform.position = GetAvatarPos(slot);

                // Dim the avatar if the player has been hit.
                var mr = _avatarShapes[i].GetComponent<MeshRenderer>();
                if (mr != null && _game.IsHit(slot))
                    mr.material.color = HitOverlay;
            }

            for (int h = 0; h < _game.HazardCount; h++)
            {
                if (h < _hazardShapes.Count)
                    _hazardShapes[h].transform.position = GetHazardPos(h);
            }
        }

        private Vector3 GetAvatarPos(PlayerSlot slot) =>
            new Vector3(_game.GetAvatarX(slot), _game.GetAvatarY(slot), 0f);

        private Vector3 GetHazardPos(int h) =>
            new Vector3(_game.GetHazardX(h), _game.GetHazardY(h), 0f);

        private void DestroyShapes()
        {
            foreach (var go in _avatarShapes) if (go != null) Destroy(go);
            foreach (var go in _hazardShapes) if (go != null) Destroy(go);
            _avatarShapes.Clear();
            _hazardShapes.Clear();
        }
    }
}
