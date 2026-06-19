namespace Barcade.Core.Content
{
    /// <summary>
    /// Engine-free state machine encoding the catalog-check decision logic:
    ///   - How long to wait for a remote catalog before giving up.
    ///   - Whether to proceed on success, fall back on timeout, or fall back on error.
    ///
    /// Intended to be driven by <c>ContentUpdater</c> (the MonoBehaviour wrapper)
    /// one tick at a time. Keeping all branching logic here enables deterministic
    /// unit tests in the fast-test suite without a running Unity scene.
    ///
    /// Pure C# — no UnityEngine references.
    /// </summary>
    public sealed class ContentUpdatePolicy
    {
        // ── Configuration ─────────────────────────────────────────────────────────

        /// <summary>
        /// Seconds to wait for the remote catalog before triggering an offline fallback.
        /// 8 seconds is the recommended value for a bar/kiosk environment.
        /// </summary>
        public readonly float TimeoutSeconds;

        // ── State ─────────────────────────────────────────────────────────────────

        /// <summary>Current phase of the update check lifecycle.</summary>
        public UpdatePhase Phase { get; private set; }

        /// <summary>
        /// Elapsed time since the last phase that has an active timer.
        /// Resets to zero on each phase transition.
        /// </summary>
        public float Elapsed { get; private set; }

        /// <summary>
        /// True when the policy has reached a terminal phase (Succeeded, TimedOut,
        /// or Failed). The caller should stop driving <see cref="Tick"/> once this
        /// is true.
        /// </summary>
        public bool IsTerminal =>
            Phase == UpdatePhase.Succeeded ||
            Phase == UpdatePhase.TimedOut  ||
            Phase == UpdatePhase.Failed;

        // ── Constructor ───────────────────────────────────────────────────────────

        /// <param name="timeoutSeconds">
        /// Seconds before a catalog-wait phase transitions to TimedOut.
        /// Must be positive.
        /// </param>
        public ContentUpdatePolicy(float timeoutSeconds = 8f)
        {
            TimeoutSeconds = timeoutSeconds > 0f ? timeoutSeconds : 8f;
            Phase          = UpdatePhase.Idle;
        }

        // ── Lifecycle: phase transitions ──────────────────────────────────────────

        /// <summary>
        /// Advances the elapsed timer by <paramref name="deltaTime"/> seconds.
        /// If the elapsed time has exceeded <see cref="TimeoutSeconds"/> while
        /// waiting for a catalog, transitions to <see cref="UpdatePhase.TimedOut"/>.
        ///
        /// Call this every frame from the MonoBehaviour Update (or the coroutine
        /// yield loop) while <see cref="Phase"/> is a waiting phase.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (IsTerminal) return;

            Elapsed += deltaTime;

            if ((Phase == UpdatePhase.WaitingForInit ||
                 Phase == UpdatePhase.WaitingForCatalogCheck ||
                 Phase == UpdatePhase.WaitingForCatalogDownload) &&
                Elapsed >= TimeoutSeconds)
            {
                TransitionTo(UpdatePhase.TimedOut);
            }
        }

        /// <summary>Transitions from Idle to WaitingForInit. Call when the async init starts.</summary>
        public void OnInitStarted()
        {
            if (Phase != UpdatePhase.Idle) return;
            TransitionTo(UpdatePhase.WaitingForInit);
        }

        /// <summary>
        /// Call when <c>Addressables.InitializeAsync()</c> completes successfully.
        /// Transitions to WaitingForCatalogCheck.
        /// </summary>
        public void OnInitSucceeded()
        {
            if (Phase != UpdatePhase.WaitingForInit) return;
            TransitionTo(UpdatePhase.WaitingForCatalogCheck);
        }

        /// <summary>
        /// Call when <c>Addressables.InitializeAsync()</c> fails.
        /// Transitions to Failed (offline fallback).
        /// </summary>
        public void OnInitFailed()
        {
            if (IsTerminal) return;
            TransitionTo(UpdatePhase.Failed);
        }

        /// <summary>
        /// Call when <c>Addressables.CheckForCatalogUpdates()</c> completes.
        /// <paramref name="staleCount"/> is the number of stale catalogs returned.
        /// Zero means everything is current — transition directly to Succeeded.
        /// Positive means we need to download — transition to WaitingForCatalogDownload.
        /// </summary>
        public void OnCatalogCheckCompleted(int staleCount)
        {
            if (Phase != UpdatePhase.WaitingForCatalogCheck) return;

            if (staleCount <= 0)
                TransitionTo(UpdatePhase.Succeeded);
            else
                TransitionTo(UpdatePhase.WaitingForCatalogDownload);
        }

        /// <summary>
        /// Call when <c>Addressables.CheckForCatalogUpdates()</c> fails.
        /// Transitions to Failed (offline fallback).
        /// </summary>
        public void OnCatalogCheckFailed()
        {
            if (IsTerminal) return;
            TransitionTo(UpdatePhase.Failed);
        }

        /// <summary>
        /// Call when <c>Addressables.UpdateCatalogs()</c> completes (success or failure).
        /// Either way the game can proceed — we have either fresh or cached content.
        /// Transitions to Succeeded.
        /// </summary>
        public void OnCatalogDownloadFinished()
        {
            if (IsTerminal) return;
            TransitionTo(UpdatePhase.Succeeded);
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private void TransitionTo(UpdatePhase next)
        {
            Phase   = next;
            Elapsed = 0f;
        }
    }

    // ── Phase enum ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ordered phases of the content-update check lifecycle.
    ///
    /// Terminal phases: Succeeded, TimedOut, Failed.
    /// Waiting phases: WaitingForInit, WaitingForCatalogCheck, WaitingForCatalogDownload.
    /// </summary>
    public enum UpdatePhase
    {
        /// <summary>Not yet started.</summary>
        Idle,

        /// <summary>Addressables.InitializeAsync() in flight.</summary>
        WaitingForInit,

        /// <summary>CheckForCatalogUpdates() in flight.</summary>
        WaitingForCatalogCheck,

        /// <summary>UpdateCatalogs() in flight.</summary>
        WaitingForCatalogDownload,

        /// <summary>Terminal: catalog is up-to-date or successfully updated.</summary>
        Succeeded,

        /// <summary>Terminal: a waiting phase exceeded TimeoutSeconds. Use cached content.</summary>
        TimedOut,

        /// <summary>Terminal: a non-timeout failure occurred. Use cached content.</summary>
        Failed,
    }
}
