using NUnit.Framework;
using UnityEngine;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
using Barcade.Framework.Stage;
using Barcade.Presentation;

namespace Barcade.Framework.Tests.EditMode
{
    /// <summary>
    /// TASK-027 (GDD T-104b) AC5: EditMode regression lock for the
    /// Vector3/Quaternion-producing slice of StagePresenter's math (the part
    /// that references UnityEngine and so cannot join the fast dotnet suite —
    /// see Barcade.Presentation.Tests for the pure math these wrap). No play
    /// mode required: Vector3/Quaternion construction and a bare Camera
    /// component both work in the editor without entering Play mode.
    /// </summary>
    [TestFixture]
    public class StageVectorMathTests
    {
        private static PlaneMapping DefaultMapping() => new PlaneMapping(10f, 10f, 0f, 0f, 0f);

        private static RenderEntity MakeEntity(float x, float y, float height, float rotation, float scale, float progress01 = 0f)
        {
            return new RenderEntity
            {
                Kind = EntityKind.PlayerAvatar,
                OwnerSeat = 0,
                X = x,
                Y = y,
                Height = height,
                Rotation = rotation,
                Scale = scale,
                Progress01 = progress01,
            };
        }

        [Test]
        public void ToWorldPosition_MatchesPureProjection()
        {
            PlaneMapping mapping = DefaultMapping();

            Vector3 result = StageVectorMath.ToWorldPosition(0.75f, 0.25f, 1f, mapping);

            StageProjection.Project(0.75f, 0.25f, 1f, mapping, out float x, out float y, out float z);
            Assert.That(result.x, Is.EqualTo(x).Within(1e-5f));
            Assert.That(result.y, Is.EqualTo(y).Within(1e-5f));
            Assert.That(result.z, Is.EqualTo(z).Within(1e-5f));
        }

        [Test]
        public void ToWorldRotation_IsYawAroundWorldUp()
        {
            Quaternion q = StageVectorMath.ToWorldRotation(90f);

            Vector3 forward = q * Vector3.forward;
            Assert.That(forward.y, Is.EqualTo(0f).Within(1e-4f), "a pure yaw never tilts off the horizontal plane");
        }

        [Test]
        public void LerpPosition_AtT0_EqualsPrevious_AtT1_EqualsCurrent()
        {
            PlaneMapping mapping = DefaultMapping();
            RenderEntity previous = MakeEntity(0.2f, 0.2f, 0f, 0f, 1f);
            RenderEntity current = MakeEntity(0.8f, 0.8f, 1f, 0f, 1f);

            Vector3 atStart = StageVectorMath.LerpPosition(previous, current, mapping, 0f);
            Vector3 atEnd = StageVectorMath.LerpPosition(previous, current, mapping, 1f);

            Vector3 expectedStart = StageVectorMath.ToWorldPosition(previous, mapping);
            Vector3 expectedEnd = StageVectorMath.ToWorldPosition(current, mapping);
            Assert.That(Vector3.Distance(atStart, expectedStart), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(atEnd, expectedEnd), Is.LessThan(1e-4f));
        }

        [Test]
        public void LerpRotation_NoProgress01_UsesTickFactor()
        {
            RenderEntity previous = MakeEntity(0f, 0f, 0f, 0f, 1f);
            RenderEntity current = MakeEntity(0f, 0f, 0f, 90f, 1f, progress01: 0f);

            Quaternion result = StageVectorMath.LerpRotation(previous, current, tickFactor: 0.5f);

            float resultYaw = result.eulerAngles.y;
            Assert.That(resultYaw, Is.EqualTo(45f).Within(1e-2f));
        }

        [Test]
        public void LerpRotation_WithProgress01_OverridesTickFactor()
        {
            // ¡APUNTA!'s aim-interp progress opts into its own smoothing window.
            RenderEntity previous = MakeEntity(0f, 0f, 0f, 0f, 1f);
            RenderEntity current = MakeEntity(0f, 0f, 0f, 100f, 1f, progress01: 0.25f);

            Quaternion result = StageVectorMath.LerpRotation(previous, current, tickFactor: 1f);

            float resultYaw = result.eulerAngles.y;
            Assert.That(resultYaw, Is.EqualTo(25f).Within(1e-2f), "should use progress01 (0.25), not the tick factor (1.0)");
        }

        [Test]
        public void LerpScale_Interpolates()
        {
            RenderEntity previous = MakeEntity(0f, 0f, 0f, 0f, 1f);
            RenderEntity current = MakeEntity(0f, 0f, 0f, 0f, 2f);

            float scale = StageVectorMath.LerpScale(previous, current, 0.5f);

            Assert.That(scale, Is.EqualTo(1.5f).Within(1e-4f));
        }

        [Test]
        public void ApplyCameraPlacement_Orthographic_SetsOrthoSizeNotFov()
        {
            var go = new GameObject("TestCam");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                var placement = new CameraPlacement(
                    posX: 1f, posY: 2f, posZ: 3f,
                    pitchDeg: 90f, yawDeg: 0f,
                    orthographic: true, orthoSize: 5f, fieldOfViewDeg: 0f);

                Vector3 basePos = StageVectorMath.ApplyCameraPlacement(cam, placement, Vector3.zero);

                Assert.That(cam.orthographic, Is.True);
                Assert.That(cam.orthographicSize, Is.EqualTo(5f));
                Assert.That(basePos, Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(cam.transform.position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ApplyCameraPlacement_Perspective_SetsFovNotOrtho()
        {
            var go = new GameObject("TestCam");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                var placement = new CameraPlacement(
                    posX: 0f, posY: 1.6f, posZ: -8f,
                    pitchDeg: 8f, yawDeg: 0f,
                    orthographic: false, orthoSize: 0f, fieldOfViewDeg: 35f);

                StageVectorMath.ApplyCameraPlacement(cam, placement, Vector3.zero);

                Assert.That(cam.orthographic, Is.False);
                Assert.That(cam.fieldOfView, Is.EqualTo(35f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ApplyCameraPlacement_AddsShakeOffset_ToBasePosition()
        {
            var go = new GameObject("TestCam");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                var placement = new CameraPlacement(0f, 0f, 0f, 90f, 0f, true, 5f, 0f);
                var shake = new Vector3(0.1f, -0.1f, 0f);

                StageVectorMath.ApplyCameraPlacement(cam, placement, shake);

                Assert.That(cam.transform.position, Is.EqualTo(shake));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ToUnityColor_MatchesRgbComponents()
        {
            var rgb = new RgbColor(0.1f, 0.2f, 0.3f);

            Color color = StageVectorMath.ToUnityColor(rgb);

            Assert.That(color.r, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(color.g, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(color.b, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(color.a, Is.EqualTo(1f));
        }
    }
}
