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

        private void Awake()
        {
            // Hide by default; MicrogameLoopController activates us per phase.
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Activates the view and sets the displayed verb text.
        /// </summary>
        /// <param name="verbText">The command verb, e.g. "¡ESQUIVA!".</param>
        public void Show(string verbText)
        {
            if (_verbLabel != null)
                _verbLabel.text = verbText ?? string.Empty;

            gameObject.SetActive(true);
        }

        /// <summary>Hides the view.</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
