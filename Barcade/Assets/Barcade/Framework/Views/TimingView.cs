using UnityEngine;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Renders the Timing (Press on cue) microgame each frame.
    ///
    /// Rendering contract:
    ///   - A horizontal track bar (grey rect) spanning the screen.
    ///   - A green zone rect marking [zoneMin, zoneMax] on the track.
    ///   - A white marker square (small) that moves along the track.
    ///   - Per-player outcome indicator squares (one row below track):
    ///       no press = player colour, hit = green, miss = red.
    ///
    /// The zone boundaries are passed at bind time since they are private
    /// on the microgame (read from GetMarkerPosition and config-level constants).
    /// To keep the view simple we show the zone visually based on the bounds
    /// supplied by the host.
    ///
    /// Read-only view: never mutates microgame state.
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public class TimingView : MonoBehaviour
    {
        private TimingMicrogame _game;
        private PlayerSlot[]    _players;

        // Zone bounds (0..1 normalised, matching TimingMicrogame._zoneMin/_zoneMax).
        private float _zoneMin;
        private float _zoneMax;

        private GameObject   _trackBar;
        private GameObject   _zoneRect;
        private GameObject   _marker;
        private GameObject[] _outcomeSquares;

        // Layout (world units).
        private const float TrackY      =  1.0f;
        private const float TrackWidth  = 10.0f;
        private const float TrackHeight =  0.4f;
        private const float MarkerSize  =  0.5f;
        private const float OutcomeY    = -1.0f;
        private const float OutcomeSize =  0.8f;
        private const float OutcomeSpacing = 1.2f;

        private static readonly Color TrackColor  = new Color(0.3f, 0.3f, 0.3f);
        private static readonly Color ZoneColor   = new Color(0.1f, 0.8f, 0.2f, 0.7f);
        private static readonly Color MarkerColor = Color.white;
        private static readonly Color HitColor    = Color.green;
        private static readonly Color MissColor   = Color.red;

        // ── Initialise ────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds this view to the given TimingMicrogame instance and player set.
        /// <paramref name="zoneMin"/> and <paramref name="zoneMax"/> are [0,1] normalised zone bounds.
        /// Call immediately after Prepare() has run on the microgame.
        /// </summary>
        public void Bind(TimingMicrogame game, PlayerSlot[] players, float zoneMin, float zoneMax)
        {
            _game    = game;
            _players = players;
            _zoneMin = zoneMin;
            _zoneMax = zoneMax;
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

            // Track bar.
            _trackBar = ShapeFactory.MakeRect(TrackColor,
                new Vector3(0f, TrackY, 0f),
                TrackWidth, TrackHeight, transform);
            _trackBar.name = "TrackBar";

            // Zone rect.
            float zoneWidth  = (_zoneMax - _zoneMin) * TrackWidth;
            float zoneCenterX = (_zoneMin + _zoneMax) * 0.5f * TrackWidth - TrackWidth * 0.5f;
            _zoneRect = ShapeFactory.MakeRect(ZoneColor,
                new Vector3(zoneCenterX, TrackY, 0f),
                zoneWidth, TrackHeight * 1.2f, transform);
            _zoneRect.name = "ZoneRect";

            // Marker (starts at left edge).
            _marker = ShapeFactory.MakeSquare(MarkerColor,
                new Vector3(-TrackWidth * 0.5f, TrackY, 0f),
                MarkerSize, transform);
            _marker.name = "Marker";

            // Per-player outcome squares.
            _outcomeSquares = new GameObject[_players.Length];
            float totalW = _players.Length * OutcomeSpacing;
            float startX = -totalW * 0.5f + OutcomeSpacing * 0.5f;
            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];
                var go = ShapeFactory.MakeSquare(slot,
                    new Vector3(startX + i * OutcomeSpacing, OutcomeY, 0f),
                    OutcomeSize, transform);
                go.name = $"Outcome_{slot}";
                _outcomeSquares[i] = go;
            }
        }

        private void RefreshShapes()
        {
            // Update marker position.
            float pos = _game.GetMarkerPosition(); // [0,1)
            float markerX = pos * TrackWidth - TrackWidth * 0.5f;
            if (_marker != null)
                _marker.transform.position = new Vector3(markerX, TrackY, 0f);

            // Update outcome squares.
            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];

                // TimingMicrogame.GetLatch is not public — outcome is not directly
                // readable; we rely on the visual default (player colour = undecided).
                // To show hit/miss we need the result — the host sets a flag after Evaluate.
                // For live feedback: the square stays player colour while undecided.
                // (A future enhancement could expose GetLatch on TimingMicrogame.)
            }
        }

        /// <summary>
        /// Called by MicrogameHost after Evaluate() to show final hit/miss state per player.
        /// </summary>
        public void ShowResult(PlayerSlot slot, bool win)
        {
            int i = System.Array.IndexOf(_players, slot);
            if (i < 0 || i >= _outcomeSquares.Length) return;

            var mr = _outcomeSquares[i].GetComponent<MeshRenderer>();
            if (mr != null)
                mr.material.color = win ? HitColor : MissColor;
        }

        private void DestroyShapes()
        {
            if (_trackBar != null) { Destroy(_trackBar); _trackBar = null; }
            if (_zoneRect != null) { Destroy(_zoneRect); _zoneRect = null; }
            if (_marker   != null) { Destroy(_marker);   _marker   = null; }

            if (_outcomeSquares != null)
            {
                foreach (var go in _outcomeSquares) if (go != null) Destroy(go);
                _outcomeSquares = null;
            }
        }
    }
}
