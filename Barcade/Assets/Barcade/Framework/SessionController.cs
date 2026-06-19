using UnityEngine;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Adds a session concept on top of MicrogameLoopController: plays N microgames
    /// (a full session), then shows the end-of-session ResultsScreen with final
    /// standings. A RESTART resets the ScoreModel and begins a new session.
    ///
    /// Design:
    ///   - SessionLength (default 8) rounds constitute one session.
    ///   - After the Nth round completes (SequencerDirector.Score.GetGamesPlayed
    ///     reaches SessionLength), the loop pauses and ResultsScreen.Show() is called.
    ///   - High-tier feedback fires via FeedbackController on each round result
    ///     (Mid when individual rounds end, High at session end).
    ///   - On restart: the scene is reloaded or the loop + score are re-initialised
    ///     in-place (simpler for a persistent manager scene).
    ///
    /// Wire-up (done by SceneBuilder):
    ///   _loopController    -- the MicrogameLoopController in the scene.
    ///   _resultsScreen     -- the ResultsScreen GameObject.
    ///   _feedbackController -- the FeedbackController.
    ///   _hudController     -- the HudController (refreshed each round).
    ///
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public class SessionController : MonoBehaviour
    {
        [Header("Session settings")]
        [Tooltip("Number of microgame rounds per session.")]
        [SerializeField] private int _sessionLength = 8;

        [Header("Wired references (SceneBuilder or Inspector)")]
        [SerializeField] private MicrogameLoopController _loopController;
        [SerializeField] private ResultsScreen            _resultsScreen;
        [SerializeField] private FeedbackController       _feedbackController;
        [SerializeField] private HudController            _hudController;

        private SequencerDirector _director;
        private int               _lastGamesPlayed;    // games played at last check (any slot)
        private bool              _sessionEnded;

        // Expose session ended state for test assertions.
        public bool SessionEnded => _sessionEnded;
        public int  SessionLength => _sessionLength;

        // Public API

        /// <summary>
        /// Wires all references from code. Called by SceneBuilder or in test setup.
        /// </summary>
        public void Wire(
            MicrogameLoopController loop,
            ResultsScreen results,
            FeedbackController feedback,
            HudController hud)
        {
            _loopController     = loop;
            _resultsScreen      = results;
            _feedbackController = feedback;
            _hudController      = hud;
        }

        // Unity lifecycle

        private void Start()
        {
            if (_resultsScreen != null)
                _resultsScreen.OnRestartRequested += HandleRestartRequested;

            _sessionEnded    = false;
            _lastGamesPlayed = 0;
        }

        private void Update()
        {
            if (_sessionEnded) return;

            // Latch director once available.
            if (_director == null)
            {
                if (_loopController == null || _loopController.Director == null) return;
                _director = _loopController.Director;
            }

            // Count games played (use Rojo as the canonical counter -- all 4 slots
            // advance together since RecordRound writes to all 4 simultaneously).
            int played = _director.Score.GetGamesPlayed(PlayerSlot.Rojo);

            if (played > _lastGamesPlayed)
            {
                // One or more rounds just completed.
                _lastGamesPlayed = played;

                // Refresh HUD.
                _hudController?.RefreshAll();

                // Fire Mid feedback on round result.
                _feedbackController?.PlayMid();

                // Check if session is complete.
                if (played >= _sessionLength)
                    EndSession();
            }
        }

        // Private helpers

        private void EndSession()
        {
            _sessionEnded = true;

            // Fire High feedback for the climax.
            _feedbackController?.PlayHigh();

            // Show results screen.
            if (_resultsScreen != null && _director != null)
                _resultsScreen.Show(_director.Score);

            Debug.Log("[SessionController] Session complete after " + _lastGamesPlayed + " rounds.");
        }

        private void HandleRestartRequested()
        {
            _sessionEnded    = false;
            _lastGamesPlayed = 0;
            _director        = null;

            if (_resultsScreen != null)
                _resultsScreen.Hide();

            // Reload the scene for a clean restart.
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }
}
