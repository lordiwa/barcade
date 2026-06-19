using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Barcade.Core.Content;

namespace Barcade.Framework
{
    /// <summary>
    /// Boot-time MonoBehaviour that checks for remote catalog updates with a
    /// short timeout and falls back to cached / local content when the server
    /// is unreachable.
    ///
    /// Usage: attach to a GameObject in Boot.unity (before <see cref="GameBootstrapper"/>
    /// loads the Manager scene). Call <c>StartCoroutine(CheckAndUpdateCatalogs())</c>
    /// and yield on the returned coroutine before proceeding.
    ///
    /// The decision logic lives in <see cref="ContentUpdatePolicy"/> (pure C#,
    /// engine-free) so it can be exercised by the fast-test suite. This class is
    /// a thin coroutine adapter around that logic and the Addressables API.
    ///
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public class ContentUpdater : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────

        [Tooltip("Seconds to wait for the remote catalog before giving up and " +
                 "running on last-known-good content. 8 seconds recommended for " +
                 "bar/kiosk environments with unreliable WiFi.")]
        [SerializeField] private float _catalogTimeoutSeconds = 8f;

        // ── Runtime result ────────────────────────────────────────────────────────

        /// <summary>
        /// True when the coroutine has completed (regardless of success or fallback).
        /// Boot logic should yield on the coroutine rather than polling this directly.
        /// </summary>
        public bool IsComplete { get; private set; }

        /// <summary>
        /// Final phase reached by the policy after the coroutine completes.
        /// <see cref="UpdatePhase.Succeeded"/> means the catalog is current.
        /// <see cref="UpdatePhase.TimedOut"/> or <see cref="UpdatePhase.Failed"/>
        /// means we are running on cached / local content.
        /// </summary>
        public UpdatePhase ResultPhase { get; private set; }

        // ── Public entry point ────────────────────────────────────────────────────

        /// <summary>
        /// Coroutine that runs the full catalog-check sequence. Start this in Boot.unity
        /// before loading the Manager scene:
        /// <code>
        ///   yield return StartCoroutine(contentUpdater.CheckAndUpdateCatalogs());
        /// </code>
        /// </summary>
        public IEnumerator CheckAndUpdateCatalogs()
        {
            IsComplete  = false;
            ResultPhase = UpdatePhase.Idle;

            var policy = new ContentUpdatePolicy(_catalogTimeoutSeconds);

            // ── Step 1: InitializeAsync ───────────────────────────────────────────
            // Addressables.InitializeAsync contacts the remote catalog URL.
            // We drive the policy timer each frame and bail on timeout.

            var initHandle = Addressables.InitializeAsync();
            policy.OnInitStarted();

            while (!initHandle.IsDone)
            {
                policy.Tick(Time.deltaTime);
                if (policy.IsTerminal)
                {
                    // Timed out while waiting for init.
                    Debug.LogWarning(
                        $"[ContentUpdater] Catalog init timed out after {_catalogTimeoutSeconds}s " +
                        "— running on last-known-good content.");
                    Addressables.Release(initHandle);
                    Finish(policy);
                    yield break;
                }
                yield return null;
            }

            if (initHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogWarning("[ContentUpdater] Addressables.InitializeAsync failed — offline fallback.");
                policy.OnInitFailed();
                Addressables.Release(initHandle);
                Finish(policy);
                yield break;
            }

            policy.OnInitSucceeded();
            Addressables.Release(initHandle);

            // ── Step 2: CheckForCatalogUpdates ────────────────────────────────────

            var checkHandle = Addressables.CheckForCatalogUpdates(autoReleaseHandle: false);

            while (!checkHandle.IsDone)
            {
                policy.Tick(Time.deltaTime);
                if (policy.IsTerminal)
                {
                    Debug.LogWarning(
                        $"[ContentUpdater] Catalog check timed out after {_catalogTimeoutSeconds}s " +
                        "— running on last-known-good content.");
                    Addressables.Release(checkHandle);
                    Finish(policy);
                    yield break;
                }
                yield return null;
            }

            if (checkHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogWarning("[ContentUpdater] CheckForCatalogUpdates failed — offline fallback.");
                policy.OnCatalogCheckFailed();
                Addressables.Release(checkHandle);
                Finish(policy);
                yield break;
            }

            List<string> stale = checkHandle.Result;
            policy.OnCatalogCheckCompleted(stale != null ? stale.Count : 0);
            Addressables.Release(checkHandle);

            if (policy.Phase == UpdatePhase.Succeeded)
            {
                // Already up-to-date; nothing to download.
                Debug.Log("[ContentUpdater] Catalog already up-to-date.");
                Finish(policy);
                yield break;
            }

            // ── Step 3: UpdateCatalogs ────────────────────────────────────────────

            var updateHandle = Addressables.UpdateCatalogs(stale, autoCleanBundleCache: true);

            while (!updateHandle.IsDone)
            {
                policy.Tick(Time.deltaTime);
                if (policy.IsTerminal)
                {
                    Debug.LogWarning(
                        $"[ContentUpdater] Catalog download timed out after {_catalogTimeoutSeconds}s " +
                        "— running on last-known-good content.");
                    Addressables.Release(updateHandle);
                    Finish(policy);
                    yield break;
                }
                yield return null;
            }

            if (updateHandle.Status == AsyncOperationStatus.Succeeded)
                Debug.Log($"[ContentUpdater] Catalog updated — {stale.Count} catalog(s) refreshed.");
            else
                Debug.LogWarning("[ContentUpdater] Catalog download failed — running on cached content.");

            Addressables.Release(updateHandle);

            policy.OnCatalogDownloadFinished();
            Finish(policy);
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private void Finish(ContentUpdatePolicy policy)
        {
            ResultPhase = policy.Phase;
            IsComplete  = true;
        }
    }
}
