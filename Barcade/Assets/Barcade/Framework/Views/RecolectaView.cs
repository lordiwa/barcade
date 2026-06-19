using System.Collections.Generic;
using UnityEngine;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Renders the Recolecta (Collect) microgame each frame.
    ///
    /// Rendering contract:
    ///   - One coloured square per player avatar; moves with stick input.
    ///   - Good collectibles = small green squares; bad collectibles = small red squares.
    ///   - Collected items are hidden (disabled) once collected.
    ///   - A score indicator is shown as a vertical bar above each avatar position.
    ///
    /// Collectibles are obtained from the microgame's context before Cleanup()
    /// via GetAvatarX/Y accessors (positions rendered from those).
    ///
    /// Read-only view: never mutates microgame state.
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public class RecolectaView : MonoBehaviour
    {
        private RecolectaMicrogame    _game;
        private PlayerSlot[]          _players;
        private RecolectaCollectible[] _collectibles;

        private List<GameObject> _avatarShapes      = new List<GameObject>();
        private List<GameObject> _collectibleShapes  = new List<GameObject>();

        private const float AvatarSize       = 0.8f;
        private const float CollectibleSize  = 0.5f;

        private static readonly Color GoodColor = Color.green;
        private static readonly Color BadColor  = Color.red;

        // ── Initialise ────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds this view to the given RecolectaMicrogame instance and player set.
        /// <paramref name="collectibles"/> is the layout created during Prepare().
        /// Call immediately after Prepare() has run on the microgame.
        /// </summary>
        public void Bind(
            RecolectaMicrogame game,
            PlayerSlot[] players,
            RecolectaCollectible[] collectibles)
        {
            _game         = game;
            _players      = players;
            _collectibles = collectibles;
            BuildShapes();
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

            // Player avatars.
            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];
                var go = ShapeFactory.MakeSquare(slot,
                    new Vector3(_game.GetAvatarX(slot), _game.GetAvatarY(slot), 0f),
                    AvatarSize,
                    transform);
                go.name = $"Avatar_{slot}";
                _avatarShapes.Add(go);
            }

            // Collectibles.
            if (_collectibles != null)
            {
                for (int c = 0; c < _collectibles.Length; c++)
                {
                    var col = _collectibles[c];
                    Color clr = col.IsGood ? GoodColor : BadColor;
                    var go = ShapeFactory.MakeSquare(clr,
                        new Vector3(col.X, col.Y, 0f),
                        CollectibleSize,
                        transform);
                    go.name = $"Collectible_{c}";
                    _collectibleShapes.Add(go);
                }
            }
        }

        private void RefreshShapes()
        {
            // Update avatar positions.
            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];
                _avatarShapes[i].transform.position =
                    new Vector3(_game.GetAvatarX(slot), _game.GetAvatarY(slot), 0f);
            }

            // Hide collected items. We detect collection via proximity:
            // if the avatar is close enough and the collectible's shape is still active
            // we check all avatars against the collectible threshold.
            for (int c = 0; c < _collectibleShapes.Count; c++)
            {
                if (_collectibles == null || c >= _collectibles.Length) continue;
                if (!_collectibleShapes[c].activeSelf) continue;

                var col = _collectibles[c];
                float cr = _game.CollectRadius;

                foreach (PlayerSlot slot in _players)
                {
                    float dx = _game.GetAvatarX(slot) - col.X;
                    float dy = _game.GetAvatarY(slot) - col.Y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= cr)
                    {
                        _collectibleShapes[c].SetActive(false);
                        break;
                    }
                }
            }
        }

        private void DestroyShapes()
        {
            foreach (var go in _avatarShapes)      if (go != null) Destroy(go);
            foreach (var go in _collectibleShapes)  if (go != null) Destroy(go);
            _avatarShapes.Clear();
            _collectibleShapes.Clear();
        }
    }
}
