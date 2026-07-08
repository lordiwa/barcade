using NUnit.Framework;
using UnityEngine;
using Barcade.Core;
using Barcade.Framework; // ShapeFactory
using Barcade.Framework.Stage;
using Barcade.Presentation;

namespace Barcade.Framework.Tests.EditMode
{
    /// <summary>
    /// TASK-027 (GDD T-104b) AC4/AC5: EditMode coverage for the Unity-dependent
    /// pieces added for the avatar shape-marker channel — ShapeFactory's two new
    /// shapes (it previously only had circle/square) and AvatarMarkerFactory's
    /// overhead placement. No play mode required.
    /// </summary>
    [TestFixture]
    public class ShapeFactoryStageAdditionsTests
    {
        [Test]
        public void MakeTriangle_HasAMeshScaledToSize()
        {
            GameObject go = ShapeFactory.MakeTriangle(PlayerSlot.Amarillo, Vector3.zero, size: 0.6f);
            try
            {
                Assert.That(go.GetComponent<MeshFilter>()?.sharedMesh, Is.Not.Null);
                Assert.That(go.GetComponent<MeshRenderer>(), Is.Not.Null);
                Assert.That(go.transform.localScale, Is.EqualTo(new Vector3(0.6f, 0.6f, 1f)));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MakeRhombus_IsASquareRotated45Degrees()
        {
            GameObject go = ShapeFactory.MakeRhombus(PlayerSlot.Verde, Vector3.zero, size: 0.5f);
            try
            {
                Assert.That(go.transform.localScale, Is.EqualTo(new Vector3(0.5f, 0.5f, 1f)));
                Assert.That(go.transform.localRotation.eulerAngles.z, Is.EqualTo(45f).Within(1e-3f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(PlayerSlot.Rojo, SeatShape.Circle)]
        [TestCase(PlayerSlot.Azul, SeatShape.Square)]
        [TestCase(PlayerSlot.Amarillo, SeatShape.Triangle)]
        [TestCase(PlayerSlot.Verde, SeatShape.Rhombus)]
        public void AvatarMarkerFactory_Create_LiesFlatOverhead(PlayerSlot slot, SeatShape shape)
        {
            var parent = new GameObject("AvatarStandIn");
            try
            {
                GameObject marker = AvatarMarkerFactory.Create(slot, shape, parent.transform);

                // Rotated onto the XZ plane (readable from an overhead/low-angle
                // stage camera) instead of ShapeFactory's default camera-facing XY.
                Assert.That(marker.transform.localRotation.eulerAngles.x, Is.EqualTo(90f).Within(1e-3f));
                Assert.That(marker.transform.localPosition.y, Is.EqualTo(AvatarMarkerFactory.HeightAboveAvatar).Within(1e-4f));
                Assert.That(marker.transform.parent, Is.EqualTo(parent.transform));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
