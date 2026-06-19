using UnityEngine;
using UnityEngine.UI;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Renders the Aporrea (Mash) microgame each frame.
    ///
    /// Rendering contract:
    ///   - One coloured bar per player whose height grows proportionally to press count
    ///     relative to the win threshold.
    ///   - Bars are arranged side-by-side in world space.
    ///   - A thin white horizontal line marks the threshold at full bar height.
    ///
    /// Read-only view: never mutates microgame state.
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public class AporreaView : MonoBehaviour
    {
        private AporreaMicrogame _game;
        private PlayerSlot[]     _players;

        private GameObject[] _barShapes;
        // Small player-coloured outlined square shown below each bar so players
        // know which bar is theirs without counting from left.
        private GameObject[] _avatarSquares;
        private GameObject[] _avatarOutlines;
        private GameObject   _thresholdLine;

        // Layout constants (world units).
        private const float BarWidth      = 1.0f;
        private const float BarSpacing    = 1.4f;
        private const float MaxHeight     = 4.0f;
        private const float BaseY         = -2.0f; // bottom of bars in world space
        private const float AvatarMarkY   = -3.0f; // below the bars
        private const float AvatarInner   = 0.7f;
        private const float AvatarOuter   = 0.95f;

        // ── Initialise ────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds this view to the given AporreaMicrogame instance and player set.
        /// Call immediately after Prepare() has run on the microgame.
        /// </summary>
        public void Bind(AporreaMicrogame game, PlayerSlot[] players)
        {
            _game    = game;
            _players = players;
            BuildShapes();
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        private void Update()
        {
            if (_game == null) return;
            RefreshBars();
        }

        private void OnDisable()
        {
            DestroyShapes();
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private void BuildShapes()
        {
            DestroyShapes();
            _barShapes      = new GameObject[_players.Length];
            _avatarSquares  = new GameObject[_players.Length];
            _avatarOutlines = new GameObject[_players.Length];

            float totalWidth = _players.Length * BarSpacing;
            float startX = -totalWidth * 0.5f + BarSpacing * 0.5f;

            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];
                float x = startX + i * BarSpacing;

                // Start at minimum height = 0.1 so the bar is visible from the start.
                var go = ShapeFactory.MakeRect(slot,
                    new Vector3(x, BaseY, 0f),
                    BarWidth,
                    0.1f,
                    transform);
                go.name = $"Bar_{slot}";
                _barShapes[i] = go;

                // Avatar marker below each bar — outlined so players recognise their
                // slot at a glance (matches HUD corner colour).
                GameObject outline;
                var avatar = ShapeFactory.MakeOutlinedSquare(
                    slot,
                    new Vector3(x, AvatarMarkY, 0f),
                    AvatarInner,
                    AvatarOuter,
                    transform,
                    out outline);
                avatar.name  = $"AvatarMark_{slot}";
                outline.name = $"AvatarMarkOutline_{slot}";
                _avatarSquares[i]  = avatar;
                _avatarOutlines[i] = outline;
            }

            // Threshold line — white horizontal rule at MaxHeight.
            _thresholdLine = ShapeFactory.MakeLine(
                Color.white,
                new Vector3(-totalWidth * 0.5f, BaseY + MaxHeight, 0f),
                new Vector3( totalWidth * 0.5f, BaseY + MaxHeight, 0f),
                0.05f,
                transform);
            _thresholdLine.name = "ThresholdLine";
        }

        private void RefreshBars()
        {
            float totalWidth = _players.Length * BarSpacing;
            float startX = -totalWidth * 0.5f + BarSpacing * 0.5f;
            int threshold = _game.Threshold;

            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];
                int presses = _game.GetPressCount(slot);
                float ratio = threshold > 0 ? Mathf.Clamp01((float)presses / threshold) : 0f;
                float height = Mathf.Max(0.1f, ratio * MaxHeight);
                float x = startX + i * BarSpacing;

                // Reposition so the bar grows upward from BaseY.
                _barShapes[i].transform.position   = new Vector3(x, BaseY + height * 0.5f, 0f);
                _barShapes[i].transform.localScale  = new Vector3(BarWidth, height, 1f);
            }
        }

        private void DestroyShapes()
        {
            if (_barShapes != null)
            {
                foreach (var go in _barShapes) if (go != null) Destroy(go);
                _barShapes = null;
            }
            if (_avatarSquares != null)
            {
                foreach (var go in _avatarSquares) if (go != null) Destroy(go);
                _avatarSquares = null;
            }
            if (_avatarOutlines != null)
            {
                foreach (var go in _avatarOutlines) if (go != null) Destroy(go);
                _avatarOutlines = null;
            }
            if (_thresholdLine != null) { Destroy(_thresholdLine); _thresholdLine = null; }
        }
    }
}
