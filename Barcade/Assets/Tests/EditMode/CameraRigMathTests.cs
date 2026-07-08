using NUnit.Framework;
using Barcade.Core.Content;
using Barcade.Presentation;

namespace Barcade.Presentation.Tests
{
    /// <summary>
    /// TASK-027 (GDD T-104b) AC1/AC3/AC5 regression lock: the 4 stage camera
    /// rigs place a camera consistent with the DESIGN CONTRACT RATIFIED
    /// 2026-07-08 (topDownOrtho is orthographic; frontFixed/runnerLateral/
    /// boardOverview are perspective with no ortho fields set); runnerLateral is
    /// the only rig whose placement changes with player progress (the sole
    /// travelling exception, AC3); CameraShake decays to 0 within the GDD §8.2
    /// 300ms cap. No Unity required -- pure C#.
    /// </summary>
    [TestFixture]
    public class CameraRigMathTests
    {
        private static PlaneMapping DefaultMapping() => new PlaneMapping(10f, 10f, 0f, 0f, 0f);

        // ── rig identity: camera-string parsing (closed set, GDD §11.1) ──────────

        [TestCase("topDownOrtho", CameraRigKind.TopDownOrtho)]
        [TestCase("frontFixed", CameraRigKind.FrontFixed)]
        [TestCase("runnerLateral", CameraRigKind.RunnerLateral)]
        [TestCase("boardOverview", CameraRigKind.BoardOverview)]
        public void TryParse_AllFourGddStrings_Recognized(string camera, CameraRigKind expected)
        {
            bool ok = CameraRigKindCodec.TryParse(camera, out CameraRigKind kind);

            Assert.That(ok, Is.True);
            Assert.That(kind, Is.EqualTo(expected));
        }

        [Test]
        public void TryParse_UnknownString_Fails()
        {
            Assert.That(CameraRigKindCodec.TryParse("isometric", out _), Is.False);
        }

        [Test]
        public void OnlyRunnerLateral_MovesDuringPlay()
        {
            Assert.That(CameraRigKind.RunnerLateral.MovesDuringPlay(), Is.True);
            Assert.That(CameraRigKind.TopDownOrtho.MovesDuringPlay(), Is.False);
            Assert.That(CameraRigKind.FrontFixed.MovesDuringPlay(), Is.False);
            Assert.That(CameraRigKind.BoardOverview.MovesDuringPlay(), Is.False);
        }

        // ── topDownOrtho: orthographic cenital (RULED, GDD §10.4) ────────────────

        [Test]
        public void TopDownOrtho_IsOrthographic_LookingStraightDown()
        {
            CameraPlacement placement = CameraRigMath.TopDownOrtho(DefaultMapping());

            Assert.That(placement.Orthographic, Is.True);
            Assert.That(placement.OrthoSize, Is.GreaterThan(0f));
            Assert.That(placement.FieldOfViewDeg, Is.EqualTo(0f));
            Assert.That(placement.PitchDeg, Is.EqualTo(90f)); // straight down
            Assert.That(placement.PosY, Is.GreaterThan(0f));  // above the plane
        }

        // ── frontFixed: RULED low perspective, narrow FOV, NOT ortho ─────────────

        [Test]
        public void FrontFixed_IsPerspective_NarrowFov_NotOrtho()
        {
            CameraPlacement placement = CameraRigMath.FrontFixed(DefaultMapping());

            Assert.That(placement.Orthographic, Is.False);
            Assert.That(placement.OrthoSize, Is.EqualTo(0f));
            Assert.That(placement.FieldOfViewDeg, Is.GreaterThan(0f).And.LessThan(60f), "narrow FOV per the ratified contract");
        }

        // ── runnerLateral: perspective baja, the sole travelling exception ──────

        [Test]
        public void RunnerLateral_IsPerspective()
        {
            CameraPlacement placement = CameraRigMath.RunnerLateral(DefaultMapping(), 0f);

            Assert.That(placement.Orthographic, Is.False);
            Assert.That(placement.FieldOfViewDeg, Is.GreaterThan(0f));
        }

