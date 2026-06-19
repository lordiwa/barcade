using NUnit.Framework;
using Barcade.Core.Content;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// EditMode (fast-test) coverage for <see cref="ContentUpdatePolicy"/>.
    ///
    /// Tests verify: timeout transitions, error/fallback paths, the already-current
    /// fast-path, and the full happy-path through init → check → download → Succeeded.
    ///
    /// No Unity scene required — pure C#.
    /// </summary>
    [TestFixture]
    public class ContentUpdatePolicyTests
    {
        // ── Construction ──────────────────────────────────────────────────────────

        [Test]
        public void DefaultConstructor_StartsIdle()
        {
            var policy = new ContentUpdatePolicy();
            Assert.That(policy.Phase,   Is.EqualTo(UpdatePhase.Idle));
            Assert.That(policy.Elapsed, Is.EqualTo(0f));
            Assert.That(policy.IsTerminal, Is.False);
        }

        [Test]
        public void Constructor_ZeroOrNegativeTimeout_ClampedToEightSeconds()
        {
            var p0 = new ContentUpdatePolicy(0f);
            var pNeg = new ContentUpdatePolicy(-5f);
            Assert.That(p0.TimeoutSeconds,   Is.EqualTo(8f));
            Assert.That(pNeg.TimeoutSeconds, Is.EqualTo(8f));
        }

        [Test]
        public void Constructor_PositiveTimeout_Stored()
        {
            var policy = new ContentUpdatePolicy(15f);
            Assert.That(policy.TimeoutSeconds, Is.EqualTo(15f));
        }

        // ── Init phase ────────────────────────────────────────────────────────────

        [Test]
        public void OnInitStarted_TransitionsFromIdleToWaitingForInit()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.WaitingForInit));
        }

        [Test]
        public void OnInitStarted_WhenNotIdle_IsIgnored()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.OnInitStarted(); // second call ignored
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.WaitingForInit));
        }

        [Test]
        public void Tick_DuringWaitingForInit_AccumulatesElapsed()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.Tick(3f);
            Assert.That(policy.Elapsed, Is.EqualTo(3f).Within(0.001f));
            Assert.That(policy.Phase,   Is.EqualTo(UpdatePhase.WaitingForInit));
        }

        [Test]
        public void Tick_ExceedsTimeout_DuringInit_TransitionsToTimedOut()
        {
            var policy = new ContentUpdatePolicy(5f);
            policy.OnInitStarted();
            policy.Tick(5.1f);
            Assert.That(policy.Phase,      Is.EqualTo(UpdatePhase.TimedOut));
            Assert.That(policy.IsTerminal, Is.True);
        }

        [Test]
        public void Tick_ExactlyAtTimeout_DuringInit_TransitionsToTimedOut()
        {
            var policy = new ContentUpdatePolicy(5f);
            policy.OnInitStarted();
            policy.Tick(5f);
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.TimedOut));
        }

        [Test]
        public void Tick_BelowTimeout_DuringInit_RemainsWaiting()
        {
            var policy = new ContentUpdatePolicy(5f);
            policy.OnInitStarted();
            policy.Tick(4.99f);
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.WaitingForInit));
        }

        [Test]
        public void OnInitFailed_TransitionsToFailed()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.OnInitFailed();
            Assert.That(policy.Phase,      Is.EqualTo(UpdatePhase.Failed));
            Assert.That(policy.IsTerminal, Is.True);
        }

        [Test]
        public void OnInitSucceeded_TransitionsToWaitingForCatalogCheck()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.OnInitSucceeded();
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.WaitingForCatalogCheck));
        }

        [Test]
        public void OnInitSucceeded_ResetsElapsed()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.Tick(3f);
            policy.OnInitSucceeded();
            Assert.That(policy.Elapsed, Is.EqualTo(0f).Within(0.001f));
        }

        // ── Catalog check phase ───────────────────────────────────────────────────

        [Test]
        public void Tick_ExceedsTimeout_DuringCatalogCheck_TransitionsToTimedOut()
        {
            var policy = new ContentUpdatePolicy(5f);
            policy.OnInitStarted();
            policy.OnInitSucceeded();
            policy.Tick(5.1f);
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.TimedOut));
        }

        [Test]
        public void OnCatalogCheckFailed_TransitionsToFailed()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.OnInitSucceeded();
            policy.OnCatalogCheckFailed();
            Assert.That(policy.Phase,      Is.EqualTo(UpdatePhase.Failed));
            Assert.That(policy.IsTerminal, Is.True);
        }

        [Test]
        public void OnCatalogCheckCompleted_ZeroStale_TransitionsToSucceeded()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.OnInitSucceeded();
            policy.OnCatalogCheckCompleted(0);
            Assert.That(policy.Phase,      Is.EqualTo(UpdatePhase.Succeeded));
            Assert.That(policy.IsTerminal, Is.True);
        }

        [Test]
        public void OnCatalogCheckCompleted_PositiveStale_TransitionsToWaitingForDownload()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.OnInitSucceeded();
            policy.OnCatalogCheckCompleted(3);
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.WaitingForCatalogDownload));
        }

        // ── Catalog download phase ────────────────────────────────────────────────

        [Test]
        public void Tick_ExceedsTimeout_DuringDownload_TransitionsToTimedOut()
        {
            var policy = new ContentUpdatePolicy(5f);
            policy.OnInitStarted();
            policy.OnInitSucceeded();
            policy.OnCatalogCheckCompleted(2);
            policy.Tick(5.1f);
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.TimedOut));
        }

        [Test]
        public void OnCatalogDownloadFinished_TransitionsToSucceeded()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.OnInitSucceeded();
            policy.OnCatalogCheckCompleted(2);
            policy.OnCatalogDownloadFinished();
            Assert.That(policy.Phase,      Is.EqualTo(UpdatePhase.Succeeded));
            Assert.That(policy.IsTerminal, Is.True);
        }

        // ── Terminal-state guard ──────────────────────────────────────────────────

        [Test]
        public void Tick_AfterTimedOut_DoesNotChangePhase()
        {
            var policy = new ContentUpdatePolicy(5f);
            policy.OnInitStarted();
            policy.Tick(5.1f);
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.TimedOut));

            policy.Tick(100f); // should be no-op
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.TimedOut));
        }

        [Test]
        public void Tick_AfterSucceeded_DoesNotChangePhase()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.OnInitSucceeded();
            policy.OnCatalogCheckCompleted(0);
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.Succeeded));

            policy.Tick(100f); // should be no-op
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.Succeeded));
        }

        // ── Multiple tick accumulation ────────────────────────────────────────────

        [Test]
        public void Tick_MultipleFrames_AccumulatesCorrectly()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.Tick(2f);
            policy.Tick(2f);
            policy.Tick(2f);
            // 6f total — still below 10f timeout.
            Assert.That(policy.Elapsed, Is.EqualTo(6f).Within(0.001f));
            Assert.That(policy.Phase,   Is.EqualTo(UpdatePhase.WaitingForInit));
        }

        [Test]
        public void Tick_MultipleFrames_EventuallyTimesOut()
        {
            var policy = new ContentUpdatePolicy(5f);
            policy.OnInitStarted();
            policy.Tick(2f);
            policy.Tick(2f);
            // 4f — still waiting.
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.WaitingForInit));
            policy.Tick(2f);
            // 6f total — timeout.
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.TimedOut));
        }

        // ── Happy path full sequence ──────────────────────────────────────────────

        [Test]
        public void HappyPath_InitCheckDownload_ReachesSucceeded()
        {
            var policy = new ContentUpdatePolicy(10f);

            // Start
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.Idle));

            // Init
            policy.OnInitStarted();
            policy.Tick(0.1f);
            policy.OnInitSucceeded();
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.WaitingForCatalogCheck));

            // Check
            policy.Tick(0.1f);
            policy.OnCatalogCheckCompleted(2);
            Assert.That(policy.Phase, Is.EqualTo(UpdatePhase.WaitingForCatalogDownload));

            // Download
            policy.Tick(0.5f);
            policy.OnCatalogDownloadFinished();
            Assert.That(policy.Phase,      Is.EqualTo(UpdatePhase.Succeeded));
            Assert.That(policy.IsTerminal, Is.True);
        }

        [Test]
        public void HappyPath_AlreadyCurrent_ReachesSucceededEarly()
        {
            var policy = new ContentUpdatePolicy(10f);
            policy.OnInitStarted();
            policy.OnInitSucceeded();
            policy.OnCatalogCheckCompleted(0); // nothing stale
            Assert.That(policy.Phase,      Is.EqualTo(UpdatePhase.Succeeded));
            Assert.That(policy.IsTerminal, Is.True);
        }
    }
}
