using UnityEngine;

namespace Barcade.Framework
{
    /// <summary>
    /// Shows an intermission screen between microgames.
    ///
    /// Wire-up:
    ///   - Add to any GameObject (e.g. a Panel in the Manager Canvas).
    ///   - MicrogameLoopController calls Show() when entering Intermission phase
    ///     and Hide() when the phase ends.
    ///   - Extend with animator / audio cues as needed; the controller only toggles
    ///     visibility — decoration is owned by this component.
    ///
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// Unity integration compile verified in the batched integration pass.
    /// </summary>
    public class IntermissionView : MonoBehaviour
    {
        private void Awake()
        {
            // Start hidden; MicrogameLoopController activates us per phase.
            gameObject.SetActive(false);
        }

        /// <summary>Activates the intermission view.</summary>
        public void Show()
        {
            gameObject.SetActive(true);
        }

        /// <summary>Hides the intermission view.</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