        [Test]
        public void RunnerLateral_PlacementChanges_WithPlayerProgress()
        {
            PlaneMapping mapping = DefaultMapping();

            CameraPlacement start = CameraRigMath.RunnerLateral(mapping, 0f);
            CameraPlacement mid = CameraRigMath.RunnerLateral(mapping, 0.5f);
            CameraPlacement end = CameraRigMath.RunnerLateral(mapping, 1f);

            // Dollies along world X with progress; side offset / height stay fixed.
            Assert.That(mid.PosX, Is.GreaterThan(start.PosX));
            Assert.That(end.PosX, Is.GreaterThan(mid.PosX));
            Assert.That(mid.PosY, Is.EqualTo(start.PosY).Within(1e-5f));
            Assert.That(mid.PosZ, Is.EqualTo(start.PosZ).Within(1e-5f));
        }

        [Test]
        public void RunnerLateral_ProgressOutOfRange_Clamped()
        {
            PlaneMapping mapping = DefaultMapping();

            CameraPlacement belowZero = CameraRigMath.RunnerLateral(mapping, -5f);
            CameraPlacement atZero = CameraRigMath.RunnerLateral(mapping, 0f);
            CameraPlacement aboveOne = CameraRigMath.RunnerLateral(mapping, 5f);
            CameraPlacement atOne = CameraRigMath.RunnerLateral(mapping, 1f);

            Assert.That(belowZero.PosX, Is.EqualTo(atZero.PosX).Within(1e-5f));
            Assert.That(aboveOne.PosX, Is.EqualTo(atOne.PosX).Within(1e-5f));
        }

        // ── boardOverview: [ASSUMED] angled perspective framing the ring ────────

        [Test]
        public void BoardOverview_IsPerspective_LookingDownAtAnAngle()
        {
            CameraPlacement placement = CameraRigMath.BoardOverview(DefaultMapping());

            Assert.That(placement.Orthographic, Is.False);
            Assert.That(placement.FieldOfViewDeg, Is.GreaterThan(0f));
            Assert.That(placement.PitchDeg, Is.GreaterThan(0f).And.LessThan(90f), "angled, not straight down");
        }

        // ── ComputeFixedPlacement dispatch ───────────────────────────────────────

        [Test]
        public void ComputeFixedPlacement_DispatchesToTheMatchingRig()
        {
            PlaneMapping mapping = DefaultMapping();

            CameraPlacement viaDispatch = CameraRigMath.ComputeFixedPlacement(CameraRigKind.TopDownOrtho, mapping);
            CameraPlacement direct = CameraRigMath.TopDownOrtho(mapping);

            Assert.That(viaDispatch.Orthographic, Is.EqualTo(direct.Orthographic));
            Assert.That(viaDispatch.OrthoSize, Is.EqualTo(direct.OrthoSize));
            Assert.That(viaDispatch.PosY, Is.EqualTo(direct.PosY));
        }

        // ── CameraShake: GDD §8.2 "Screen shake <= 300 ms" ───────────────────────

        [Test]
        public void CameraShake_PeaksAtStart_DecaysToZeroByDuration()
        {
            float atStart = CameraShake.Amplitude(0f, CameraShake.MaxDurationSeconds, 1f);
            float atMid = CameraShake.Amplitude(CameraShake.MaxDurationSeconds * 0.5f, CameraShake.MaxDurationSeconds, 1f);
            float atEnd = CameraShake.Amplitude(CameraShake.MaxDurationSeconds, CameraShake.MaxDurationSeconds, 1f);
            float afterEnd = CameraShake.Amplitude(CameraShake.MaxDurationSeconds + 0.1f, CameraShake.MaxDurationSeconds, 1f);

            Assert.That(atStart, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(atMid, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(atEnd, Is.EqualTo(0f));
            Assert.That(afterEnd, Is.EqualTo(0f));
        }

        [Test]
        public void CameraShake_NonPositiveInputs_ReturnZero()
        {
            Assert.That(CameraShake.Amplitude(0.1f, 0f, 1f), Is.EqualTo(0f));
            Assert.That(CameraShake.Amplitude(0.1f, 0.3f, 0f), Is.EqualTo(0f));
            Assert.That(CameraShake.Amplitude(-0.1f, 0.3f, 1f), Is.EqualTo(0f));
        }
    }
}
