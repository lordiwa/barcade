using UnityEngine;
using UnityEngine.UI;

namespace Barcade.Framework
{
    /// <summary>
    /// Shows the giant imperative verb (e.g. "¡ESQUIVA!") during the CommandShow phase.
    ///
    /// Wire-up:
    ///   - Add to a Canvas child GameObject that has a Text (UGUI) component.
    ///   - Assign _verbLabel in the Inspector to the Text component.
    ///   - MicrogameLoopController calls Show(verbText) / Hide() each phase change.
    ///
    /// Uses plain UGUI Text (no TMP dependency) so it compiles without the
    /// TextMeshPro package.  Swap Text for TMP_Text once TMP is added to the project.
    ///
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// Unity integration compile verified in the batched integration pass.
    /// </summary>
    public class CommandDisplayView : MonoBehaviour
    {
        [SerializeField] private Text _verbLabel;

        [Tooltip("Optional short hint shown below the verb. Wired by SceneBuilder.")]
        [SerializeField] private Text _hintLabel;

        private void Awake()
        {
            // Hide by default; MicrogameLoopController activates us per phase.
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Activates the view and sets the displayed verb and optional hint text.
        /// </summary>
        /// <param name="verbText">The command verb, e.g. "¡ESQUIVA!".</param>
        /// <param name="hintText">Optional one-line hint, e.g. "Mueve tu figura y evita los obstáculos".</param>
        public void Show(string verbText, string hintText = null)
        {
            if (_verbLabel != null)
                _verbLabel.text = verbText ?? string.Empty;

            if (_hintLabel != null)
                _hintLabel.text = hintText ?? string.Empty;

            gameObject.SetActive(true);
        }

        /// <summary>Hides the view.</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
