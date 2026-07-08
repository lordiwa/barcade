using UnityEngine;
using Barcade.Core.Content;
using Barcade.Core.Microgames.V2;
using Barcade.Presentation;

namespace Barcade.Framework.Stage
{
    /// <summary>
    /// Thin <c>UnityEngine.Vector3</c>/<c>Quaternion</c> wrapper around the pure
    /// <c>Barcade.Presentation</c> math (GDD §10.4, TASK-027). Kept
    /// separate from <see cref="StagePresenter"/> so this conversion logic is
    /// directly EditMode-testable without a running scene (Vector3/Quaternion
    /// math needs no play mode) — see
    /// <c>Barcade.Framework.Tests.EditMode.StageVectorMathTests</c>. Cannot join
    /// the fast dotnet suite itself (it references UnityEngine); the pure math
    /// it wraps already is (see <c>Barcade.Presentation.Tests</c>).
    /// </summary>
    public static class StageVectorMath
    {
        public static Vector3 ToWorldPosition(float logicalX, float logicalY, float height, PlaneMapping mapping)
        {
            StageProjection.Project(logicalX, logicalY, height, mapping, out float x, out float y, out float z);
            return new Vector3(x, y, z);
        }

        public static Vector3 ToWorldPosition(in RenderEntity entity, PlaneMapping mapping) =>
            ToWorldPosition(entity.X, entity.Y, entity.Height, mapping);

        /// <summary>Logical <see cref="RenderEntity.Rotation"/> is a yaw around world up (Y) — the plane's normal.</summary>
        public static Quaternion ToWorldRotation(float rotationDegrees) => Quaternion.Euler(0f, rotationDegrees, 0f);

        /// <summary>Interpolates world position between two ticks of the same entity slot (GDD §10.3 double buffer).</summary>
        public static Vector3 LerpPosition(in RenderEntity previous, in RenderEntity current, PlaneMapping mapping, float tickFactor)
        {
            Vector3 prevWorld = ToWorldPosition(previous, mapping);
            Vector3 currWorld = ToWorldPosition(current, mapping);
            return Vector3.Lerp(prevWorld, currWorld, RenderInterpolation.Clamp01(tickFactor));
        }

        /// <summary>
        /// Interpolates rotation, preferring <see cref="RenderEntity.Progress01"/>
        /// over the tick factor when the mechanic populates it (see
        /// <see cref="RenderInterpolation.ResolveRotationFactor"/>).
        /// </summary>
        public static Quaternion LerpRotation(in RenderEntity previous, in RenderEntity current, float tickFactor)
        {
            float rotationFactor = RenderInterpolation.ResolveRotationFactor(tickFactor, current.Progress01);
            float degrees = RenderInterpolation.LerpAngleDegrees(previous.Rotation, current.Rotation, rotationFactor);
            return ToWorldRotation(degrees);
        }

        public static float LerpScale(in RenderEntity previous, in RenderEntity current, float tickFactor) =>
            RenderInterpolation.Lerp(previous.Scale, current.Scale, tickFactor);

        /// <summary>Applies a pure <see cref="CameraPlacement"/> to a real Camera, offset by an additive shake vector. Returns the un-shaken base world position.</summary>
        public static Vector3 ApplyCameraPlacement(Camera camera, CameraPlacement placement, Vector3 shakeOffset)
        {
            camera.orthographic = placement.Orthographic;
            if (placement.Orthographic)
                camera.orthographicSize = placement.OrthoSize;
            else
                camera.fieldOfView = placement.FieldOfViewDeg;

            camera.transform.rotation = Quaternion.Euler(placement.PitchDeg, placement.YawDeg, 0f);

            var basePos = new Vector3(placement.PosX, placement.PosY, placement.PosZ);
            camera.transform.position = basePos + shakeOffset;
            return basePos;
        }

        public static Color ToUnityColor(RgbColor color) => new Color(color.R, color.G, color.B, 1f);
    }
}
