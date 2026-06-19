using UnityEngine;
using UnityEngine.UI;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Per-player corner HUD panel: shows the player color name, current wins,
    /// and a color-coded background anchored to one screen corner.
    ///
    /// Corner mapping (bar-grade, big + readable):
    ///   Rojo     (0) -- top-left
    ///   Azul     (1) -- top-right
    ///   Amarillo (2) -- bottom-left
    ///   Verde    (3) -- bottom-right
    ///
    /// Wired by HudController which creates and positions these at runtime.
    /// Can also be placed manually in the Inspector on Canvas children.
    ///
    /// Lives in Barcade.Framework (UnityEngine.UI allowed).
    /// </summary>
    public class PlayerCornerHud : MonoBehaviour
    {
        [Header("Player Identity")]
        [Tooltip("Which player slot this HUD corner belongs to.")]
        [SerializeField] private PlayerSlot _slot;

        [Header("UI References (wired by HudController or Inspector)")]
        [Tooltip("Background panel Image; its color is set to the player's color.")]
        [SerializeField] private Image _backgroundPanel;

        [Tooltip("Large label showing the player color name.")]
        [SerializeField] private Text _nameLabel;

        [Tooltip("Large label showing current wins.")]
        [SerializeField] private Text _winsLabel;

        // Rojo=#E8212B  Azul=#1E5FBE  Amarillo=#F5C400  Verde=#2BA84A
        private static readonly Color[] SlotColors = new Color[]
        {
            new Color(0.910f, 0.129f, 0.169f), // Rojo
            new Color(0.118f, 0.373f, 0.745f), // Azul
            new Color(0.961f, 0.769f, 0.000f), // Amarillo
            new Color(0.169f, 0.659f, 0.290f), // Verde
        };

        private static readonly string[] SlotNames = new string[]
        {
            "ROJO", "AZUL", "AMARILLO", "VERDE"
        };

        private SequencerDirector _director;
        private int               _displayedWins = -1; // -1 forces first refresh

        // Public API

        /// <summary>
        /// Runtime wiring helper called by HudController when it creates corner panels
        /// procedurally. Sets the UI component references that would normally be
        /// assigned via the Inspector.
        /// </summary>
        public void WireLabels(Image panel, Text nameLabel, Text winsLabel)
        {
            _backgroundPanel = panel;
            _nameLabel       = nameLabel;
            _winsLabel       = winsLabel;
        }

        /// <summary>
        /// Initialises this corner HUD for the given slot.
        /// Call once after the GameObject is created.
        /// </summary>
        public void Init(PlayerSlot slot)
        {
            _slot = slot;
            ApplyStaticStyling();
        }

        /// <summary>
        /// Injects the director whose ScoreModel is polled each frame.
        /// Call when the director becomes available (after MicrogameLoopController.Start).
        /// </summary>
        public void SetDirector(SequencerDirector director)
        {
            _director = director;
            _displayedWins = -1; // force refresh
            RefreshScore();
        }

        /// <summary>
        /// Forces an immediate score refresh from the director's ScoreModel.
        /// Called by HudController at end-of-round.
        /// </summary>
        public void RefreshNow()
        {
            _displayedWins = -1;
            RefreshScore();
        }

        // Unity lifecycle

        private void Start()
        {
            ApplyStaticStyling();
        }

        private void Update()
        {
            RefreshScore();
        }

        // Private helpers

        private void ApplyStaticStyling()
        {
            int idx = (int)_slot;
            Color col = SlotColors[idx];

            // Slightly transparent so microgame content behind is not fully blocked.
            Color panelColor = new Color(col.r, col.g, col.b, 0.85f);

            if (_backgroundPanel != null)
                _backgroundPanel.color = panelColor;

            if (_nameLabel != null)
            {
                _nameLabel.text      = SlotNames[idx];
                _nameLabel.color     = Color.white;
                _nameLabel.fontStyle = FontStyle.Bold;
                _nameLabel.fontSize  = 28;
            }

            if (_winsLabel != null)
            {
                _winsLabel.color     = Color.white;
                _winsLabel.fontStyle = FontStyle.Bold;
                _winsLabel.fontSize  = 48;
            }
        }

        private void RefreshScore()
        {
            if (_director == null) return;

            int wins = _director.Score.GetWins(_slot);
            if (wins == _displayedWins) return;

            _displayedWins = wins;

            if (_winsLabel != null)
                _winsLabel.text = wins.ToString();
        }
    }
}
