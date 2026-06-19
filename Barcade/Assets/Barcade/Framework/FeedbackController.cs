using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Barcade.Framework
{
    /// <summary>
    /// Three-tier feedback controller for bar-grade impact:
    ///
    ///   Low  -- subtle pulse: a quick camera FOV twitch or a small shake.
    ///   Mid  -- notable: a stronger camera shake + brief panel glow overlay.
    ///   High -- aggressive: full-screen flash + sustained camera shake. Fires
    ///           automatically at the end of each session round result (wired via
    ///           MicrogameLoopController hook) so the win/loss climax lands hard.
    ///
    /// Camera shake is implemented as a world-space position offset applied each
    /// frame during the effect and cleanly reset afterwards. The camera reference
    /// must be set via SetCamera(); the SceneBuilder wires this.
    ///
    /// Flash overlay: a full-screen Image on a UI Canvas. Set via SetFlashOverlay().
    ///
    /// Usage:
    ///   feedbackController.PlayLow();
    ///   feedbackController.PlayMid();
    ///   feedbackController.PlayHigh();
    ///   -- or via the slot-indexed overload (slot is unused; same effect for all) --
    ///   feedbackController.Play(FeedbackTier.High, PlayerSlot.Rojo);
    ///
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public class FeedbackController : MonoBehaviour
    {
        // Tiers as a public enum so callers can pass them without magic ints.
        public enum FeedbackTier { Low, Mid, High }

        [Header("Camera (for screen-shake)")]
        [Tooltip("The main game camera. The SceneBuilder wires this.")]
        [SerializeField] private Camera _camera;

        [Header("Full-screen flash overlay")]
        [Tooltip("A full-screen Image on the HUD canvas. The SceneBuilder wires this.")]
        [SerializeField] private Image _flashOverlay;

        // Shake parameters per tier
        private static readonly float[] ShakeDuration  = { 0.08f, 0.20f, 0.45f };
        private static readonly float[] ShakeMagnitude = { 0.04f, 0.12f, 0.28f };

        // Flash parameters per tier (Mid + High only; Low skips flash)
        private static readonly float[] FlashAlpha    = { 0f,    0.30f, 0.85f };
        private static readonly float[] FlashDuration = { 0f,    0.18f, 0.38f };

        private Coroutine _shakeCoroutine;
        private Coroutine _flashCoroutine;
        private Vector3   _cameraBasePos;
        private bool      _cameraBaseLocked;

        // Public API

        /// <summary>Sets the camera reference (called by SceneBuilder or at runtime).</summary>
        public void SetCamera(Camera cam) { _camera = cam; }

        /// <summary>Sets the flash overlay image (called by SceneBuilder or at runtime).</summary>
        public void SetFlashOverlay(Image overlay) { _flashOverlay = overlay; }

        /// <summary>Plays the Low-tier feedback (subtle pulse).</summary>
        public void PlayLow()  { PlayTier(FeedbackTier.Low); }

        /// <summary>Plays the Mid-tier feedback (panel glow + shake).</summary>
        public void PlayMid()  { PlayTier(FeedbackTier.Mid); }

        /// <summary>
        /// Plays the High-tier feedback (screen shake + full flash).
        /// Wire this to end-of-round / session result for maximum impact.
        /// </summary>
        public void PlayHigh() { PlayTier(FeedbackTier.High); }

        /// <summary>
        /// Slot-indexed variant for API symmetry (slot is informational only;
        /// the effect is screen-wide). Called from MicrogameLoopController hooks.
        /// </summary>
        public void Play(FeedbackTier tier, Barcade.Core.PlayerSlot slot)
        {
            PlayTier(tier);
        }

        // Unity lifecycle

        private void OnDestroy()
        {
            RestoreCamera();
        }

        // Private helpers

        private void PlayTier(FeedbackTier tier)
        {
            int t = (int)tier;

            // Camera shake
            if (_camera != null && ShakeDuration[t] > 0f)
            {
                if (_shakeCoroutine != null) { StopCoroutine(_shakeCoroutine); RestoreCamera(); }
                _shakeCoroutine = StartCoroutine(ShakeRoutine(ShakeDuration[t], ShakeMagnitude[t]));
            }

            // Flash overlay (Mid + High)
            if (_flashOverlay != null && FlashDuration[t] > 0f)
            {
                if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
                _flashCoroutine = StartCoroutine(FlashRoutine(FlashAlpha[t], FlashDuration[t]));
            }
        }

        private IEnumerator ShakeRoutine(float duration, float magnitude)
        {
            if (!_cameraBaseLocked)
            {
                _cameraBasePos    = _camera.transform.localPosition;
                _cameraBaseLocked = true;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float strength = magnitude * (1f - t); // linear falloff

                _camera.transform.localPosition = _cameraBasePos + new Vector3(
                    Random.Range(-strength, strength),
                    Random.Range(-strength, strength),
                    0f);

                elapsed += Time.deltaTime;
                yield return null;
            }

            RestoreCamera();
            _shakeCoroutine = null;
        }

        private IEnumerator FlashRoutine(float peakAlpha, float duration)
        {
            if (_flashOverlay == null) yield break;

            _flashOverlay.gameObject.SetActive(true);

            // Fade in to peak over 20% of duration, fade out over 80%.
            float fadeInEnd = duration * 0.20f;
            float elapsed   = 0f;

            while (elapsed < duration)
            {
                float alpha;
                if (elapsed < fadeInEnd)
                    alpha = Mathf.Lerp(0f, peakAlpha, elapsed / fadeInEnd);
                else
                    alpha = Mathf.Lerp(peakAlpha, 0f, (elapsed - fadeInEnd) / (duration - fadeInEnd));

                Color c = _flashOverlay.color;
                _flashOverlay.color = new Color(c.r, c.g, c.b, alpha);

                elapsed += Time.deltaTime;
                yield return null;
            }

            Color cf = _flashOverlay.color;
            _flashOverlay.color = new Color(cf.r, cf.g, cf.b, 0f);
            _flashOverlay.gameObject.SetActive(false);
            _flashCoroutine = null;
        }

        private void RestoreCamera()
        {
            if (_camera != null && _cameraBaseLocked)
                _camera.transform.localPosition = _cameraBasePos;

            _cameraBaseLocked = false;
        }
    }
}
