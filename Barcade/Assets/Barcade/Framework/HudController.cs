using UnityEngine;
using UnityEngine.UI;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Manages the four PlayerCornerHud elements and wires them to the active
    /// SequencerDirector as soon as it is available.
    ///
    /// Corner layout (bar-grade: big + readable, center kept clear):
    ///   Rojo     (0) -- top-left
    ///   Azul     (1) -- top-right
    ///   Amarillo (2) -- bottom-left
    ///   Verde    (3) -- bottom-right
    ///
    /// Call SetController() once the MicrogameLoopController has been started so
    /// the director is available.
    ///
    /// Lives in Barcade.Framework (UnityEngine.UI allowed).
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [Header("Corner HUD panels (one per player) -- leave null to auto-create")]
        [SerializeField] private PlayerCornerHud _hudRojo;
        [SerializeField] private PlayerCornerHud _hudAzul;
        [SerializeField] private PlayerCornerHud _hudAmarillo;
        [SerializeField] private PlayerCornerHud _hudVerde;

        [Header("Controller (assigned at runtime or via Inspector)")]
        [SerializeField] private MicrogameLoopController _loopController;

        private const float PanelWidth  = 220f;
        private const float PanelHeight = 140f;
        private const float Padding     = 16f;

        private PlayerCornerHud[] _corners;   // indexed by (int)PlayerSlot
        private SequencerDirector _director;

        // Public API

        /// <summary>
        /// Supplies the running MicrogameLoopController. The HUD reads its Director
        /// as soon as it becomes non-null (after Start). Safe to call any time.
        /// </summary>
        public void SetController(MicrogameLoopController controller)
        {
            _loopController = controller;
        }

        /// <summary>
        /// Forces all four corner HUDs to refresh immediately from the director's
        /// ScoreModel. Called at end of each round by the loop.
        /// </summary>
        public void RefreshAll()
        {
            if (_corners == null) return;
            foreach (var hud in _corners)
                hud?.RefreshNow();
        }

        // Unity lifecycle

        private void Start()
        {
            EnsureCorners();
        }

        private void Update()
        {
            // Latch the director as soon as the controller's Start() has run.
            if (_director == null && _loopController != null && _loopController.Director != null)
            {
                _director = _loopController.Director;
                WireDirector(_director);
            }
        }

        // Private helpers

        private void EnsureCorners()
        {
            // Find the owning canvas for layout.
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = GetComponentInChildren<Canvas>();

            RectTransform canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;

            _corners = new PlayerCornerHud[4];
            _corners[(int)PlayerSlot.Rojo]     = _hudRojo;
            _corners[(int)PlayerSlot.Azul]     = _hudAzul;
            _corners[(int)PlayerSlot.Amarillo] = _hudAmarillo;
            _corners[(int)PlayerSlot.Verde]    = _hudVerde;

            for (int i = 0; i < 4; i++)
            {
                if (_corners[i] == null)
                    _corners[i] = CreateCorner((PlayerSlot)i, canvasRect);
                else
                    _corners[i].Init((PlayerSlot)i);
            }
        }

        private PlayerCornerHud CreateCorner(PlayerSlot slot, RectTransform canvasRect)
        {
            var go = new GameObject("HUD_" + slot.ToString());
            Transform parent = canvasRect != null ? canvasRect : transform;
            go.transform.SetParent(parent, worldPositionStays: false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            SetCornerAnchor(rt, slot);

            var bg = go.AddComponent<Image>();
            bg.raycastTarget = false;

            // Name label child
            var nameGO = new GameObject("NameLabel");
            nameGO.transform.SetParent(go.transform, false);
            var nameLabelRT = nameGO.AddComponent<RectTransform>();
            nameLabelRT.anchorMin = new Vector2(0f, 0.5f);
            nameLabelRT.anchorMax = new Vector2(1f, 1f);
            nameLabelRT.offsetMin = new Vector2(8f, 4f);
            nameLabelRT.offsetMax = new Vector2(-8f, -4f);
            var nameText = nameGO.AddComponent<Text>();
            nameText.alignment     = TextAnchor.UpperCenter;
            nameText.raycastTarget = false;

            // Wins label child
            var winsGO = new GameObject("WinsLabel");
            winsGO.transform.SetParent(go.transform, false);
            var winsLabelRT = winsGO.AddComponent<RectTransform>();
            winsLabelRT.anchorMin = new Vector2(0f, 0f);
            winsLabelRT.anchorMax = new Vector2(1f, 0.55f);
            winsLabelRT.offsetMin = new Vector2(8f, 4f);
            winsLabelRT.offsetMax = new Vector2(-8f, -4f);
            var winsText = winsGO.AddComponent<Text>();
            winsText.alignment     = TextAnchor.LowerCenter;
            winsText.raycastTarget = false;

            var hud = go.AddComponent<PlayerCornerHud>();
            hud.WireLabels(bg, nameText, winsText);
            hud.Init(slot);
            return hud;
        }

        private static void SetCornerAnchor(RectTransform rt, PlayerSlot slot)
        {
            switch (slot)
            {
                case PlayerSlot.Rojo:      // top-left
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot     = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(Padding, -Padding);
                    break;

                case PlayerSlot.Azul:      // top-right
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot     = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(-Padding, -Padding);
                    break;

                case PlayerSlot.Amarillo:  // bottom-left
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                    rt.pivot     = new Vector2(0f, 0f);
                    rt.anchoredPosition = new Vector2(Padding, Padding);
                    break;

                case PlayerSlot.Verde:     // bottom-right
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
                    rt.pivot     = new Vector2(1f, 0f);
                    rt.anchoredPosition = new Vector2(-Padding, Padding);
                    break;
            }
        }

        private void WireDirector(SequencerDirector director)
        {
            if (_corners == null) return;
            foreach (var hud in _corners)
                hud?.SetDirector(director);
        }
    }
}
