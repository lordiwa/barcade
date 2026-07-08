using NUnit.Framework;
using Barcade.Core.Content;
using Barcade.Presentation;

namespace Barcade.Presentation.Tests
{
    /// <summary>
    /// TASK-027 (GDD T-104b) AC5 regression lock: logical [0,1]^2 + Height ->
    /// world projection, fixed axis convention (logical X -> world X, logical Y
    /// -> world Z, Height -> world Y). No Unity required -- pure C#.
    /// </summary>
    [TestFixture]
    public class StageProjectionTests
    {
        private static PlaneMapping DefaultMapping() => new PlaneMapping(10f, 10f, 0f, 0f, 0f);

        [Test]
        public void LogicalCenter_ProjectsToWorldOrigin()
        {
            PlaneMapping mapping = DefaultMapping();

            StageProjection.Project(0.5f, 0.5f, 0f, mapping, out float x, out float y, out float z);

            Assert.That(x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(y, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(z, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void LogicalCorners_ProjectToOppositeWorldCorners()
        {
            PlaneMapping mapping = DefaultMapping();

            StageProjection.Project(0f, 0f, 0f, mapping, out float x0, out _, out float z0);
            StageProjection.Project(1f, 1f, 0f, mapping, out float x1, out _, out float z1);

            Assert.That(x0, Is.EqualTo(-5f).Within(1e-5f));
            Assert.That(z0, Is.EqualTo(-5f).Within(1e-5f));
            Assert.That(x1, Is.EqualTo(5f).Within(1e-5f));
            Assert.That(z1, Is.EqualTo(5f).Within(1e-5f));
        }

        [Test]
        public void LogicalY_MapsToWorldZ_NotWorldY()
        {
            // Fixed axis convention (DESIGN CONTRACT RATIFIED 2026-07-08):
            // logical Y is the plane's OTHER horizontal axis, not up.
            PlaneMapping mapping = DefaultMapping();

            StageProjection.Project(0.5f, 1f, 0f, mapping, out float x, out float y, out float z);

            Assert.That(x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(y, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(z, Is.EqualTo(5f).Within(1e-5f));
        }

        [Test]
        public void Height_MapsToWorldY_Up()
        {
            PlaneMapping mapping = DefaultMapping();

            StageProjection.Project(0.5f, 0.5f, 1.5f, mapping, out _, out float y, out _);

            Assert.That(y, Is.EqualTo(1.5f).Within(1e-5f));
        }

        [Test]
        public void WorldOrigin_OffsetsAllAxes()
        {
            var mapping = new PlaneMapping(10f, 10f, worldOriginX: 3f, worldOriginY: 2f, worldOriginZ: -4f);

            StageProjection.Project(0.5f, 0.5f, 0f, mapping, out float x, out float y, out float z);

            Assert.That(x, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(y, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(z, Is.EqualTo(-4f).Within(1e-5f));
        }

        [Test]
        public void NonSquarePlane_ScalesXAndZIndependently()
        {
            var mapping = new PlaneMapping(worldSizeX: 20f, worldSizeZ: 4f, worldOriginX: 0f, worldOriginY: 0f, worldOriginZ: 0f);

            StageProjection.Project(1f, 1f, 0f, mapping, out float x, out _, out float z);

            Assert.That(x, Is.EqualTo(10f).Within(1e-5f));
            Assert.That(z, Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void HeightScale_Overload_ScalesHeight()
        {
            PlaneMapping mapping = DefaultMapping();

            StageProjection.Project(0.5f, 0.5f, 2f, mapping, heightScale: 0.5f, out _, out float y, out _);

            Assert.That(y, Is.EqualTo(1f).Within(1e-5f));
        }
    }
}
