using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// End-of-session results screen: lists the 4 players ranked by wins, highlights
    /// the winner(s) with a gold border, and offers a RESTART button to begin a new
    /// session.
    ///
    /// Designed to be bar-grade: all text large, primary colors, readable at 2 m.
    ///
    /// Wire-up:
    ///   - Add to a Canvas child GameObject (a full-screen Panel).
    ///   - Call Show(scoreModel) when the session ends (N rounds complete).
    ///   - Call Hide() when a new session begins.
    ///   - Subscribe to OnRestartRequested to handle the restart action.
    ///
    /// The SceneBuilder creates this and wires it into SessionController.
    ///
    /// Lives in Barcade.Framework (UnityEngine.UI allowed).
    /// </summary>
    public class ResultsScreen : MonoBehaviour
    {
        // Raised when the player presses the on-screen Restart button.
        public event System.Action OnRestartRequested;

        [Header("Root container (shown/hidden as a whole)")]
        [Tooltip("The root panel to show/hide. Defaults to this GameObject.")]
        [SerializeField] private GameObject _root;

        [Header("Title label")]
        [SerializeField] private Text _titleLabel;

        [Header("Player row container")]
        [Tooltip("Vertical layout group to hold the four player rows.")]
        [SerializeField] private RectTransform _rowContainer;

        [Header("Restart button")]
        [SerializeField] private Button _restartButton;

        // Player colors
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

        private static readonly Color GoldColor    = new Color(1f, 0.84f, 0f);
        private static readonly Color NormalBorder = new Color(1f, 1f, 1f, 0.3f);

        private readonly List<GameObject> _rows = new List<GameObject>();

        // Public API

        /// <summary>
        /// Populates the results screen with final standings from scoreModel and
        /// activates it. Call once at the end of a session.
        /// </summary>
        public void Show(ScoreModel score)
        {
            if (_root == null) _root = gameObject;
            _root.SetActive(true);

            if (_titleLabel != null)
            {
                _titleLabel.text     = "RESULTADOS";
                _titleLabel.fontSize = 64;
                _titleLabel.fontStyle = FontStyle.Bold;
                _titleLabel.color    = Color.white;
            }

            BuildRows(score);
        }

        /// <summary>Hides the results screen.</summary>
        public void Hide()
        {
            if (_root == null) _root = gameObject;
            _root.SetActive(false);
        }

        // Unity lifecycle

        private void Awake()
        {
            if (_root == null) _root = gameObject;
            _root.SetActive(false);

            if (_restartButton != null)
                _restartButton.onClick.AddListener(OnRestartClicked);
        }

        // Private helpers

        private void OnRestartClicked()
        {
            OnRestartRequested?.Invoke();
        }

        private void BuildRows(ScoreModel score)
        {
            // Destroy previous rows.
            foreach (var r in _rows)
                if (r != null) Destroy(r);
            _rows.Clear();

            // Determine winners.
            List<PlayerSlot> leaders = score.GetLeaders();

            // Sort all 4 players by wins descending, then by slot index ascending.
            PlayerSlot[] order = new PlayerSlot[] {
                PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde };
            System.Array.Sort(order, (a, b) =>
            {
                int wa = score.GetWins(a), wb = score.GetWins(b);
                if (wb != wa) return wb - wa;
                return (int)a - (int)b;
            });

            RectTransform container = _rowContainer;
            if (container == null) container = GetComponent<RectTransform>();

            for (int rank = 0; rank < 4; rank++)
            {
                PlayerSlot slot = order[rank];
                bool isLeader = leaders.Contains(slot);
                GameObject row = BuildRow(slot, score.GetWins(slot), rank + 1, isLeader, container);
                _rows.Add(row);
            }
        }

        private static GameObject BuildRow(
            PlayerSlot slot, int wins, int rank, bool isWinner, RectTransform container)
        {
            int idx = (int)slot;

            var row = new GameObject("Row_" + SlotNames[idx]);
            row.transform.SetParent(container, false);

            var rowRT = row.AddComponent<RectTransform>();
            rowRT.sizeDelta = new Vector2(0f, 90f);
            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(1f, 1f);
            // Stacked vertically: each row offset downward.
            rowRT.anchoredPosition = new Vector2(0f, -(rank - 1) * 96f - 48f);
            rowRT.offsetMin = new Vector2(16f, rowRT.offsetMin.y);
            rowRT.offsetMax = new Vector2(-16f, rowRT.offsetMax.y);

            // Background
            var bg = row.AddComponent<Image>();
            bg.color = new Color(SlotColors[idx].r, SlotColors[idx].g, SlotColors[idx].b, 0.85f);
            bg.raycastTarget = false;

            if (isWinner)
            {
                // Gold outline effect: add an Outline component if available.
                var outline = row.AddComponent<Outline>();
                outline.effectColor = GoldColor;
                outline.effectDistance = new Vector2(4f, -4f);
            }

            // Rank label
            AddLabel(row.transform, "#" + rank, 36, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Rect(0.02f, 0f, 0.12f, 1f), Color.white);

            // Name label
            AddLabel(row.transform, SlotNames[idx], 42, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Rect(0.15f, 0f, 0.50f, 1f), Color.white);

            // Wins label
            string winsText = wins + " WINS";
            if (isWinner) winsText += "  CAMPEON!";
            AddLabel(row.transform, winsText, isWinner ? 38 : 34, FontStyle.Bold, TextAnchor.MiddleRight,
                new Rect(0.55f, 0f, 0.44f, 1f), isWinner ? GoldColor : Color.white);

            return row;
        }

        private static void AddLabel(
            Transform parent, string text, int size, FontStyle style,
            TextAnchor anchor, Rect relativeRect, Color color)
        {
            var go = new GameObject("Label_" + text.Replace(" ", "_").Substring(0, System.Math.Min(text.Length, 8)));
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(relativeRect.x, relativeRect.y);
            rt.anchorMax = new Vector2(relativeRect.x + relativeRect.width,
                                       relativeRect.y + relativeRect.height);
            rt.offsetMin = new Vector2(4f, 4f);
            rt.offsetMax = new Vector2(-4f, -4f);

            var label = go.AddComponent<Text>();
            label.text        = text;
            label.fontSize    = size;
            label.fontStyle   = style;
            label.alignment   = anchor;
            label.color       = color;
            label.raycastTarget = false;
        }
    }
}
