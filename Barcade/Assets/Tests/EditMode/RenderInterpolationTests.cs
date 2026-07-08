using NUnit.Framework;
using Barcade.Presentation;

namespace Barcade.Presentation.Tests
{
    /// <summary>
    /// TASK-027 (GDD T-104b) AC5 regression lock: the GDD §10.3 double-buffer
    /// interpolation factor and the shortest-path angle lerp used for rotation.
    /// No Unity required -- pure C#.
    /// </summary>
    [TestFixture]
    public class RenderInterpolationTests
    {
        // ── Lerp / Clamp01 ────────────────────────────────────────────────────

        [TestCase(0f, 10f, 0f, 0f)]
        [TestCase(0f, 10f, 1f, 10f)]
        [TestCase(0f, 10f, 0.5f, 5f)]
        [TestCase(0f, 10f, -1f, 0f)]  // clamps below 0
        [TestCase(0f, 10f, 2f, 10f)]  // clamps above 1
        public void Lerp_ClampsAndInterpolates(float a, float b, float t, float expected)
        {
            Assert.That(RenderInterpolation.Lerp(a, b, t), Is.EqualTo(expected).Within(1e-5f));
        }

        // ── LerpAngleDegrees ──────────────────────────────────────────────────

        [Test]
        public void LerpAngleDegrees_ShortestPath_AcrossWraparound()
        {
            // 350 -> 10 the "short way" goes forward through 360/0, not backward
            // through 180.
            float result = RenderInterpolation.LerpAngleDegrees(350f, 10f, 0.5f);

            Assert.That(NormalizeDeg(result), Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void LerpAngleDegrees_NoWraparound_MatchesPlainLerp()
        {
            float result = RenderInterpolation.LerpAngleDegrees(10f, 50f, 0.5f);

            Assert.That(result, Is.EqualTo(30f).Within(1e-3f));
        }

        private static float NormalizeDeg(float deg)
        {
            float d = deg % 360f;
            if (d < 0f) d += 360f;
            return d > 180f ? d - 360f : d;
        }

        // ── ComputeTickFactor ─────────────────────────────────────────────────

        [Test]
        public void ComputeTickFactor_HalfwayThroughTick_IsPointFive()
        {
            float t = RenderInterpolation.ComputeTickFactor(timeSinceLastTickSeconds: 1f / 120f, tickDurationSeconds: 1f / 60f);

            Assert.That(t, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void ComputeTickFactor_LateFrame_ClampsToOne()
        {
            // A hitch: more real time elapsed than one whole tick.
            float t = RenderInterpolation.ComputeTickFactor(timeSinceLastTickSeconds: 1f, tickDurationSeconds: 1f / 60f);

            Assert.That(t, Is.EqualTo(1f));
        }

        [Test]
        public void ComputeTickFactor_ZeroTickDuration_ReturnsOne()
        {
            Assert.That(RenderInterpolation.ComputeTickFactor(0.01f, 0f), Is.EqualTo(1f));
        }

        // ── ResolveRotationFactor ─────────────────────────────────────────────

        [Test]
        public void ResolveRotationFactor_ProgressZero_FallsBackToTickFactor()
        {
            float result = RenderInterpolation.ResolveRotationFactor(tickFactor: 0.3f, progress01: 0f);

            Assert.That(result, Is.EqualTo(0.3f));
        }

        [Test]
        public void ResolveRotationFactor_ProgressPositive_OverridesTickFactor()
        {
            // ¡APUNTA!'s aim-interp progress: opts into its own smoothing window.
            float result = RenderInterpolation.ResolveRotationFactor(tickFactor: 0.3f, progress01: 0.8f);

            Assert.That(result, Is.EqualTo(0.8f));
        }
    }
}
